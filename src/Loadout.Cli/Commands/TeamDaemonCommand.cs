using System.ComponentModel;
using System.Text.Json;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Teams;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// The resident process: it fires the schedules and serves the dashboard.
/// </summary>
/// <remarks>
/// <para>
/// Two jobs rather than one because they want the same thing: something that
/// is still there when nobody is at the keyboard. A schedule with nothing
/// resident never fires, and a dashboard that only exists while somebody is
/// watching it answers the question they were already watching.
/// </para>
/// <para>
/// It starts runs by running the command somebody would type, through the same
/// parser. That is the rule the launcher already follows and the reason it
/// matters is unchanged: two implementations of one behaviour drift, and the
/// one nobody is watching drifts furthest.
/// </para>
/// <para>
/// One run at a time. Two team runs against one repository fight for the
/// branch, and a daemon that started a second because the clock said so would
/// have cost twice for one answer. A schedule that comes due during a run
/// waits for the next check.
/// </para>
/// </remarks>
[Description("Stay running: fire the scheduled team runs and serve the dashboard.")]
[CommandMeta(CommandCategory.Start, Intent = "daemon resident service schedules fire background team", Mutates = true)]
public sealed class TeamDaemonCommand : AsyncCommand<TeamDaemonCommand.Settings>
{
    /// <summary>How often the schedules are looked at.</summary>
    /// <remarks>
    /// A minute is far finer than anything here is scheduled to, and coarse
    /// enough that an idle daemon costs nothing worth measuring. It is also
    /// well inside the hour a daily schedule stays due for, so one that comes
    /// round while this is running is never missed.
    /// </remarks>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    /// <summary>How often the controls beside the note are looked at.</summary>
    /// <remarks>
    /// Far oftener than the schedules, because somebody who typed "stop" is
    /// waiting for it; a minute of nothing reads as a command that did not
    /// work. Two seconds of reading two small files costs nothing.
    /// </remarks>
    internal static readonly TimeSpan Glance = TimeSpan.FromSeconds(2);

    /// <summary>How long a successor waits for the daemon it replaces to go.</summary>
    private static readonly TimeSpan Handover = TimeSpan.FromSeconds(30);

    /// <summary>How long the runs it started get to write their endings once it is stopping.</summary>
    /// <remarks>
    /// Long enough for a lead to be ended and its ending written, which is
    /// most of a node's fifteen seconds of grace; short enough that "now"
    /// still means now. A run that has not finished by then goes with the
    /// process, as it always did.
    /// </remarks>
    internal static readonly TimeSpan Unwinding = TimeSpan.FromSeconds(20);

    private readonly IScheduleService _schedules;
    private readonly IRunJournal _journal;
    private readonly ICommandCatalogue _commands;
    private readonly Loadout.Core.Ideas.IIdeaService _ideas;
    private readonly Loadout.Core.Ideas.IIdeaDumps _dumps;
    private readonly IPlatformPaths _paths;
    private readonly Loadout.Core.Projects.IProjectService _projects;
    private readonly Loadout.Core.Tasks.ITaskService _tasks;
    private readonly Loadout.Core.Git.IGitManager _git;
    private readonly IProcessInspector _processes;
    private readonly ISecretProvider _secrets;
    private readonly HttpClient _client;
    private readonly Loadout.Core.Configuration.IConfigurationService _configuration;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;

    private readonly AccessibleMode _accessible;
    private readonly Loadout.Platform.Abstractions.ISpeech _speech;
    private readonly Loadout.Core.Teams.ITeamCatalogue _teams;
    private readonly Loadout.Core.Instructions.ISpecialistLibrary _library;
    private readonly Loadout.Core.Workspace.IWorkspaceManager _workspace;
    private readonly Loadout.Agents.IAgentRegistry _agents;
    private readonly IProcessLauncher _launcher;
    private readonly Loadout.Core.Tools.IToolNominationPass _nominations;
    private readonly TeamBin _bin;

    /// <summary>
    /// Every command this daemon runs, counted while it runs.
    /// </summary>
    /// <remarks>
    /// A stop that is not "now" waits for these, and they are all of the runs
    /// this process has in hand: the schedules', the page's and the webhook's
    /// all go through the one catalogue.
    /// </remarks>
    private readonly InFlight _inFlight;

    /// <summary>Past conversations and resuming them; null in a daemon the tests build without it.</summary>
    private readonly Loadout.Core.Sessions.ISessionResumption? _resumption;

    /// <summary>What works through the teams' queues, or null for a daemon built without one.</summary>
    private readonly TeamQueueDriver? _queues;

    public TeamDaemonCommand(
        ISecretProvider secrets,
        HttpClient client,
        Loadout.Core.Configuration.IConfigurationService configuration,
        IScheduleService schedules,
        IRunJournal journal,
        ICommandCatalogue commands,
        IPlatformPaths paths,
        Loadout.Core.Projects.IProjectService projects,
        Loadout.Core.Tasks.ITaskService tasks,
        Loadout.Core.Git.IGitManager git,
        IProcessInspector processes,
        IAnsiConsole console,
        TimeProvider time,
        AccessibleMode accessible,
        Loadout.Platform.Abstractions.ISpeech speech,
        Loadout.Core.Teams.ITeamCatalogue teams,
        Loadout.Core.Instructions.ISpecialistLibrary library,
        Loadout.Core.Workspace.IWorkspaceManager workspace,
        Loadout.Agents.IAgentRegistry agents,
        IProcessLauncher launcher,
        Loadout.Core.Tools.IToolNominationPass nominations,
        Loadout.Core.Ideas.IIdeaService ideas,
        Loadout.Core.Ideas.IIdeaDumps dumps,
        TeamBin bin,
        IChildLifetime lifetime,
        Loadout.Core.Instructions.IMemoryService memory,
        Loadout.Core.Instructions.IMemoryImporter importer,
        Loadout.Core.Sessions.LaunchSheet sheet,
        Loadout.Core.Sessions.ISessionResumption? resumption = null,
        TeamQueueDriver? queues = null,
        Loadout.Core.Updates.IUpdateService? updates = null,
        IEnvironmentProvider? environment = null,
        Loadout.Core.Configuration.YamlStore? yaml = null,
        IFilePermissions? permissions = null,
        IExecutableResolver? resolver = null)
    {
        _resolver = resolver;
        _updateWatch = updates is null ? null : new Loadout.Core.Updates.UpdateWatch(updates, configuration, time);
        _yaml = yaml;
        _permissions = permissions;
        // Every daemon runs the trigger rules; one built without a YAML store
        // (a test that wants nothing of them) has nowhere to read them from.
        _triggering = yaml is not null;
        _queues = queues;
        _resumption = resumption;
        _bin = bin;
        _sheet = sheet;
        _memory = memory;
        _importer = importer;

        // Made here rather than registered: the daemon owns them and ends
        // them when it stops, and the container, disposing synchronously,
        // refuses a singleton that can only be disposed asynchronously.
        _sessions = new Loadout.Core.Sessions.Hosting.HostedSessions(
            Loadout.Platform.PlatformServices.CreatePseudoTerminal,
            Loadout.Core.Agents.LauncherInvocation.Parts,
            lifetime,
            Loadout.Platform.PlatformServices.CreateProcessTree(launcher),
            // Read here only to name the directory; a daemon built without
            // paths, as the tests that drive its work build one, hosts
            // sessions whose hooks have nowhere to report.
            SignalsOf(paths),
            time,
            // How often idle turned out to be background work, and how often
            // that went quiet past its time: counts only, for the log.
            line => console.WriteLine(line),
            // A tab renamed in Studio names the agent's own conversation
            // too, through whatever command its adapter says does that.
            (agent, name) => agents.Resolve(agent) is { Succeeded: true, Value: { } adapter } ? adapter.RenameInput(name) : null);
        _ideas = ideas;
        _dumps = dumps;
        _launcher = launcher;
        _nominations = nominations;
        _inFlight = new InFlight(commands);
        commands = _inFlight;
        _teams = teams;
        _library = library;
        _workspace = workspace;
        _agents = agents;
        _accessible = accessible;
        _speech = speech;
        _secrets = secrets;
        _client = client;
        _configuration = configuration;
        _schedules = schedules;
        _journal = journal;
        _commands = commands;
        _paths = paths;
        _projects = projects;
        _tasks = tasks;
        _git = git;
        _processes = processes;
        _console = console;
        _time = time;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--port <PORT>")]
        [Description("The port the dashboard listens on. The dashboard-port setting, or 47611, when omitted.")]
        public int Port { get; init; }

        [CommandOption("--listen <ADDRESS>")]
        [Description(
            "The address to listen on, overriding teams.webhook_listen. 127.0.0.1 is this "
            + "machine only; 0.0.0.0 reaches the network you are on.")]
        public string? Listen { get; init; }

        [CommandOption("--no-dashboard")]
        [Description("Fire the schedules and serve nothing.")]
        public bool NoDashboard { get; init; }

        /// <summary>
        /// The daemon this one is replacing, which it waits to see gone.
        /// </summary>
        /// <remarks>
        /// Hidden because it is how a restart hands over, not something
        /// anybody types. The old daemon starts its successor on its way out
        /// and is still, for a moment, the running daemon - without this the
        /// successor would refuse, correctly, because one is already running.
        /// </remarks>
        [CommandOption("--after <PID>", IsHidden = true)]
        public int? After { get; init; }

        [CommandOption("--foreground")]
        [Description(
            "Run in this terminal and end with it, rather than in the background. For a "
            + "service manager, a container, or a window you mean to keep open.")]
        public bool Foreground { get; init; }

        /// <summary>
        /// The log a background daemon writes to, which is also how it knows
        /// it is one.
        /// </summary>
        /// <remarks>
        /// Hidden because it is how <c>daemon</c> starts the daemon it
        /// then shows, not something anybody types. Read by <c>Program</c>
        /// before parsing as well, because by the time this is parsed the
        /// console has already been made.
        /// </remarks>
        [CommandOption("--log <PATH>", IsHidden = true)]
        public string? Log { get; init; }
    }

    /// <summary>What a daemon writes as it hands over to the one replacing it.</summary>
    /// <remarks>
    /// A terminal showing the log reads this to know the daemon it was showing
    /// is being replaced rather than stopped, and to wait for the new one.
    /// </remarks>
    internal const string Restarting = "Restarting: a new daemon is starting with the same settings.";

    /// <summary>How often a terminal showing the daemon looks for more.</summary>
    internal static readonly TimeSpan Follow = TimeSpan.FromMilliseconds(250);

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.DryRun)
        {
            var ready = await ReadyAsync(_time.GetUtcNow(), record: false, cancellationToken)
                .ConfigureAwait(false);

            output.WriteLine(
                $"[dim]Dry run: nothing was started or served.[/] {ready.Count} schedule(s) "
                + "would start now.");

            foreach (var schedule in ready)
            {
                output.WriteLine(
                    $"  {Markup.Escape(schedule.Id)}: {Markup.Escape(schedule.Team)} on "
                    + Markup.Escape(schedule.Project));
            }

            return CommandOutput.Success();
        }

        // Started in the background and shown here, unless asked to stay in
        // this terminal or this already is the one in the background. A daemon
        // run in a terminal ended when the terminal was closed, which is the
        // one thing a resident process is not supposed to do.
        if (!settings.Foreground && settings.Log is null)
        {
            return await BackgroundAsync(settings, output, !Console.IsOutputRedirected, cancellationToken)
                .ConfigureAwait(false);
        }

        /*
          One daemon per machine. A second one started beside the first used to
          overwrite the note and carry on, so the first was forgotten by
          everything that reads the note and kept running anyway: found with
          one started at login still holding 140 MB at 23:00 beside the one
          started from a terminal at 22:44. Two of them would also each fire
          every schedule, which is two runs and twice the money.
        */
        if (settings.After is { } predecessor)
        {
            await PredecessorGoneAsync(predecessor, cancellationToken).ConfigureAwait(false);
        }

        if (DaemonNote.Live(_paths, _processes) is { } running && running.Pid != Environment.ProcessId)
        {
            return output.Fail(
                $"A daemon is already running (process {running.Pid}, since "
                + $"{running.Since.ToLocalTime():yyyy-MM-dd HH:mm})"
                + (running.Address is { Length: > 0 } address ? $", serving {address}" : string.Empty)
                + ". Stop that one first, or use it.",
                ExitCode.InvalidArguments);
        }

        // A stop left behind by the daemon before this one would end this one
        // the moment it looked. A hold is kept: see DaemonControl.
        DaemonControl.ClearStop(_paths);

        // An update no daemon handed over is one whose daemon went without
        // restarting, killed or crashed: carried out at the next restart, it
        // would surprise whoever asked for that one. One handed over belongs
        // to the helper, which may be what started this daemon.
        if (Loadout.Core.Updates.UpdatePlan.Read(_paths) is { DaemonPid: null })
        {
            Loadout.Core.Updates.UpdatePlan.Clear(Loadout.Core.Updates.UpdatePlan.PathFor(_paths));
        }

        _started = settings;

        // What needed you, kept: the daemon is the one thing running long
        // enough to watch for it. Read by the page and by 'loadout events'.
        var events = new Loadout.Core.Events.EventStore(_paths.Paths.State);

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var server = settings.NoDashboard ? null : new DashboardServer(_journal, _git);

        if (server is not null)
        {
            var machine = await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false);
            var teams = machine.Value?.Teams;

            // The flag wins over the configured address, because somebody
            // typing one means it for this run and not for ever.
            // The same port every time unless somebody names one, because an
            // installed app window is tied to the address it was installed
            // from. Taken is reported, never swapped for another.
            var port = settings.Port != 0 ? settings.Port : teams?.DashboardPort ?? DashboardServer.DefaultPort;

            server.SignIn = SignIn.ForMachine(_paths);

            var started = server.Start(
                port,
                settings.Listen is { Length: > 0 } asked ? asked : Webhook.Listen(teams));

            if (started.Failed)
            {
                return settings.Port != 0
                    ? output.Fail(started)
                    : output.Fail(OperationResult.Fail(
                        started.Error + $" To serve on another port from now on: loadout config set dashboard-port <port>",
                        started.ExitCode));
            }

            // The office art, if this machine has any. Read once: a set is a
            // directory somebody filled before starting the daemon.
            var office = OfficeArt.Chosen(_paths, teams?.OfficeSet);

            server.OfficeRoot = office.Root;
            server.OfficeSet = office.Set;
            server.OfficeScale = OfficeScale.Parse(teams?.OfficeScale) ?? OfficeScale.Default;
            server.OfficeCastPins = OfficeCast.Pins(teams?.OfficeCast);
            server.Bin = new TeamBin(_paths);
            server.IsDaemon = true;
            server.Triggers = _triggering;
            server.DaemonPaused = () => DaemonControl.Paused(_paths);
            server.Update = UpdateAsync;
            server.BinDays = TeamBin.Days(teams?.BinDays);
            server.Events = events;
            server.EventsDays = Loadout.Core.Events.EventStore.Days(teams?.EventsDays);

            // The same page the dashboard command would serve. Two dashboards
            // that looked different depending on which command started them
            // would be two dashboards.
            TeamDashboardCommand.Presented(server, view: null, _accessible);

            server.Voice = _speech;
            server.MaySpeak = TeamDashboardCommand.Speaking(_accessible);

            // Read per request rather than once: a schedule made while the
            // daemon is up should appear in the waiting area without somebody
            // having to restart the thing that fires it.
            server.WaitingFor = token => WaitingRoom.ReadAsync(
                _schedules, _tasks, _projects, _time.GetUtcNow(), token);

            // Asked per request rather than read once here, so turning the
            // webhook off with 'team webhook disable' takes effect on the next
            // request rather than the next restart.
            server.Admits = async (token, team, ct) => Webhook.Refuse(
                token,
                await Webhook.TokenAsync(_secrets, ct).ConfigureAwait(false),
                team,
                (await _configuration.LoadMachineAsync(ct).ConfigureAwait(false)).Value?.Teams.WebhookTeams);

            // Through the same parser somebody would type at, exactly as the
            // schedules go. This is the only reason the route lives on the
            // daemon rather than on the dashboard: the dashboard has nothing
            // that could start a run, and should not acquire one.
            server.Trigger = (asking, ct) => TriggeredAsync(asking, output, ct);

            // Everything the page can do to a run, done by running the command
            // somebody would have typed. The page decides nothing; this maps
            // its ask onto a command line and the parser does the rest.
            server.Act = (action, ct) => ActedOnAsync(action, output, ct);

            // And starting one, which is the same rule with a longer wait: the
            // page asks, this types "team run", and the parser decides whether
            // any of it means anything.
            server.Begin = (asking, ct) => BeganAsync(asking, output, ct);

            // And writing one, because the start form was read as the way to
            // make a team and there was no other way from the page at all.
            server.Make = (asking, ct) => DashboardActions.MadeAsync(
                _commands, _time, asking, output, ct);

            // And arranging for one to happen again, which is what this process
            // exists to honour: the daemon is the thing that fires them.
            server.Plan = (asking, ct) => DashboardActions.PlannedAsync(
                _commands, _time, asking, output, ct);

            // And clearing out the runs nobody wants any more. The page could
            // forget one from inside it, which is fine for one and is twenty
            // journeys for twenty.
            server.Clear = (asking, ct) => DashboardActions.ClearedAsync(
                _commands, _time, asking, output, ct);

            // And the ideas: read for the page and worked through from it, by
            // typing the command for each step. A round with an agent runs on
            // here, which outlives the page that started it.
            var ideas = new DashboardIdeas(_commands, _ideas, _dumps, _projects, _time, output);
            server.IdeasFor = ideas.ReadAsync;
            server.Ideate = ideas.DoAsync;

            // And the tasks: read for the page, changed by typing the task
            // command, and a team run on one with 'team run --task'.
            var tasks = new DashboardTasks(_commands, _tasks, _projects, _configuration, _time, output);
            server.TasksFor = tasks.ReadAsync;
            server.Tend = tasks.DoAsync;

            // And one list or one task at a time, through the service itself, so a
            // change from the page is the change 'task apply' makes. Watching only
            // is the routes' own to refuse.
            server.Tasks = _tasks;
            server.Ideas = _ideas;
            server.IdeasBusy = () => ideas.Marked;

            // What there is to start, read per request like the waiting area:
            // a team written a moment ago, from the page or from a terminal,
            // belongs in the next answer rather than the next restart.
            server.Choices = ct => DashboardActions.OfferedAsync(
                _teams, _library, _workspace, _projects, _agents, ct);

            // What this machine is set to, and changing it. Read per request
            // like the teams and the projects: a setting changed from a
            // terminal belongs in the next answer rather than the next restart.
            server.Settings = token => DashboardActions.SetAsync(
                _configuration, _paths, _secrets, token);

            server.Settle = (change, token) => DashboardActions.SettledAsync(
                _commands, _time, change, output, token);

            // And the second credential, which the dashboard's own token does
            // not grant: typing at a live node is not something the run
            // offered to have decided.
            server.Attach = new Attaching(_secrets, _time);

            // Sessions hosted for Studio, which end when this daemon does,
            // so the note says how many are running for 'daemon stop' to warn.
            server.Sessions = _sessions;
            server.PseudoTerminal = Loadout.Platform.PlatformServices.PseudoTerminalName;

            // The history Studio resumes from, through the same service as
            // 'loadout resume', and registering the folder an unregistered
            // conversation ran in through 'project add' itself.
            server.Resumption = _resumption;
            server.Register = RegisterAsync;

            // What earlier daemons' sessions left: kept a week, so a page
            // opened after a restart can still offer to resume one.
            Loadout.Core.Sessions.Hosting.SessionSignals.Prune(
                Loadout.Core.Sessions.Hosting.SessionSignals.Directory(_paths.Paths.State), _time.GetUtcNow());
            _sessions.Changed += HostedChanged;

            // Which page the address opens, read each time so setting it is
            // seen at the next load rather than the next restart.
            server.StudioFirst = async token => DashboardServer.OpensStudio(
                (await _configuration.LoadMachineAsync(token).ConfigureAwait(false)).Value?.Teams.Dashboard);

            // And Studio's memory page: read through Core, saved by typing
            // 'memory replace'.
            server.Memory = new DashboardMemory(_commands, _memory, _importer, _projects, _workspace, _time, output);

            // And the New session sheet, from the same source as the
            // terminal launcher's, and Studio's own settings, read per
            // request and changed through 'config set' like any other.
            // And every setting, for Studio's Settings page to show; changing
            // one runs 'config set' like any other job.
            server.Configuration = _configuration;

            // And every other command, run from Studio's forms as a child of
            // this launcher, so the page does what typing it would.
            server.Jobs = new CommandJobs(
                new DashboardCommands(_launcher),
                _time,
                path => output.WriteLine(
                    $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] from Studio: loadout {Markup.Escape(path)}"))
            {
                // A team run typed into the Commands page is a run too.
                Holding = path => string.Equals(path, "team run", StringComparison.Ordinal) ? Holding() : null,
            };

            server.Launch = new DashboardLaunch(_projects, _workspace, _agents, _configuration, _sheet, _time);
            server.StudioPreferences = async token => StudioPreferences.Read(
                (await _configuration.LoadConfigAsync(token).ConfigureAwait(false)).Value?.Studio);

            _address = server.Address;

            foreach (var address in server.Reachable())
            {
                output.WriteLine($"[bold]{Markup.Escape(address)}[/]");
            }

            TeamDashboardCommand.Warn(output, server);

            if (await Webhook.TokenAsync(_secrets, cancellationToken).ConfigureAwait(false) is not null)
            {
                output.WriteLine(
                    $"[dim]accepting triggered runs of: "
                    + $"{Markup.Escape(teams?.WebhookTeams is { Count: > 0 } named ? string.Join(", ", named) : "nothing")}[/]");
            }
        }

        var kept = (await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false)).Value?.Teams.EventsDays;

        _watch = new Loadout.Core.Events.EventWatch(events, _journal, _sessions, _time);
        _watch.Start(Loadout.Core.Events.EventStore.Days(kept));

        if (await TellingAsync(cancellationToken).ConfigureAwait(false) is { Length: > 0 })
        {
            var where = (await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false))
                .Value?.Teams.NotifyKind;

            if (where is { Length: > 0 })
            {
                output.WriteLine($"[dim]telling {Markup.Escape(where)} when a run needs you[/]");
            }
        }

        await WriteStatusAsync(server?.Address, cancellationToken).ConfigureAwait(false);

        output.WriteLine("[dim]Watching the schedules. " + (settings.Log is not null
            ? "Running in the background: 'loadout daemon stop' stops it.[/]"
            : Console.IsInputRedirected
                ? "It stops when whatever started it closes its input, or on 'loadout daemon stop'.[/]"
                : "Press Ctrl+C or run 'loadout daemon stop' to stop.[/]"));

        if (DaemonControl.Paused(_paths))
        {
            output.WriteLine(
                "[yellow]Paused:[/] no schedule fires until 'loadout daemon resume'. "
                + "It was held when the last daemon stopped, and a hold is kept until it is lifted.");
        }

        var serving = server is null
            ? Task.CompletedTask
            : server.ListenAsync(stopping.Token);

        // Not in the background, where there is nothing on the other end of the
        // input and nothing that closing it would mean.
        var ending = settings.Log is null ? TeamDashboardCommand.Ends(stopping) : Task.CompletedTask;
        var watching = WatchAsync(stopping, output);
        var recording = _watch?.WatchAsync(stopping.Token) ?? Task.CompletedTask;
        var looking = _updateWatch?.WatchAsync(stopping.Token) ?? Task.CompletedTask;

        var triggering = TriggersAsync(output, stopping.Token);

        await LoopsAsync(output, stopping.Token).ConfigureAwait(false);

        await stopping.CancelAsync().ConfigureAwait(false);
        await triggering.ConfigureAwait(false);

        try
        {
            await serving.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        // The runs it started have heard the stop, and each writes how far it
        // got before it goes. Leaving first took them mid-round, and a journal
        // with no ending reads as a run still going, for good.
        if (!await _inFlight.SettledAsync(Unwinding, _time).ConfigureAwait(false))
        {
            output.WriteLine($"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] "
                + $"{_inFlight.Running} run(s) had not finished ending after {Unwinding.TotalSeconds:0} seconds, "
                + "and go with the daemon.");
        }

        // A last look while the sessions are still there to be looked at,
        // and the history told that nothing is watched from here.
        await recording.ConfigureAwait(false);
        await looking.ConfigureAwait(false);
        _watch?.Stop();

        // After the listener, so no page can start one while the rest end,
        // and with the note closed first, so ending them does not write it.
        _sessions.Changed -= HostedChanged;
        await _statusGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        _statusClosed = true;
        _statusGate.Release();
        await _sessions.DisposeAsync().ConfigureAwait(false);

        // Not waited for when the stop came from somewhere else. It is a read
        // of standard input waiting to see it close, and a read of a pipe on
        // Windows does not hear its cancellation: a daemon told to stop by
        // 'daemon stop' sat here, stopped in every other sense, until
        // whatever started it closed its input - which for a daemon started
        // with its input held open is never. The process ending takes it.
        await Task.WhenAny(ending, Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None))
            .ConfigureAwait(false);
        await watching.ConfigureAwait(false);

        var request = DaemonControl.Stopping(_paths);

        DaemonControl.ClearStop(_paths);

        Forget();

        var update = Loadout.Core.Updates.UpdatePlan.Read(_paths);

        if (request is { Restart: true } && _started is { } was)
        {
            if (update is not null && HandOver(output, server?.Address, was, update))
            {
                return (int)ExitCode.Success;
            }

            Succeed(output, server?.Address, was);
        }
        else if (update is not null)
        {
            // Stopped rather than restarted while an update waited. Whoever
            // stopped it has not asked for it to come back on a new version
            // the next time somebody starts it, so the update goes with it.
            Loadout.Core.Updates.UpdatePlan.Clear(Loadout.Core.Updates.UpdatePlan.PathFor(_paths));
            output.WriteLine($"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] Stopped, so the update that was "
                + $"waiting ({Markup.Escape(update.Described)}) was dropped. Ask for it again from Studio, or: loadout update");
        }

        return (int)ExitCode.Success;
    }

    /// <summary>
    /// Hands the restart to the update helper, which replaces this daemon's
    /// binary once it has gone and then starts its successor. Says whether it
    /// did: when it could not, the restart goes ahead without the update.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The helper runs from a copy outside the install folder. Replacing a
    /// binary from inside it works for an archive install, where the running
    /// image is renamed aside, but the MSI ends every <c>loadout.exe</c> on
    /// its way in and replaces the folder, so a helper running from there
    /// would be ended by the install it started.
    /// </para>
    /// <para>
    /// Internal so a test can see what it starts without a daemon to stop.
    /// </para>
    /// </remarks>
    internal bool HandOver(CommandOutput output, string? address, Settings was, Loadout.Core.Updates.UpdatePlan plan)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(was);
        ArgumentNullException.ThrowIfNull(plan);

        var path = Loadout.Core.Updates.UpdatePlan.PathFor(_paths);
        var when = $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] ";

        if (Loadout.Core.Agents.LauncherInvocation.Parts() is not ({ Length: > 0 } command, var prefix))
        {
            Loadout.Core.Updates.UpdatePlan.Clear(path);
            output.WriteLine(when + "[red]Could not update:[/] this launcher cannot say how it was started, "
                + "so it restarts on the version it is.");

            return false;
        }

        try
        {
            var helper = Helper(command, prefix);
            var daemon = new List<string> { command };

            daemon.AddRange(Arguments(prefix, was, Port(address, was)));

            plan.WithDaemon(daemon, _processes.CurrentProcessId, _processes.CurrentProcessStartedAt)
                .WriteAsync(path).GetAwaiter().GetResult();

            var arguments = new List<string>(helper.Skip(1)) { "update", "--finish", path };

            var started = _launcher.StartBackground(new ProcessRequest(
                helper[0],
                arguments,
                WorkingDirectory: CurrentDirectory()));

            if (started.Failed)
            {
                Loadout.Core.Updates.UpdatePlan.Clear(path);
                output.WriteLine(when + $"[red]Could not update:[/] the helper did not start "
                    + $"({Shown.Safely(started.Error ?? "no reason given")}), so it restarts on the version it is.");

                return false;
            }

            output.WriteLine(when + $"Restarting for the update ({Markup.Escape(plan.Described)}): the update helper "
                + $"is process {started.Value!.Pid}, and says how it went below.");

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Loadout.Core.Updates.UpdatePlan.Clear(path);
            output.WriteLine(when + $"[red]Could not update:[/] {Markup.Escape(Shown.Safely(ex.Message))}. "
                + "It restarts on the version it is.");

            return false;
        }
    }

    /// <summary>
    /// The command line that runs the update helper: a copy of this
    /// executable outside the install folder, or, for a development build
    /// run through dotnet, the build itself.
    /// </summary>
    private List<string> Helper(string command, IReadOnlyList<string> prefix)
    {
        if (prefix.Count > 0)
        {
            return [command, .. prefix];
        }

        var folder = Path.Combine(_paths.Paths.Cache, "update-helper");

        Directory.CreateDirectory(folder);

        // Earlier copies, which have finished. One still running is in use
        // and is left; it is tidied by the next update.
        foreach (var old in Directory.EnumerateFiles(folder))
        {
            try
            {
                File.Delete(old);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        // Not named loadout: the MSI ends every process of that name on its
        // way in, and this one is the process running it.
        var copy = Path.Combine(folder, "loadout-update-"
            + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(command));

        // File.Copy keeps the mode bits on Unix, so the copy runs as the
        // original does.
        File.Copy(command, copy);

        return [copy];
    }

    /// <summary>
    /// The port a successor serves on: the same as before when this one was
    /// serving, even if it had been left to the machine to choose.
    /// </summary>
    private static int Port(string? address, Settings was) =>
        was.Port == 0 && address is { Length: > 0 } && Uri.TryCreate(address, UriKind.Absolute, out var served)
            ? served.Port
            : was.Port;

    private readonly Loadout.Core.Sessions.Hosting.HostedSessions _sessions;

    /// <summary>The daily look at the release feed, for Studio's badge; null in a build without updates.</summary>
    private readonly Loadout.Core.Updates.UpdateWatch? _updateWatch;

    /// <summary>
    /// What Studio's Settings shows about updates: what the feed offers, how
    /// this copy was installed, what a restart now would cut off, and an
    /// update already waiting for the restart.
    /// </summary>
    internal async Task<object> UpdateAsync(bool fresh, CancellationToken ct)
    {
        var status = _updateWatch is null
            ? null
            : fresh ? await _updateWatch.CheckNowAsync(ct).ConfigureAwait(false) : _updateWatch.Status;
        var asked = DaemonControl.Stopping(_paths);
        var plan = asked is { Restart: true } ? Loadout.Core.Updates.UpdatePlan.Read(_paths) : null;

        return new
        {
            current = status?.Current,
            available = status?.Available,
            isNewer = status?.IsNewer ?? false,
            notes = status?.Notes,
            checkedAt = status?.CheckedAt,
            problem = status?.Problem,
            channel = status?.Channel,
            canRollBack = status?.CanRollBack ?? false,
            automatic = status?.Automatic ?? false,
            runs = _inFlight.Running,
            hosted = _sessions.Running,
            pending = plan is null
                ? null
                : new
                {
                    kind = plan.Kind == Loadout.Core.Updates.UpdateKind.RollBack ? "roll-back" : "install",
                    version = plan.Version,
                    now = asked!.Now,
                    described = plan.Described,
                },
        };
    }

    /// <summary>
    /// Registers a folder for 'Register and resume', returning its slug.
    /// </summary>
    /// <remarks>
    /// Through 'project add', so Studio registers a folder exactly as a
    /// terminal does. The command reports only its exit code back, so the
    /// slug is read from the registry afterwards rather than parsed from
    /// what it printed.
    /// </remarks>
    private async Task<OperationResult<string>> RegisterAsync(string directory, CancellationToken ct)
    {
        var exit = await _commands.RunAsync("project add", [directory], ct).ConfigureAwait(false);

        if (exit != 0)
        {
            return OperationResult<string>.Fail(
                $"'loadout project add' did not register {directory} (exit code {exit}). Run it in a terminal to see why.");
        }

        var resolved = await _projects.ResolveFromDirectoryAsync(directory, ct).ConfigureAwait(false);

        return resolved.Succeeded
            ? OperationResult<string>.Ok(resolved.Value!.Entry.Slug)
            : OperationResult<string>.Fail(resolved.Error!, resolved.ExitCode);
    }

    /// <summary>Where hosted sessions' hooks report, or null for a daemon built without paths.</summary>
    private static string? SignalsOf(IPlatformPaths? paths) =>
        paths?.Paths is { } where ? Loadout.Core.Sessions.Hosting.SessionSignals.Directory(where.State) : null;
    private readonly Loadout.Core.Instructions.IMemoryService _memory;
    private readonly Loadout.Core.Sessions.LaunchSheet _sheet;
    private readonly Loadout.Core.Instructions.IMemoryImporter _importer;

    /// <summary>The settings this daemon was started with, for a restart to use again.</summary>
    private Settings? _started;

    /// <summary>
    /// Starts the daemon that replaces this one, with the settings this one had.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The daemon restarts itself rather than being restarted from outside,
    /// because it is the only thing that knows how it was started and it is
    /// the thing that knows when its runs have finished. A restart asked for
    /// while a run is going would otherwise mean the command that asked
    /// sitting in somebody's terminal for twenty minutes, or giving up and
    /// starting nothing.
    /// </para>
    /// <para>
    /// The same port as before when this one was serving, even if it had
    /// been left to the machine to choose: a bookmarked page should still be
    /// there after a restart. Started in the background, writing to the same
    /// log, so a terminal showing this one goes on to show its successor.
    /// </para>
    /// </remarks>
    internal void Succeed(CommandOutput output, string? address, Settings was)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(was);

        if (Loadout.Core.Agents.LauncherInvocation.Parts() is not ({ Length: > 0 } command, var prefix))
        {
            output.WriteLine("[red]Could not restart:[/] this launcher cannot say how it was started. "
                + "Start the daemon again with: loadout daemon");

            return;
        }

        var arguments = Arguments(prefix, was, Port(address, was));

        arguments.Add("--after");
        arguments.Add(_processes.CurrentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // In the background whichever way this one was started. A daemon in a
        // terminal cannot start its successor in that terminal, and a new
        // window of its own is one more window that ends it when it is closed.
        var started = _launcher.StartBackground(new ProcessRequest(
            command,
            arguments,
            WorkingDirectory: CurrentDirectory()));

        output.WriteLine(started.Succeeded
            ? Restarting
            : $"[red]Could not restart:[/] {Shown.Safely(started.Error ?? "the new daemon did not start")}. "
              + "Start it again with: loadout daemon");
    }

    /// <summary>
    /// The command line for a daemon in the background with these settings.
    /// </summary>
    private List<string> Arguments(IReadOnlyList<string> prefix, Settings was, int port)
    {
        var arguments = new List<string>(prefix) { "daemon" };

        if (was.NoDashboard)
        {
            arguments.Add("--no-dashboard");
        }
        else if (port > 0)
        {
            arguments.Add("--port");
            arguments.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (was.Listen is { Length: > 0 } listen)
        {
            arguments.Add("--listen");
            arguments.Add(listen);
        }

        arguments.Add(DaemonLog.Flag);
        arguments.Add(DaemonLog.PathFor(_paths));

        return arguments;
    }

    /// <summary>
    /// Starts the daemon in the background and, where somebody is watching,
    /// shows what it says until they stop watching.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what <c>daemon</c> does now. It used to be the daemon, in
    /// the terminal, and ended when the terminal was closed: a console program
    /// goes with the window it is attached to, and there is no refusing that.
    /// Now the daemon has no window to go with, and this terminal only shows
    /// its log. Closing it, or Ctrl+C, stops the showing.
    /// </para>
    /// <para>
    /// Where nobody is watching - output going to a file, a pipe, a login item
    /// with no terminal - it waits only until the daemon says where it is, and
    /// returns. Following a log into a pipe for ever helps nobody.
    /// </para>
    /// <para>
    /// A daemon already running is shown rather than refused when somebody is
    /// watching: that is what somebody who closed the terminal and wants to see
    /// it again types. Where nobody is, it is refused as it always was, since a
    /// script asking to start one needs to know that it did not.
    /// </para>
    /// </remarks>
    internal async Task<int> BackgroundAsync(Settings settings, CommandOutput output, bool watched, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(output);

        var log = DaemonLog.PathFor(_paths);

        if (DaemonNote.Live(_paths, _processes) is { } running)
        {
            var described = $"A daemon is already running (process {running.Pid}, since "
                + $"{running.Since.ToLocalTime():yyyy-MM-dd HH:mm})"
                + (running.Address is { Length: > 0 } address ? $", serving {address}" : string.Empty);

            if (!watched)
            {
                return output.Fail(
                    described + $". Stop that one first, or use it. What it says is in {log}.",
                    ExitCode.InvalidArguments);
            }

            output.WriteLine(Markup.Escape(described) + ".");
            output.WriteLine(
                "[dim]Showing what it says from here on. It keeps the settings it was started with; "
                + "stop it first to change them. Ctrl+C stops showing it, not the daemon.[/]");

            return await FollowAsync(
                log,
                DaemonLog.Length(log),
                new BackgroundProcess(running.Pid, running.StartedAt),
                output,
                ct).ConfigureAwait(false);
        }

        if (Loadout.Core.Agents.LauncherInvocation.Parts() is not ({ Length: > 0 } command, var prefix))
        {
            return output.Fail(
                "This launcher cannot say how it was started, so it cannot start itself in the background. "
                + "Run it in this terminal instead with: loadout daemon --foreground",
                ExitCode.GeneralFailure);
        }

        // Where the log is now, so what is shown is this daemon's and not the
        // last one's.
        var from = DaemonLog.Length(log);

        var started = _launcher.StartBackground(new ProcessRequest(
            command,
            Arguments(prefix, settings, settings.Port),
            WorkingDirectory: CurrentDirectory()));

        if (started.Failed)
        {
            return output.Fail(started);
        }

        var daemon = started.Value!;

        if (!watched)
        {
            return await SettledAsync(log, from, daemon, output, ct).ConfigureAwait(false);
        }

        output.WriteLine(
            $"[dim]Started in the background as process {daemon.Pid}. What it says is shown here, "
            + $"and kept in {Markup.Escape(log)}.[/]");
        output.WriteLine(
            "[dim]Closing this terminal or pressing Ctrl+C stops showing it; the daemon keeps running. "
            + "'loadout daemon stop' stops it.[/]");

        return await FollowAsync(log, from, daemon, output, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Shows the daemon's log as it grows, for as long as the daemon runs or
    /// until whoever is watching stops.
    /// </summary>
    /// <remarks>
    /// Follows a restart onto the daemon that replaces this one, because the
    /// person watching asked to see the daemon, not one process of it.
    /// </remarks>
    internal async Task<int> FollowAsync(
        string log,
        long from,
        BackgroundProcess daemon,
        CommandOutput output,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(daemon);
        ArgumentNullException.ThrowIfNull(output);

        var position = from;
        var current = daemon;

        // Whether it got as far as writing its note. One that ends before then
        // never started, and that is a failure rather than a stop.
        var settled = false;
        var restarting = false;

        try
        {
            while (true)
            {
                restarting |= Show(DaemonLog.Since(log, ref position));

                if (DaemonNote.Read(_paths) is { } note && note.Pid == current.Pid)
                {
                    settled = true;
                }

                if (!_processes.IsRunning(current.Pid, current.StartedAt))
                {
                    restarting |= Show(DaemonLog.Since(log, ref position));

                    if (restarting && await SuccessorAsync(current.Pid, ct).ConfigureAwait(false) is { } next)
                    {
                        current = new BackgroundProcess(next.Pid, next.StartedAt);
                        restarting = false;

                        continue;
                    }

                    if (!settled)
                    {
                        return output.Fail(
                            $"The daemon stopped before it had started. What it said is above, and in {log}.",
                            ExitCode.GeneralFailure);
                    }

                    output.WriteLine("[dim]The daemon has stopped.[/]");

                    return CommandOutput.Success();
                }

                await Task.Delay(Follow, _time, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            Show(DaemonLog.Since(log, ref position));

            output.WriteLine(
                $"Stopped showing it. The daemon is still running as process {current.Pid}: "
                + "'loadout daemon' shows it again, 'loadout daemon stop' stops it.");

            return CommandOutput.Success();
        }
    }

    /// <summary>
    /// Waits for a daemon nobody is watching to say where it is, and says it.
    /// </summary>
    internal async Task<int> SettledAsync(
        string log,
        long from,
        BackgroundProcess daemon,
        CommandOutput output,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(daemon);
        ArgumentNullException.ThrowIfNull(output);

        var until = _time.GetUtcNow() + Handover;

        while (_time.GetUtcNow() < until)
        {
            if (DaemonNote.Read(_paths) is { } note && note.Pid == daemon.Pid)
            {
                output.WriteLine($"Started the daemon in the background (process {daemon.Pid}).");

                if (note.Address is { Length: > 0 } address)
                {
                    output.WriteLine($"[bold]{Markup.Escape(address)}[/]");
                }

                output.WriteLine($"[dim]What it says is in {Markup.Escape(log)}. "
                    + "'loadout daemon stop' stops it.[/]");

                return CommandOutput.Success();
            }

            if (!_processes.IsRunning(daemon.Pid, daemon.StartedAt))
            {
                var position = from;
                var said = DaemonLog.Since(log, ref position).Trim();

                return output.Fail(
                    "The daemon stopped before it had started"
                    + (said.Length > 0 ? $": {said}" : $". Nothing was written to {log}."),
                    ExitCode.GeneralFailure);
            }

            await Task.Delay(Follow, _time, ct).ConfigureAwait(false);
        }

        return output.Fail(
            $"The daemon (process {daemon.Pid}) was started but has not said where it is after "
            + $"{Handover.TotalSeconds:0} seconds. What it has said is in {log}.",
            ExitCode.GeneralFailure);
    }

    /// <summary>The daemon that has replaced this one, once it is there.</summary>
    private async Task<DaemonState?> SuccessorAsync(int was, CancellationToken ct)
    {
        var until = _time.GetUtcNow() + Handover;

        while (_time.GetUtcNow() < until)
        {
            if (DaemonNote.Live(_paths, _processes) is { } now && now.Pid != was)
            {
                return now;
            }

            await Task.Delay(Follow, _time, ct).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>Writes what the daemon said, as it said it.</summary>
    /// <returns>Whether it said it was handing over to a successor.</returns>
    /// <remarks>
    /// Straight to the terminal rather than as markup: the daemon has already
    /// decided what its lines say, and a square bracket in a goal or a path
    /// would otherwise be read as a style.
    /// </remarks>
    private bool Show(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        _console.Profile.Out.Writer.Write(text);
        _console.Profile.Out.Writer.Flush();

        return text.Contains(Restarting, StringComparison.Ordinal);
    }

    private static string? CurrentDirectory()
    {
        try
        {
            return Directory.GetCurrentDirectory();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Waits for the daemon this one replaces to have gone, for a little while.
    /// </summary>
    /// <remarks>
    /// Bounded, because a predecessor that never goes must not leave its
    /// successor waiting in a window for ever. If it is still there, the
    /// ordinary one-daemon check below refuses and says so.
    /// </remarks>
    private async Task PredecessorGoneAsync(int pid, CancellationToken ct)
    {
        var until = _time.GetUtcNow() + Handover;

        while (_time.GetUtcNow() < until
            && DaemonNote.Read(_paths) is { } note
            && note.Pid == pid
            && _processes.IsRunning(note.Pid, note.StartedAt))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), _time, ct).ConfigureAwait(false);
        }

        // The note can be gone while the process is still closing its port.
        // A moment more is cheaper than a restart that fails to listen.
        await Task.Delay(TimeSpan.FromSeconds(1), _time, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Watches for somebody asking this daemon to stop or restart, and ends it
    /// when it should.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Now" ends it at once, which ends the runs it started the way Ctrl+C
    /// always has: they are commands inside this process. Otherwise it stops
    /// starting anything new and waits for what it is running to finish,
    /// because a node mid-turn that is killed loses the turn and the money
    /// spent on it - the same reasoning that makes a run's own stop land
    /// between rounds.
    /// </para>
    /// <para>
    /// Internal so the tests can drive it with a request and a held run
    /// rather than a whole daemon.
    /// </para>
    /// </remarks>
    internal async Task WatchAsync(CancellationTokenSource stopping, CommandOutput output)
    {
        ArgumentNullException.ThrowIfNull(stopping);

        var said = false;

        try
        {
            while (!stopping.IsCancellationRequested)
            {
                if (DaemonControl.Stopping(_paths) is { } asked)
                {
                    var what = !asked.Restart
                        ? "Stopping"
                        : Loadout.Core.Updates.UpdatePlan.Read(_paths) is { } update
                            ? $"Restarting for the update ({Markup.Escape(update.Described)})"
                            : "Restarting";

                    if (asked.Now || _inFlight.Running == 0)
                    {
                        output.WriteLine($"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] {what}"
                            + (_inFlight.Running > 0 ? $", ending {_inFlight.Running} run(s) it started." : "."));

                        await stopping.CancelAsync().ConfigureAwait(false);

                        return;
                    }

                    if (!said)
                    {
                        output.WriteLine($"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] {what} when the "
                            + $"{_inFlight.Running} run(s) it started have finished. Nothing new starts meanwhile.");
                        said = true;
                    }
                }

                await Task.Delay(Glance, _time, stopping.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped some other way: Ctrl+C, or the input closing.
        }
    }

    /// <summary>
    /// The command catalogue, counting what is running through it.
    /// </summary>
    internal sealed class InFlight(ICommandCatalogue inner) : ICommandCatalogue
    {
        private int _running;

        /// <summary>How many commands are running through this now.</summary>
        public int Running => Volatile.Read(ref _running);

        /// <summary>
        /// Waits for every command running through this to return, or for the
        /// time to run out; true when nothing is left running.
        /// </summary>
        public async Task<bool> SettledAsync(TimeSpan within, TimeProvider time)
        {
            ArgumentNullException.ThrowIfNull(time);

            var until = time.GetUtcNow() + within;

            while (Running > 0 && time.GetUtcNow() < until)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), time, CancellationToken.None).ConfigureAwait(false);
            }

            return Running == 0;
        }

        public IReadOnlyList<CatalogueEntry> Commands => inner.Commands;

        public async Task<int> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _running);

            try
            {
                return await inner.RunAsync(path, arguments, ct).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }

    /// <summary>
    /// Starts a run something outside this machine asked for.
    /// </summary>
    /// <remarks>
    /// The same command the schedules run, with the same flags, because a run
    /// started from outside is not a different kind of run. Non-interactive,
    /// which is the honest description and also what refuses a manual team:
    /// manual means a person at every step, and a webhook is the case where
    /// there is nobody.
    /// </remarks>
    internal async Task<OperationResult> TriggeredAsync(
        TriggerRequest asking,
        CommandOutput output,
        CancellationToken ct)
    {
        if (asking.Project is not { Length: > 0 } project)
        {
            return OperationResult.Fail(
                "A triggered run needs a project: add \"project\" to the body.",
                ExitCode.InvalidArguments);
        }

        // First, whatever would start the run: a daemon waiting to restart
        // for an update takes on nothing new, from a rule or otherwise.
        if (Holding() is { } held)
        {
            return OperationResult.Fail(held, ExitCode.PolicyViolation);
        }

        // Where a rule waits for webhooks, a webhook is one more event and the
        // rules decide what it starts, under their caps: one path for every
        // event, rather than one that spends without asking the ledger. Where
        // none does, it starts its team as it always has, so a webhook set up
        // before rules existed keeps working rather than going quiet.
        if (_spine is { } spine && await WebhookRuledAsync(ct).ConfigureAwait(false))
        {
            return await PostedAsync(spine, Loadout.Core.Triggers.WebhookEvents.From(asking, project, _time.GetUtcNow()), output, ct).ConfigureAwait(false);
        }

        output.WriteLine(
            $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] triggered: "
            + $"{Markup.Escape(asking.Team)} on {Markup.Escape(project)}");

        /*
          Answered as soon as the run is under way rather than when it finishes,
          which is the same shape the page's own start takes and for a sharper
          reason.

          This waited for the whole run - twenty minutes on a good day - and
          then replied "Started". Whatever called it had given up long before:
          a GitHub webhook waits ten seconds and then retries, and every retry
          came through here as another run of the same team on the same
          repository, merging into the same branch. The reply said 202 and
          meant "finished", which is the one thing 202 does not mean.
        */
        _ = Task.Run(
            async () =>
            {
                try
                {
                    var code = await _commands.RunAsync(
                        "team run",
                        [asking.Team, asking.Goal, "--project", project, "--non-interactive"],
                        ct).ConfigureAwait(false);

                    if (code != (int)ExitCode.Success)
                    {
                        output.WriteLine(
                            $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] "
                            + $"{Markup.Escape(asking.Team)} ended with exit code {code}.");
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
                {
                    output.WriteLine(
                        $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] "
                        + $"{Markup.Escape(asking.Team)} could not be started: {Shown.Safely(ex.Message)}");
                }
            },
            CancellationToken.None);

        return OperationResult.Ok();
    }

    /// <summary>Whether any usable rule waits for webhooks, read afresh so a new rule takes effect at once.</summary>
    private async Task<bool> WebhookRuledAsync(CancellationToken ct)
    {
        if (_yaml is not { } yaml)
        {
            return false;
        }

        var listed = await new Loadout.Core.Triggers.TriggerRules(_paths.Paths.State, yaml).ListAsync(ct).ConfigureAwait(false);

        return listed.Succeeded
            && listed.Value!.Any(one => one.Usable && one.Rule.Enabled
                && string.Equals(one.Rule.On, Loadout.Core.Triggers.TriggerEventKinds.Webhook, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Starts a team because somebody asked from the page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Answered as soon as the run is under way rather than when it finishes.
    /// A team run takes twenty minutes on a good day and a browser holding a
    /// request open that long has already given up; the run appears in the
    /// list within a few seconds, which is the answer somebody wanted.
    /// </para>
    /// <para>
    /// Which means a failure has nowhere to be reported to. It is written to
    /// the daemon's own output, where the schedules report theirs, and the run
    /// that did not start is simply not in the list - which is the same thing
    /// somebody sees when a schedule fails, rather than a new kind of silence.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Starts a team, which the page asked for.
    /// </summary>
    /// <remarks>
    /// The work is in <see cref="DashboardActions" />, because
    /// <c>dashboard</c> draws the same form and answered it with "this
    /// server does not start runs".
    /// </remarks>
    internal Task<OperationResult> BeganAsync(
        StartRequest asking,
        CommandOutput output,
        CancellationToken ct) =>
        Holding() is { } held
            ? Task.FromResult(OperationResult.Fail(held, ExitCode.PolicyViolation))
            : DashboardActions.BeganAsync(_commands, _time, asking, output, ct);

    /// <summary>
    /// Why nothing new may start now, or null when it may: the daemon has
    /// been asked to stop or restart, for an update or otherwise, and is
    /// waiting for the runs it started.
    /// </summary>
    /// <remarks>
    /// The schedules already hold while it waits. A run started from the
    /// page or by a webhook meanwhile would be one more run to wait for, or,
    /// started just as the last one ended, a run the restart cut off.
    /// </remarks>
    internal string? Holding()
    {
        // A daemon built without paths, as some tests build it, has no controls to read.
        if (_paths is null || DaemonControl.Stopping(_paths) is not { } asked)
        {
            return null;
        }

        var waiting = _inFlight.Running == 1 ? "the run it started has" : $"the {_inFlight.Running} runs it started have";

        return asked.Restart && Loadout.Core.Updates.UpdatePlan.Read(_paths) is { } update
            ? $"An update is pending: the daemon restarts for it ({update.Described}) once {waiting} finished, "
              + "and starts nothing new meanwhile."
            : $"The daemon is {(asked.Restart ? "restarting" : "stopping")} once {waiting} finished, "
              + "and starts nothing new meanwhile.";
    }

    /// <summary>
    /// Does what the page asked of a run, through the command line.
    /// </summary>
    /// <remarks>
    /// Every verb here is a command that exists and that somebody could type.
    /// Nothing about answering a gate, saying something to a lead or stopping a
    /// run is implemented twice: the page asks, this types it, and whatever the
    /// terminal would have done happens.
    /// </remarks>
    /// <summary>
    /// Does what a button asked.
    /// </summary>
    /// <remarks>
    /// The mapping itself is in <see cref="DashboardActions" />, because
    /// <c>dashboard</c> serves the same page and drew the same buttons
    /// while answering every one of them with "this server only reads".
    /// </remarks>
    private Task<OperationResult> ActedOnAsync(
        RunAction action,
        CommandOutput output,
        CancellationToken ct) =>
        DashboardActions.RanAsync(_commands, _time, action, output, ct);

    /// <summary>Whether triggers were switched on when this daemon started.</summary>
    private readonly bool _triggering;

    private readonly Loadout.Core.Configuration.YamlStore? _yaml;
    private readonly IFilePermissions? _permissions;
    private readonly IExecutableResolver? _resolver;

    /// <summary>Reads from GitHub for the trigger source; a test scripts it.</summary>
    internal Loadout.Core.Triggers.IGitHubReads? GitHubReads { get; init; }

    /// <summary>Finds the token the GitHub source reads with; a test supplies one.</summary>
    internal Func<CancellationToken, Task<Loadout.Core.Triggers.TriggerCredential>>? GitHubCredential { get; init; }

    /// <summary>How often each watched repository is read when quota is plentiful; a test shortens it.</summary>
    internal TimeSpan GitHubEvery { get; init; } = Loadout.Core.Triggers.GitHubSource.Every;

    /// <summary>How long a found token is used before it is looked for again, which is how a renewal is noticed.</summary>
    internal TimeSpan CredentialEvery { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>The trigger spine, while it runs, or null with triggers off.</summary>
    private Loadout.Core.Triggers.TriggerSpine? _spine;

    /// <summary>How often finished runs are looked for; a test shortens it.</summary>
    internal TimeSpan TriggerEvery { get; init; } = Interval;

    /// <summary>
    /// Carries events to the trigger rules and starts what they match, until
    /// told to stop. Nothing at all while the switch is off.
    /// </summary>
    /// <remarks>
    /// On a task of its own beside the schedules, the notices and the queues,
    /// for the reason they each have one: the schedules' loop waits for every
    /// run it starts, and an event that waited behind one would be acted on
    /// against a state that had moved on.
    /// </remarks>
    internal async Task TriggersAsync(CommandOutput output, CancellationToken ct)
    {
        if (!_triggering || _yaml is not { } yaml)
        {
            return;
        }

        var state = _paths.Paths.State;
        var journal = new Loadout.Core.Triggers.TriggerJournal(state, _permissions);
        var spine = new Loadout.Core.Triggers.TriggerSpine(
            new Loadout.Core.Triggers.TriggerRules(state, yaml),
            journal,
            new DaemonTriggerActions(_commands, _journal, _teams, _library, _workspace),
            _time);
        var finished = new Loadout.Core.Triggers.RunFinishedSource(state, _journal);
        var rules = new Loadout.Core.Triggers.TriggerRules(state, yaml);
        var drift = new Loadout.Core.Triggers.DocsDriftSource(state, _projects, _git, DocsPolicyAsync);
        var clock = new Loadout.Core.Triggers.ClockSource(state);
        var github = new Loadout.Core.Triggers.GitHubSource(
            state, GitHubReads ?? new Loadout.Core.Triggers.GitHubApi(_client), GitHubEvery);
        var allow = new Loadout.Core.Triggers.TriggerAllowStore(state, yaml);
        var credential = Loadout.Core.Triggers.TriggerCredential.Missing;
        var credentialAt = DateTimeOffset.MinValue;
        var drifted = DateTimeOffset.MinValue;
        var pruned = DateTimeOffset.MinValue;

        _spine = spine;

        var running = spine.RunAsync(ct);
        var answering = AnswerAsync(spine, new Loadout.Core.Triggers.TriggerInbox(state), output, ct);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await LookedAsync(
                    "finished runs could not be read for triggers",
                    async () =>
                    {
                        foreach (var one in finished.Look())
                        {
                            await spine.PostAsync(one, ct).ConfigureAwait(false);
                        }
                    },
                    output).ConfigureAwait(false);

                await LookedAsync(
                    "clock rules could not be read for triggers",
                    async () =>
                    {
                        var listed = await rules.ListAsync(ct).ConfigureAwait(false);

                        if (listed.Failed)
                        {
                            return;
                        }

                        foreach (var one in clock.Look(listed.Value!.Where(each => each.Usable).Select(each => each.Rule), _time.GetUtcNow()))
                        {
                            await spine.PostAsync(one, ct).ConfigureAwait(false);
                        }
                    },
                    output).ConfigureAwait(false);

                await LookedAsync(
                    "GitHub could not be read for triggers",
                    async () =>
                    {
                        // Nothing is asked of GitHub, or of the credential
                        // store, until a rule waits for something there.
                        if (await GitHubWatchedAsync(rules, ct).ConfigureAwait(false) is not { Count: > 0 } watched)
                        {
                            return;
                        }

                        var now = _time.GetUtcNow();

                        if (now - credentialAt >= CredentialEvery)
                        {
                            credential = await (GitHubCredential ?? CredentialAsync)(ct).ConfigureAwait(false);
                            credentialAt = now;
                        }

                        var allowed = (await allow.ListAsync(ct).ConfigureAwait(false)).Value ?? [];
                        var look = await github.LookAsync(watched, credential, allowed, now, ct).ConfigureAwait(false);

                        foreach (var note in look.Notes)
                        {
                            journal.Append(note);
                            output.WriteLine($"[dim]{now.ToLocalTime():HH:mm}[/] GitHub: {Markup.Escape(note.Reason ?? note.Kind)}");
                        }

                        foreach (var one in look.Events)
                        {
                            await spine.PostAsync(one, ct).ConfigureAwait(false);
                        }
                    },
                    output).ConfigureAwait(false);

                // Daily, from the first pass, so a daemon that is restarted
                // often still prunes.
                if (_time.GetUtcNow() - pruned >= TimeSpan.FromDays(1))
                {
                    pruned = _time.GetUtcNow();

                    await LookedAsync(
                        "the trigger journal could not be pruned",
                        async () =>
                        {
                            // Read each time, so a changed limit is taken
                            // without restarting the daemon.
                            var allowance = Loadout.Core.Triggers.TriggerJournal.Allowance(
                                (await _configuration.LoadMachineAsync(ct).ConfigureAwait(false)).Value?.Teams.TriggerJournalMegabytes);
                            var kept = Loadout.Core.Triggers.TriggerJournal.Kept;

                            journal.Prune(pruned, kept, "kept for 90 days", allowance);

                            // Over with nothing left to prune: kept writing, and
                            // said, rather than losing records the ledger needs.
                            if (journal.OverAllowance(pruned, allowance) is { } over)
                            {
                                journal.Append(new Loadout.Core.Triggers.TriggerEntry(
                                    pruned, Loadout.Core.Triggers.TriggerEntryKinds.OverAllowance, Reason: over));
                                output.WriteLine($"[yellow]{Markup.Escape(over)}[/]");
                            }
                        },
                        output).ConfigureAwait(false);

                    await LookedAsync(
                        "stale trigger events could not be aged out",
                        () => spine.AgeOutAsync(ct),
                        output).ConfigureAwait(false);
                }

                if (_time.GetUtcNow() - drifted >= DocsDriftEvery)
                {
                    drifted = _time.GetUtcNow();

                    await LookedAsync(
                        "documentation could not be audited for triggers",
                        async () =>
                        {
                            if (await DocsWatchedAsync(rules, ct).ConfigureAwait(false) is { } watched)
                            {
                                foreach (var one in await drift.LookAsync(watched, drifted, ct).ConfigureAwait(false))
                                {
                                    await spine.PostAsync(one, ct).ConfigureAwait(false);
                                }
                            }
                        },
                        output).ConfigureAwait(false);
                }

                await Task.Delay(TriggerEvery, _time, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping, which is how this ends.
        }
        finally
        {
            _spine = null;
            await running.ConfigureAwait(false);
            await answering.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs one look of a trigger source, and says so when it fails rather
    /// than letting the failure end the loop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything but a stop is caught here, which is not how the rest of the
    /// daemon catches, and the reason is what an uncaught one did. A look that
    /// threw something no catch named ended the whole trigger loop: finished
    /// runs stopped being heard as well as the source that threw, nothing said
    /// so, and the exception surfaced only when the daemon was stopped. A
    /// resident loop nobody is watching has to outlive one bad look; the line
    /// it writes is where the cause gets found.
    /// </para>
    /// </remarks>
    private async Task LookedAsync(string failed, Func<Task> look, CommandOutput output)
    {
        try
        {
            await look().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            output.WriteLine(
                $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] {failed}: "
                + $"{Markup.Escape(ex.GetType().Name)}: {Markup.Escape(ex.Message)}");
        }
    }

    /// <summary>How often documentation is audited for drift; a test shortens it.</summary>
    /// <remarks>
    /// Less often than finished runs are looked for, because each look asks
    /// git where every watched checkout is, and documentation drifts over
    /// commits, not minutes.
    /// </remarks>
    internal TimeSpan DocsDriftEvery { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Which projects a docs-drift rule waits on, so nothing is audited that
    /// no rule would act on.
    /// </summary>
    private static async Task<Func<string, bool>?> DocsWatchedAsync(
        Loadout.Core.Triggers.TriggerRules rules,
        CancellationToken ct)
    {
        var listed = await rules.ListAsync(ct).ConfigureAwait(false);

        var waiting = listed.Succeeded
            ? listed.Value!
                .Where(one => one.Usable && one.Rule.Enabled
                    && string.Equals(one.Rule.On, Loadout.Core.Triggers.TriggerEventKinds.DocsDrift, StringComparison.OrdinalIgnoreCase))
                .Select(one => one.Rule)
                .ToList()
            : [];

        // Null when no rule waits for drift at all, so nothing is listed or asked of git.
        if (waiting.Count == 0)
        {
            return null;
        }

        return slug => waiting.Any(rule =>
            rule.Projects.Count == 0 || rule.Projects.Contains(slug, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The repositories a rule waits on GitHub for: each watched project whose
    /// remote is on github.com, or every such project for a rule that names none.
    /// </summary>
    private async Task<IReadOnlyList<Loadout.Core.Triggers.WatchedRepo>?> GitHubWatchedAsync(
        Loadout.Core.Triggers.TriggerRules rules,
        CancellationToken ct)
    {
        var listed = await rules.ListAsync(ct).ConfigureAwait(false);

        var waiting = listed.Succeeded
            ? listed.Value!
                .Where(one => one.Usable && one.Rule.Enabled && Loadout.Core.Triggers.TriggerEventKinds.FromGitHub(one.Rule.On))
                .Select(one => one.Rule)
                .ToList()
            : [];

        if (waiting.Count == 0)
        {
            return null;
        }

        var projects = await _projects.ListAsync(ct).ConfigureAwait(false);

        if (projects.Failed)
        {
            return null;
        }

        return
        [
            .. projects.Value!
                .Where(project => waiting.Any(rule =>
                    rule.Projects.Count == 0 || rule.Projects.Contains(project.Entry.Slug, StringComparer.OrdinalIgnoreCase)))
                .Select(project => (project.Entry.Slug, Repo: Loadout.Core.Triggers.GitHubRepo.From(project.Entry.Remote)))
                .Where(one => one.Repo is not null)
                .Select(one => new Loadout.Core.Triggers.WatchedRepo(one.Slug, one.Repo!)),
        ];
    }

    /// <summary>The token the GitHub source reads with: the dedicated one, or gh's.</summary>
    private Task<Loadout.Core.Triggers.TriggerCredential> CredentialAsync(CancellationToken ct) =>
        _resolver is null
            ? Task.FromResult(Loadout.Core.Triggers.TriggerCredential.Missing)
            : Loadout.Core.Triggers.TriggerCredential.ResolveAsync(_secrets, _resolver, _launcher, ct);

    /// <summary>A project's documentation policy, read where 'docs audit' reads it.</summary>
    private async Task<Loadout.Models.Instructions.DocsPolicy?> DocsPolicyAsync(string slug, CancellationToken ct)
    {
        if (_yaml is null || !_workspace.IsAvailable())
        {
            return null;
        }

        var path = Path.Combine(_workspace.LocalPath, "projects", slug, "docs.yaml");

        if (!File.Exists(path))
        {
            return null;
        }

        var loaded = await _yaml.LoadAsync(path, () => new Loadout.Models.Instructions.DocsPolicy(), ct).ConfigureAwait(false);

        return loaded.Succeeded ? loaded.Value : null;
    }

    /// <summary>How often the trigger inbox is looked at; a person is waiting on each answer.</summary>
    internal TimeSpan AnswerEvery { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Makes the changes to the rules that the trigger commands ask for, and
    /// answers them, until told to stop.
    /// </summary>
    private async Task AnswerAsync(
        Loadout.Core.Triggers.TriggerSpine spine,
        Loadout.Core.Triggers.TriggerInbox inbox,
        CommandOutput output,
        CancellationToken ct)
    {
        var swept = DateTimeOffset.MinValue;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    foreach (var asking in inbox.Waiting())
                    {
                        Loadout.Core.Triggers.TriggerAnswer answer;

                        try
                        {
                            answer = await spine.ApplyAsync(asking, ct).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            // Answered rather than left waiting: left, it would be
                            // tried again every glance, and the person who asked
                            // would see a daemon that never answers instead of
                            // why it could not.
                            answer = new Loadout.Core.Triggers.TriggerAnswer(
                                asking.Id, false, $"The daemon could not make the change: {ex.GetType().Name}: {ex.Message}");
                        }

                        inbox.Answer(answer);
                        output.WriteLine(
                            $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] trigger rules: {Markup.Escape(answer.Message)}");
                    }

                    if (_time.GetUtcNow() - swept > TimeSpan.FromHours(1))
                    {
                        swept = _time.GetUtcNow();
                        inbox.Sweep(swept);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    output.WriteLine(
                        $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] the trigger inbox could not be read: "
                        + Markup.Escape(ex.Message));
                }

                await Task.Delay(AnswerEvery, _time, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping, which is how this ends.
        }
    }

    /// <summary>Hands a webhook's event to the spine and says so.</summary>
    private async Task<OperationResult> PostedAsync(
        Loadout.Core.Triggers.TriggerSpine spine,
        Loadout.Core.Triggers.TriggerEvent happened,
        CommandOutput output,
        CancellationToken ct)
    {
        await spine.PostAsync(happened, ct).ConfigureAwait(false);

        output.WriteLine(
            $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] webhook for {Markup.Escape(happened.Field("team") ?? "?")} "
            + $"on {Markup.Escape(happened.Project ?? "?")}: handed to the trigger rules");

        return OperationResult.Ok();
    }

    /// <summary>How often notices are looked for; a test shortens it.</summary>
    internal TimeSpan NoticeEvery { get; init; } = Interval;

    /// <summary>
    /// Readies the notices and reads where they go, which is null when nowhere.
    /// </summary>
    internal async Task<string?> TellingAsync(CancellationToken ct)
    {
        _notices = new Notices(_secrets, _client);
        _sendTo = await _notices.AddressAsync(ct).ConfigureAwait(false);

        return _sendTo;
    }

    /// <summary>
    /// The schedules, and beside them the notices, until told to stop.
    /// </summary>
    /// <remarks>
    /// The notices had been on the schedules' loop, which waits for each
    /// scheduled run to end. So while one was going nothing was sent, and the
    /// run going is the one most likely to be asking something: its question
    /// reached Slack when it had finished, by which time nobody could answer
    /// it. On a loop of their own, as the event history is, they go out within
    /// a minute whatever the schedules are waiting on.
    /// </remarks>
    internal async Task LoopsAsync(CommandOutput output, CancellationToken ct)
    {
        // Its own token as well as the daemon's, so a schedule loop that ends
        // by throwing does not leave this waiting on a loop that never stops.
        using var ending = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var noticing = NoticingAsync(output, ending.Token);
        var queuing = QueuingAsync(output, ending.Token);

        try
        {
            await FireAsync(output, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Asked to stop, which is how this ends.
        }
        finally
        {
            await ending.CancelAsync().ConfigureAwait(false);
            await noticing.ConfigureAwait(false);
            await queuing.ConfigureAwait(false);
        }
    }

    /// <summary>How often the queues are looked at; a test shortens it.</summary>
    internal TimeSpan QueueEvery { get; init; } = Interval;

    /// <summary>
    /// Works through the teams' queues, every so often, until told to stop.
    /// </summary>
    /// <remarks>
    /// On a loop of its own, as the notices are, because the schedules' loop
    /// waits for each scheduled run to end, and a queue whose window opened
    /// during one would lose that time. Queued runs are processes of their
    /// own, so a pass never waits for one: it starts the next item, reads how
    /// the last one went, and comes back a minute later.
    /// </remarks>
    private async Task QueuingAsync(CommandOutput output, CancellationToken ct)
    {
        if (_queues is null)
        {
            return;
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _queues.PassAsync(
                        Firing(output),
                        line => output.WriteLine($"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] {Markup.Escape(line)}"),
                        (title, detail) => TellAsync(output, title, detail, ct),
                        ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // One bad pass is not the end of the queue: said, and tried
                    // again at the next look.
                    output.WriteLine(
                        $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] the queues could not be worked through: "
                        + Markup.Escape(ex.Message));
                }

                await Task.Delay(QueueEvery, _time, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping, which is how this ends.
        }
    }

    /// <summary>
    /// Says what a queue window did: in the daemon's log always, and wherever
    /// notices go when this machine sends them.
    /// </summary>
    private async Task TellAsync(CommandOutput output, string title, string detail, CancellationToken ct)
    {
        output.WriteLine($"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] {Markup.Escape(title)}");

        foreach (var line in detail.Split('\n'))
        {
            output.WriteLine($"  {Markup.Escape(line)}");
        }

        var machine = await _configuration.LoadMachineAsync(ct).ConfigureAwait(false);
        var teams = machine.Value?.Teams;

        if (_notices is not null && Notices.KindOf(teams?.NotifyKind) is { } kind && _sendTo is { Length: > 0 } address)
        {
            await _notices.PostAsync(
                kind, title, detail, _address is { Length: > 0 } page ? page : "loadout team queue list",
                teams!.NotifyChat, address, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Says what is newly worth saying, every so often, until told to stop.</summary>
    private async Task NoticingAsync(CommandOutput output, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await NoticeAsync(output, _sendTo, ct).ConfigureAwait(false);
                await Task.Delay(NoticeEvery, _time, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping, which is how this ends.
        }
    }

    /// <summary>
    /// Says out loud, somewhere else, anything newly worth saying.
    /// </summary>
    /// <remarks>
    /// Only ever from <see cref="NoticingAsync"/>, one pass at a time, which
    /// <see cref="Notices"/> relies on: what it has said is kept in a field
    /// it replaces at the end of each pass.
    /// </remarks>
    private async Task NoticeAsync(CommandOutput output, string? address, CancellationToken ct)
    {
        var machine = await _configuration.LoadMachineAsync(ct).ConfigureAwait(false);
        var teams = machine.Value?.Teams;

        if (Notices.KindOf(teams?.NotifyKind) is not { } kind || address is not { Length: > 0 })
        {
            return;
        }

        var runs = new List<RunSummary>();

        foreach (var id in _journal.List(30))
        {
            if (_journal.Summarise(id) is { Succeeded: true } read)
            {
                runs.Add(read.Value!);
            }
        }

        var where = teams!.NotifyKind;
        var sent = await _notices!
            .SayAsync(
                runs, kind, teams.NotifyChat, address, Link, _time.GetUtcNow(), ct,
                _watch is { } watch ? (run, reason, landed) => watch.Told(where, run, reason, landed) : null)
            .ConfigureAwait(false);

        if (sent > 0)
        {
            output.WriteLine(
                $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] told "
                + $"{Markup.Escape(teams.NotifyKind)} about {sent} thing(s)");
        }
    }

    /// <summary>When the bin was last looked at, so it is looked at hourly rather than every minute.</summary>
    private DateTimeOffset _binSwept = DateTimeOffset.MinValue;

    /// <summary>
    /// Deletes what has been in the bin longer than this machine keeps things.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On the schedules' loop, so it waits while a scheduled run goes, which
    /// costs nothing when retention is counted in days. Hourly
    /// rather than every pass, because retention is counted in days and
    /// reading every entry's record sixty times an hour would learn nothing
    /// new fifty-nine times.
    /// </para>
    /// <para>
    /// Removing something also sweeps, so a machine that never runs the daemon
    /// still clears its bin; this is what clears it on one where nobody has
    /// removed anything for a while.
    /// </para>
    /// </remarks>
    private async Task EmptyBinAsync(CommandOutput output, CancellationToken ct)
    {
        var now = _time.GetUtcNow();

        if (now - _binSwept < TimeSpan.FromHours(1))
        {
            return;
        }

        _binSwept = now;

        var days = await Binning.DaysAsync(_configuration, ct).ConfigureAwait(false);
        var gone = _bin.Sweep(now, days);

        if (gone.Count > 0)
        {
            output.WriteLine(
                $"[dim]{now.ToLocalTime():HH:mm}[/] emptied {gone.Count} thing(s) from the bin "
                + $"after {days} day(s)");
        }
    }

    /// <summary>Where the dashboard is, so a notice can point at it.</summary>
    /// <remarks>
    /// A notice that says something is wrong and leaves somebody to find it is
    /// half a notice. Falls back to the command when no page is being served.
    /// </remarks>
    private string Link(RunSummary run) =>
        _address is { Length: > 0 }
            ? _address
            : $"loadout team status {run.RunId}";

    private Notices? _notices;
    private string? _address;

    /// <summary>What records the event history, or null for a daemon with nowhere to keep it.</summary>
    private Loadout.Core.Events.EventWatch? _watch;

    /// <summary>Where notices go, read once when the daemon starts.</summary>
    /// <remarks>
    /// Read once rather than every minute: it lives in the credential store,
    /// and asking the operating system for a secret sixty times an hour to
    /// learn the same answer is work nobody asked for. Changing it takes
    /// effect when the daemon is next started, which the command says.
    /// </remarks>
    private string? _sendTo;

    /// <summary>
    /// Starts what is due, one at a time, until told to stop.
    /// </summary>
    /// <remarks>
    /// The schedule is marked as started before the run begins rather than
    /// after it ends. A run takes minutes; marking it afterwards would leave
    /// the same schedule due for every check in between, and the second check
    /// would start it again.
    /// </remarks>
    private async Task FireAsync(CommandOutput output, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await EmptyBinAsync(output, ct).ConfigureAwait(false);

            var now = _time.GetUtcNow();

            // Nothing is read as due while held or stopping. Reading them would
            // arm a commit watcher on a head it then never fires for.
            var ready = Firing(output)
                ? await ReadyAsync(now, record: true, ct).ConfigureAwait(false)
                : [];

            foreach (var schedule in ready)
            {
                if (ct.IsCancellationRequested || !Firing(output))
                {
                    break;
                }

                await _schedules.StartedAsync(schedule.Id, now, string.Empty, ct).ConfigureAwait(false);

                output.WriteLine(
                    $"[dim]{now.ToLocalTime():HH:mm}[/] starting {Markup.Escape(schedule.Id)}: "
                    + $"{Markup.Escape(schedule.Team)} on {Markup.Escape(schedule.Project)}");

                var code = await StartAsync(schedule, now, output, ct).ConfigureAwait(false);

                // The run names itself, and the newest one on this machine is
                // the one just finished. Recorded afterwards so the schedule
                // points at something a person can read.
                if (_journal.List(1).FirstOrDefault() is { Length: > 0 } runId)
                {
                    await _schedules.StartedAsync(schedule.Id, now, runId, CancellationToken.None)
                        .ConfigureAwait(false);
                }

                // And where the repository is NOW, which is the thing that
                // stops a watcher starting itself for ever.
                //
                // A run that merges a worker's branch moves the checked-out
                // branch. The head was written down before the run, so a minute
                // later the watcher saw a repository that had moved - because
                // of the run - and started another, which merged, which moved
                // it again. Unattended, that is a loop that costs a team run a
                // minute and nothing in it would ever have said why.
                await SeenAsync(schedule, CancellationToken.None).ConfigureAwait(false);

                output.WriteLine(
                    $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] {Markup.Escape(schedule.Id)} "
                    + (code == 0 ? "finished" : $"ended with exit code {code}"));
            }

            await RestAsync(output, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Starts one schedule's team, nominating first where it reads the catalogue.</summary>
    /// <returns>The run's exit code.</returns>
    /// <remarks>
    /// The nomination goes before the team starts, so what finished work
    /// nominated is in the inbox the Creator is about to read. It is not
    /// allowed to stop the run: a pass that cannot read something says so in
    /// one line and the team starts anyway, because the run is what was
    /// scheduled and the nominations can wait for the next one.
    /// </remarks>
    internal async Task<int> StartAsync(TeamSchedule schedule, DateTimeOffset now, CommandOutput output, CancellationToken ct)
    {
        void Log(string line) =>
            output.WriteLine($"[dim]{now.ToLocalTime():HH:mm}[/] {Markup.Escape(line)}");

        try
        {
            var nominated = await _nominations.BeforeAsync(schedule, Log, ct).ConfigureAwait(false);

            if (nominated.Count(one => one.Filed is { Succeeded: true }) is > 0 and var filed)
            {
                Log($"nominated {filed} for the tool catalogue");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            Log($"nominated nothing before {schedule.Id}: {ex.Message}");
        }

        return await _commands.RunAsync(
            "team run",
            [
                schedule.Team,
                schedule.Goal,
                "--project", schedule.Project,
                "--autonomy", schedule.Autonomy,
                "--non-interactive",
                .. schedule.Budget is { Length: > 0 } budget ? (string[])["--usd", budget] : [],
            ],
            ct).ConfigureAwait(false);
    }

    /// <summary>Whether the schedules were held at the last look.</summary>
    private bool _held;

    /// <summary>
    /// Whether schedules may fire now, saying so once each time that changes.
    /// </summary>
    /// <remarks>
    /// Internal so a test can hold and release the daemon and see what it
    /// would do, without a minute's wait between the two.
    /// </remarks>
    internal bool Firing(CommandOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var held = DaemonControl.Paused(_paths);

        if (held != _held)
        {
            _held = held;

            output.WriteLine($"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] " + (held
                ? "Paused: no schedule fires until 'loadout daemon resume'. The dashboard is still served, "
                  + "and runs already going carry on."
                : "Resumed: schedules fire again."));
        }

        return !held && DaemonControl.Stopping(_paths) is null;
    }

    /// <summary>
    /// Waits for the next look at the schedules, a glance at a time.
    /// </summary>
    /// <remarks>
    /// In glances rather than one minute's sleep, so a hold lifted a second
    /// after a look is noticed within seconds, not at the next minute.
    /// </remarks>
    private async Task RestAsync(CommandOutput output, CancellationToken ct)
    {
        var until = _time.GetUtcNow() + Interval;
        var held = _held;

        while (_time.GetUtcNow() < until)
        {
            await Task.Delay(Glance, _time, ct).ConfigureAwait(false);

            // Looked at every glance rather than every minute, so the daemon's
            // own output says it is held within seconds of being asked - which
            // is where somebody who paused it will look to see that it did.
            Firing(output);

            if (held && !_held)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Everything that should start now: what the clock says, and what has
    /// happened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A schedule watching for a commit is due when the project's repository
    /// is not where it was last time this looked. The first look never fires:
    /// writing down a trigger and having it go off immediately, against
    /// whatever happened to be checked out, is not what anybody meant by "when
    /// the repository moves".
    /// </para>
    /// <para>
    /// <paramref name="record"/> is false for the preview, which must be able
    /// to say what would happen without changing what happens next. A dry run
    /// that wrote down the commit it saw would arm the trigger it was only
    /// asked about.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<TeamSchedule>> ReadyAsync(
        DateTimeOffset now,
        bool record,
        CancellationToken ct)
    {
        var listed = await _schedules.ListAsync(ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return [];
        }

        var ready = new List<TeamSchedule>();

        foreach (var schedule in listed.Value!)
        {
            if (ScheduleService.IsDue(schedule, now))
            {
                ready.Add(schedule);

                continue;
            }

            if (!schedule.Enabled || schedule.On.Length == 0)
            {
                continue;
            }

            if (string.Equals(schedule.On, ScheduleService.RunFinishedEvent, StringComparison.OrdinalIgnoreCase))
            {
                var runs = _journal.List(50)
                    .Select(_journal.Summarise)
                    .Where(one => one.Succeeded)
                    .Select(one => one.Value!)
                    .ToList();
                var (fire, seen) = ScheduleService.RunFinished(schedule, runs, now);

                if (record && seen is not null)
                {
                    await _schedules.SawAsync(schedule.Id, seen, ct).ConfigureAwait(false);
                }

                if (fire)
                {
                    ready.Add(schedule);
                }

                continue;
            }

            if (await MovedAsync(schedule, record, ct).ConfigureAwait(false))
            {
                ready.Add(schedule);
            }
        }

        return ready;
    }

    /// <summary>
    /// Takes the repository as seen, wherever it is now.
    /// </summary>
    /// <remarks>
    /// Called when a watching schedule's run has finished, so that whatever the
    /// run did to the repository is not read as a reason to run again. A commit
    /// somebody else made while the run was going is taken as seen too, which
    /// is the price: one trigger can cover work that arrived during it, and the
    /// alternative is a loop that cannot stop itself.
    /// </remarks>
    private async Task SeenAsync(TeamSchedule schedule, CancellationToken ct)
    {
        // A run-finished schedule keeps a run identifier where this would
        // write a commit, and its watermark was written when it fired.
        if (schedule.On.Length == 0
            || string.Equals(schedule.On, ScheduleService.RunFinishedEvent, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var resolution = await _projects.ResolveAsync(schedule.Project, ct).ConfigureAwait(false);

        if (resolution.Failed || resolution.Value?.LocalPath is not { Length: > 0 } repository)
        {
            return;
        }

        var state = await _git.GetStateAsync(repository, ct).ConfigureAwait(false);

        if (state.Succeeded && state.Value?.HeadCommit is { Length: > 0 } head)
        {
            await _schedules.SawAsync(schedule.Id, head, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Whether the project this watches has moved since it last looked.</summary>
    private async Task<bool> MovedAsync(TeamSchedule schedule, bool record, CancellationToken ct)
    {
        var resolution = await _projects.ResolveAsync(schedule.Project, ct).ConfigureAwait(false);

        if (resolution.Failed || resolution.Value?.LocalPath is not { Length: > 0 } repository)
        {
            return false;
        }

        var state = await _git.GetStateAsync(repository, ct).ConfigureAwait(false);

        if (state.Failed || state.Value?.HeadCommit is not { Length: > 0 } head)
        {
            return false;
        }

        var moved = ScheduleService.Moved(schedule, head);

        if (record && !string.Equals(head, schedule.LastCommit, StringComparison.Ordinal))
        {
            // Written down whether or not it fires. The first look records
            // where the repository is so that the next move is one.
            await _schedules.SawAsync(schedule.Id, head, ct).ConfigureAwait(false);
        }

        return moved;
    }

    /// <summary>Where the daemon says it is, for whatever wants to find it.</summary>
    private string StatusPath => DaemonNote.PathFor(_paths);

    /// <remarks>
    /// The process start time goes in beside the identifier, because
    /// identifiers are reused: without it, doctor would eventually report
    /// somebody else's process as a daemon of ours that is still going.
    /// </remarks>
    private async Task WriteStatusAsync(string? address, CancellationToken ct)
    {
        // One at a time, because a session ending writes this from whatever
        // thread it ended on, and two writes at once can interleave.
        await _statusGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            // Written again whenever a hosted session starts or ends, and
            // never once the daemon has begun ending them: a note written
            // after Forget would outlive the daemon.
            if (_statusClosed)
            {
                return;
            }

            _since ??= _time.GetUtcNow();
            _statusAddress = address;

            Directory.CreateDirectory(Path.GetDirectoryName(StatusPath)!);

            await File.WriteAllTextAsync(
                StatusPath,
                JsonSerializer.Serialize(new DaemonState(
                    _processes.CurrentProcessId,
                    _processes.CurrentProcessStartedAt,
                    address,
                    _since.Value,
                    _sessions.Running,
                    _triggering)),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Somewhere to look is a convenience. A daemon that would not
            // start because it could not write down that it had is worse than
            // one nothing can find.
        }
        finally
        {
            _statusGate.Release();
        }
    }

    private readonly SemaphoreSlim _statusGate = new(1, 1);
    private DateTimeOffset? _since;
    private string? _statusAddress;
    private bool _statusClosed;

    /// <summary>Writes the note again with the current number of hosted sessions.</summary>
    private void HostedChanged() => _ = WriteStatusAsync(_statusAddress, CancellationToken.None);

    private void Forget()
    {
        try
        {
            File.Delete(StatusPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
