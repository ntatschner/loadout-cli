using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams.Daemon;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// What the daemon control commands share: finding the daemon, and saying
/// plainly when there is none.
/// </summary>
/// <remarks>
/// <para>
/// The daemon is usually somewhere nobody is looking - started at login in a
/// minimised window, or in a terminal long since buried - and the only way to
/// stop it used to be finding that window and pressing Ctrl+C in it. These
/// reach it from any shell through the files it watches beside its note.
/// </para>
/// <para>
/// Each one names the process it is talking to, because "stopped" said about a
/// daemon somebody did not know was running is the kind of success that
/// surprises people later.
/// </para>
/// </remarks>
public abstract class DaemonControlCommand<TSettings> : AsyncCommand<TSettings>
    where TSettings : GlobalSettings
{
    /// <summary>How long a stop or restart is watched before the command answers.</summary>
    /// <remarks>
    /// Long enough for an idle daemon to notice, which takes at most one of its
    /// two-second glances; short enough that somebody is not left staring at a
    /// prompt while a twenty-minute run finishes. A daemon still busy after
    /// this is said to be busy rather than waited for.
    /// </remarks>
    internal static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    protected DaemonControlCommand(IPlatformPaths paths, IProcessInspector processes, IAnsiConsole console, TimeProvider time)
    {
        Paths = paths;
        Processes = processes;
        Console = console;
        Time = time;
    }

    protected IPlatformPaths Paths { get; }

    protected IProcessInspector Processes { get; }

    protected IAnsiConsole Console { get; }

    protected TimeProvider Time { get; }

    /// <summary>The running daemon, or a failure saying there is none.</summary>
    protected DaemonState? Running(CommandOutput output, out int failure)
    {
        ArgumentNullException.ThrowIfNull(output);

        failure = 0;

        if (DaemonNote.Live(Paths, Processes) is { } running)
        {
            return running;
        }

        failure = output.Fail(
            "No daemon is running on this machine, so there is nothing to control. "
            + "Start one with: loadout team daemon",
            ExitCode.GeneralFailure);

        return null;
    }

    /// <summary>
    /// Waits a little for a daemon to be gone, or to be replaced.
    /// </summary>
    /// <returns>The daemon now running, if another one has taken its place; null if none is.</returns>
    protected async Task<(bool Gone, DaemonState? Now)> WatchAsync(int pid, CancellationToken ct)
    {
        var until = Time.GetUtcNow() + Patience;

        while (Time.GetUtcNow() < until)
        {
            var now = DaemonNote.Live(Paths, Processes);

            if (now is null || now.Pid != pid)
            {
                return (true, now);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), Time, ct).ConfigureAwait(false);
        }

        return (false, DaemonNote.Live(Paths, Processes));
    }

    protected static string Named(DaemonState running) =>
        $"the daemon (process {running.Pid}, since {running.Since.ToLocalTime():yyyy-MM-dd HH:mm})";
}

/// <summary>Settings for stopping or restarting.</summary>
public sealed class DaemonStopSettings : GlobalSettings
{
    [CommandOption("--now")]
    [Description(
        "End the runs it started at once, as Ctrl+C in its window would, rather than "
        + "waiting for them to finish.")]
    public bool Now { get; init; }
}

/// <summary>Stops the daemon.</summary>
/// <remarks>
/// Waits for the runs it started unless told otherwise, and the command says
/// which it did. A run is a set of headless agents mid-turn; ending one loses
/// the turn and what was paid for it, which is why the default is to let them
/// finish and why "now" is a word somebody has to type.
/// </remarks>
[Description("Stop the running team daemon, after the runs it started have finished.")]
[CommandMeta(CommandCategory.Start, Intent = "daemon stop shut down end quit kill background", Mutates = true)]
public sealed class TeamDaemonStopCommand(
    IPlatformPaths paths, IProcessInspector processes, IAnsiConsole console, TimeProvider time)
    : DaemonControlCommand<DaemonStopSettings>(paths, processes, console, time)
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        DaemonStopSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        if (Running(output, out var failure) is not { } running)
        {
            return failure;
        }

        if (settings.DryRun)
        {
            output.WriteLine($"Would stop {Markup.Escape(Named(running))}"
                + (settings.Now ? ", ending any run it started at once." : ", once the runs it started have finished.")
                + " Nothing was written.");

            return CommandOutput.Success();
        }

        await DaemonControl.StopAsync(Paths, new DaemonStopRequest(settings.Now, Restart: false), cancellationToken)
            .ConfigureAwait(false);

        var (gone, _) = await WatchAsync(running.Pid, cancellationToken).ConfigureAwait(false);

        if (output.IsJson)
        {
            output.WriteJson(new { daemon = running.Pid, stopped = gone, waiting = !gone });

            return CommandOutput.Success();
        }

        output.WriteLine(gone
            ? $"[green]+[/] Stopped {Markup.Escape(Named(running))}."
            : $"[green]+[/] Asked {Markup.Escape(Named(running))} to stop. It is finishing the runs it started, "
              + "starts nothing new meanwhile, and stops when they end. "
              + "'loadout team daemon stop --now' ends them at once.");

        return CommandOutput.Success();
    }
}

/// <summary>Restarts the daemon with the settings it was started with.</summary>
/// <remarks>
/// The daemon restarts itself: it knows how it was started and when its runs
/// have finished, and this command knows neither. So this asks, watches for a
/// while, and says what it saw.
/// </remarks>
[Description("Restart the running team daemon with the same settings, after the runs it started have finished.")]
[CommandMeta(CommandCategory.Start, Intent = "daemon restart reload background", Mutates = true)]
public sealed class TeamDaemonRestartCommand(
    IPlatformPaths paths, IProcessInspector processes, IAnsiConsole console, TimeProvider time)
    : DaemonControlCommand<DaemonStopSettings>(paths, processes, console, time)
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        DaemonStopSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        if (Running(output, out var failure) is not { } running)
        {
            return failure;
        }

        if (settings.DryRun)
        {
            output.WriteLine($"Would restart {Markup.Escape(Named(running))} with the settings it was started with"
                + (settings.Now ? ", ending any run it started at once." : ", once the runs it started have finished.")
                + " Nothing was written.");

            return CommandOutput.Success();
        }

        await DaemonControl.StopAsync(Paths, new DaemonStopRequest(settings.Now, Restart: true), cancellationToken)
            .ConfigureAwait(false);

        var (gone, now) = await WatchAsync(running.Pid, cancellationToken).ConfigureAwait(false);

        // The successor waits for the old one to go and then a moment more, so
        // it is often not up yet when the old one has gone. Given a little
        // longer to appear before this says what it saw.
        if (gone && now is null)
        {
            var until = Time.GetUtcNow() + Patience;

            while (now is null && Time.GetUtcNow() < until)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), Time, cancellationToken).ConfigureAwait(false);

                now = DaemonNote.Live(Paths, Processes);
            }
        }

        if (output.IsJson)
        {
            output.WriteJson(new { daemon = running.Pid, restarted = now?.Pid, waiting = !gone });

            return CommandOutput.Success();
        }

        output.WriteLine(!gone
            ? $"[green]+[/] Asked {Markup.Escape(Named(running))} to restart. It is finishing the runs it started, "
              + "and restarts with the same settings when they end. 'loadout team daemon restart --now' does not wait."
            : now is { } successor
                ? $"[green]+[/] Restarted: the daemon is now process {successor.Pid}"
                  + (successor.Address is { Length: > 0 } address ? $", serving {Markup.Escape(address)}." : ".")
                : $"[yellow]![/] {Markup.Escape(Named(running))} stopped, and no new daemon has appeared yet. "
                  + "Its window, or 'loadout doctor', will say whether one started; if not, start it with: "
                  + "loadout team daemon");

        return CommandOutput.Success();
    }
}

/// <summary>Holds the schedules.</summary>
/// <remarks>
/// Only the schedules. The page is still served, runs already going carry on,
/// and anything started from the page or a webhook still starts: those are a
/// person asking now, which a hold on the clock is not meant to refuse.
/// </remarks>
[Description("Hold the team daemon's schedules: nothing fires until it is resumed. The dashboard stays up.")]
[CommandMeta(CommandCategory.Start, Intent = "daemon pause hold schedules suspend background", Mutates = true)]
public sealed class TeamDaemonPauseCommand(
    IPlatformPaths paths, IProcessInspector processes, IAnsiConsole console, TimeProvider time)
    : DaemonControlCommand<GlobalSettings>(paths, processes, console, time)
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        if (Running(output, out var failure) is not { } running)
        {
            return failure;
        }

        var already = DaemonControl.Paused(Paths);

        if (settings.DryRun)
        {
            output.WriteLine(already
                ? $"{Markup.Escape(Named(running))} is already paused. Nothing was written."
                : $"Would pause the schedules of {Markup.Escape(Named(running))}. Nothing was written.");

            return CommandOutput.Success();
        }

        await DaemonControl.PauseAsync(Paths, cancellationToken).ConfigureAwait(false);

        if (output.IsJson)
        {
            output.WriteJson(new { daemon = running.Pid, paused = true, already });

            return CommandOutput.Success();
        }

        output.WriteLine(already
            ? $"{Markup.Escape(Named(running))} was already paused."
            : $"[green]+[/] Paused {Markup.Escape(Named(running))}: no schedule fires until "
              + "'loadout team daemon resume'. The dashboard is still served and runs already going carry on. "
              + "The hold is kept if the daemon stops, so the next one starts paused too.");

        return CommandOutput.Success();
    }
}

/// <summary>Lets the schedules fire again.</summary>
/// <remarks>
/// Also clears a hold left by a daemon that has since stopped, which is the
/// one case where it acts with no daemon running: otherwise the next daemon
/// would start held for a reason nobody remembers.
/// </remarks>
[Description("Let a paused team daemon's schedules fire again.")]
[CommandMeta(CommandCategory.Start, Intent = "daemon resume continue unpause schedules background", Mutates = true)]
public sealed class TeamDaemonResumeCommand(
    IPlatformPaths paths, IProcessInspector processes, IAnsiConsole console, TimeProvider time)
    : DaemonControlCommand<GlobalSettings>(paths, processes, console, time)
{
    /// <inheritdoc />
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);
        var running = DaemonNote.Live(Paths, Processes);
        var held = DaemonControl.Paused(Paths);

        if (running is null && !held)
        {
            Running(output, out var failure);

            return Task.FromResult(failure);
        }

        if (settings.DryRun)
        {
            output.WriteLine((held ? "Would lift the hold" : "Nothing is held")
                + (running is null ? " left by a daemon that has stopped" : $" on {Markup.Escape(Named(running))}")
                + ". Nothing was written.");

            return Task.FromResult(CommandOutput.Success());
        }

        DaemonControl.Resume(Paths);

        if (output.IsJson)
        {
            output.WriteJson(new { daemon = running?.Pid, paused = false, wasPaused = held });

            return Task.FromResult(CommandOutput.Success());
        }

        output.WriteLine(running is null
            ? "[green]+[/] No daemon is running; the hold it left is lifted, so the next one will fire its schedules."
            : held
                ? $"[green]+[/] Resumed {Markup.Escape(Named(running))}: schedules fire again, within a few seconds."
                : $"{Markup.Escape(Named(running))} was not paused. Its schedules are firing.");

        return Task.FromResult(CommandOutput.Success());
    }
}
