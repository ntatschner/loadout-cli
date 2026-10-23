using System.Globalization;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Updates;

/// <summary>
/// The short-lived helper that replaces the daemon once it has gone, starts
/// the new one and, for an archive install, puts the old one back if the new
/// one does not come up.
/// </summary>
/// <remarks>
/// <para>
/// A process of its own because the daemon cannot replace itself: it is the
/// thing being replaced, and with the Restart Manager off the MSI cannot
/// replace a running daemon at all. Run from a copy outside the install
/// folder, which the daemon makes, so the MSI is not replacing the helper's
/// own image either.
/// </para>
/// <para>
/// Everything it does is written to the daemon's log, a line at a time,
/// because for an MSI install the log is the only place a failure is
/// reported: there is no page to show it on while the daemon is down.
/// </para>
/// </remarks>
public sealed class UpdateFinisher(
    IProcessLauncher launcher,
    IProcessInspector processes,
    IFilePermissions permissions,
    IPlatformPaths paths,
    TimeProvider time)
{
    /// <summary>How long the daemon is given to go before the helper gives up on it.</summary>
    internal TimeSpan Departure { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How long a new daemon is given to say it is serving.</summary>
    internal TimeSpan Arrival { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// msiexec's own limit. An install that has not finished in ten minutes
    /// is waiting on something nobody can see, and the daemon is down meanwhile.
    /// </summary>
    internal static readonly TimeSpan Installing = TimeSpan.FromMinutes(10);

    internal TimeSpan Glance { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Carries out the plan at <paramref name="planPath"/>, saying what happens on <paramref name="log"/>.</summary>
    /// <returns>Whether the daemon came back on the version the plan asked for.</returns>
    public async Task<bool> RunAsync(string planPath, TextWriter log, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (UpdatePlan.Read(planPath) is not { } plan)
        {
            Say(log, $"there is no update to finish at {planPath}.");

            return false;
        }

        try
        {
            return await CarryOutAsync(plan, log, ct).ConfigureAwait(false);
        }
        finally
        {
            // Taken back whatever happened: a plan left behind would be
            // carried out again by the next restart, with a payload that may
            // already be installed.
            UpdatePlan.Clear(planPath);
        }
    }

    private async Task<bool> CarryOutAsync(UpdatePlan plan, TextWriter log, CancellationToken ct)
    {
        Say(log, $"{plan.Described}, asked for at {plan.AskedAt.ToLocalTime():HH:mm}.");

        if (plan.Daemon is not { Count: > 0 } daemon)
        {
            Say(log, "the daemon did not say how to start it again, so nothing was changed. Start it with: loadout daemon");

            return false;
        }

        if (plan.DaemonPid is { } pid && plan.DaemonStartedAt is { } started
            && !await GoneAsync(pid, started, ct).ConfigureAwait(false))
        {
            Say(log, $"the daemon (process {pid}) had not stopped after {Departure.TotalMinutes:0} minutes, "
                + "so nothing was changed.");

            return false;
        }

        var applied = await ApplyAsync(plan, log, ct).ConfigureAwait(false);

        if (applied.Failed)
        {
            Say(log, applied.Error!);
            Say(log, "starting the daemon again on the version it was.");

            await StartAsync(daemon, log, ct).ConfigureAwait(false);

            return false;
        }

        if (await StartAsync(daemon, log, ct).ConfigureAwait(false))
        {
            Say(log, plan.Kind == UpdateKind.RollBack
                ? "rolled back: the daemon is serving again on the previous version."
                : $"updated: the daemon is serving again on {plan.Version}.");

            Tidy(plan);

            return true;
        }

        if (plan.Channel == InstallChannel.Installer || plan.Kind == UpdateKind.RollBack)
        {
            // Nothing to put back: the MSI keeps no previous binary, and a
            // roll back has just used the only one there was.
            Say(log, "the new daemon did not start. Look above for why, or start it by hand: loadout daemon");

            return false;
        }

        Say(log, $"the new daemon did not start, so the previous binary is being put back.");

        var restored = UpdateFiles.Restore(plan.Executable);

        if (restored.Failed)
        {
            Say(log, restored.Error! + " Put it back by hand, then start the daemon: loadout daemon");

            return false;
        }

        Say(log, await StartAsync(daemon, log, ct).ConfigureAwait(false)
            ? $"put back: the daemon is serving again on {plan.From}."
            : "put back, but the daemon did not start on that either. Start it by hand: loadout daemon");

        return false;
    }

    /// <summary>Puts the new binary in place, runs the MSI, or puts the old binary back.</summary>
    private async Task<OperationResult> ApplyAsync(UpdatePlan plan, TextWriter log, CancellationToken ct)
    {
        if (plan.Kind == UpdateKind.RollBack)
        {
            if (plan.Channel != InstallChannel.Archive)
            {
                return OperationResult.Fail("a copy the MSI installed keeps no previous binary to roll back to.");
            }

            Say(log, $"putting back {InstallChannels.PreviousOf(plan.Executable)}.");

            return UpdateFiles.Restore(plan.Executable);
        }

        if (plan.Payload is not { Length: > 0 } payload || !File.Exists(payload))
        {
            return OperationResult.Fail($"the verified download is no longer at {plan.Payload}.");
        }

        if (plan.Channel == InstallChannel.Archive)
        {
            Say(log, $"putting {plan.Version} in place at {plan.Executable}, keeping the old binary beside it.");

            var swapped = UpdateFiles.Swap(plan.Executable, payload, permissions);

            return swapped.Succeeded ? OperationResult.Ok() : OperationResult.Fail(swapped.Error!);
        }

        var msiLog = Path.Combine(Path.GetDirectoryName(payload)!, "msiexec.log");

        Say(log, $"running the {plan.Version} installer; its own log is {msiLog}.");

        var ran = await launcher.RunAsync(
            new ProcessRequest("msiexec", ["/i", payload, "/qn", "/norestart", "/l*v", msiLog]),
            Installing,
            ct).ConfigureAwait(false);

        if (ran.Failed)
        {
            return OperationResult.Fail($"the installer did not run: {ran.Error}");
        }

        // 3010 is success with a restart wanted, which a per-user install
        // that touches nothing of the system's never needs.
        return ran.Value!.ExitCode switch
        {
            0 or 3010 => OperationResult.Ok(),
            1618 => OperationResult.Fail("the installer refused: another installation is in progress. Try again when it has finished."),
            1603 => OperationResult.Fail(
                $"the installer failed (1603). A Loadout session in progress stops it, as does the launcher "
                + $"being open somewhere; {msiLog} says which."),
            var code => OperationResult.Fail($"the installer failed with exit code {code}; {msiLog} says why."),
        };
    }

    /// <summary>Starts the daemon and waits for it to say it is serving.</summary>
    private async Task<bool> StartAsync(IReadOnlyList<string> daemon, TextWriter log, CancellationToken ct)
    {
        var started = launcher.StartBackground(new ProcessRequest(daemon[0], daemon.Skip(1).ToList()));

        if (started.Failed)
        {
            Say(log, $"the daemon did not start: {started.Error}");

            return false;
        }

        var process = started.Value!;
        var until = time.GetUtcNow() + Arrival;

        while (time.GetUtcNow() < until)
        {
            if (DaemonNote.Live(paths, processes) is { } note && note.Pid == process.Pid)
            {
                return true;
            }

            if (!processes.IsRunning(process.Pid, process.StartedAt))
            {
                Say(log, $"the daemon (process {process.Pid.ToString(CultureInfo.InvariantCulture)}) exited as it started.");

                return false;
            }

            await Task.Delay(Glance, time, ct).ConfigureAwait(false);
        }

        Say(log, $"the daemon (process {process.Pid.ToString(CultureInfo.InvariantCulture)}) had not said it was serving "
            + $"after {Arrival.TotalSeconds:0} seconds.");

        return false;
    }

    private async Task<bool> GoneAsync(int pid, DateTimeOffset started, CancellationToken ct)
    {
        var until = time.GetUtcNow() + Departure;

        while (processes.IsRunning(pid, started))
        {
            if (time.GetUtcNow() >= until)
            {
                return false;
            }

            await Task.Delay(Glance, time, ct).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Clears the staging folder once it has served, leaving the helper's own
    /// copy, which is still running, to the next update.
    /// </summary>
    private static void Tidy(UpdatePlan plan)
    {
        if (plan.Payload is not { Length: > 0 } payload)
        {
            return;
        }

        try
        {
            File.Delete(payload);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Say(TextWriter log, string text) =>
        log.WriteLine($"{time.GetUtcNow().ToLocalTime():HH:mm} update: {text}");
}
