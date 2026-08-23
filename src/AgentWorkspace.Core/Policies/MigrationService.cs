using AgentWorkspace.Core.Git;
using AgentWorkspace.Core.Workspace;
using AgentWorkspace.Models.Policies;
using AgentWorkspace.Models.Results;

namespace AgentWorkspace.Core.Policies;

/// <inheritdoc />
public sealed class MigrationService : IMigrationService
{
    /// <summary>
    /// Where each well-known agent artefact belongs in the workspace.
    /// <para>
    /// Ordered longest-prefix-first so that <c>.claude/skills</c> is matched
    /// before the bare <c>.claude</c> rule that would otherwise swallow it.
    /// </para>
    /// </summary>
    private static readonly (string Source, string Destination)[] KnownMappings =
    [
        (".claude/settings.local.json", "agents/claude/settings.json"),
        (".claude/settings.json", "agents/claude/settings.json"),
        (".claude/skills", "agents/claude/skills"),
        (".claude/agents", "agents/claude/agents"),
        (".claude/commands", "agents/claude/commands"),
        (".claude", "agents/claude"),
        (".codex", "agents/codex"),
        (".cursor", "agents/cursor"),
        (".windsurf", "agents/windsurf"),
        (".continue", "agents/continue"),
        (".roo", "agents/roo"),
        (".ai", "agents/generic"),
        (".agent", "agents/generic"),
        ("CLAUDE.local.md", "agents/claude/instructions.local.md"),
        ("CLAUDE.md", "agents/claude/instructions.md"),
        ("AGENTS.override.md", "agents/codex/AGENTS.override.md"),
    ];

    private readonly IPolicyService _policies;
    private readonly IWorkspaceManager _workspace;
    private readonly IGitManager _git;

    public MigrationService(
        IPolicyService policies,
        IWorkspaceManager workspace,
        IGitManager git)
    {
        _policies = policies;
        _workspace = workspace;
        _git = git;
    }

    /// <inheritdoc />
    public async Task<OperationResult<MigrationPlan>> PlanAsync(
        string repositoryPath,
        string slug,
        bool includeIgnored = false,
        CancellationToken ct = default)
    {
        var checkResult = await _policies.CheckAsync(repositoryPath, ct).ConfigureAwait(false);
        if (checkResult.Failed)
        {
            return OperationResult<MigrationPlan>.Fail(checkResult.Error!, checkResult.ExitCode);
        }

        var report = checkResult.Value!;
        var steps = new List<MigrationStep>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Directory-level moves first: moving .claude as a unit is clearer than
        // listing forty files inside it, and it keeps the plan readable.
        foreach (var (source, destination) in KnownMappings)
        {
            ct.ThrowIfCancellationRequested();

            var absolute = Path.Combine(report.RepositoryPath,
                source.Replace('/', Path.DirectorySeparatorChar));

            var isDirectory = Directory.Exists(absolute);

            if (!isDirectory && !File.Exists(absolute))
            {
                continue;
            }

            if (IsAlreadyCovered(source, claimed))
            {
                continue;
            }

            var kind = ClassifyKind(source, report);

            if (kind == PolicyFindingKind.Ignored && !includeIgnored)
            {
                // Already excluded from the repository, so nothing here is a
                // compliance problem. Taking it would remove a working local
                // setup for no gain.
                claimed.Add(source);
                continue;
            }

            claimed.Add(source);

            steps.Add(new MigrationStep(
                absolute,
                source,
                $"projects/{slug}/{destination}",
                kind,
                isDirectory));
        }

        return OperationResult<MigrationPlan>.Ok(new MigrationPlan(slug, steps, false, []));
    }

    /// <inheritdoc />
    public async Task<OperationResult<MigrationPlan>> ApplyAsync(
        MigrationPlan plan,
        CancellationToken ct = default)
    {
        var trackedLeftInPlace = new List<string>();

        foreach (var step in plan.Steps)
        {
            ct.ThrowIfCancellationRequested();

            var destination = Path.Combine(
                _workspace.LocalPath,
                step.WorkspaceRelativePath.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                if (step.IsDirectory)
                {
                    CopyDirectory(step.SourcePath, destination);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(step.SourcePath, destination, overwrite: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return OperationResult<MigrationPlan>.Fail(
                    $"Could not copy '{step.RepositoryRelativePath}' into the workspace: {ex.Message}");
            }

            if (step.Kind == PolicyFindingKind.Tracked)
            {
                // Spec section 27 is explicit: tracked files must not be
                // silently deleted. Removing them rewrites the repository's
                // contents, which is the user's decision and belongs in a
                // commit they can see and review.
                trackedLeftInPlace.Add(step.RepositoryRelativePath);
                continue;
            }

            var removed = TryRemoveFromRepository(step);

            if (removed.Failed)
            {
                return OperationResult<MigrationPlan>.Fail(removed.Error!);
            }
        }

        return OperationResult<MigrationPlan>.Ok(
            plan with { Applied = true, TrackedLeftInPlace = trackedLeftInPlace });
    }

    /// <summary>
    /// Whether a longer mapping has already claimed this path, so that
    /// <c>.claude</c> does not move a subtree that <c>.claude/skills</c>
    /// already accounted for.
    /// </summary>
    private static bool IsAlreadyCovered(string source, HashSet<string> claimed) =>
        claimed.Any(existing =>
            source.StartsWith(existing + "/", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Classifies a path the way spec section 27 asks for. A directory is
    /// treated as tracked when anything inside it is, because that is what
    /// decides whether removing it needs a Git commit.
    /// </summary>
    private static PolicyFindingKind ClassifyKind(string source, PolicyReport report)
    {
        var prefix = source.Replace('\\', '/');

        var related = report.Findings
            .Where(f => f.Path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || f.Path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (related.Any(f => f.Kind == PolicyFindingKind.Tracked))
        {
            return PolicyFindingKind.Tracked;
        }

        return related.Any(f => f.Kind == PolicyFindingKind.UntrackedAndVisible)
            ? PolicyFindingKind.UntrackedAndVisible
            : PolicyFindingKind.Ignored;
    }

    private static OperationResult TryRemoveFromRepository(MigrationStep step)
    {
        try
        {
            if (step.IsDirectory)
            {
                Directory.Delete(step.SourcePath, recursive: true);
            }
            else
            {
                File.Delete(step.SourcePath);
            }

            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The copy already succeeded, so nothing has been lost; the user is
            // told what is still there rather than the whole migration failing.
            return OperationResult.Fail(
                $"'{step.RepositoryRelativePath}' was copied into the workspace but could not be "
                + $"removed from the repository: {ex.Message}");
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
