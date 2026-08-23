using System.Text;
using Loadout.Core.Instructions;
using Loadout.Models;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Context;

/// <inheritdoc />
public sealed class ContextCompiler : IContextCompiler
{
    /// <summary>Name of the implicit profile that loads a project's base context.</summary>
    public const string DefaultProfileName = "default";

    internal const string CompiledFileName = "compiled-context.md";

    /// <summary>
    /// Refuses to inline anything larger than this. A stray build log or
    /// database dump committed into the workspace would otherwise be pasted
    /// wholesale into an agent's system prompt.
    /// </summary>
    private const long MaximumSourceBytes = 512 * 1024;

    private readonly IFilePermissions _permissions;
    private readonly IRuleService _rules;
    private readonly IMemoryService _memory;

    public ContextCompiler(
        IFilePermissions permissions,
        IRuleService rules,
        IMemoryService memory)
    {
        _permissions = permissions;
        _rules = rules;
        _memory = memory;
    }

    /// <inheritdoc />
    public async Task<OperationResult<CompiledContext>> CompileAsync(
        ProjectManifest manifest,
        string workspacePath,
        string runtimeDirectory,
        string agentName,
        string? profileName = null,
        string? handoffPath = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(runtimeDirectory))
        {
            return OperationResult<CompiledContext>.Fail(
                $"The runtime directory '{runtimeDirectory}' does not exist.");
        }

        ContextProfile? profile = null;

        if (profileName is not null && !IsDefaultProfile(profileName))
        {
            if (!manifest.Profiles.TryGetValue(profileName, out profile))
            {
                var available = string.Join(", ", ListProfiles(manifest, agentName));

                return OperationResult<CompiledContext>.Fail(
                    $"'{manifest.Slug}' has no profile named '{profileName}'. Available: {available}.",
                    ExitCode.InvalidArguments);
            }

            if (profile.Agents.Count > 0
                && !profile.Agents.Contains(agentName, StringComparer.OrdinalIgnoreCase))
            {
                return OperationResult<CompiledContext>.Fail(
                    $"Profile '{profileName}' is not available for the {agentName} agent.",
                    ExitCode.InvalidArguments);
            }
        }

        var plan = BuildPlan(manifest, workspacePath, agentName, profile, handoffPath);

        var builder = new StringBuilder();
        var sources = new List<ContextSource>();
        var missing = new List<string>();

        WriteHeader(builder, manifest, agentName, profileName);

        foreach (var entry in plan)
        {
            ct.ThrowIfCancellationRequested();

            if (!File.Exists(entry.AbsolutePath))
            {
                // Implicit entries are ones the compiler adds itself rather
                // than ones the manifest asked for. Most projects have no
                // per-agent instructions file, and reporting its absence every
                // launch would train people to ignore the warning that matters.
                if (!entry.IsOptional)
                {
                    missing.Add(entry.DisplayPath);
                }

                continue;
            }

            var length = new FileInfo(entry.AbsolutePath).Length;

            if (length > MaximumSourceBytes)
            {
                // Skipped loudly rather than truncated: half a document is
                // worse than a clear note saying it was left out.
                missing.Add($"{entry.DisplayPath} (skipped: {length / 1024}KB exceeds the context limit)");
                continue;
            }

            var content = await File.ReadAllTextAsync(entry.AbsolutePath, ct).ConfigureAwait(false);

            builder.AppendLine();
            builder.AppendLine($"## {entry.Heading}");
            builder.AppendLine();
            builder.AppendLine($"<!-- source: {entry.DisplayPath} -->");
            builder.AppendLine();
            builder.AppendLine(content.TrimEnd());
            builder.AppendLine();

            sources.Add(new ContextSource(entry.DisplayPath, entry.Heading, length));
        }

        await AppendRulesAsync(builder, sources, workspacePath, manifest.Slug, ct)
            .ConfigureAwait(false);

        await AppendMemoryAsync(builder, sources, workspacePath, manifest.Slug, ct)
            .ConfigureAwait(false);

        var outputPath = Path.Combine(runtimeDirectory, CompiledFileName);

        try
        {
            await File.WriteAllTextAsync(outputPath, builder.ToString(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<CompiledContext>.Fail(
                $"Could not write the compiled context: {ex.Message}");
        }

        // The compiled file aggregates everything the agent will be told, so it
        // gets the same protection as any other runtime material
        // (spec section 82).
        _permissions.RestrictToCurrentUser(outputPath);

        return OperationResult<CompiledContext>.Ok(
            new CompiledContext(outputPath, sources, missing, profileName));
    }

    /// <summary>
    /// Appends the instruction rules.
    /// <para>
    /// Always-apply rules are inlined; scoped ones are listed by name, scope and
    /// path so the agent can open the one it needs. That split is the whole
    /// reason for scoping: inlining every rule at launch would put the database
    /// conventions in front of somebody editing a stylesheet, and a listing
    /// costs a line each instead of a file each.
    /// </para>
    /// </summary>
    private async Task AppendRulesAsync(
        StringBuilder builder,
        List<ContextSource> sources,
        string workspacePath,
        string slug,
        CancellationToken ct)
    {
        var loaded = await _rules.LoadAsync(workspacePath, slug, ct).ConfigureAwait(false);

        // Rules are an optional layer. A workspace with none is the ordinary
        // case, and a failure to read them must not stop a launch.
        if (loaded.Failed || loaded.Value!.Count == 0)
        {
            return;
        }

        var rules = loaded.Value;
        var always = rules.Where(r => r.AlwaysApply || r.IsUnscoped).ToList();
        var scoped = rules.Where(r => !r.AlwaysApply && !r.IsUnscoped).ToList();

        foreach (var rule in always)
        {
            builder.AppendLine();
            builder.AppendLine($"## Rule: {rule.Name}");
            builder.AppendLine();
            builder.AppendLine($"<!-- source: {DisplayRulePath(workspacePath, rule.Path)} -->");
            builder.AppendLine();
            builder.AppendLine(rule.Body.TrimEnd());
            builder.AppendLine();

            sources.Add(new ContextSource(
                DisplayRulePath(workspacePath, rule.Path), $"Rule: {rule.Name}", rule.Bytes));
        }

        if (scoped.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Rules available on demand");
        builder.AppendLine();
        builder.AppendLine(
            "These are not loaded. Read one when the work touches the paths it names.");
        builder.AppendLine();

        foreach (var rule in scoped)
        {
            var description = string.IsNullOrWhiteSpace(rule.Description)
                ? "no description"
                : rule.Description;

            builder.AppendLine(
                $"- `{rule.Name}` ({string.Join(", ", rule.Globs)}) - {description}. "
                + $"Read: `{DisplayRulePath(workspacePath, rule.Path)}`");
        }

        builder.AppendLine();
    }

    /// <summary>
    /// Appends the memory index, and only the index.
    /// <para>
    /// The topics stay on disk with their paths listed. A project accumulates
    /// memory for years, and inlining all of it would make every session pay
    /// for every fact anyone ever recorded.
    /// </para>
    /// </summary>
    private async Task AppendMemoryAsync(
        StringBuilder builder,
        List<ContextSource> sources,
        string workspacePath,
        string slug,
        CancellationToken ct)
    {
        var index = await _memory.ReadIndexAsync(workspacePath, slug, ct).ConfigureAwait(false);

        if (index.Failed || index.Value is null)
        {
            return;
        }

        var relative = $"projects/{slug}/memory";

        builder.AppendLine();
        builder.AppendLine("## Project memory");
        builder.AppendLine();
        builder.AppendLine($"<!-- source: {relative}/MEMORY.md -->");
        builder.AppendLine();
        builder.AppendLine(
            "Durable facts recorded from earlier sessions. Each entry is a file under "
            + $"`{relative}/`; read the ones that bear on the task. The repository is "
            + "authoritative: where memory and the code disagree, the code is right and the "
            + "memory needs correcting.");
        builder.AppendLine();
        builder.AppendLine(index.Value.TrimEnd());
        builder.AppendLine();

        sources.Add(new ContextSource(
            $"{relative}/MEMORY.md",
            "Project memory",
            Encoding.UTF8.GetByteCount(index.Value)));
    }

    /// <summary>
    /// Shows a rule by its path inside the workspace rather than its absolute
    /// location, so the compiled context reads the same on every machine.
    /// </summary>
    private static string DisplayRulePath(string workspacePath, string absolute) =>
        Path.GetRelativePath(workspacePath, absolute).Replace(Path.DirectorySeparatorChar, '/');

    /// <inheritdoc />
    public IReadOnlyList<string> ListProfiles(ProjectManifest manifest, string agentName)
    {
        var names = new List<string> { DefaultProfileName };

        names.AddRange(manifest.Profiles
            .Where(pair => pair.Value.Agents.Count == 0
                || pair.Value.Agents.Contains(agentName, StringComparer.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .Where(name => !IsDefaultProfile(name)));

        return names;
    }

    private static bool IsDefaultProfile(string name) =>
        string.Equals(name, DefaultProfileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the ordered list of files to include.
    /// <para>
    /// Order is deliberate and is the compiler's main editorial decision:
    /// organisation-wide policy first, then the project, then agent-specific
    /// notes, then the profile, then any handoff. Later material is more
    /// specific and more recent, so where two sources conflict the agent reads
    /// the narrower one last.
    /// </para>
    /// </summary>
    private static List<PlannedSource> BuildPlan(
        ProjectManifest manifest,
        string workspacePath,
        string agentName,
        ContextProfile? profile,
        string? handoffPath)
    {
        var plan = new List<PlannedSource>();
        var projectRoot = Path.Combine(workspacePath, "projects", manifest.Slug);

        if (profile?.IncludeGlobal ?? true)
        {
            foreach (var relative in manifest.Context.Global)
            {
                plan.Add(Workspace(workspacePath, relative, "Engineering standards"));
            }
        }

        foreach (var relative in manifest.Context.Project)
        {
            plan.Add(Project(projectRoot, manifest.Slug, relative, "Project context"));
        }

        // Agent-specific instructions live under the project's own agents
        // directory and are loaded only for the agent being launched, so a
        // Claude session is never handed Codex's notes.
        plan.Add(Project(
            projectRoot,
            manifest.Slug,
            $"agents/{agentName}/instructions.md",
            $"{agentName} instructions",
            isOptional: true));

        if (profile is not null)
        {
            foreach (var relative in profile.Context)
            {
                plan.Add(Project(projectRoot, manifest.Slug, relative, "Profile context"));
            }
        }

        if (handoffPath is not null)
        {
            plan.Add(new PlannedSource(
                handoffPath,
                Path.GetFileName(handoffPath),
                "Current handoff"));
        }

        // A file listed twice, for instance by both the base context and a
        // profile, is included once. Repeating it wastes the agent's attention
        // and implies emphasis that was not intended.
        return plan
            .GroupBy(p => p.AbsolutePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static PlannedSource Workspace(string workspacePath, string relative, string heading) =>
        new(Path.Combine(workspacePath, ToNativePath(relative)), ToDisplayPath(relative), heading);

    private static PlannedSource Project(
        string projectRoot,
        string slug,
        string relative,
        string heading,
        bool isOptional = false) =>
        new(
            Path.Combine(projectRoot, ToNativePath(relative)),
            $"projects/{slug}/{ToDisplayPath(relative)}",
            heading,
            isOptional);

    /// <summary>
    /// Workspace paths are written with forward slashes in the manifest and are
    /// shown back to the user that way, so the display form stays identical on
    /// every platform. Only the path handed to the filesystem is converted.
    /// </summary>
    private static string ToNativePath(string relative) =>
        relative.Replace('/', Path.DirectorySeparatorChar);

    private static string ToDisplayPath(string relative) =>
        relative.Replace(Path.DirectorySeparatorChar, '/');

    private static void WriteHeader(
        StringBuilder builder,
        ProjectManifest manifest,
        string agentName,
        string? profileName)
    {
        builder.AppendLine($"# {manifest.Name}");
        builder.AppendLine();
        builder.AppendLine(
            "This context was compiled by the Loadout from the central "
            + "workspace. It is regenerated on every launch; editing it here has no effect. "
            + "Change the source files in the workspace instead.");
        builder.AppendLine();
        builder.AppendLine($"- Agent: {agentName}");
        builder.AppendLine($"- Profile: {profileName ?? DefaultProfileName}");

        if (!string.IsNullOrWhiteSpace(manifest.Repository.Remote))
        {
            builder.AppendLine($"- Repository: {manifest.Repository.Remote}");
        }

        builder.AppendLine();
    }

    private sealed record PlannedSource(
        string AbsolutePath,
        string DisplayPath,
        string Heading,
        bool IsOptional = false);
}
