using Loadout.Agents;
using Loadout.Core.Configuration;
using Loadout.Core.Context;
using Loadout.Core.Diagnostics;
using Loadout.Core.Editors;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Configuration;
using Loadout.Models.Diagnostics;
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
    private readonly IAgentLauncher _launcher;
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

    public TerminalLauncher(
        IAnsiConsole console,
        IProjectService projects,
        IWorkspaceManager workspace,
        IConfigurationService configuration,
        IAgentRegistry agents,
        IAgentLauncher launcher,
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
        IEditorService editors)
    {
        _console = console;
        _projects = projects;
        _workspace = workspace;
        _configuration = configuration;
        _agents = agents;
        _launcher = launcher;
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
                    here ??= await ResolveCurrentAsync(ct).ConfigureAwait(false);

                    installed ??= (await _agents.DetectAllAsync(ct).ConfigureAwait(false))
                        .Where(agent => agent.IsInstalled)
                        .Select(agent => agent.DisplayName)
                        .ToList();

                    var projects = await _projects.ListAsync(ct).ConfigureAwait(false);

                    return (projects, here, installed);
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
                   IReadOnlyList<string> Agents)>> load,
        string workspaceState,
        bool opening,
        CancellationToken ct)
    {
        using IApplication application = Application.Create();

        application.Init();

        // Started first, deliberately. Detecting agents and resolving the
        // current repository both shell out, and running them behind the
        // animation means the launcher is ready by the time it finishes rather
        // than beginning to think once it has.
        var loading = load();

        SplashScreen.Play(application, "reading your projects", opening && Watching);

        var (projects, here, agents) = await loading.ConfigureAwait(false);

        if (projects.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(projects.Error!)}[/]");
            return null;
        }

        using var window = new LauncherWindow(
            projects.Value!,
            here,
            workspaceState,
            agents,
            (project, token) => OverviewAsync(project, token),
            w => ShowPalette(w, application),
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

        if (palette.Chosen is { Length: > 0 } chosen)
        {
            window.RunCommand(chosen);
        }
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
                return await LaunchAsync(project, intent.Agent, ct).ConfigureAwait(false);

            case LauncherAction.Shell when intent.Project?.LocalPath is { } path:
                return await OpenShellAsync(path, ct).ConfigureAwait(false);

            case LauncherAction.Resume when intent.Project is { } resuming:
                // The same command somebody would have typed, rather than a
                // second implementation of the session picker.
                await _catalogue.RunAsync(LauncherCommands.Resume, [resuming.Entry.Slug], ct)
                    .ConfigureAwait(false);
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

            case LauncherAction.Command when intent.CommandPath is { Length: > 0 } path:
                await _catalogue.RunAsync(path, [], ct).ConfigureAwait(false);

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
                $"[red]{Markup.Escape(inspected.Error ?? "Nothing could be inspected.")}[/]");

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
            _console.MarkupLine($"[red]{Markup.Escape(report.Error!)}[/]");
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
            _console.MarkupLine($"[red]{Markup.Escape(inspected.Error!)}[/]");
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
            application.Init();

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
                ? $"[red]{Markup.Escape(remedy.Description)}: {Markup.Escape(applied.Error!)}[/]"
                : $"[green]done[/] {Markup.Escape(remedy.Description)}");
        }

        Pause();
    }

    /// <summary>
    /// Shows the settings and writes back only what actually changed.
    /// </summary>
    private async Task ShowSettingsAsync(CancellationToken ct)
    {
        var loaded = await _configuration.LoadConfigAsync(ct).ConfigureAwait(false);

        if (loaded.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(loaded.Error!)}[/]");
            Pause();
            return;
        }

        var config = loaded.Value!;

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
            application.Init();

            var editor = _editors.Describe(config);

            using var window = new SettingsWindow(
                config, places, agents, editor.Command, editor.Profiles ?? [], application);

            await application.RunAsync(window, ct).ConfigureAwait(false);

            edit = window.Edit;
        }

        if (edit is not null)
        {
            await ApplySettingsAsync(config, edit, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes back the settings that changed, handling the one that is
    /// dangerous.
    /// </summary>
    private async Task ApplySettingsAsync(
        LauncherConfig config,
        SettingsEdit edit,
        CancellationToken ct)
    {
        var remoteChanged = !string.Equals(
            edit.WorkspaceRemote,
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

        config.Workspace.Remote = edit.WorkspaceRemote;
        config.Workspace.Branch = edit.WorkspaceBranch;
        config.DefaultAgent = edit.DefaultAgent;
        config.Sync.Launch = edit.SyncAtLaunch;
        config.Sync.Exit = edit.SyncAtExit;
        config.Editor.Command = edit.EditorCommand;

        // Replaced rather than merged, so clearing a field actually clears it.
        config.Editor.Profiles.Clear();

        foreach (var (agent, profile) in edit.EditorProfiles.Where(p => p.Value.Length > 0))
        {
            config.Editor.Profiles[agent] = profile;
        }

        var saved = await _configuration.SaveConfigAsync(config, ct).ConfigureAwait(false);

        if (saved.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(saved.Error!)}[/]");
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
        CancellationToken ct)
    {
        var agentName = agent ?? project.Entry.DefaultAgent;

        var profile = await ChooseProfileAsync(project.Entry.Slug, agentName, ct)
            .ConfigureAwait(false);

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
    /// Asks which context profile to start with, when there is more than one.
    /// <para>
    /// Asked only when the answer matters. A project with a single profile has
    /// nothing to choose between, and putting a dialog up to say so would be a
    /// question whose answer is already known.
    /// </para>
    /// </summary>
    private async Task<string?> ChooseProfileAsync(
        string slug,
        string agentName,
        CancellationToken ct)
    {
        var manifest = await _workspace.ReadProjectAsync(slug, ct).ConfigureAwait(false);

        if (manifest.Failed)
        {
            return null;
        }

        var profiles = _compiler.ListProfiles(manifest.Value!, agentName);

        if (profiles.Count <= 1)
        {
            return null;
        }

        var labels = profiles
            .Select(name => manifest.Value!.Profiles.TryGetValue(name, out var profile)
                && !string.IsNullOrWhiteSpace(profile.Description)
                    ? $"{name}  ({profile.Description})"
                    : name)
            .ToList();

        using IApplication application = Application.Create();

        application.Init();

        using var chooser = new ChoiceDialog("What are you working on?", labels, application);

        await application.RunAsync(chooser, ct).ConfigureAwait(false);

        return chooser.ChosenIndex is int index ? profiles[index] : null;
    }

    private async Task<int?> OpenShellAsync(string workingDirectory, CancellationToken ct)
    {
        var shell = _shells.GetInteractiveShellPath();

        if (shell.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(shell.Error!)}[/]");
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
