using Loadout.Core.Configuration;
using Loadout.Core.Context;
using Loadout.Core.Diagnostics;
using Loadout.Core.Git;
using Loadout.Core.Policies;
using Loadout.Core.Projects;
using Loadout.Core.Security;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Configuration;
using Loadout.Models.Diagnostics;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents;

/// <summary>What the caller asked for on the command line or picked in the TUI.</summary>
/// <param name="ProjectHandle">Slug, alias or name of the project to launch.</param>
/// <param name="AgentName">Agent to use, or null to take the project or global default.</param>
/// <param name="Offline">Skip the network entirely (spec section 48).</param>
/// <param name="NoSync">Skip the workspace sync but stay online for anything else.</param>
/// <param name="Worktree">Named worktree to launch in instead of the main tree (spec section 71).</param>
/// <param name="Profile">Context profile to apply (spec section 34).</param>
/// <param name="IncludeHandoff">Append the most recent handoff to the context (spec section 69).</param>
/// <param name="Environment">Environment to work in, such as production (spec section 57).</param>
/// <param name="PassthroughArguments">Arguments after a bare double dash, passed through untouched.</param>
/// <param name="ResumeSessionId">
/// A previous conversation to pick up rather than starting a new one. Handed to
/// the agent only when it advertises that it can resume (spec section 66).
/// </param>
public sealed record LaunchRequest(
    string ProjectHandle,
    string? AgentName = null,
    bool Offline = false,
    bool NoSync = false,
    string? Worktree = null,
    string? Profile = null,
    bool IncludeHandoff = false,
    string? Environment = null,
    IReadOnlyList<string>? PassthroughArguments = null,
    string? ResumeSessionId = null);

/// <summary>How a launch ended.</summary>
/// <param name="AgentExitCode">The agent's own exit status, propagated per spec section 40.</param>
/// <param name="SyncOutcome">What happened when the workspace was synchronised.</param>
/// <param name="Warnings">Non-fatal findings worth showing the user.</param>
/// <param name="Preflight">The preflight result, for callers that want to render it in full.</param>
/// <param name="PendingWorkspaceChanges">
/// Workspace files the session changed and that have not been committed
/// (spec section 45).
/// <para>
/// Populated only when the exit policy is "prompt". Deciding what to do with
/// them is a question for a person, and core must not ask one: spec section 37
/// forbids a menu appearing in a pipe or a CI job, so the decision is handed
/// back to whichever interface can actually hold a conversation.
/// </para>
/// </param>
/// <param name="ProjectName">Project that ran, for building the commit message.</param>
/// <param name="AgentName">Agent that ran, for building the commit message.</param>
/// <param name="AgentSource">
/// Which layer chose the agent. Four can, and until this said so there was no
/// answer to "why is it launching that one?" short of reading the code.
/// </param>
public sealed record LaunchOutcome(
    int AgentExitCode,
    WorkspaceSyncOutcome SyncOutcome,
    IReadOnlyList<string> Warnings,
    PreflightResult? Preflight,
    IReadOnlyList<string>? PendingWorkspaceChanges = null,
    string? ProjectName = null,
    string? AgentName = null,
    SettingSource AgentSource = SettingSource.BuiltIn);

/// <summary>Runs the launch sequence of spec section 45.</summary>
public interface IAgentLauncher
{
    Task<OperationResult<LaunchOutcome>> LaunchAsync(LaunchRequest request, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class AgentLauncher : IAgentLauncher
{
    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly IConfigurationService _configuration;
    private readonly IAgentRegistry _agents;
    private readonly IPlatformPaths _paths;
    private readonly IProcessLauncher _processes;
    private readonly IGitManager _git;
    private readonly IContextCompiler _context;
    private readonly IHandoffService _handoffs;
    private readonly IPreflightService _preflight;
    private readonly Core.Mcp.IMcpService _mcp;
    private readonly ISecurityProfileService _security;

    public AgentLauncher(
        IProjectService projects,
        IWorkspaceManager workspace,
        IConfigurationService configuration,
        IAgentRegistry agents,
        IPlatformPaths paths,
        IProcessLauncher processes,
        IGitManager git,
        IContextCompiler context,
        IHandoffService handoffs,
        IPreflightService preflight,
        ISecurityProfileService security,
        Core.Mcp.IMcpService mcp)
    {
        _projects = projects;
        _workspace = workspace;
        _configuration = configuration;
        _agents = agents;
        _paths = paths;
        _processes = processes;
        _git = git;
        _context = context;
        _handoffs = handoffs;
        _preflight = preflight;
        _mcp = mcp;
        _security = security;
    }

    /// <inheritdoc />
    public async Task<OperationResult<LaunchOutcome>> LaunchAsync(
        LaunchRequest request,
        CancellationToken ct = default)
    {
        var warnings = new List<string>();

        var configResult = await _configuration.LoadConfigAsync(ct).ConfigureAwait(false);
        if (configResult.Failed)
        {
            return OperationResult<LaunchOutcome>.Fail(configResult.Error!, configResult.ExitCode);
        }

        var config = configResult.Value!;

        // Step 1 of section 45: synchronise before resolving anything, so the
        // project definition read below is the freshest one available.
        var syncOutcome = await SynchroniseAsync(config, request, warnings, ct).ConfigureAwait(false);

        var projectResult = await _projects.ResolveAsync(request.ProjectHandle, ct).ConfigureAwait(false);
        if (projectResult.Failed)
        {
            return OperationResult<LaunchOutcome>.Fail(projectResult.Error!, projectResult.ExitCode);
        }

        var project = projectResult.Value!;

        if (project.LocalPath is null)
        {
            // Spec section 28: a project registered centrally but absent here
            // is an invitation to clone, not a dead end.
            return OperationResult<LaunchOutcome>.Fail(
                $"'{project.Entry.Name}' is registered but not present on this machine. "
                + $"Clone it with: loadout project clone {project.Entry.Slug}",
                ExitCode.RepositoryUnavailable);
        }

        var directoryResult = await ResolveWorkingDirectoryAsync(project, request.Worktree, ct)
            .ConfigureAwait(false);
        if (directoryResult.Failed)
        {
            return OperationResult<LaunchOutcome>.Fail(directoryResult.Error!, directoryResult.ExitCode);
        }

        var manifest = await LoadManifestAsync(project.Entry.Slug, warnings, ct).ConfigureAwait(false);

        var agent = ResolveAgent(request, manifest, project, config);
        var agentName = agent.Value;

        // Said only when it is worth saying. A project that names its own agent
        // is not a surprise; falling through to a personal default because
        // nothing names one is exactly the case where somebody later asks why
        // it started the agent it did.
        if (agent.Source == SettingSource.SharedConfiguration)
        {
            warnings.Add(
                $"'{project.Entry.Name}' names no agent, so {agent.Value} was used — "
                + $"{agent.Explanation}.");
        }

        var adapterResult = _agents.Resolve(agentName);
        if (adapterResult.Failed)
        {
            return OperationResult<LaunchOutcome>.Fail(adapterResult.Error!, adapterResult.ExitCode);
        }

        var adapter = adapterResult.Value!;
        var runtimeDirectory = _paths.CreateRuntimeDirectory();

        try
        {
            var compiled = await CompileContextAsync(
                manifest, runtimeDirectory, adapter.Name, request, warnings, ct).ConfigureAwait(false);

            if (compiled.Failed)
            {
                return OperationResult<LaunchOutcome>.Fail(compiled.Error!, compiled.ExitCode);
            }

            var descriptor = await adapter.DetectAsync(ct).ConfigureAwait(false);

            ResolvedEnvironment? environment = null;

            if (manifest is not null)
            {
                var environmentResult = await _security
                    .ResolveAsync(manifest, request.Environment, ct)
                    .ConfigureAwait(false);

                // Naming an environment that does not exist stops the launch.
                // Falling back to the default would hand somebody who typed
                // "prod" instead of "production" the permissive profile.
                if (environmentResult.Failed)
                {
                    return OperationResult<LaunchOutcome>.Fail(
                        environmentResult.Error!, environmentResult.ExitCode);
                }

                environment = environmentResult.Value;
            }

            var preflightResult = await _preflight.RunAsync(
                new PreflightContext(
                    project,
                    manifest,
                    directoryResult.Value!,
                    descriptor,
                    compiled.Value,
                    syncOutcome,
                    environment),
                ct).ConfigureAwait(false);

            if (preflightResult.Failed)
            {
                return OperationResult<LaunchOutcome>.Fail(
                    preflightResult.Error!, preflightResult.ExitCode);
            }

            var preflight = preflightResult.Value!;

            warnings.AddRange(preflight.Warnings.Select(w => $"{w.Name}: {w.Detail}"));

            if (!preflight.CanLaunch)
            {
                // Preflight blocks rather than letting the agent start in a
                // state the user did not intend, and names every reason at once
                // so the problem can be fixed in one pass.
                var reasons = string.Join("; ", preflight.Blocking.Select(c => $"{c.Name}: {c.Detail}"));

                return OperationResult<LaunchOutcome>.Fail(
                    $"Preflight failed. {reasons}",
                    ChooseFailureCode(preflight));
            }

            var context = new AgentLaunchContext(
                project,
                directoryResult.Value!,
                runtimeDirectory,
                _workspace.IsAvailable() ? _workspace.LocalPath : null,
                request.PassthroughArguments ?? [],
                manifest,
                compiled.Value,
                preflight.Environment,
                environment?.Profile,
                request.ResumeSessionId,

                // Declared in the workspace, so the same servers are there on
                // every machine that clones it rather than on whichever one
                // happened to have them configured.
                _mcp.ConfigFiles(project.Entry.Slug));

            var invocationResult = await adapter.BuildInvocationAsync(context, ct).ConfigureAwait(false);
            if (invocationResult.Failed)
            {
                return OperationResult<LaunchOutcome>.Fail(
                    invocationResult.Error!, invocationResult.ExitCode);
            }

            var invocation = invocationResult.Value!;

            if (invocation.Warnings is not null)
            {
                warnings.AddRange(invocation.Warnings);
            }

            // The agent inherits this process's terminal, so Ctrl+C, resize and
            // signals reach it directly and its exit code comes back unaltered
            // (spec sections 40 and 43).
            var runResult = await _processes.RunInteractiveAsync(
                new ProcessRequest(
                    invocation.Executable,
                    invocation.Arguments,
                    context.WorkingDirectory,
                    invocation.Environment),
                ct).ConfigureAwait(false);

            if (runResult.Failed)
            {
                return OperationResult<LaunchOutcome>.Fail(runResult.Error!, runResult.ExitCode);
            }

            // Recorded after the agent exits so a failed launch does not
            // pollute the recent-projects ordering.
            await _projects.RecordLaunchAsync(project.Entry.Slug, adapter.Name, ct).ConfigureAwait(false);

            var pending = await HandleExitPolicyAsync(
                config, project.Entry.Name, adapter.Name, warnings, ct).ConfigureAwait(false);

            return OperationResult<LaunchOutcome>.Ok(new LaunchOutcome(
                runResult.Value,
                syncOutcome,
                warnings,
                preflight,
                pending,
                project.Entry.Name,
                adapter.Name,
                agent.Source));
        }
        finally
        {
            CleanRuntimeDirectory(runtimeDirectory);
        }
    }

    /// <summary>
    /// Picks the exit code that best describes why preflight refused, so a
    /// script can tell a missing agent from a missing credential.
    /// </summary>
    private static ExitCode ChooseFailureCode(PreflightResult preflight)
    {
        var categories = preflight.Blocking.Select(c => c.Category).ToList();

        if (categories.Contains("Agent"))
        {
            return ExitCode.AgentUnavailable;
        }

        if (categories.Contains("Environment"))
        {
            return ExitCode.AuthenticationRequired;
        }

        return categories.Contains("Repository")
            ? ExitCode.RepositoryUnavailable
            : ExitCode.GeneralFailure;
    }

    private async Task<ProjectManifest?> LoadManifestAsync(
        string slug,
        List<string> warnings,
        CancellationToken ct)
    {
        if (!_workspace.IsAvailable())
        {
            return null;
        }

        var result = await _workspace.ReadProjectAsync(slug, ct).ConfigureAwait(false);

        if (result.Succeeded)
        {
            return result.Value;
        }

        // A registered project with no manifest still launches; it just has no
        // context, profiles or environment. Saying so beats failing outright.
        warnings.Add(
            $"No manifest was found for '{slug}', so no context was compiled. "
            + "Create one at projects/" + slug + "/project.yaml in the workspace.");

        return null;
    }

    private async Task<OperationResult<CompiledContext?>> CompileContextAsync(
        ProjectManifest? manifest,
        string runtimeDirectory,
        string agentName,
        LaunchRequest request,
        List<string> warnings,
        CancellationToken ct)
    {
        if (manifest is null || !_workspace.IsAvailable())
        {
            return OperationResult<CompiledContext?>.Ok(null);
        }

        string? handoffPath = null;

        if (request.IncludeHandoff)
        {
            var handoff = await _handoffs.GetLatestAsync(manifest.Slug, ct).ConfigureAwait(false);

            if (handoff.Succeeded && handoff.Value is not null)
            {
                handoffPath = handoff.Value.Path;
            }
            else
            {
                warnings.Add($"No handoff exists for '{manifest.Slug}', so none was included.");
            }
        }

        var compiled = await _context.CompileAsync(
            manifest,
            _workspace.LocalPath,
            runtimeDirectory,
            agentName,
            request.Profile,
            handoffPath,
            ct).ConfigureAwait(false);

        // A bad profile name is the user's mistake and must stop the launch:
        // silently running with the wrong context would be worse than an error.
        return compiled.Failed
            ? OperationResult<CompiledContext?>.Fail(compiled.Error!, compiled.ExitCode)
            : OperationResult<CompiledContext?>.Ok(compiled.Value);
    }

    /// <summary>
    /// Applies the exit policy of spec section 45 to whatever the session
    /// changed in the workspace.
    /// </summary>
    /// <returns>
    /// The pending paths when a person needs to decide, or null when the policy
    /// already settled it.
    /// </returns>
    private async Task<IReadOnlyList<string>?> HandleExitPolicyAsync(
        Models.Configuration.LauncherConfig config,
        string projectName,
        string agentName,
        List<string> warnings,
        CancellationToken ct)
    {
        if (string.Equals(config.Sync.Exit, "never", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var pendingResult = await _workspace.GetPendingChangesAsync(ct).ConfigureAwait(false);

        if (pendingResult.Failed || pendingResult.Value!.Count == 0)
        {
            // A session that only read changes nothing, which is the common
            // case and must not produce an empty commit (spec section 46).
            return null;
        }

        if (!string.Equals(config.Sync.Exit, "always", StringComparison.OrdinalIgnoreCase))
        {
            return pendingResult.Value;
        }

        var saveResult = await _workspace
            .SaveAsync(projectName, agentName, push: true, ct)
            .ConfigureAwait(false);

        if (saveResult.Failed)
        {
            // The commit may well have succeeded and only the push failed, in
            // which case nothing is lost and the next sync carries it.
            warnings.Add(saveResult.Error!);
        }

        return null;
    }

    private async Task<WorkspaceSyncOutcome> SynchroniseAsync(
        Models.Configuration.LauncherConfig config,
        LaunchRequest request,
        List<string> warnings,
        CancellationToken ct)
    {
        if (request.Offline || request.NoSync)
        {
            return WorkspaceSyncOutcome.Offline;
        }

        if (!string.Equals(config.Sync.Launch, "auto", StringComparison.OrdinalIgnoreCase))
        {
            // "prompt" is handled by the interactive caller; from here a
            // non-auto policy simply means "do not touch the network".
            return WorkspaceSyncOutcome.Offline;
        }

        var syncResult = await _workspace.SyncAsync(config, ct).ConfigureAwait(false);

        if (syncResult.Failed)
        {
            warnings.Add(SecretRedactor.Redact(syncResult.Error));
            return WorkspaceSyncOutcome.Offline;
        }

        var outcome = syncResult.Value!;

        if (outcome.Outcome is WorkspaceSyncOutcome.Offline or WorkspaceSyncOutcome.Conflict)
        {
            // The launch continues. A stale workspace is far better than a
            // developer blocked by an unreachable server (spec section 48),
            // and a conflict needs a decision that must not be made silently.
            warnings.Add(outcome.Detail);
        }

        return outcome.Outcome;
    }

    /// <summary>
    /// Chooses the agent, and records which layer chose it.
    /// </summary>
    /// <remarks>
    /// The order is unchanged and is the product decision: what was asked for
    /// beats what the project says, and what the project says beats a personal
    /// default. What is new is that the answer carries where it came from, so
    /// somebody surprised by it can be told rather than left to guess.
    /// </remarks>
    internal static Resolved<string> ResolveAgent(
        LaunchRequest request,
        ProjectManifest? manifest,
        ProjectResolution project,
        LauncherConfig config)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(config);

        if (request.AgentName is { Length: > 0 } asked)
        {
            return new Resolved<string>(asked, SettingSource.CommandLine);
        }

        if (manifest?.Agents.Default is { Length: > 0 } declared)
        {
            return new Resolved<string>(declared, SettingSource.ProjectManifest);
        }

        if (!string.IsNullOrWhiteSpace(project.Entry.DefaultAgent))
        {
            return new Resolved<string>(project.Entry.DefaultAgent, SettingSource.ProjectRegistry);
        }

        return new Resolved<string>(config.DefaultAgent, SettingSource.SharedConfiguration);
    }

    private async Task<OperationResult<string>> ResolveWorkingDirectoryAsync(
        ProjectResolution project,
        string? worktree,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(worktree))
        {
            return OperationResult<string>.Ok(project.LocalPath!);
        }

        var worktreesResult = await _git.ListWorktreesAsync(project.LocalPath!, ct).ConfigureAwait(false);
        if (worktreesResult.Failed)
        {
            return OperationResult<string>.Fail(worktreesResult.Error!, worktreesResult.ExitCode);
        }

        var match = worktreesResult.Value!.FirstOrDefault(
            w => string.Equals(w.Branch, worktree, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(w.Path), worktree, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var available = string.Join(", ", worktreesResult.Value!
                .Select(w => w.Branch ?? Path.GetFileName(w.Path)));

            return OperationResult<string>.Fail(
                $"No worktree named '{worktree}'. Available: {available}.",
                ExitCode.InvalidArguments);
        }

        return OperationResult<string>.Ok(match.Path);
    }

    /// <summary>
    /// Deletes the per-launch runtime directory. Spec section 82 requires
    /// sensitive runtime files to be cleaned after use, and this is the only
    /// place that happens.
    /// </summary>
    private static void CleanRuntimeDirectory(string runtimeDirectory)
    {
        try
        {
            if (Directory.Exists(runtimeDirectory))
            {
                Directory.Delete(runtimeDirectory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover runtime directory is untidy but harmless, and it is
            // never inside an application repository. Failing the launch over
            // it would be worse.
        }
    }
}
