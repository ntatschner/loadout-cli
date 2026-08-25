using System.ComponentModel;
using Loadout.Agents;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Git;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>Summarises workspace, projects, agents and the current repository (spec section 78).</summary>
[Description("Summarise the workspace, projects, agents and current repository.")]
[CommandMeta(CommandCategory.Health, Intent = "state summary overview what is set up")]
public sealed class StatusCommand : AsyncCommand<GlobalSettings>
{
    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly IConfigurationService _configuration;
    private readonly IAgentRegistry _agents;
    private readonly IGitManager _git;
    private readonly IAnsiConsole _console;

    public StatusCommand(
        IProjectService projects,
        IWorkspaceManager workspace,
        IConfigurationService configuration,
        IAgentRegistry agents,
        IGitManager git,
        IAnsiConsole console)
    {
        _projects = projects;
        _workspace = workspace;
        _configuration = configuration;
        _agents = agents;
        _git = git;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings)
    {
        var output = new CommandOutput(_console, settings);

        var configResult = await _configuration.LoadConfigAsync().ConfigureAwait(false);
        if (configResult.Failed)
        {
            return output.Fail(configResult);
        }

        var config = configResult.Value!;

        var projectsResult = await _projects.ListAsync().ConfigureAwait(false);
        if (projectsResult.Failed)
        {
            return output.Fail(projectsResult);
        }

        var projects = projectsResult.Value!;
        var available = projects.Count(p => p.IsAvailableLocally);

        var agents = await _agents.DetectAllAsync().ConfigureAwait(false);

        // The current repository is contextual information, so failing to find
        // one is normal: the user may be anywhere.
        var currentResult = await _projects
            .ResolveFromDirectoryAsync(settings.Repo ?? Directory.GetCurrentDirectory())
            .ConfigureAwait(false);

        var current = currentResult.Succeeded ? currentResult.Value : null;

        GitRepositoryState? currentState = null;
        if (current?.LocalPath is not null)
        {
            var stateResult = await _git.GetStateAsync(current.LocalPath).ConfigureAwait(false);
            currentState = stateResult.Value;
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                workspace = new
                {
                    configured = _workspace.IsConfigured(config),
                    cloned = _workspace.IsCloned(),
                },
                projects = new
                {
                    registered = projects.Count,
                    availableLocally = available,
                    remoteOnly = projects.Count - available,
                },
                agents = agents.Select(a => new
                {
                    name = a.Name,
                    installed = a.IsInstalled,
                    version = a.Version,
                }),
                currentRepository = current is null ? null : new
                {
                    id = current.Entry.Slug,
                    name = current.Entry.Name,
                    branch = currentState?.Branch,
                    clean = currentState?.IsClean,
                },
                defaultAgent = config.DefaultAgent,
            });

            return CommandOutput.Success();
        }

        output.WriteLine("[bold]Workspace[/]");
        output.WriteLine(_workspace.IsConfigured(config)
            ? _workspace.IsCloned()
                ? "[green]+[/] cloned"
                : "[yellow]![/] configured but not cloned"
            : "[dim]-[/] not configured (local state only)");

        output.WriteBlankLine();
        output.WriteLine("[bold]Projects[/]");
        output.WriteLine($"{projects.Count} registered");
        output.WriteLine($"{available} available locally");
        output.WriteLine($"{projects.Count - available} remote only");

        output.WriteBlankLine();
        output.WriteLine("[bold]Agents[/]");
        foreach (var agent in agents)
        {
            output.WriteLine(agent.IsInstalled
                ? $"[green]+[/] {Markup.Escape(agent.DisplayName)}  [dim]{Markup.Escape(agent.Version ?? string.Empty)}[/]"
                : $"[yellow]-[/] {Markup.Escape(agent.DisplayName)}  [dim]not installed[/]");
        }

        if (current is not null)
        {
            output.WriteBlankLine();
            output.WriteLine("[bold]Current repository[/]");
            output.WriteLine(Markup.Escape(current.Entry.Name));

            if (currentState is not null)
            {
                output.WriteLine(Markup.Escape(currentState.Branch ?? "detached HEAD"));
                output.WriteLine(currentState.IsClean ? "clean" : "[yellow]uncommitted changes[/]");
            }
        }

        output.WriteBlankLine();
        output.WriteLine($"[bold]Default agent[/]  {Markup.Escape(config.DefaultAgent)}");

        return CommandOutput.Success();
    }
}
