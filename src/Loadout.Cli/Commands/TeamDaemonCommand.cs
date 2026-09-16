using System.ComponentModel;
using System.Text.Json;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models;
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
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;

    public TeamDaemonCommand(
        IScheduleService schedules,
        IRunJournal journal,
        ICommandCatalogue commands,
        IPlatformPaths paths,
        IAnsiConsole console,
        TimeProvider time)
    {
        _schedules = schedules;
        _journal = journal;
        _commands = commands;
        _paths = paths;
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
            var due = await _schedules.DueAsync(_time.GetUtcNow(), cancellationToken).ConfigureAwait(false);

            output.WriteLine(
                $"[dim]Dry run: nothing was started or served.[/] {due.Value?.Count ?? 0} schedule(s) "
                + "would start now.");

            foreach (var schedule in due.Value ?? [])
            {
                output.WriteLine($"  {Markup.Escape(schedule.Id)}: {Markup.Escape(schedule.Team)} on {Markup.Escape(schedule.Project)}");
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
            var due = await _schedules.DueAsync(now, ct).ConfigureAwait(false);

            foreach (var schedule in due.Value ?? [])
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

    /// <summary>Where the daemon says it is, for whatever wants to find it.</summary>
    private string StatusPath => Path.Combine(_paths.Paths.State, "teams", "daemon.json");

    private async Task WriteStatusAsync(string? address, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatusPath)!);

            await File.WriteAllTextAsync(
                StatusPath,
                JsonSerializer.Serialize(new
                {
                    pid = Environment.ProcessId,
                    address,
                    since = _time.GetUtcNow(),
                }),
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
