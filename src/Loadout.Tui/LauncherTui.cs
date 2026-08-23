using Loadout.Agents;
using Loadout.Core.Configuration;
using Loadout.Core.Context;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Configuration;
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
/// <para>
/// It returns to where you came from rather than exiting. Backing out of a
/// project used to quit the launcher entirely, which made browsing impossible:
/// the only way to look at a second project was to start again.
/// </para>
/// </summary>
public sealed class LauncherTui : ILauncherTui
{
    private const string Quit = "Quit";
    private const string Back = "Back";
    private const string Settings = "Settings and paths";

    /// <summary>
    /// Stands in for the settings entry so the list can hold one thing that is
    /// not a project without the selection returning two kinds of answer.
    /// </summary>
    private static readonly ProjectResolution SettingsSentinel =
        new(new Models.Projects.ProjectRegistryEntry(), null, null, 0, false);

    private readonly IAnsiConsole _console;
    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly IConfigurationService _configuration;
    private readonly IAgentRegistry _agents;
    private readonly IAgentLauncher _launcher;
    private readonly IShellProvider _shells;
    private readonly IProcessLauncher _processes;
    private readonly IContextCompiler _compiler;
    private readonly IProjectOverviewService _overviews;
    private readonly IApplicationLauncher _opener;
    private readonly IPlatformPaths _paths;

    public LauncherTui(
        IAnsiConsole console,
        IProjectService projects,
        IWorkspaceManager workspace,
        IConfigurationService configuration,
        IAgentRegistry agents,
        IAgentLauncher launcher,
        IShellProvider shells,
        IProcessLauncher processes,
        IContextCompiler compiler,
        IProjectOverviewService overviews,
        IApplicationLauncher opener,
        IPlatformPaths paths)
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
        _overviews = overviews;
        _opener = opener;
        _paths = paths;
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

        // The repository you are standing in is almost always the one you
        // meant, so it is offered first rather than left to be hunted for in a
        // list sorted by something else.
        var here = await ResolveCurrentAsync(ct).ConfigureAwait(false);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

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
                _console.MarkupLine(
                    "[dim]Or find existing repositories with:[/] loadout project discover");

                return (int)ExitCode.Success;
            }

            var selected = SelectProject(projects, here);

            if (selected is null)
            {
                return (int)ExitCode.Success;
            }

            if (ReferenceEquals(selected, SettingsSentinel))
            {
                ShowSettings(config);

                continue;
            }

            var outcome = await ShowProjectAsync(selected, config, ct).ConfigureAwait(false);

            // A launch ends the session: the agent has had the terminal and its
            // exit code is the launcher's. Anything else returns to the list.
            if (outcome is not null)
            {
                return outcome.Value;
            }

            _console.WriteLine();
        }
    }

    /// <summary>
    /// Shows where configuration lives and what it currently says.
    /// <para>
    /// Here because the question "where is any of this kept" had no answer
    /// inside the launcher at all: the paths were only visible by running
    /// doctor and reading carefully, and the settings only by knowing that a
    /// config command existed. A tool that hides its own configuration is
    /// asking people to take it on faith.
    /// </para>
    /// <para>
    /// Read-only. Changing a setting from a menu means validating input in a
    /// prompt, and the command that already does that is named at the bottom.
    /// </para>
    /// </summary>
    private void ShowSettings(LauncherConfig config)
    {
        _console.WriteLine();
        _console.Write(new Rule("[bold]Settings[/]").LeftJustified());

        var table = new Table().Border(TableBorder.None).HideHeaders();
        table.AddColumn(new TableColumn(string.Empty).PadRight(2));
        table.AddColumn(string.Empty);

        table.AddRow("[dim]Workspace remote[/]", string.IsNullOrWhiteSpace(config.Workspace.Remote)
            ? "[yellow]not set[/]"
            : Markup.Escape(config.Workspace.Remote));

        table.AddRow("[dim]Workspace branch[/]", Markup.Escape(config.Workspace.Branch));
        table.AddRow("[dim]Local clone[/]", Markup.Escape(_workspace.LocalPath));
        table.AddRow("[dim]Default agent[/]", Markup.Escape(config.DefaultAgent));
        table.AddRow("[dim]Sync at launch[/]", Markup.Escape(config.Sync.Launch));
        table.AddRow("[dim]Sync at exit[/]", Markup.Escape(config.Sync.Exit));
        table.AddRow("[dim]Secrets[/]", Markup.Escape(config.Secrets.Provider));

        table.AddRow(
            "[dim]Shared file[/]",
            Markup.Escape(Path.Combine(_paths.Paths.Config, "config.yaml")));

        table.AddRow(
            "[dim]Machine file[/]",
            Markup.Escape(Path.Combine(_paths.Paths.State, "machines.yaml")));

        _console.Write(table);

        _console.WriteLine();
        _console.MarkupLine("[dim]See everything:[/]  loadout config list");
        _console.MarkupLine("[dim]What one means:[/]  loadout config get <setting> --explain");
        _console.MarkupLine("[dim]Change one:[/]      loadout config set <setting> <value>");
        _console.MarkupLine("[dim]Edit the file:[/]   loadout config edit");
        _console.WriteLine();
    }

    /// <summary>
    /// The project owning the current directory, or null when there is none.
    /// A failure here is not worth reporting: it means "you are not in a
    /// registered repository", which is the ordinary case.
    /// </summary>
    private async Task<ProjectResolution?> ResolveCurrentAsync(CancellationToken ct)
    {
        var resolved = await _projects
            .ResolveFromDirectoryAsync(Directory.GetCurrentDirectory(), ct)
            .ConfigureAwait(false);

        return resolved.Succeeded ? resolved.Value : null;
    }

    private async Task ShowHeaderAsync(LauncherConfig config, CancellationToken ct)
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
    /// Shows the project list. Ordering comes from the service, which puts
    /// pinned first, then most recent, then most frequent (spec section 23),
    /// except that the current repository is lifted to the top.
    /// </summary>
    private ProjectResolution? SelectProject(
        IReadOnlyList<ProjectResolution> projects,
        ProjectResolution? here)
    {
        var ordered = Order(projects, here);

        var choices = ordered
            .Select(project => FormatProject(project, here))
            .ToList();

        choices.Add(Settings);
        choices.Add(Quit);

        var prompt = new SelectionPrompt<string>()
            .Title("[bold]Projects[/]")
            .PageSize(15)
            .MoreChoicesText("[dim](move up and down for more)[/]")
            .AddChoices(choices);

        // Search-as-you-type is what makes the list usable once a person has
        // more than a screenful of projects (spec section 23).
        prompt.SearchEnabled = true;

        var answer = _console.Prompt(prompt);

        if (answer == Quit)
        {
            return null;
        }

        return answer == Settings ? SettingsSentinel : ordered[choices.IndexOf(answer)];
    }

    internal static List<ProjectResolution> Order(
        IReadOnlyList<ProjectResolution> projects,
        ProjectResolution? here)
    {
        if (here is null)
        {
            return projects.ToList();
        }

        return projects
            .Where(project => project.Entry.Slug == here.Entry.Slug)
            .Concat(projects.Where(project => project.Entry.Slug != here.Entry.Slug))
            .ToList();
    }

    private static string FormatProject(ProjectResolution project, ProjectResolution? here)
    {
        var marker = here is not null && project.Entry.Slug == here.Entry.Slug
            ? "[green]>[/] "
            : project.Pinned ? "[yellow]*[/] " : "  ";

        var suffix = project.IsAvailableLocally
            ? $"[dim]{Markup.Escape(project.Entry.DefaultAgent)}[/]"
            : "[yellow]not on this machine[/]";

        return $"{marker}{Markup.Escape(project.Entry.Name)}  {suffix}";
    }

    /// <summary>
    /// Shows one project and what can be done with it. Returns an exit code when
    /// the session is over, and null to go back to the list.
    /// </summary>
    private async Task<int?> ShowProjectAsync(
        ProjectResolution project,
        LauncherConfig config,
        CancellationToken ct)
    {
        _console.WriteLine();
        _console.Write(new Rule($"[bold]{Markup.Escape(project.Entry.Name)}[/]").LeftJustified());

        if (project.LocalPath is null)
        {
            return await OfferCloneAsync(project, ct).ConfigureAwait(false);
        }

        var described = await _overviews.DescribeAsync(project, ct).ConfigureAwait(false);

        if (described.Succeeded)
        {
            WriteOverview(described.Value!);
        }
        else
        {
            _console.MarkupLine($"[dim]{Markup.Escape(project.LocalPath)}[/]");
        }

        _console.WriteLine();

        return await ChooseActionAsync(project, described.Value, config, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Renders what the session is about to start with.
    /// <para>
    /// The point is that the cost of a launch and anything wrong with it are
    /// visible before it happens, not discoverable afterwards from a separate
    /// command somebody has to know exists.
    /// </para>
    /// </summary>
    private void WriteOverview(ProjectOverview overview)
    {
        _console.MarkupLine($"[dim]{Markup.Escape(overview.Project.LocalPath!)}[/]");

        var branch = overview.Branch is null
            ? "[dim]detached[/]"
            : Markup.Escape(overview.Branch);

        var state = overview.IsClean ? "[dim]clean[/]" : "[yellow]uncommitted changes[/]";

        _console.MarkupLine($"  {branch}  {state}");

        var budget = overview.IsOverBudget
            ? $"[yellow]{FormatBytes(overview.AlwaysLoadedBytes)}[/]"
            : $"[green]{FormatBytes(overview.AlwaysLoadedBytes)}[/]";

        var scoped = overview.ScopedRules == 1 ? "1 scoped rule" : $"{overview.ScopedRules} scoped rules";

        _console.MarkupLine(
            $"  {budget} loaded every session  [dim]plus {scoped} on demand[/]");

        if (overview.MemoryTopics > 0)
        {
            _console.MarkupLine(
                $"  [dim]{overview.MemoryTopics} memory topic(s)[/]");
        }

        foreach (var warning in Warnings(overview))
        {
            _console.MarkupLine($"  [yellow]![/] {warning}");
        }
    }

    /// <summary>
    /// Things worth saying before a launch, in the order they matter.
    /// </summary>
    internal static IEnumerable<string> Warnings(ProjectOverview overview)
    {
        if (overview.TrackedAgentFiles > 0)
        {
            yield return
                $"{overview.TrackedAgentFiles} agent file(s) are committed to this repository";
        }

        if (overview.PendingImports > 0)
        {
            yield return
                $"{overview.PendingImports} memory topic(s) recorded outside the workspace";
        }

        if (overview.IsOverBudget)
        {
            yield return "the always-loaded instructions are larger than they need to be";
        }

        if (!overview.Protected)
        {
            yield return "no pre-commit protection in this clone";
        }
    }

    private async Task<int?> ChooseActionAsync(
        ProjectResolution project,
        ProjectOverview? overview,
        LauncherConfig config,
        CancellationToken ct)
    {
        var defaultAgent = string.IsNullOrWhiteSpace(project.Entry.DefaultAgent)
            ? config.DefaultAgent
            : project.Entry.DefaultAgent;

        var actions = new List<string> { $"Launch {defaultAgent}" };

        actions.AddRange(_agents.Adapters
            .Where(a => !string.Equals(a.Name, defaultAgent, StringComparison.OrdinalIgnoreCase))
            .Select(a => $"Launch {a.Name}"));

        actions.Add("Open development shell");
        actions.Add("Open in file manager");

        if (overview?.HasWarnings == true)
        {
            actions.Add("Explain the warnings");
        }

        actions.Add(Back);

        var choice = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .AddChoices(actions));

        if (choice == Back)
        {
            return null;
        }

        if (choice == "Explain the warnings")
        {
            ExplainWarnings(project, overview!);

            return await ChooseActionAsync(project, overview, config, ct).ConfigureAwait(false);
        }

        if (choice == "Open in file manager")
        {
            await _opener.OpenInFileManagerAsync(project.LocalPath!, ct).ConfigureAwait(false);

            return null;
        }

        if (choice == "Open development shell")
        {
            return await OpenShellAsync(project.LocalPath!, ct).ConfigureAwait(false);
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
    /// Says what each warning means and which command fixes it.
    /// <para>
    /// A warning nobody can act on is only noise. The commands are printed
    /// rather than run, because each of them changes files and the launcher is
    /// not the place to agree to that in passing.
    /// </para>
    /// </summary>
    private void ExplainWarnings(ProjectResolution project, ProjectOverview overview)
    {
        var slug = Markup.Escape(project.Entry.Slug);

        _console.WriteLine();

        if (overview.TrackedAgentFiles > 0)
        {
            _console.MarkupLine(
                "[yellow]Agent files are committed to this repository.[/] "
                + "[dim]They belong in the workspace, not in the application's history:[/]");

            _console.MarkupLine($"  loadout migrate {slug}");
        }

        if (overview.PendingImports > 0)
        {
            _console.MarkupLine(
                "[yellow]An agent recorded memory outside the workspace.[/] "
                + "[dim]Nothing else reads it there:[/]");

            _console.MarkupLine($"  loadout memory import {slug}");
        }

        if (overview.IsOverBudget)
        {
            _console.MarkupLine(
                $"[yellow]{FormatBytes(overview.AlwaysLoadedBytes)} loads on every session[/] "
                + "[dim]whatever the task. See what it is, and what could be scoped:[/]");

            _console.MarkupLine($"  loadout rules budget {slug}");
            _console.MarkupLine($"  loadout rules split {slug} --write-map");
        }

        if (!overview.Protected)
        {
            _console.MarkupLine(
                "[yellow]This clone has no pre-commit protection.[/] "
                + "[dim]Hooks are per-clone, so a fresh clone never has one:[/]");

            _console.MarkupLine("  loadout protect");
        }

        _console.WriteLine();
    }

    /// <summary>
    /// Offers to fetch a project that is registered but not here.
    /// <para>
    /// Spec section 28 calls this an offer to fix rather than a dead end, and
    /// printing the command somebody should type instead of asking them is a
    /// dead end with extra steps.
    /// </para>
    /// </summary>
    private async Task<int?> OfferCloneAsync(ProjectResolution project, CancellationToken ct)
    {
        _console.MarkupLine("[yellow]This repository is not on this machine.[/]");
        _console.MarkupLine($"[dim]{Markup.Escape(project.Entry.Remote)}[/]");
        _console.WriteLine();

        if (!_console.Confirm("Clone it now?", defaultValue: false))
        {
            return null;
        }

        var result = await _projects.CloneAsync(project.Entry.Slug, null, ct).ConfigureAwait(false);

        if (result.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(result.Error!)}[/]");

            return null;
        }

        _console.MarkupLine(
            $"[green]Cloned[/] to {Markup.Escape(result.Value!.LocalPath ?? string.Empty)}");

        return null;
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

    private async Task<int?> OpenShellAsync(string workingDirectory, CancellationToken ct)
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

    internal static string FormatBytes(long bytes) => bytes < 1024
        ? bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + "B"
        : (bytes / 1024.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "KB";
}
