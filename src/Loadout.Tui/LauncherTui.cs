using Loadout.Agents;
using Loadout.Core.Configuration;
using Loadout.Core.Context;
using Loadout.Core.Projects;
using Loadout.Core.Diagnostics;
using Loadout.Core.Sessions;
using Loadout.Models.Diagnostics;
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
    internal const string Back = "Back";

    /// <summary>The menu entry that reopens a previous conversation.</summary>
    internal const string ResumeEntry = "Resume a session";

    /// <summary>The menu entry that reviews what is wrong and offers to fix it.</summary>
    internal const string ProblemsEntry = "Review problems";

    /// <summary>The settings entry that checks the machine rather than a project.</summary>
    private const string CheckMachineEntry = "Check this machine";

    /// <summary>
    /// The entry that reaches everything else. The grouped menus carry what is
    /// done often; this carries the rest, so the launcher is never a subset of
    /// the command line.
    /// </summary>
    internal const string AllCommandsEntry = "All commands…";
    private const string Settings = "Settings and paths";
    private const string AddProject = "Add a project";

    /// <summary>
    /// Stands in for the settings entry so the list can hold one thing that is
    /// not a project without the selection returning two kinds of answer.
    /// </summary>
    private static readonly ProjectResolution SettingsSentinel =
        new(new Models.Projects.ProjectRegistryEntry(), null, null, 0, false);

    private static readonly ProjectResolution AddSentinel =
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
    private readonly IProjectOnboarding _onboarding;
    private readonly ISessionHistoryService _sessions;
    private readonly IDriftService _drift;
    private readonly IDoctorService _doctor;
    private readonly TuiScreen _screen;
    private readonly CommandPalette _palette;
    private readonly IRemediationService _remediation;

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
        IPlatformPaths paths,
        IProjectOnboarding onboarding,
        ISessionHistoryService sessions,
        ICommandCatalogue catalogue,
        IDriftService drift,
        IDoctorService doctor,
        IRemediationService remediation)
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
        _onboarding = onboarding;
        _sessions = sessions;
        _drift = drift;
        _doctor = doctor;
        _remediation = remediation;
        _screen = new TuiScreen(console);
        _palette = new CommandPalette(console, catalogue, _screen);
    }

    /// <inheritdoc />
    public Task<int> RunAsync(CancellationToken ct = default) =>
        // The whole session runs in the alternate screen where the terminal has
        // one, so the launcher draws over its own area and gives the scrollback
        // back untouched when it exits. A launcher that scrolls somebody's
        // history away to show a menu has taken something it cannot return.
        _screen.RunAsync(() => RunSessionAsync(ct));

    private async Task<int> RunSessionAsync(CancellationToken ct)
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
                // Telling somebody the command to run is not the same as
                // helping them: the launcher is already open, already knows
                // where to look, and can simply ask.
                _console.MarkupLine("[yellow]No projects are registered yet.[/]");
                _console.WriteLine();

                await AddProjectsAsync(ct).ConfigureAwait(false);

                var added = await _projects.ListAsync(ct).ConfigureAwait(false);

                if (added.Failed || added.Value!.Count == 0)
                {
                    return (int)ExitCode.Success;
                }

                continue;
            }

            var selected = SelectProject(projects, here);

            if (selected is null)
            {
                return (int)ExitCode.Success;
            }

            if (ReferenceEquals(selected, SettingsSentinel))
            {
                var reloaded = await ShowSettingsAsync(config, ct).ConfigureAwait(false);

                if (reloaded is not null)
                {
                    config = reloaded;
                }

                continue;
            }

            if (ReferenceEquals(selected, AddSentinel))
            {
                await AddProjectsAsync(ct).ConfigureAwait(false);

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
    /// Brings a repository in, either by scanning the configured folders or by
    /// being told where one is.
    /// </summary>
    private async Task AddProjectsAsync(CancellationToken ct)
    {
        _screen.Begin("Add a project");

        var how = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("Where should it look?")
                .PageSize(_screen.PageSize)
                .AddChoices("Scan the folders I have configured", "I will give it a path", Back));

        if (how == Back)
        {
            return;
        }

        if (how.StartsWith("Scan", StringComparison.Ordinal))
        {
            await _onboarding.AddAsync(new OnboardingOptions(), ct).ConfigureAwait(false);

            return;
        }

        var path = _console.Prompt(
            new TextPrompt<string>("Path to the repository:").AllowEmpty());

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await _onboarding.AddPathAsync(path, new OnboardingOptions(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Shows where configuration lives, what it says, and lets the answers be
    /// changed.
    /// <para>
    /// Here because the question "where is any of this kept, and how do I point
    /// it somewhere else" had no answer inside the launcher: the paths were
    /// visible only by running doctor and reading carefully, and the settings
    /// only by knowing a config command existed. A tool that hides its own
    /// configuration is asking to be taken on faith.
    /// </para>
    /// <para>
    /// Returns the reloaded configuration when something changed, so the rest
    /// of the session is not working from a stale copy.
    /// </para>
    /// </summary>
    private async Task<LauncherConfig?> ShowSettingsAsync(LauncherConfig config, CancellationToken ct)
    {
        var changed = false;

        while (true)
        {
            _screen.Begin("Settings");

            WriteSettings(config);

            _console.WriteLine();

            var choice = _console.Prompt(
                new SelectionPrompt<string>()
                    .Title("Change anything?")
                .PageSize(_screen.PageSize)
                    .AddChoices(
                        "Central workspace repository",
                        "Where new clones are placed",
                        "Folders scanned for repositories",
                        "Default agent",
                        "Open the config file",
                        CheckMachineEntry,
                        Back));

            if (choice == Back)
            {
                return changed ? config : null;
            }

            if (choice == CheckMachineEntry)
            {
                await CheckMachineAsync(ct).ConfigureAwait(false);

                continue;
            }

            if (choice == "Open the config file")
            {
                await _opener
                    .OpenInFileManagerAsync(_paths.Paths.Config, ct)
                    .ConfigureAwait(false);

                continue;
            }

            var edited = choice switch
            {
                "Central workspace repository" => await ChangeWorkspaceAsync(config, ct)
                    .ConfigureAwait(false),

                "Where new clones are placed" => await ChangeMachineValueAsync(
                    "clone-root",
                    "Where should new clones go?",
                    ct).ConfigureAwait(false),

                "Folders scanned for repositories" => await ChangeMachineValueAsync(
                    "discovery-roots",
                    "Which folders should be scanned? [dim](comma separated)[/]",
                    ct).ConfigureAwait(false),

                _ => await ChangeAgentAsync(config, ct).ConfigureAwait(false),
            };

            if (!edited)
            {
                continue;
            }

            changed = true;

            var reloaded = await _configuration.LoadConfigAsync(ct).ConfigureAwait(false);

            if (reloaded.Succeeded)
            {
                config = reloaded.Value!;
            }
        }
    }

    private void WriteSettings(LauncherConfig config)
    {
        var table = new Table().Border(TableBorder.None).HideHeaders();
        table.AddColumn(new TableColumn(string.Empty).PadRight(2));
        table.AddColumn(string.Empty);

        table.AddRow("[dim]Workspace repository[/]", string.IsNullOrWhiteSpace(config.Workspace.Remote)
            ? "[yellow]not set[/]"
            : Markup.Escape(config.Workspace.Remote));

        table.AddRow("[dim]Branch[/]", Markup.Escape(config.Workspace.Branch));

        table.AddRow("[dim]Local clone[/]", _workspace.IsCloned()
            ? Markup.Escape(_workspace.LocalPath)
            : $"[yellow]not cloned[/]  [dim]{Markup.Escape(_workspace.LocalPath)}[/]");

        table.AddRow("[dim]Default agent[/]", Markup.Escape(config.DefaultAgent));
        table.AddRow("[dim]Sync at launch[/]", Markup.Escape(config.Sync.Launch));
        table.AddRow("[dim]Sync at exit[/]", Markup.Escape(config.Sync.Exit));
        table.AddRow("[dim]Secrets[/]", Markup.Escape(config.Secrets.Provider));

        table.AddRow(
            "[dim]Shared settings[/]",
            Markup.Escape(Path.Combine(_paths.Paths.Config, "config.yaml")));

        table.AddRow(
            "[dim]This machine[/]",
            Markup.Escape(Path.Combine(_paths.Paths.State, "machines.yaml")));

        _console.Write(table);
    }

    /// <summary>
    /// Points the launcher at a different central workspace.
    /// <para>
    /// The dangerous half is the clone that already exists. Changing the remote
    /// leaves a directory full of another repository's projects, and the next
    /// sync would either fail or, worse, appear to work against the wrong
    /// history. The old clone is therefore moved aside rather than reused or
    /// deleted: nothing is lost, and the new remote starts from nothing.
    /// </para>
    /// </summary>
    private async Task<bool> ChangeWorkspaceAsync(LauncherConfig config, CancellationToken ct)
    {
        var current = config.Workspace.Remote;

        _console.WriteLine();
        _console.MarkupLine(
            "[dim]The private Git repository holding your projects, instructions and memory.[/]");

        var remote = _console.Prompt(
            new TextPrompt<string>("Repository URL:")
                .DefaultValue(current ?? string.Empty)
                .AllowEmpty());

        if (string.IsNullOrWhiteSpace(remote) || remote == current)
        {
            return false;
        }

        if (_workspace.IsCloned())
        {
            _console.WriteLine();
            _console.MarkupLine(
                $"[yellow]A clone of the current workspace is already at "
                + $"{Markup.Escape(_workspace.LocalPath)}.[/]");

            _console.MarkupLine(
                "[dim]It belongs to the old repository, so it is moved aside rather than "
                + "reused. Nothing in it is deleted.[/]");

            if (!_console.Confirm("Continue?", defaultValue: false))
            {
                return false;
            }

            var moved = MoveCloneAside();

            if (moved is null)
            {
                _console.MarkupLine(
                    "[red]The existing clone could not be moved, so the remote was left "
                    + "unchanged.[/]");

                return false;
            }

            _console.MarkupLine($"[dim]Moved to {Markup.Escape(moved)}[/]");
        }

        config.Workspace.Remote = remote;

        var saved = await _configuration.SaveConfigAsync(config, ct).ConfigureAwait(false);

        if (saved.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(saved.Error!)}[/]");

            return false;
        }

        _console.MarkupLine("[green]Saved.[/] [dim]Fetch it with:[/] loadout workspace sync");

        return true;
    }

    /// <summary>
    /// Renames the existing clone out of the way, returning where it went.
    /// <para>
    /// A timestamp rather than a fixed name, so doing this twice does not
    /// overwrite the first one.
    /// </para>
    /// </summary>
    private string? MoveCloneAside()
    {
        var destination = _workspace.LocalPath + ".previous-"
            + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);

        try
        {
            Directory.Move(_workspace.LocalPath, destination);

            return destination;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<bool> ChangeAgentAsync(LauncherConfig config, CancellationToken ct)
    {
        var names = _agents.Adapters.Select(a => a.Name).ToList();

        if (names.Count == 0)
        {
            return false;
        }

        var chosen = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("Which agent should a project use when it names none?")
                .PageSize(_screen.PageSize)
                .AddChoices(names));

        if (chosen == config.DefaultAgent)
        {
            return false;
        }

        config.DefaultAgent = chosen;

        var saved = await _configuration.SaveConfigAsync(config, ct).ConfigureAwait(false);

        if (saved.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(saved.Error!)}[/]");

            return false;
        }

        return true;
    }

    /// <summary>
    /// Edits one of the machine-local settings, which describe this machine's
    /// layout and never travel to another one (spec section 15).
    /// </summary>
    private async Task<bool> ChangeMachineValueAsync(
        string key,
        string question,
        CancellationToken ct)
    {
        var machine = await _configuration.LoadMachineAsync(ct).ConfigureAwait(false);

        if (machine.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(machine.Error!)}[/]");

            return false;
        }

        var current = key == "clone-root"
            ? machine.Value!.DefaultCloneRoot
            : string.Join(", ", machine.Value!.DiscoveryRoots);

        _console.WriteLine();

        var answer = _console.Prompt(
            new TextPrompt<string>(question).DefaultValue(current ?? string.Empty).AllowEmpty());

        if (string.IsNullOrWhiteSpace(answer) || answer == current)
        {
            return false;
        }

        if (key == "clone-root")
        {
            machine.Value!.DefaultCloneRoot = answer.Trim();
        }
        else
        {
            machine.Value!.DiscoveryRoots = answer
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        var saved = await _configuration.SaveMachineAsync(machine.Value!, ct).ConfigureAwait(false);

        if (saved.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(saved.Error!)}[/]");

            return false;
        }

        _console.MarkupLine("[green]Saved.[/]");

        return true;
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
        _screen.Begin("Loadout");

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

        choices.Add(AddProject);
        choices.Add(Settings);
        choices.Add(Quit);

        var prompt = new SelectionPrompt<string>()
            .Title("[bold]Projects[/]")
                .PageSize(_screen.PageSize)
            .MoreChoicesText("[dim](move up and down for more)[/]")
            .AddChoices(choices);

        // Search-as-you-type is what makes the list usable once a person has
        // more than a screenful of projects (spec section 23).
        prompt.SearchEnabled = true;

        var answer = _console.Prompt(prompt);

        return answer switch
        {
            Quit => null,
            Settings => SettingsSentinel,
            AddProject => AddSentinel,
            _ => ordered[choices.IndexOf(answer)],
        };
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
        _screen.Begin(project.Entry.Name, project.LocalPath);

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

        var actions = ProjectActions(
            defaultAgent,
            _agents.Adapters.Select(a => a.Name),
            overview?.HasWarnings == true);

        var choice = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .PageSize(_screen.PageSize)
                .AddChoices(actions));

        if (choice == Back)
        {
            return null;
        }

        if (choice == ResumeEntry)
        {
            var resumed = await ResumeAsync(project, ct).ConfigureAwait(false);

            // Nothing chosen means back to this menu rather than out of the
            // launcher, the same as backing out of any other question.
            return resumed ?? await ChooseActionAsync(project, overview, config, ct)
                .ConfigureAwait(false);
        }

        if (choice == AllCommandsEntry)
        {
            await _palette.RunAsync(project.Entry.Slug, ct).ConfigureAwait(false);

            return await ChooseActionAsync(project, overview, config, ct).ConfigureAwait(false);
        }

        if (choice == ProblemsEntry)
        {
            await ReviewProblemsAsync(project, ct).ConfigureAwait(false);

            // Back to the same menu: the overview is re-read on the way round,
            // so anything just fixed stops being listed.
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
    /// The project menu, in order.
    /// <para>
    /// One method rather than a list built here and a copy of its order kept in
    /// the tests. That copy drifted three times in a single sitting: each entry
    /// added here silently moved every index below it, and the tests failed
    /// somewhere unrelated to the change that broke them.
    /// </para>
    /// </summary>
    /// <param name="defaultAgent">Agent offered first, because it is the usual answer.</param>
    /// <param name="agents">Every installed agent, so the rest are offered after it.</param>
    /// <param name="hasWarnings">Whether there is anything to review.</param>
    internal static List<string> ProjectActions(
        string defaultAgent,
        IEnumerable<string> agents,
        bool hasWarnings)
    {
        var actions = new List<string> { $"Launch {defaultAgent}" };

        actions.AddRange(agents
            .Where(a => !string.Equals(a, defaultAgent, StringComparison.OrdinalIgnoreCase))
            .Select(a => $"Launch {a}"));

        actions.Add(ResumeEntry);
        actions.Add("Open development shell");
        actions.Add("Open in file manager");
        actions.Add(AllCommandsEntry);

        if (hasWarnings)
        {
            actions.Add(ProblemsEntry);
        }

        actions.Add(Back);

        return actions;
    }

    /// <summary>
    /// Offers this project's previous conversations and reopens the chosen one.
    /// <para>
    /// Both agents can already resume, but only from inside themselves and only
    /// by identifier. Doing it here means the choice is made against titles and
    /// times, and the session comes back with its project — synchronised
    /// workspace, recompiled context — rather than just its transcript.
    /// </para>
    /// </summary>
    private async Task<int?> ResumeAsync(ProjectResolution project, CancellationToken ct)
    {
        var listed = await _sessions
            .ListAsync(new SessionQuery(project.Entry.Slug, Limit: 15), ct)
            .ConfigureAwait(false);

        if (listed.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(listed.Error!)}[/]");

            return null;
        }

        var sessions = listed.Value!;

        if (sessions.Count == 0)
        {
            _console.MarkupLine(
                $"[dim]No recorded sessions for {Markup.Escape(project.Entry.Slug)} yet.[/]");
            _console.WriteLine();

            return null;
        }

        var width = Math.Max(40, _console.Profile.Width - 4);

        var choices = sessions
            .Select(s => SessionDisplay.Line(s, width))
            .ToList();

        choices.Add(Back);

        var chosen = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("Which session?")
                .PageSize(_screen.PageSize)
                .AddChoices(choices));

        if (chosen == Back)
        {
            return null;
        }

        // Matched by position: the rendered line is padded and truncated, so it
        // is not a reliable key to look the session back up by.
        var session = sessions[choices.IndexOf(chosen)];

        var result = await _launcher.LaunchAsync(
            new LaunchRequest(
                project.Entry.Slug,
                session.Agent,
                ResumeSessionId: session.SessionId),
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
    /// Runs the same checks as the doctor and offers to put right what it can.
    /// <para>
    /// The project screen covers one repository. This covers the machine: the
    /// global Git excludes, the agents, the workspace. Both end in the same
    /// remediation service, so a fix behaves identically wherever it was
    /// started from.
    /// </para>
    /// </summary>
    private async Task CheckMachineAsync(CancellationToken ct)
    {
        _screen.Begin("This machine");

        var result = await _console.Status()
            .StartAsync("Checking...", _ => _doctor.RunAsync(ct))
            .ConfigureAwait(false);

        if (result.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(result.Error!)}[/]");

            return;
        }

        var report = result.Value!;

        foreach (var check in report.Checks)
        {
            if (check.Severity == DiagnosticSeverity.Info)
            {
                continue;
            }

            var colour = check.Severity == DiagnosticSeverity.Error ? "red" : "yellow";

            _console.MarkupLine(
                $"[{colour}]{Markup.Escape(check.Name)}[/] [dim]{Markup.Escape(check.Detail)}[/]");
        }

        await OfferRemediesAsync(report.Remedies, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Shows everything wrong with a project and offers to put right the part
    /// that can be.
    /// <para>
    /// Driven by the same drift service the command line uses rather than by a
    /// second list of warnings kept here. The two had already diverged once:
    /// this screen never mentioned a pre-commit hook left behind by an older
    /// version, because only the drift check knew about it.
    /// </para>
    /// <para>
    /// Findings without a remedy are shown too, with the command that would
    /// help. Untracking committed files rewrites a repository and splitting an
    /// instruction layer is a judgement call, so neither is something to do to
    /// somebody from a menu.
    /// </para>
    /// </summary>
    private async Task ReviewProblemsAsync(ProjectResolution project, CancellationToken ct)
    {
        _screen.Begin("Problems", project.Entry.Slug);

        var inspected = await _drift.InspectAsync(project.Entry.Slug, ct).ConfigureAwait(false);

        if (inspected.Failed || inspected.Value is not { Count: > 0 } reports)
        {
            _console.MarkupLine($"[red]{Markup.Escape(inspected.Error ?? "Nothing could be inspected.")}[/]");

            return;
        }

        var report = reports[0];

        _console.WriteLine();

        foreach (var finding in report.Findings)
        {
            if (finding.Severity == DiagnosticSeverity.Info)
            {
                continue;
            }

            var colour = finding.Severity == DiagnosticSeverity.Error ? "red" : "yellow";

            _console.MarkupLine(
                $"[{colour}]{Markup.Escape(finding.Name)}[/] "
                + $"[dim]{Markup.Escape(finding.Detail)}[/]");
        }

        await OfferRemediesAsync(report.Remedies, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Previews every fix, asks once, then applies.
    /// <para>
    /// Shared by the project screen and the machine screen so a fix behaves
    /// the same wherever it was reached from, and matches the command line for
    /// the same reason.
    /// </para>
    /// </summary>
    private async Task OfferRemediesAsync(IReadOnlyList<Remedy> remedies, CancellationToken ct)
    {
        if (remedies.Count == 0)
        {
            _console.WriteLine();
            _console.MarkupLine("[dim]None of these can be put right automatically.[/]");
            _console.WriteLine();

            return;
        }

        _console.WriteLine();
        _console.MarkupLine($"[bold]{remedies.Count} of these can be put right now[/]");

        // Previewed before anything is agreed to, exactly as the command line
        // does it. A menu is a worse place to be surprised by a change than a
        // terminal, not a better one.
        var previews = new List<RemedyOutcome>();

        foreach (var remedy in remedies)
        {
            var preview = await _remediation.PreviewAsync(remedy, ct).ConfigureAwait(false);

            if (preview.Failed)
            {
                _console.MarkupLine(
                    $"  [yellow]![/] {Markup.Escape(remedy.Description)} "
                    + $"[dim]{Markup.Escape(preview.Error!)}[/]");

                continue;
            }

            previews.Add(preview.Value!);

            _console.MarkupLine($"  [green]+[/] {Markup.Escape(remedy.Description)}");
            _console.MarkupLine($"    [dim]{Markup.Escape(preview.Value!.Detail)}[/]");
        }

        _console.WriteLine();

        if (previews.Count == 0 || !_console.Confirm($"Apply {previews.Count} fix(es)?", defaultValue: false))
        {
            _console.MarkupLine("[dim]Nothing was changed.[/]");
            _console.WriteLine();

            return;
        }

        foreach (var preview in previews)
        {
            var applied = await _remediation.ApplyAsync(preview.Remedy, ct).ConfigureAwait(false);

            // One failing must not stop the others: they are independent, and
            // stopping halfway leaves the least explicable state of all.
            _console.MarkupLine(applied.Failed
                ? $"  [red]x[/] {Markup.Escape(preview.Remedy.Description)} [dim]{Markup.Escape(applied.Error!)}[/]"
                : $"  [green]+[/] {Markup.Escape(applied.Value!.Detail)}");
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
                .PageSize(_screen.PageSize)
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
