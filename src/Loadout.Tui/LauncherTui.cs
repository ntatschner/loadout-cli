using Loadout.Agents;
using Loadout.Core.Configuration;
using Loadout.Core.Context;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Projects;
using Loadout.Platform.Abstractions;
using Spectre.Console;

namespace Loadout.Tui;

/// <summary>The interactive launcher shown when loadout is run with no arguments.</summary>
public interface ILauncherTui
{
    Task<int> RunAsync(CancellationToken ct = default);
}

/// <summary>
/// Keyboard-driven project selector (spec sections 21, 22 and 23).
/// <para>
/// Built on plain prompts rather than a full-screen widget toolkit so it
/// behaves correctly over SSH, in a narrow terminal and on a machine with no
/// desktop session, which spec section 21 requires and section 86 depends on.
/// There is no mouse interaction anywhere.
/// </para>
/// </summary>
public sealed class LauncherTui : ILauncherTui
{
    private readonly IAnsiConsole _console;
    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly IConfigurationService _configuration;
    private readonly IAgentRegistry _agents;
    private readonly IAgentLauncher _launcher;
    private readonly IShellProvider _shells;
    private readonly IProcessLauncher _processes;
    private readonly IContextCompiler _compiler;

    public LauncherTui(
        IAnsiConsole console,
        IProjectService projects,
        IWorkspaceManager workspace,
        IConfigurationService configuration,
        IAgentRegistry agents,
        IAgentLauncher launcher,
        IShellProvider shells,
        IProcessLauncher processes,
        IContextCompiler compiler)
    {
        _console = console;
        _projects = projects;
        _workspace = workspace;
        _configuration = configuration;
        _agents = agents;
        _launcher = launcher;
        _shells = shells;
        _processes = processes;
        _compiler = compiler;
    }

    /// <inheritdoc />
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var configResult = await _configuration.LoadConfigAsync(ct).ConfigureAwait(false);
        if (configResult.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(configResult.Error!)}[/]");
            return (int)configResult.ExitCode;
        }

        var config = configResult.Value!;

        await ShowHeaderAsync(config, ct).ConfigureAwait(false);

        var projectsResult = await _projects.ListAsync(ct).ConfigureAwait(false);
        if (projectsResult.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(projectsResult.Error!)}[/]");
            return (int)projectsResult.ExitCode;
        }

        var projects = projectsResult.Value!;

        if (projects.Count == 0)
        {
            _console.MarkupLine("[yellow]No projects are registered yet.[/]");
            _console.MarkupLine("[dim]Register one with:[/] loadout project add <path>");
            _console.MarkupLine("[dim]Or find existing repositories with:[/] loadout project discover");
            return (int)ExitCode.Success;
        }

        var selected = SelectProject(projects);
        if (selected is null)
        {
            return (int)ExitCode.Success;
        }

        return await ShowProjectMenuAsync(selected, config, ct).ConfigureAwait(false);
    }

    private async Task ShowHeaderAsync(Models.Configuration.LauncherConfig config, CancellationToken ct)
    {
        _console.Write(new Rule("[bold]Loadout[/]").LeftJustified());

        var workspaceLine = !_workspace.IsConfigured(config)
            ? "[dim]Workspace: not configured (local state only)[/]"
            : _workspace.IsCloned()
                ? "[green]Workspace: connected[/]"
                : "[yellow]Workspace: configured, not cloned[/]";

        _console.MarkupLine(workspaceLine);

        var agents = await _agents.DetectAllAsync(ct).ConfigureAwait(false);

        foreach (var agent in agents)
        {
            _console.MarkupLine(agent.IsInstalled
                ? $"[green]+[/] {Markup.Escape(agent.DisplayName)}"
                : $"[dim]-[/] [dim]{Markup.Escape(agent.DisplayName)} not installed[/]");
        }

        _console.WriteLine();
    }

    /// <summary>
    /// Shows the recent-projects list. Ordering comes from the service, which
    /// puts pinned first, then most recent, then most frequent
    /// (spec section 23).
    /// </summary>
    private ProjectResolution? SelectProject(IReadOnlyList<ProjectResolution> projects)
    {
        const string Cancel = "Quit";

        var choices = projects.Select(FormatProject).ToList();
        choices.Add(Cancel);

        var prompt = new SelectionPrompt<string>()
            .Title("[bold]Recent projects[/]")
            .PageSize(15)
            .MoreChoicesText("[dim](move up and down for more)[/]")
            .AddChoices(choices);

        // Search-as-you-type is what makes the list usable once a person has
        // more than a screenful of projects (spec section 23).
        prompt.SearchEnabled = true;

        var answer = _console.Prompt(prompt);

        if (answer == Cancel)
        {
            return null;
        }

        return projects[choices.IndexOf(answer)];
    }

    private static string FormatProject(ProjectResolution project)
    {
        var pin = project.Pinned ? "[yellow]*[/] " : "  ";

        var suffix = project.IsAvailableLocally
            ? $"[dim]{Markup.Escape(project.Entry.DefaultAgent)}[/]"
            : "[yellow]not on this machine[/]";

        return $"{pin}{Markup.Escape(project.Entry.Name)}  {suffix}";
    }

    private async Task<int> ShowProjectMenuAsync(
        ProjectResolution project,
        Models.Configuration.LauncherConfig config,
        CancellationToken ct)
    {
        _console.WriteLine();
        _console.Write(new Rule($"[bold]{Markup.Escape(project.Entry.Name)}[/]").LeftJustified());

        if (project.LocalPath is null)
        {
            // Spec section 28: this is an offer to fix, not a dead end. The
            // clone flow itself arrives with the project wizard.
            _console.MarkupLine("[yellow]This repository is not available on this machine.[/]");
            _console.MarkupLine(
                $"[dim]Clone it with:[/] loadout project clone {Markup.Escape(project.Entry.Slug)}");

            return (int)ExitCode.RepositoryUnavailable;
        }

        _console.MarkupLine($"[dim]{Markup.Escape(project.LocalPath)}[/]");
        _console.WriteLine();

        var defaultAgent = string.IsNullOrWhiteSpace(project.Entry.DefaultAgent)
            ? config.DefaultAgent
            : project.Entry.DefaultAgent;

        var actions = new List<string> { $"Launch {defaultAgent}" };

        actions.AddRange(_agents.Adapters
            .Where(a => !string.Equals(a.Name, defaultAgent, StringComparison.OrdinalIgnoreCase))
            .Select(a => $"Launch {a.Name}"));

        actions.Add("Open development shell");
        actions.Add("Back");

        var choice = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .AddChoices(actions));

        if (choice == "Back")
        {
            return (int)ExitCode.Success;
        }

        if (choice == "Open development shell")
        {
            return await OpenShellAsync(project.LocalPath, ct).ConfigureAwait(false);
        }

        var agentName = choice["Launch ".Length..];
        var profile = await ChooseProfileAsync(project.Entry.Slug, agentName, ct).ConfigureAwait(false);

        var result = await _launcher.LaunchAsync(
            new LaunchRequest(project.Entry.Slug, agentName, Profile: profile),
            ct).ConfigureAwait(false);

        if (result.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(result.Error!)}[/]");
            return (int)result.ExitCode;
        }

        foreach (var warning in result.Value!.Warnings)
        {
            _console.MarkupLine($"[yellow]warning[/] {Markup.Escape(warning)}");
        }

        return result.Value.AgentExitCode;
    }

    /// <summary>
    /// Asks what the session is for, so only the relevant context is loaded
    /// (spec section 34).
    /// <para>
    /// The question is skipped entirely when a project defines no profiles.
    /// Presenting a one-item menu would add a keystroke to every launch and
    /// teach people to press Enter without reading it.
    /// </para>
    /// </summary>
    private async Task<string?> ChooseProfileAsync(string slug, string agentName, CancellationToken ct)
    {
        var manifestResult = await _workspace.ReadProjectAsync(slug, ct).ConfigureAwait(false);

        if (manifestResult.Failed)
        {
            return null;
        }

        var profiles = _compiler.ListProfiles(manifestResult.Value!, agentName);

        if (profiles.Count <= 1)
        {
            return null;
        }

        var labels = profiles
            .Select(name => manifestResult.Value!.Profiles.TryGetValue(name, out var profile)
                && !string.IsNullOrWhiteSpace(profile.Description)
                    ? $"{name}  ({profile.Description})"
                    : name)
            .ToList();

        var chosen = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("What are you working on?")
                .AddChoices(labels));

        return profiles[labels.IndexOf(chosen)];
    }

    private async Task<int> OpenShellAsync(string workingDirectory, CancellationToken ct)
    {
        var shellResult = _shells.GetInteractiveShellPath();

        if (shellResult.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(shellResult.Error!)}[/]");
            return (int)ExitCode.GeneralFailure;
        }

        var result = await _processes.RunInteractiveAsync(
            new ProcessRequest(shellResult.Value!, [], workingDirectory),
            ct).ConfigureAwait(false);

        return result.Succeeded ? result.Value : (int)result.ExitCode;
    }
}
