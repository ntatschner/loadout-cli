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

    private readonly IScheduleService _schedules;
    private readonly IRunJournal _journal;
    private readonly ICommandCatalogue _commands;
    private readonly IPlatformPaths _paths;
    private readonly Loadout.Core.Projects.IProjectService _projects;
    private readonly Loadout.Core.Git.IGitManager _git;
    private readonly IProcessInspector _processes;
    private readonly ISecretProvider _secrets;
    private readonly HttpClient _client;
    private readonly Loadout.Core.Configuration.IConfigurationService _configuration;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;

    public TeamDaemonCommand(
        ISecretProvider secrets,
        HttpClient client,
        Loadout.Core.Configuration.IConfigurationService configuration,
        IScheduleService schedules,
        IRunJournal journal,
        ICommandCatalogue commands,
        IPlatformPaths paths,
        Loadout.Core.Projects.IProjectService projects,
        Loadout.Core.Git.IGitManager git,
        IProcessInspector processes,
        IAnsiConsole console,
        TimeProvider time)
    {
        _secrets = secrets;
        _client = client;
        _configuration = configuration;
        _schedules = schedules;
        _journal = journal;
        _commands = commands;
        _paths = paths;
        _projects = projects;
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
            ? "It stops when whatever started it closes its input.[/]"
            : "Press Ctrl+C to stop.[/]"));

        var serving = server is null
            ? Task.CompletedTask
            : server.ListenAsync(stopping.Token);

        var ending = TeamDashboardCommand.Ends(stopping);

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

        await ending.ConfigureAwait(false);

        Forget();

        return (int)ExitCode.Success;
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
    private async Task<OperationResult> TriggeredAsync(
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

        output.WriteLine(
            $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] triggered: "
            + $"{Markup.Escape(asking.Team)} on {Markup.Escape(project)}");

        var code = await _commands.RunAsync(
            "team run",
            [asking.Team, asking.Goal, "--project", project, "--non-interactive"],
            ct).ConfigureAwait(false);

        return code == (int)ExitCode.Success
            ? OperationResult.Ok()
            : OperationResult.Fail(
                $"The run ended with exit code {code}. Read it with: loadout team log",
                ExitCode.GeneralFailure);
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
    private Task<OperationResult> BeganAsync(
        StartRequest asking,
        CommandOutput output,
        CancellationToken ct)
    {
        var arguments = new List<string> { asking.Team, asking.Goal };

        if (asking.Project is { Length: > 0 } project)
        {
            arguments.Add("--project");
            arguments.Add(project);
        }

        if (asking.Rounds is { } rounds and > 0)
        {
            arguments.Add("--rounds");
            arguments.Add(rounds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (asking.Autonomy is { Length: > 0 } autonomy)
        {
            arguments.Add("--autonomy");
            arguments.Add(autonomy);
        }

        arguments.Add("--non-interactive");

        output.WriteLine(
            $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] from the dashboard: "
            + $"team run {Markup.Escape(asking.Team)}"
            + (asking.Project is { Length: > 0 } on ? $" on {Markup.Escape(on)}" : string.Empty));

        // Not awaited on purpose, and not left to chance either: whatever it
        // ends up doing is written where the schedules write theirs.
        _ = Task.Run(
            async () =>
            {
                try
                {
                    var code = await _commands.RunAsync("team run", arguments, ct).ConfigureAwait(false);

                    if (code != (int)ExitCode.Success)
                    {
                        output.WriteLine(
                            $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] "
                            + $"that run ended with exit code {code}.");
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
                {
                    output.WriteLine(
                        $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] "
                        + $"that run could not be started: {Markup.Escape(ex.Message)}");
                }
            },
            CancellationToken.None);

        return Task.FromResult(OperationResult.Ok());
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
    private async Task<OperationResult> ActedOnAsync(
        RunAction action,
        CommandOutput output,
        CancellationToken ct)
    {
        var (command, arguments) = action.Verb switch
        {
            "gates" or "gate" => ("team gate", Gate(action)),
            "message" => ("team message", new List<string> { action.Run, "--message", action.Message ?? string.Empty }),
            "stop" => ("team halt", [action.Run]),
            "pause" => ("team halt", [action.Run, "--pause"]),
            "resume" => ("team halt", [action.Run, "--resume"]),
            _ => (string.Empty, []),
        };

        if (command.Length == 0)
        {
            return OperationResult.Fail(
                $"There is nothing called '{action.Verb}' to do to a run.", ExitCode.InvalidArguments);
        }

        if (action.Verb == "message" && action.Message is not { Length: > 0 })
        {
            return OperationResult.Fail("Say something to say.", ExitCode.InvalidArguments);
        }

        output.WriteLine(
            $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] from the dashboard: "
            + $"{Markup.Escape(command)} {Markup.Escape(action.Run)}");

        var code = await _commands
            .RunAsync(command, [.. arguments, "--non-interactive"], ct)
            .ConfigureAwait(false);

        return code == (int)ExitCode.Success
            ? OperationResult.Ok()
            : OperationResult.Fail($"'{command}' ended with exit code {code}.", ExitCode.GeneralFailure);
    }

    /// <summary>The command line for answering one gate.</summary>
    private static List<string> Gate(RunAction action)
    {
        var arguments = new List<string> { action.Run };

        if (action.Gate is { Length: > 0 } gate)
        {
            arguments.Add("--gate");
            arguments.Add(gate);
        }

        arguments.Add("--answer");
        arguments.Add(action.Answer ?? "no");
        arguments.Add("--by");
        arguments.Add("dashboard");

        if (action.Reason is { Length: > 0 } reason)
        {
            arguments.Add("--reason");
            arguments.Add(reason);
        }

        return arguments;
    }

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
            var ready = await ReadyAsync(now, record: true, ct).ConfigureAwait(false);

            foreach (var schedule in ready)
            {
                if (ct.IsCancellationRequested)
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

                output.WriteLine(
                    $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] {Markup.Escape(schedule.Id)} "
                    + (code == 0 ? "finished" : $"ended with exit code {code}"));
            }

            await Task.Delay(Interval, ct).ConfigureAwait(false);
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
