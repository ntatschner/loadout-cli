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
[CommandMeta(CommandCategory.Start, Intent = "team daemon resident service schedules fire background", Mutates = true)]
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

    private readonly IScheduleService _schedules;
    private readonly IRunJournal _journal;
    private readonly ICommandCatalogue _commands;
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

    /// <summary>
    /// Every command this daemon runs, counted while it runs.
    /// </summary>
    /// <remarks>
    /// A stop that is not "now" waits for these, and they are all of the runs
    /// this process has in hand: the schedules', the page's and the webhook's
    /// all go through the one catalogue.
    /// </remarks>
    private readonly InFlight _inFlight;

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
        IProcessLauncher launcher)
    {
        _launcher = launcher;
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
        [Description("The port the dashboard listens on. One the machine chooses when omitted.")]
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
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
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

        _started = settings;

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var server = settings.NoDashboard ? null : new DashboardServer(_journal, _git);

        if (server is not null)
        {
            var machine = await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false);
            var teams = machine.Value?.Teams;

            // The flag wins over the configured address, because somebody
            // typing one means it for this run and not for ever.
            var started = server.Start(
                settings.Port,
                settings.Listen is { Length: > 0 } asked ? asked : Webhook.Listen(teams));

            if (started.Failed)
            {
                return output.Fail(started);
            }

            // The office art, if this machine has any. Read once: a set is a
            // directory somebody filled before starting the daemon.
            var office = OfficeArt.Chosen(_paths, teams?.OfficeSet);

            server.OfficeRoot = office.Root;
            server.OfficeSet = office.Set;
            server.WaitingSet = OfficeArt.Chosen(_paths, teams?.WaitingSet).Set;

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

        _notices = new Notices(_secrets, _client);

        _sendTo = await _notices.AddressAsync(cancellationToken).ConfigureAwait(false);

        if (_sendTo is { Length: > 0 })
        {
            var where = (await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false))
                .Value?.Teams.NotifyKind;

            if (where is { Length: > 0 })
            {
                output.WriteLine($"[dim]telling {Markup.Escape(where)} when a run needs you[/]");
            }
        }

        await WriteStatusAsync(server?.Address, cancellationToken).ConfigureAwait(false);

        output.WriteLine("[dim]Watching the schedules. " + (Console.IsInputRedirected
            ? "It stops when whatever started it closes its input, or on 'loadout team daemon stop'.[/]"
            : "Press Ctrl+C or run 'loadout team daemon stop' to stop.[/]"));

        if (DaemonControl.Paused(_paths))
        {
            output.WriteLine(
                "[yellow]Paused:[/] no schedule fires until 'loadout team daemon resume'. "
                + "It was held when the last daemon stopped, and a hold is kept until it is lifted.");
        }

        var serving = server is null
            ? Task.CompletedTask
            : server.ListenAsync(stopping.Token);

        var ending = TeamDashboardCommand.Ends(stopping);
        var watching = WatchAsync(stopping, output);

        try
        {
            await FireAsync(output, stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Asked to stop, which is how this ends.
        }

        await stopping.CancelAsync().ConfigureAwait(false);

        try
        {
            await serving.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        // Not waited for when the stop came from somewhere else. It is a read
        // of standard input waiting to see it close, and a read of a pipe on
        // Windows does not hear its cancellation: a daemon told to stop by
        // 'team daemon stop' sat here, stopped in every other sense, until
        // whatever started it closed its input - which for a daemon started
        // with its input held open is never. The process ending takes it.
        await Task.WhenAny(ending, Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None))
            .ConfigureAwait(false);
        await watching.ConfigureAwait(false);

        var request = DaemonControl.Stopping(_paths);

        DaemonControl.ClearStop(_paths);

        Forget();

        if (request is { Restart: true } && _started is { } was)
        {
            Succeed(output, server?.Address, was);
        }

        return (int)ExitCode.Success;
    }

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
    /// there after a restart. Started detached, the way the status line
    /// starts the launcher, so it outlives this process; on Windows that
    /// means a console window of its own.
    /// </para>
    /// </remarks>
    internal void Succeed(CommandOutput output, string? address, Settings was)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(was);

        if (Loadout.Core.Agents.LauncherInvocation.Parts() is not ({ Length: > 0 } command, var prefix))
        {
            output.WriteLine("[red]Could not restart:[/] this launcher cannot say how it was started. "
                + "Start the daemon again with: loadout team daemon");

            return;
        }

        var arguments = new List<string>(prefix)
        {
            "team", "daemon",
            "--after", _processes.CurrentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        var port = was.Port;

        if (port == 0 && address is { Length: > 0 } && Uri.TryCreate(address, UriKind.Absolute, out var served))
        {
            port = served.Port;
        }

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

        var started = _launcher.StartDetached(new ProcessRequest(
            command,
            arguments,
            WorkingDirectory: CurrentDirectory()));

        output.WriteLine(started.Succeeded
            ? "Restarting: a new daemon is starting with the same settings."
            : $"[red]Could not restart:[/] {Shown.Safely(started.Error ?? "the new daemon did not start")}. "
              + "Start it again with: loadout team daemon");
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
                    var what = asked.Restart ? "Restarting" : "Stopping";

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
    internal Task<OperationResult> TriggeredAsync(
        TriggerRequest asking,
        CommandOutput output,
        CancellationToken ct)
    {
        if (asking.Project is not { Length: > 0 } project)
        {
            return Task.FromResult(OperationResult.Fail(
                "A triggered run needs a project: add \"project\" to the body.",
                ExitCode.InvalidArguments));
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

        return Task.FromResult(OperationResult.Ok());
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
    /// <c>team dashboard</c> draws the same form and answered it with "this
    /// server does not start runs".
    /// </remarks>
    internal Task<OperationResult> BeganAsync(
        StartRequest asking,
        CommandOutput output,
        CancellationToken ct) =>
        DashboardActions.BeganAsync(_commands, _time, asking, output, ct);

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
    /// <c>team dashboard</c> serves the same page and drew the same buttons
    /// while answering every one of them with "this server only reads".
    /// </remarks>
    private Task<OperationResult> ActedOnAsync(
        RunAction action,
        CommandOutput output,
        CancellationToken ct) =>
        DashboardActions.RanAsync(_commands, _time, action, output, ct);

    /// <summary>
    /// Says out loud, somewhere else, anything newly worth saying.
    /// </summary>
    /// <remarks>
    /// On the same loop as the schedules rather than a second one: a run that
    /// wants somebody is not more urgent than a minute, and a second timer
    /// would be a second thing to reason about when one of them stops.
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

        var sent = await _notices!
            .SayAsync(runs, kind, teams!.NotifyChat, address, Link, _time.GetUtcNow(), ct)
            .ConfigureAwait(false);

        if (sent > 0)
        {
            output.WriteLine(
                $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] told "
                + $"{Markup.Escape(teams.NotifyKind)} about {sent} thing(s)");
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
            await NoticeAsync(output, _sendTo, ct).ConfigureAwait(false);

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

                var code = await _commands.RunAsync(
                    "team run",
                    [
                        schedule.Team,
                        schedule.Goal,
                        "--project", schedule.Project,
                        "--autonomy", schedule.Autonomy,
                        "--non-interactive",
                    ],
                    ct).ConfigureAwait(false);

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
                ? "Paused: no schedule fires until 'loadout team daemon resume'. The dashboard is still served, "
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
        if (schedule.On.Length == 0)
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
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatusPath)!);

            await File.WriteAllTextAsync(
                StatusPath,
                JsonSerializer.Serialize(new DaemonState(
                    _processes.CurrentProcessId,
                    _processes.CurrentProcessStartedAt,
                    address,
                    _time.GetUtcNow())),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Somewhere to look is a convenience. A daemon that would not
            // start because it could not write down that it had is worse than
            // one nothing can find.
        }
    }

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
