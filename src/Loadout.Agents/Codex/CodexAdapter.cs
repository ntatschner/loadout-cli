using Loadout.Models.Agents;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents.Codex;

/// <summary>
/// Launches Codex against a project-specific home held outside the application
/// repository (spec section 32).
/// <para>
/// The home is assembled fresh in the launch's runtime directory rather than
/// pointed straight at the workspace clone. Codex writes session state and
/// caches into its home, and letting it write into the clone would leave the
/// workspace repository permanently dirty and put transient state on a path to
/// being committed, which spec section 12 rules out. The durable material is
/// copied in; anything Codex writes back stays in the runtime directory and is
/// discarded with it.
/// </para>
/// <para>
/// CODEX_HOME is set on the child process only, so the launcher's own
/// environment and any sibling process are untouched.
/// </para>
/// </summary>
public sealed class CodexAdapter : AgentAdapterBase
{
    private const string CodexHomeVariable = "CODEX_HOME";

    /// <summary>Codex reads project instructions from this file inside its home.</summary>
    private const string InstructionsFileName = "AGENTS.md";

    public CodexAdapter(
        IExecutableResolver resolver,
        IProcessLauncher processes,
        IReadOnlyList<string> configuredSearchPaths)
        : base(resolver, processes, configuredSearchPaths)
    {
    }

    /// <inheritdoc />
    public override string Name => "codex";

    /// <inheritdoc />
    public override string DisplayName => "Codex";

    /// <inheritdoc />
    protected override string ExecutableName => "codex";

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string[]> CapabilityMarkers =>
        new Dictionary<string, string[]>
        {
            [AgentCapabilities.ExternalHome] = [CodexHomeVariable, "--config"],
            [AgentCapabilities.Sandboxing] = ["--sandbox"],
            [AgentCapabilities.SessionResume] = ["resume"],
            [ModelSelection] = ["--model"],
        };

    /// <inheritdoc />
    public override async Task<OperationResult<AgentInvocation>> BuildInvocationAsync(
        AgentLaunchContext context,
        CancellationToken ct = default)
    {
        var descriptor = await DetectAsync(ct).ConfigureAwait(false);

        if (!descriptor.IsInstalled || descriptor.ExecutablePath is null)
        {
            return OperationResult<AgentInvocation>.Fail(
                $"{DisplayName} is not installed.", Models.ExitCode.AgentUnavailable);
        }

        var environment = context.ResolvedEnvironment is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(context.ResolvedEnvironment);

        var warnings = new List<string>();

        var homeResult = await PrepareHomeAsync(context, warnings, ct).ConfigureAwait(false);

        if (homeResult is not null)
        {
            environment[CodexHomeVariable] = homeResult;
        }

        var arguments = new List<string>();

        // Codex resumes through a subcommand rather than a flag, so it has to
        // come first: everything after it is read as arguments to "resume".
        if (context.ResumeSessionId is { Length: > 0 } session)
        {
            if (descriptor.Supports(AgentCapabilities.SessionResume))
            {
                arguments.Add("resume");
                arguments.Add(session);
            }
            else
            {
                warnings.Add(
                    "This build of Codex does not advertise a resume subcommand, so a new "
                    + "session was started instead of continuing the previous one.");
            }
        }

        AddSecurityProfile(context, descriptor, arguments, warnings);
        AddModel(context, descriptor, arguments, warnings);

        arguments.AddRange(context.PassthroughArguments);

        return OperationResult<AgentInvocation>.Ok(
            new AgentInvocation(descriptor.ExecutablePath, arguments, environment, warnings));
    }

    /// <summary>
    /// Translates the generic security profile into Codex's sandbox modes
    /// (spec section 58).
    /// <para>
    /// Only ever tightens. danger-full-access and the bypass flag are
    /// deliberately unreachable from here: a security profile lives in a shared
    /// repository, and nothing in one should be able to disable a sandbox on
    /// somebody else's machine.
    /// </para>
    /// </summary>

    /// <summary>Capability key for choosing the model.</summary>
    private const string ModelSelection = "model_selection";

    /// <summary>
    /// Asks for the model the project pinned, where the agent can be told.
    /// </summary>
    /// <remarks>
    /// Added before the passthrough arguments, so somebody who still types a
    /// model after <c>--</c> gets the one they typed. Whether this build takes
    /// the option is asked rather than assumed: the marker is looked for in the
    /// agent's own help, so a Codex that spells it differently reports the gap
    /// instead of being handed a flag it does not have.
    /// </remarks>
    private static void AddModel(
        AgentLaunchContext context,
        AgentDescriptor descriptor,
        List<string> arguments,
        List<string> warnings)
    {
        if (context.Model is not { Length: > 0 } model)
        {
            return;
        }

        if (!descriptor.Supports(ModelSelection))
        {
            warnings.Add(
                "This build of Codex does not advertise a model option, so the project's "
                + $"model ({model}) was not applied.");

            return;
        }

        arguments.Add("--model");
        arguments.Add(model);
    }

    private static void AddSecurityProfile(
        AgentLaunchContext context,
        AgentDescriptor descriptor,
        List<string> arguments,
        List<string> warnings)
    {
        if (context.Security is null)
        {
            return;
        }

        var sandbox = context.Security.Filesystem switch
        {
            Models.Policies.FilesystemAccess.ReadOnly => "read-only",

            // Restricted is stricter than ordinary development, and read-only
            // is the strictest mode that still lets the agent work.
            Models.Policies.FilesystemAccess.Restricted => "read-only",

            _ => "workspace-write",
        };

        if (!descriptor.Supports(AgentCapabilities.Sandboxing))
        {
            warnings.Add(
                "This build of Codex does not advertise --sandbox, so the security profile's "
                + $"filesystem setting ({context.Security.Filesystem}) was not applied.");

            return;
        }

        arguments.Add("--sandbox");
        arguments.Add(sandbox);
    }

    /// <summary>
    /// Builds the ephemeral Codex home, returning its path or null when there
    /// is nothing to supply and Codex should use its own default.
    /// </summary>
    private static async Task<string?> PrepareHomeAsync(
        AgentLaunchContext context,
        List<string> warnings,
        CancellationToken ct)
    {
        if (context.CompiledContext is null && context.WorkspacePath is null)
        {
            return null;
        }

        var home = Path.Combine(context.RuntimeDirectory, "codex-home");

        try
        {
            Directory.CreateDirectory(home);

            if (context.WorkspacePath is not null && context.Manifest is not null)
            {
                var source = Path.Combine(
                    context.WorkspacePath,
                    "projects",
                    context.Manifest.Slug,
                    "agents",
                    "codex");

                if (Directory.Exists(source))
                {
                    CopyDurableFiles(source, home);
                }
            }

            if (context.CompiledContext is not null)
            {
                // The compiled context replaces any AGENTS.md copied in above:
                // the compiler already folded that file's content into what it
                // produced, so keeping both would repeat it.
                File.Copy(
                    context.CompiledContext.FilePath,
                    Path.Combine(home, InstructionsFileName),
                    overwrite: true);
            }

            return home;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add(
                $"The Codex home could not be prepared, so Codex will use its own default "
                + $"configuration: {ex.Message}");

            return null;
        }
    }

    /// <summary>
    /// Copies the workspace's durable Codex material into the ephemeral home.
    /// <para>
    /// Session histories, caches and logs are deliberately left behind: spec
    /// section 12 keeps them out of the workspace, and copying them forward
    /// would defeat the isolation this whole approach exists to provide.
    /// </para>
    /// </summary>
    private static void CopyDurableFiles(string source, string destination)
    {
        string[] excludedDirectories = ["sessions", "history", "logs", "cache", ".git"];

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);

            var segments = relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);

            if (segments.Any(segment =>
                excludedDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
