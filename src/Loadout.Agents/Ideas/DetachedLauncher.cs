using Loadout.Core.Configuration;
using Loadout.Core.Diagnostics;
using Loadout.Models;
using Loadout.Models.Agents;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents.Ideas;

/// <summary>A headless session that belongs to no project.</summary>
/// <param name="WorkingDirectory">Where the agent starts, and the only place it is told about.</param>
/// <param name="Purpose">What the session is for, as its records name it.</param>
/// <param name="AgentName">The agent to run, or null for the machine's default.</param>
/// <param name="Model">The model to ask for, or null for the agent's own default.</param>
/// <param name="DryRun">Build the invocation and start nothing.</param>
public sealed record DetachedLaunchRequest(
    string WorkingDirectory,
    string Purpose,
    string? AgentName = null,
    string? Model = null,
    bool DryRun = false);

/// <summary>
/// Starts an agent headlessly with none of a project's launch around it: no
/// compiled context, no preflight, no workspace servers, no ledger entry.
/// </summary>
/// <remarks>
/// <para>
/// For work that happens before there is a project, or beside one without
/// being a session on it. Fleshing out an idea is the case that needs it: the
/// idea may belong to nothing yet, and the ordinary launch refuses without a
/// registered, cloned project, which is right for a session that will work on
/// one and wrong for a conversation about whether there should be one.
/// </para>
/// <para>
/// Everything the agent is allowed is in the options the caller passes, and
/// nothing is inherited: no project settings, no hooks, no MCP servers beyond
/// those named. The caller is the one deciding what an unattended session may
/// touch, so it is the one that says.
/// </para>
/// </remarks>
public interface IDetachedLauncher
{
    /// <summary>Starts the session, or on a dry run says what it would have started.</summary>
    Task<OperationResult<HeadlessLaunch>> StartAsync(
        DetachedLaunchRequest request,
        HeadlessOptions options,
        CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class DetachedLauncher : IDetachedLauncher
{
    private readonly IConfigurationService _configuration;
    private readonly IAgentRegistry _agents;
    private readonly IPlatformPaths _paths;
    private readonly IProcessLauncher _processes;

    public DetachedLauncher(
        IConfigurationService configuration,
        IAgentRegistry agents,
        IPlatformPaths paths,
        IProcessLauncher processes)
    {
        _configuration = configuration;
        _agents = agents;
        _paths = paths;
        _processes = processes;
    }

    /// <inheritdoc />
    public async Task<OperationResult<HeadlessLaunch>> StartAsync(
        DetachedLaunchRequest request,
        HeadlessOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (!Directory.Exists(request.WorkingDirectory))
        {
            return OperationResult<HeadlessLaunch>.Fail(
                $"'{request.WorkingDirectory}' does not exist, so there is nowhere to start the agent.",
                ExitCode.RepositoryUnavailable);
        }

        var config = await _configuration.LoadConfigAsync(ct).ConfigureAwait(false);

        if (config.Failed)
        {
            return OperationResult<HeadlessLaunch>.Fail(config.Error!, config.ExitCode);
        }

        var agentName = request.AgentName is { Length: > 0 } asked ? asked : config.Value!.DefaultAgent;
        var adapter = _agents.Resolve(agentName);

        if (adapter.Failed)
        {
            return OperationResult<HeadlessLaunch>.Fail(adapter.Error!, adapter.ExitCode);
        }

        if (adapter.Value!.HeadlessProtocol is not { } protocol)
        {
            return OperationResult<HeadlessLaunch>.Fail(
                $"{adapter.Value.DisplayName} cannot be driven without a terminal, so it cannot do this. "
                + "Name one that can with --agent; claude can.",
                ExitCode.AgentUnavailable);
        }

        var runtimeDirectory = _paths.CreateRuntimeDirectory();

        try
        {
            // A project in shape only. The adapter's interface takes one, and
            // everything it reads from it beyond the working directory is
            // absent here on purpose: no manifest, no workspace, no context.
            var nobody = new ProjectResolution(
                new ProjectRegistryEntry { Name = request.Purpose, DefaultAgent = agentName },
                request.WorkingDirectory,
                null,
                0,
                false);

            var context = new AgentLaunchContext(
                nobody,
                request.WorkingDirectory,
                runtimeDirectory,
                WorkspacePath: null,
                PassthroughArguments: [],
                Model: request.Model,
                Headless: options);

            var invocation = await adapter.Value.BuildInvocationAsync(context, ct).ConfigureAwait(false);

            if (invocation.Failed)
            {
                Clean(runtimeDirectory);

                return OperationResult<HeadlessLaunch>.Fail(invocation.Error!, invocation.ExitCode);
            }

            var built = invocation.Value!;
            var warnings = built.Warnings?.ToList() ?? [];

            var plan = new LaunchPlan(
                built.Executable,
                built.Arguments,
                request.WorkingDirectory,
                built.Environment.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList(),
                [],
                null,
                0,
                0,
                null,
                null,
                request.Purpose);

            var preflight = new PreflightResult([], new Dictionary<string, string>());

            if (request.DryRun)
            {
                Clean(runtimeDirectory);
                warnings.Add("Dry run: nothing was launched.");

                return OperationResult<HeadlessLaunch>.Ok(new HeadlessLaunch(
                    plan, warnings, preflight, request.Purpose, adapter.Value.Name,
                    launchId: null, session: null, complete: (_, _) => Task.CompletedTask));
            }

            var started = await _processes.StartPipedAsync(
                new ProcessRequest(
                    built.Executable,
                    built.Arguments,
                    request.WorkingDirectory,
                    built.Environment,
                    RemoveEnvironmentPrefixes: built.RemoveEnvironmentPrefixes),
                ct).ConfigureAwait(false);

            if (started.Failed)
            {
                Clean(runtimeDirectory);

                return OperationResult<HeadlessLaunch>.Fail(started.Error!, started.ExitCode);
            }

            return OperationResult<HeadlessLaunch>.Ok(new HeadlessLaunch(
                plan,
                warnings,
                preflight,
                request.Purpose,
                adapter.Value.Name,
                launchId: null,
                new HeadlessSession(started.Value!, protocol),
                complete: (_, _) =>
                {
                    Clean(runtimeDirectory);

                    return Task.CompletedTask;
                }));
        }
        catch
        {
            Clean(runtimeDirectory);
            throw;
        }
    }

    private static void Clean(string runtimeDirectory)
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
            // Untidy and harmless: the reaper collects runtime directories a
            // launch left behind.
        }
    }
}
