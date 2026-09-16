using System.ComponentModel;
using System.Text.Json;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models;
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
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;

    public TeamDaemonCommand(
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
        using var server = settings.NoDashboard ? null : new DashboardServer(_journal);

        if (server is not null)
        {
            var started = server.Start(settings.Port);

            if (started.Failed)
            {
                return output.Fail(started);
            }

            output.WriteLine($"[bold]{Markup.Escape(server.Address)}[/]");
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
