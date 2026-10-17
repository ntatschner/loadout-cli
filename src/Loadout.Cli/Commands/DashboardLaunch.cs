using Loadout.Agents;
using Loadout.Core.Configuration;
using Loadout.Core.Projects;
using Loadout.Core.Sessions;
using Loadout.Core.Sessions.Hosting;
using Loadout.Core.Teams.Daemon;
using Loadout.Core.Workspace;
using Loadout.Models.Agents;
using Loadout.Models.Projects;

namespace Loadout.Cli.Commands;

/// <summary>
/// Studio's New session sheet: what it offers and what a launch would load,
/// from <see cref="LaunchSheet"/>, the terminal launcher's sheet's own source.
/// </summary>
/// <remarks>
/// <para>
/// Beside the commands rather than in Core because the agents this machine
/// has are the agent registry's to say, and Core does not reach it.
/// </para>
/// <para>
/// The registry finds an agent by running it, twice, so what it said is kept
/// for a few minutes: the sheet asks every time the project changes, and a
/// sheet that waits seconds on each choice is one nobody uses. An agent
/// installed in the meantime shows up once that runs out.
/// </para>
/// </remarks>
internal sealed class DashboardLaunch : IStudioLaunch
{
    private static readonly TimeSpan AgentsKept = TimeSpan.FromMinutes(5);

    /// <summary>The modes a task can be worked in, as the terminal launcher's sheet offers them.</summary>
    internal static readonly IReadOnlyList<string> Modes = ["advise", "implement", "investigate", "review"];

    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly IAgentRegistry _agents;
    private readonly IConfigurationService _configuration;
    private readonly LaunchSheet _sheet;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _detecting = new(1, 1);
    private (DateTimeOffset At, IReadOnlyList<AgentDescriptor> Found)? _detected;

    public DashboardLaunch(
        IProjectService projects,
        IWorkspaceManager workspace,
        IAgentRegistry agents,
        IConfigurationService configuration,
        LaunchSheet sheet,
        TimeProvider time)
    {
        _projects = projects;
        _workspace = workspace;
        _agents = agents;
        _configuration = configuration;
        _sheet = sheet;
        _time = time;
    }

    /// <inheritdoc />
    public async Task<object> SheetAsync(string? project, string? agent, CancellationToken ct)
    {
        var listed = await _projects.ListAsync(ct).ConfigureAwait(false);
        var projects = listed.Succeeded
            ? listed.Value!
                .OrderByDescending(one => one.LastLaunchedUtc ?? DateTimeOffset.MinValue)
                .ThenBy(one => one.Entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

        var chosen = project is { Length: > 0 }
            ? projects.FirstOrDefault(one => string.Equals(one.Entry.Slug, project, StringComparison.OrdinalIgnoreCase))
            : projects.FirstOrDefault();

        var agents = await AgentsAsync(ct).ConfigureAwait(false);
        var defaultAgent = chosen is null ? null : await DefaultAgentAsync(chosen, ct).ConfigureAwait(false);
        var agentName = agent is { Length: > 0 } ? agent : defaultAgent ?? agents.FirstOrDefault(one => one.IsInstalled)?.Name ?? "claude";
        var choices = chosen is null ? LaunchChoices.None : await _sheet.ChoicesAsync(chosen, agentName, ct).ConfigureAwait(false);
        var budget = (await _configuration.LoadConfigAsync(ct).ConfigureAwait(false)).Value?.InstructionContext;

        return new
        {
            projects = projects.Select(one => new
            {
                slug = one.Entry.Slug,
                name = one.Entry.Name is { Length: > 0 } name ? name : one.Entry.Slug,
                path = one.LocalPath,
                here = one.IsAvailableLocally,
            }),
            project = chosen?.Entry.Slug,
            agent = agentName,
            defaultAgent,
            agents = agents.Select(one => new { name = one.Name, display = one.DisplayName, installed = one.IsInstalled }),
            modes = Modes,
            profiles = choices.Profiles.Select(one => new { label = one.Label, value = one.Value }),
            worktrees = choices.Worktrees.Select(one => new { label = one.Label, value = one.Value }),
            tasks = choices.Declared.Select(one => new
            {
                id = one.Id,
                title = one.Title,
                state = one.State.ToString().ToLowerInvariant(),
            }),
            budget = new
            {
                tokenBudget = budget?.MaxTokens ?? 12000,
                warnAtPercent = budget?.WarnAtPercent ?? 80,
            },
        };
    }

    /// <inheritdoc />
    public async Task<object> PreviewAsync(HostedLaunch launch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(launch);

        var command = HostedSessions.CommandLine(launch);

        if (command.Failed)
        {
            return new { command = (string?)null, error = command.Error };
        }

        var project = await _projects.ResolveAsync(launch.Project, ct).ConfigureAwait(false);

        if (project.Failed)
        {
            return new { command = command.Value, error = project.Error };
        }

        // A recorded task is resolved by its title, which is what the launch
        // itself puts to the resolver when given --task-id without --task.
        var task = launch.Task;

        if (task is null && launch.TaskId is { Length: > 0 } id)
        {
            var choices = await _sheet.ChoicesAsync(project.Value!, launch.Agent ?? "claude", ct).ConfigureAwait(false);
            task = choices.Declared.FirstOrDefault(one => string.Equals(one.Id, id, StringComparison.Ordinal))?.Title;
        }

        var agent = launch.Agent ?? await DefaultAgentAsync(project.Value!, ct).ConfigureAwait(false) ?? "claude";

        try
        {
            var resolved = await _sheet
                .PreviewAsync(new LaunchPreviewRequest(project.Value!, agent, launch.Profile, task, launch.Mode), ct)
                .ConfigureAwait(false);

            if (resolved is null)
            {
                return new { command = command.Value, error = (string?)null, specialists = Array.Empty<object>() };
            }

            return new
            {
                command = command.Value,
                error = (string?)null,
                mode = resolved.Mode,
                specialists = resolved.Selected.Select(one => new
                {
                    id = one.Specialist.Id,
                    kind = one.Specialist.Kind.ToString().ToLowerInvariant(),
                    reason = one.Reason,
                    estimatedTokens = one.Specialist.EstimatedTokens,
                }),
                omitted = resolved.Omitted.Select(one => new { id = one.Specialist.Id, reason = one.Reason }),
                budget = new
                {
                    estimatedTokens = resolved.Budget.EstimatedTokens,
                    tokenBudget = resolved.Budget.TokenBudget,
                    warnAtPercent = resolved.Budget.WarnAtPercent,
                },
            };
        }
        catch (InvalidOperationException ex)
        {
            return new { command = command.Value, error = ex.Message };
        }
    }

    private async Task<string?> DefaultAgentAsync(ProjectResolution project, CancellationToken ct)
    {
        var manifest = await _workspace.ReadProjectAsync(project.Entry.Slug, ct).ConfigureAwait(false);

        return manifest.Succeeded ? manifest.Value!.Agents.Default : project.Entry.DefaultAgent;
    }

    private async Task<IReadOnlyList<AgentDescriptor>> AgentsAsync(CancellationToken ct)
    {
        await _detecting.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (_detected is { } kept && _time.GetUtcNow() - kept.At < AgentsKept)
            {
                return kept.Found;
            }

            var found = await _agents.DetectAllAsync(ct).ConfigureAwait(false);
            _detected = (_time.GetUtcNow(), found);

            return found;
        }
        finally
        {
            _detecting.Release();
        }
    }
}
