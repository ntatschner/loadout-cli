using Loadout.Agents;
using Loadout.Core.Configuration;
using Loadout.Core.Context;
using Loadout.Core.Diagnostics;
using Loadout.Core.Editors;
using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Manager;
using Loadout.Core.Projects;
using Loadout.Core.Sessions;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Configuration;
using Loadout.Models.Diagnostics;
using Loadout.Models.Instructions;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;
using Spectre.Console;
using Terminal.Gui.App;

namespace Loadout.Tui.Terminal;

/// <summary>
/// The launcher, drawn as a screen rather than printed as a sequence of
/// questions.
/// <para>
/// What changed and why: the previous launcher asked one question at a time and
/// redrew by clearing the terminal between them. That works, but it can only
/// ever show one thing — choosing a project meant losing sight of the list, and
/// seeing what was wrong with a project meant losing sight of the project. A
/// screen with panels shows the list and the detail together, and keeps them
/// both while somebody moves around.
/// </para>
/// <para>
/// The toolkit owns the terminal while it runs, so anything that needs the
/// terminal for itself — an agent, a shell, a command writing output — cannot
/// happen inside it. The screen records what was asked for and closes; this
/// class then does the work with the terminal handed back. That is also what
/// makes the launcher able to run anything the command line can: it hands the
/// request to the same parser rather than reimplementing it.
/// </para>
/// </summary>
public sealed class TerminalLauncher : ILauncherTui
{
    private readonly IAnsiConsole _console;
    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly IConfigurationService _configuration;
    private readonly IAgentRegistry _agents;
    private readonly IShellProvider _shells;
    private readonly IProcessLauncher _processes;
    private readonly IProjectOverviewService _overviews;
    private readonly ICommandCatalogue _catalogue;
    private readonly IApplicationLauncher _opener;
    private readonly IProjectOnboarding _onboarding;
    private readonly IDriftService _drift;
    private readonly IRemediationService _remediation;
    private readonly IContextCompiler _compiler;
    private readonly IDoctorService _doctor;
    private readonly IPlatformPaths _paths;
    private readonly IEditorService _editors;
    private readonly ISessionHistoryService _sessions;
    private readonly IManagerInventory _manager;
    private readonly IInstructionService _instructions;
    private readonly IGitManager _git;

    /// <summary>Agents detected on this machine, once the first screen has asked.</summary>
    private IReadOnlyList<string> _installed = [];

    public TerminalLauncher(
        IAnsiConsole console,
        IProjectService projects,
        IWorkspaceManager workspace,
        IConfigurationService configuration,
        IAgentRegistry agents,
        IShellProvider shells,
        IProcessLauncher processes,
        IProjectOverviewService overviews,
        ICommandCatalogue catalogue,
        IApplicationLauncher opener,
        IProjectOnboarding onboarding,
        IDriftService drift,
        IRemediationService remediation,
        IContextCompiler compiler,
        IDoctorService doctor,
        IPlatformPaths paths,
        IEditorService editors,
        ISessionHistoryService sessions,
        IManagerInventory manager,
        IInstructionService instructions,
        IGitManager git)
    {
        _instructions = instructions;
        _git = git;
        _console = console;
        _projects = projects;
        _workspace = workspace;
        _configuration = configuration;
        _agents = agents;
        _shells = shells;
        _processes = processes;
        _overviews = overviews;
        _catalogue = catalogue;
        _opener = opener;
        _onboarding = onboarding;
        _drift = drift;
        _remediation = remediation;
        _compiler = compiler;
        _doctor = doctor;
        _paths = paths;
        _editors = editors;
        _sessions = sessions;
        _manager = manager;
    }

    /// <inheritdoc />
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var configResult = await _configuration.LoadConfigAsync(ct).ConfigureAwait(false);

        if (configResult.Failed)
        {
            _console.MarkupLine($"[red]{Shown.Safely(configResult.Error!)}[/]");
            return (int)configResult.ExitCode;
        }

        var config = configResult.Value!;

        var workspaceState = !_workspace.IsConfigured(config)
            ? "workspace not configured"
            : _workspace.IsCloned()
                ? "workspace connected"
                : "workspace not cloned";

        // Worked out once. Which repository somebody is standing in does not
        // change while the launcher is open, and it costs a git call.
        ProjectResolution? here = null;
        IReadOnlyList<string>? installed = null;

        var opening = true;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var intent = await ShowAsync(
                async () =>
                {
                    // Started together, awaited afterwards. These ask four
                    // unrelated questions — which repository you are standing
                    // in, which agents this machine has, what is registered,
                    // and what you were last doing — and not one of them needs
                    // another's answer. Awaiting them in turn only added the
                    // four times together, and two of them shell out.
                    var locating = here is null
                        ? ResolveCurrentAsync(ct)
                        : Task.FromResult<ProjectResolution?>(here);

                    var detecting = installed is null
                        ? InstalledAgentsAsync(ct)
                        : Task.FromResult(installed);

                    var listing = _projects.ListAsync(ct);

                    // Tolerated when it fails: not being able to say what you
                    // were last doing is no reason to refuse to open.
                    var recalling = _sessions.ListAsync(new SessionQuery(Limit: 5), ct);

                    here = await locating.ConfigureAwait(false);
                    installed = await detecting.ConfigureAwait(false);
                    _installed = installed;

                    var projects = await listing.ConfigureAwait(false);
                    var sessions = await recalling.ConfigureAwait(false);

                    return (
                        projects,
                        here,
                        installed,
                        sessions.Succeeded
                            ? sessions.Value!
                            : (IReadOnlyList<AgentSession>)[]);
                },
                workspaceState,
                opening,
                ct).ConfigureAwait(false);

            opening = false;

            if (intent is null)
            {
                return (int)ExitCode.GeneralFailure;
            }

            var outcome = await ActOnAsync(intent, ct).ConfigureAwait(false);

            // An exit code means the session is over: something else has had
            // the terminal and its result is the launcher's. Anything else goes
            // back to the screen, with the project list re-read on the way so
            // that whatever just ran is reflected in it.
            if (outcome is not null)
            {
                return outcome.Value;
            }
        }
    }

    /// <summary>
    /// Puts the screen up and waits for it to close.
    /// </summary>
    /// <param name="load">
    /// Reads what the screen needs. Started before the opening animation rather
    /// than after it, so the animation covers the wait instead of adding to it.
    /// </param>
    /// <param name="workspaceState">One phrase describing the workspace.</param>
    /// <param name="opening">Whether this is the first screen of the session.</param>
    /// <param name="ct">Cancels the session.</param>
    private async Task<LauncherIntent?> ShowAsync(
        Func<Task<(OperationResult<IReadOnlyList<ProjectResolution>> Projects,
                   ProjectResolution? Here,
                   IReadOnlyList<string> Agents,
                   IReadOnlyList<AgentSession> Recent)>> load,
        string workspaceState,
        bool opening,
        CancellationToken ct)
    {
        using IApplication application = Application.Create();

        application.InitLegibly();

        // Started first, deliberately. Detecting agents and resolving the
        // current repository both shell out, and running them behind the
        // animation means the launcher is ready by the time it finishes rather
        // than beginning to think once it has.
        var loading = load();

        SplashScreen.Play(application, "reading your projects", opening && Watching);

        var (projects, here, agents, recent) = await loading.ConfigureAwait(false);

        if (projects.Failed)
        {
            _console.MarkupLine($"[red]{Shown.Safely(projects.Error!)}[/]");
            return null;
        }

        using var window = new LauncherWindow(
            projects.Value!,
            here,
            workspaceState,
            agents,
            (project, token) => OverviewAsync(project, token),
            w => ShowPalette(w, application),
            recent,
            application);

        await application.RunAsync(window, ct).ConfigureAwait(false);

        return window.Intent ?? LauncherIntent.Quit;
    }

    /// <summary>
    /// Whether there is somebody at the terminal to see any of this. False for
    /// a redirected run, where an animation would spend a second of a script's
    /// time on something nobody will look at.
    /// </summary>
    private bool Watching => _console.Profile.Capabilities.Interactive;

    /// <summary>
    /// Offers everything the command line can do, so the launcher is never a
    /// subset of it.
    /// </summary>
    private void ShowPalette(LauncherWindow window, IApplication application)
    {
        using var palette = new CommandPaletteDialog(_catalogue.Commands, application);

        application.Run(palette);

        if (palette.Chosen is not { Length: > 0 } chosen)
        {
            return;
        }

        // A command that cannot run without an argument is asked for one here
        // rather than started and told off by the parser. Choosing 'export'
        // from the palette used to do nothing but print that a specialist was
        // missing, with no way from that screen to say which.
        var entry = _catalogue.Commands.FirstOrDefault(
            command => string.Equals(command.Path, chosen, StringComparison.Ordinal));

        if (entry is { RequiredArgument.Length: > 0 })
        {
            using var ask = new CommandArgumentDialog(
                chosen, entry.RequiredArgument, entry.Example, application);

            application.Run(ask);

            if (ask.Chosen is not { Length: > 0 } value)
            {
                return;
            }

            // Quoted, because the value is often prose — a task to explain, a
            // name for a project.
            window.RunCommand($"{chosen} \"{value}\"");

            return;
        }

        window.RunCommand(chosen);
    }

    private async Task<ProjectOverview?> OverviewAsync(
        ProjectResolution project,
        CancellationToken ct)
    {
        if (!project.IsAvailableLocally)
        {
            return null;
        }

        var result = await _overviews.DescribeAsync(project, ct).ConfigureAwait(false);

        return result.Succeeded ? result.Value : null;
    }

    /// <summary>
    /// Carries out what was chosen, with the terminal back in our hands.
    /// Returns an exit code when the session is over, and null to go round
    /// again.
    /// </summary>
    private async Task<int?> ActOnAsync(LauncherIntent intent, CancellationToken ct)
    {
        switch (intent.Action)
        {
            case LauncherAction.Quit:
                return (int)ExitCode.Success;

            case LauncherAction.Launch when intent.Project is { } project:
                return await LaunchAsync(project, intent.Agent, intent.Options, ct).ConfigureAwait(false);

            case LauncherAction.Shell when intent.Project?.LocalPath is { } path:
                return await OpenShellAsync(path, ct).ConfigureAwait(false);

            case LauncherAction.Resume:
                // The same command somebody would have typed, rather than a
                // second implementation of the session picker.
                await _catalogue.RunAsync(
                    LauncherCommands.Resume,
                    ResumeArguments(intent),
                    ct).ConfigureAwait(false);

                Pause();
                return null;

            case LauncherAction.FileManager when intent.Project?.LocalPath is { } directory:
                await _opener.OpenInFileManagerAsync(directory, ct).ConfigureAwait(false);
                return null;

            case LauncherAction.AddProject:
                // A sequence of questions, which reads better asked one at a
                // time than laid out on a screen. Run with the terminal handed
                // back, like everything else that needs it.
                await _onboarding.AddAsync(new OnboardingOptions(), ct).ConfigureAwait(false);
                Pause();
                return null;

            case LauncherAction.Clone when intent.Project is { } cloning:
                await _catalogue.RunAsync(LauncherCommands.Clone, [cloning.Entry.Slug], ct)
                    .ConfigureAwait(false);
                Pause();
                return null;

            case LauncherAction.Problems when intent.Project is { } troubled:
                await ReviewProblemsAsync(troubled, ct).ConfigureAwait(false);
                return null;

            case LauncherAction.MachineCheck:
                await CheckMachineAsync(ct).ConfigureAwait(false);
                return null;

            case LauncherAction.Settings:
                await ShowSettingsAsync(ct).ConfigureAwait(false);
                return null;

            case LauncherAction.Drift:
                await CheckDriftAsync(ct).ConfigureAwait(false);
                return null;

            case LauncherAction.Manager when intent.Project is { } managed:
                await ShowManagerAsync(managed, ct).ConfigureAwait(false);
                return null;

            case LauncherAction.Command when intent.CommandPath is { Length: > 0 } path:
                // Run against the project on screen. The launcher knew which
                // one was selected and threw it away here, so a command that
                // works out where it is from the working directory looked at
                // wherever the launcher had been started from — for a Start
                // Menu launch, the directory the launcher is installed in.
                // Choosing "code" then reported that the install directory is
                // not a Git repository, which is true and no help at all.
                //
                // --repo is a global option, so this is the one argument every
                // command understands. Nothing is added when the project is not
                // on this machine, because a path that is not there would be a
                // worse answer than the working directory.
                await _catalogue.RunAsync(
                    path,
                    intent.Project?.LocalPath is { Length: > 0 } local ? ["--repo", local] : [],
                    ct).ConfigureAwait(false);

                // Back to the launcher, having shown whatever the command
                // printed. Somebody who ran "doctor" wants to read it and carry
                // on, not be returned to a screen that has painted over it.
                Pause();
                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Shows what is wrong with a project and applies whatever was ticked.
    /// <para>
    /// Inspecting and previewing both happen before the screen opens, and
    /// applying happens after it closes. Neither is quick enough to do while a
    /// screen is being drawn, and a screen that stops repainting mid-fix is
    /// indistinguishable from one that has crashed.
    /// </para>
    /// </summary>
    private async Task ReviewProblemsAsync(ProjectResolution project, CancellationToken ct)
    {
        var inspected = await _drift.InspectAsync(project.Entry.Slug, ct).ConfigureAwait(false);

        if (inspected.Failed || inspected.Value is not { Count: > 0 } reports)
        {
            _console.MarkupLine(
                $"[red]{Shown.Safely(inspected.Error ?? "Nothing could be inspected.")}[/]");

            Pause();
            return;
        }

        var report = reports[0];

        await ShowFindingsAsync(
            $"Problems - {project.Entry.Name}", report.Findings, report.Remedies, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Checks the machine over, on the same screen a project's problems use.
    /// <para>
    /// The two are the same shape - a list of findings, some of which can be
    /// put right - so they are the same screen. A second one would be the first
    /// one built twice, and the two would drift.
    /// </para>
    /// </summary>
    private async Task CheckMachineAsync(CancellationToken ct)
    {
        var report = await _doctor.RunAsync(ct).ConfigureAwait(false);

        if (report.Failed)
        {
            _console.MarkupLine($"[red]{Shown.Safely(report.Error!)}[/]");
            Pause();
            return;
        }

        var checks = report.Value!.Checks;

        var remedies = checks.Select(check => check.Remedy).OfType<Remedy>().ToList();

        await ShowFindingsAsync("This machine", checks, remedies, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Shows where every project has drifted from what was recorded for it.
    /// <para>
    /// Findings carry the project they came from, because a list of twenty that
    /// does not say which repository each belongs to is a list nobody can act
    /// on.
    /// </para>
    /// </summary>
    private async Task CheckDriftAsync(CancellationToken ct)
    {
        var inspected = await _drift.InspectAsync(ct: ct).ConfigureAwait(false);

        if (inspected.Failed)
        {
            _console.MarkupLine($"[red]{Shown.Safely(inspected.Error!)}[/]");
            Pause();
            return;
        }

        var reports = inspected.Value!;

        var findings = reports
            .SelectMany(report => report.Findings
                .Select(finding => finding with { Name = $"{report.Slug}: {finding.Name}" }))
            .ToList();

        var remedies = reports.SelectMany(report => report.Remedies).ToList();

        await ShowFindingsAsync("Configuration drift", findings, remedies, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Shows findings, and applies whatever was ticked.
    /// <para>
    /// Previewing happens before the screen opens and applying after it closes.
    /// Neither is quick enough to do while a screen is being drawn, and a
    /// screen that stops repainting mid-fix looks like one that has crashed.
    /// </para>
    /// </summary>
    private async Task ShowFindingsAsync(
        string heading,
        IReadOnlyList<DiagnosticCheck> findings,
        IReadOnlyList<Remedy> remedies,
        CancellationToken ct)
    {
        var offered = new List<OfferedRemedy>();

        foreach (var remedy in remedies)
        {
            var preview = await _remediation.PreviewAsync(remedy, ct).ConfigureAwait(false);

            offered.Add(new OfferedRemedy(
                remedy,
                preview.Succeeded
                    ? preview.Value!.Detail
                    : preview.Error ?? "This could not be previewed."));
        }

        IReadOnlyList<Remedy> chosen;

        using (IApplication application = Application.Create())
        {
            application.InitLegibly();

            using var window = new ProblemsWindow(heading, findings, offered, application);

            await application.RunAsync(window, ct).ConfigureAwait(false);

            chosen = window.Chosen;
        }

        if (chosen.Count == 0)
        {
            return;
        }

        foreach (var remedy in chosen)
        {
            var applied = await _remediation.ApplyAsync(remedy, ct).ConfigureAwait(false);

            _console.MarkupLine(applied.Failed
                ? $"[red]{Markup.Escape(remedy.Description)}: {Shown.Safely(applied.Error!)}[/]"
                : $"[green]done[/] {Markup.Escape(remedy.Description)}");
        }

        Pause();
    }

    /// <summary>
    /// How <c>resume</c> is called for what was chosen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reported from use: Resume did not work. The button and the menu entry
    /// pass a project rather than a session, and the project's slug was being
    /// handed to <c>resume</c>'s positional argument — which is a session id.
    /// A slug never prefixes a session id, so nothing matched, and a session
    /// named but not found used to be indistinguishable from backing out of
    /// the picker: no output, exit zero, nothing to see. The comment here
    /// claimed it reached the picker. It did not.
    /// </para>
    /// <para>
    /// A project scopes the list instead, which is what the option is for, so
    /// Resume on a project offers that project's sessions.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> ResumeArguments(LauncherIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        if (intent.SessionId is { Length: > 0 } chosen)
        {
            return [chosen];
        }

        return intent.Project is { } scope
            ? ["--project", scope.Entry.Slug]
            : [];
    }

    /// <summary>The agents this machine actually has, by display name.</summary>
    private async Task<IReadOnlyList<string>> InstalledAgentsAsync(CancellationToken ct) =>
        (await _agents.DetectAllAsync(ct).ConfigureAwait(false))
            .Where(agent => agent.IsInstalled)
            .Select(agent => agent.DisplayName)
            .ToList();

    /// <summary>
    /// Shows the settings and writes back only what actually changed.
    /// </summary>
    /// <summary>
    /// Shows what is loaded for a project, and runs whatever was chosen there.
    /// </summary>
    /// <remarks>
    /// The screen returns a command line rather than doing anything, and it is
    /// run through the same catalogue every other command goes through. That is
    /// the rule the launcher keeps everywhere: a screen never implements
    /// command behaviour, or the trust boundary has two implementations and one
    /// of them drifts.
    /// </remarks>
    private async Task ShowManagerAsync(ProjectResolution project, CancellationToken ct)
    {
        var read = await _manager
            .ReadAsync(project.Entry.Slug, project.LocalPath ?? string.Empty, ct)
            .ConfigureAwait(false);

        if (read.Failed)
        {
            _console.MarkupLine($"[red]{Shown.Safely(read.Error!)}[/]");
            return;
        }

        string? chosen;

        using (IApplication application = Application.Create())
        {
            application.InitLegibly();

            using var window = new ManagerWindow(project.Entry.Slug, read.Value!, application);

            await application.RunAsync(window, ct).ConfigureAwait(false);

            chosen = window.Chosen;
        }

        if (chosen is not { Length: > 0 } path)
        {
            return;
        }

        await _catalogue.RunAsync(
            path,
            project.LocalPath is { Length: > 0 } local ? ["--repo", local] : [],
            ct).ConfigureAwait(false);
    }

    private async Task ShowSettingsAsync(CancellationToken ct)
    {
        var loaded = await _configuration.LoadConfigAsync(ct).ConfigureAwait(false);

        if (loaded.Failed)
        {
            _console.MarkupLine($"[red]{Shown.Safely(loaded.Error!)}[/]");
            Pause();
            return;
        }

        var config = loaded.Value!;

        var loadedMachine = await _configuration.LoadMachineAsync(ct).ConfigureAwait(false);

        if (loadedMachine.Failed)
        {
            _console.MarkupLine($"[red]{Shown.Safely(loadedMachine.Error!)}[/]");
            Pause();
            return;
        }

        var machine = loadedMachine.Value!;

        var places = new List<(string, string)>
        {
            ("Shared settings", Path.Combine(_paths.Paths.Config, "config.yaml")),
            ("This machine", Path.Combine(_paths.Paths.State, "machines.yaml")),
            ("Workspace clone", _workspace.IsCloned()
                ? _workspace.LocalPath
                : $"{_workspace.LocalPath}  (not cloned)"),
            ("State", _paths.Paths.State),
            ("Logs", _paths.Paths.Logs),
        };

        var agents = (await _agents.DetectAllAsync(ct).ConfigureAwait(false))
            .Where(agent => agent.IsInstalled)
            .Select(agent => agent.DisplayName)
            .ToList();

        SettingsEdit? edit;

        using (IApplication application = Application.Create())
        {
            application.InitLegibly();

            var editor = _editors.Describe(config);

            using var window = new SettingsWindow(
                config, machine, places, agents, editor.Command, editor.Profiles ?? [],
                application);

            await application.RunAsync(window, ct).ConfigureAwait(false);

            edit = window.Edit;
        }

        if (edit is not null)
        {
            await ApplySettingsAsync(config, machine, edit, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes back the settings that changed, handling the one that is
    /// dangerous.
    /// </summary>
    private async Task ApplySettingsAsync(
        LauncherConfig config,
        MachineConfig machine,
        SettingsEdit edit,
        CancellationToken ct)
    {
        var remoteChanged = edit.Values.TryGetValue("workspace-remote", out var remote)
            && !string.Equals(
                remote,
                config.Workspace.Remote ?? string.Empty,
                StringComparison.Ordinal);

        if (remoteChanged && _workspace.IsCloned())
        {
            // The clone belongs to the old repository. Reusing it would leave a
            // directory full of another repository's projects, and the next
            // sync would either fail or, worse, appear to work against the
            // wrong history. Moved aside rather than deleted: nothing is lost.
            var moved = MoveCloneAside();

            if (moved is null)
            {
                _console.MarkupLine(
                    "[red]The existing workspace clone could not be moved, so the repository "
                    + "was left unchanged.[/] Nothing else was saved either.");

                Pause();
                return;
            }

            _console.MarkupLine($"[dim]Moved the previous clone to {Markup.Escape(moved)}[/]");
        }

        // Written through the same registry the screen was built from and
        // that 'loadout config set' writes through, so a setting cannot be
        // editable on one and inert on the other.
        foreach (var (key, value) in edit.Values)
        {
            var entry = ConfigKeys.Find(key);

            if (entry is null)
            {
                continue;
            }

            var current = entry.Read(config, machine) ?? string.Empty;

            if (string.Equals(current, value, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                entry.Write(config, machine, value);
            }
            catch (Exception failure) when (failure is FormatException or OverflowException
                or ArgumentException)
            {
                // Said and skipped, not thrown. One mistyped number must not
                // discard the other twenty settings somebody just changed.
                _console.MarkupLine(
                    $"[yellow]{Markup.Escape(key)} was left alone:[/] "
                    + Markup.Escape(failure.Message));
            }
        }

        // Replaced rather than merged, so clearing a field actually clears it.
        config.Editor.Profiles.Clear();

        foreach (var (agent, profile) in edit.EditorProfiles.Where(p => p.Value.Length > 0))
        {
            config.Editor.Profiles[agent] = profile;
        }

        var saved = await _configuration.SaveConfigAsync(config, ct).ConfigureAwait(false);

        if (saved.Failed)
        {
            _console.MarkupLine($"[red]{Shown.Safely(saved.Error!)}[/]");
            Pause();
            return;
        }

        // Two of the settings on that screen live here, and a machine's own
        // layout never travels to another machine.
        var savedMachine = await _configuration.SaveMachineAsync(machine, ct).ConfigureAwait(false);

        if (savedMachine.Failed)
        {
            _console.MarkupLine($"[red]{Shown.Safely(savedMachine.Error!)}[/]");
            Pause();
            return;
        }

        _console.MarkupLine("[green]Saved.[/]");

        if (remoteChanged)
        {
            _console.MarkupLine("[dim]Fetch the new workspace with:[/] loadout workspace sync");
        }

        Pause();
    }

    /// <summary>
    /// Renames the existing clone out of the way, returning where it went.
    /// Timestamped, so doing this twice does not overwrite the first one.
    /// </summary>
    private string? MoveCloneAside()
    {
        var destination = _workspace.LocalPath + ".previous-"
            + DateTime.Now.ToString(
                "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);

        try
        {
            Directory.Move(_workspace.LocalPath, destination);

            return destination;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<int?> LaunchAsync(
        ProjectResolution project,
        string? agent,
        LaunchOptions? options,
        CancellationToken ct)
    {
        // The screen said which project. Everything else a launch can be
        // asked is asked on the sheet, with the terminal to itself, unless the
        // caller already had the answers.
        options ??= await AskAsync(project, ct).ConfigureAwait(false);

        // Dismissed means dismissed: back to the list, and no session started
        // that nobody asked for.
        if (options is null)
        {
            return null;
        }

        // The same command somebody would have typed, with what the sheet
        // collected spelled as its flags. This used to build a launch request
        // of its own and hand it to the launcher directly, which was a second
        // way in: it printed less than 'launch -v' did and never asked about
        // workspace changes on the way out, because that question lives in the
        // command. Going through the parser gives a screen launch everything a
        // typed one has, and keeps it that way as the command grows.
        return await _catalogue.RunAsync(
            LauncherCommands.Launch,
            LaunchArguments(project, agent, options),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// What the sheet chose, as the arguments the launch command takes.
    /// </summary>
    /// <remarks>
    /// The defaults are left out rather than spelled: a launch with no
    /// <c>--profile</c> is a launch with the project's own settings, and
    /// passing "default" by name would fail with "no profile named default".
    /// </remarks>
    internal static IReadOnlyList<string> LaunchArguments(
        ProjectResolution project,
        string? agent,
        LaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);

        var arguments = new List<string> { project.Entry.Slug };

        var agentName = options.Agent ?? agent;

        if (agentName is { Length: > 0 })
        {
            arguments.AddRange(["--agent", agentName]);
        }

        if (options.Task is { Length: > 0 } task)
        {
            arguments.AddRange(["--task", task]);
        }

        if (options.Mode is { Length: > 0 } mode)
        {
            arguments.AddRange(["--mode", mode]);
        }

        if (options.Profile is { Length: > 0 } profile)
        {
            arguments.AddRange(["--profile", profile]);
        }

        if (options.Worktree is { Length: > 0 } worktree)
        {
            arguments.AddRange(["--worktree", worktree]);
        }

        if (options.IncludeHandoff)
        {
            arguments.Add("--handoff");
        }

        if (options.Offline)
        {
            arguments.Add("--offline");
        }

        if (options.NoSync)
        {
            arguments.Add("--no-sync");
        }

        return arguments;
    }

    /// <summary>
    /// Puts the launch sheet up and returns what it collected, or null when it
    /// was dismissed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A screen of its own, after the launcher has closed, for the reason the
    /// profile chooser it replaces was: the sheet reads the repository to say
    /// what a launch would load, and doing that under a screen that is still
    /// drawing would freeze it.
    /// </para>
    /// <para>
    /// Skipped where nobody is at the terminal. A redirected run cannot answer
    /// a sheet, and blocking on one would hang a script; it gets the defaults,
    /// which are what Enter on the sheet would have given.
    /// </para>
    /// </remarks>
    private async Task<LaunchOptions?> AskAsync(ProjectResolution project, CancellationToken ct)
    {
        if (!Watching)
        {
            return new LaunchOptions();
        }

        using IApplication application = Application.Create();

        application.InitLegibly();

        using var sheet = new LaunchOptionsDialog(
            project,
            _installed,
            application,
            new LaunchSheetSources(ChoicesAsync, PreviewAsync));

        await application.RunAsync(sheet, ct).ConfigureAwait(false);

        return sheet.Chosen;
    }

    /// <summary>
    /// The profiles the agent can use and the working trees the repository
    /// has, for the sheet to offer.
    /// </summary>
    private async Task<LaunchChoices> ChoicesAsync(
        ProjectResolution project,
        string agentName,
        CancellationToken ct)
    {
        var profiles = LaunchChoices.None.Profiles;
        var worktrees = LaunchChoices.None.Worktrees;

        var manifest = await _workspace.ReadProjectAsync(project.Entry.Slug, ct).ConfigureAwait(false);

        if (manifest.Succeeded)
        {
            // The default profile is the absence of one, which is how the
            // launch spells it; the rest go by name.
            profiles = _compiler.ListProfiles(manifest.Value!, agentName)
                .Select((name, index) => new LaunchChoice(
                    manifest.Value!.Profiles.TryGetValue(name, out var profile)
                        && !string.IsNullOrWhiteSpace(profile.Description)
                        ? $"{name}  ({profile.Description})"
                        : name,
                    index == 0 ? null : name))
                .ToList();
        }

        if (project.LocalPath is { Length: > 0 } path)
        {
            var listed = await _git.ListWorktreesAsync(path, ct).ConfigureAwait(false);

            if (listed.Succeeded && listed.Value!.Count > 0)
            {
                // Named the way --worktree expects: by branch, or by directory
                // where there is no branch. The primary is the main working
                // tree, which a launch spells as no worktree at all.
                worktrees = listed.Value
                    .OrderByDescending(w => w.IsPrimary)
                    .Select(w => new LaunchChoice(
                        w.IsPrimary
                            ? $"{w.Branch ?? Path.GetFileName(w.Path)}  (main working tree)"
                            : w.Branch ?? Path.GetFileName(w.Path),
                        w.IsPrimary ? null : w.Branch ?? Path.GetFileName(w.Path)))
                    .ToList();
            }
        }

        return new LaunchChoices(profiles, worktrees);
    }

    /// <summary>
    /// What a launch as described would load: the same question the launch
    /// asks, put to the same resolver.
    /// </summary>
    private async Task<EffectiveInstructions?> PreviewAsync(
        LaunchPreviewRequest request,
        CancellationToken ct)
    {
        var manifest = await _workspace
            .ReadProjectAsync(request.Project.Entry.Slug, ct)
            .ConfigureAwait(false);

        var resolved = await _instructions.ResolveAsync(
            new InstructionRequest(
                manifest.Succeeded ? manifest.Value : null,
                RepositoryPath: request.Project.LocalPath,
                WorkspacePath: _workspace.LocalPath,
                AgentName: request.Agent,
                ProfileName: request.Profile,
                Task: request.Task,
                Mode: request.Mode),
            ct).ConfigureAwait(false);

        if (resolved.Failed)
        {
            throw new InvalidOperationException(resolved.Error);
        }

        return resolved.Value;
    }

    private async Task<int?> OpenShellAsync(string workingDirectory, CancellationToken ct)
    {
        var shell = _shells.GetInteractiveShellPath();

        if (shell.Failed)
        {
            _console.MarkupLine($"[red]{Shown.Safely(shell.Error!)}[/]");
            return (int)ExitCode.GeneralFailure;
        }

        var result = await _processes.RunInteractiveAsync(
            new ProcessRequest(shell.Value!, [], workingDirectory),
            ct).ConfigureAwait(false);

        return result.Succeeded ? result.Value : (int)result.ExitCode;
    }

    /// <summary>
    /// Waits before the screen paints over whatever a command just printed.
    /// Skipped where there is nobody to wait for, so a redirected run does not
    /// block forever.
    /// </summary>
    private void Pause()
    {
        if (!_console.Profile.Capabilities.Interactive)
        {
            return;
        }

        _console.WriteLine();
        _console.MarkupLine("[dim]Press any key to return to the launcher.[/]");
        System.Console.ReadKey(intercept: true);
    }

    private async Task<ProjectResolution?> ResolveCurrentAsync(CancellationToken ct)
    {
        var result = await _projects
            .ResolveFromDirectoryAsync(Directory.GetCurrentDirectory(), ct)
            .ConfigureAwait(false);

        return result.Succeeded ? result.Value : null;
    }
}
