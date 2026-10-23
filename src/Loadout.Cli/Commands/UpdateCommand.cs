using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams.Daemon;
using Loadout.Core.Updates;
using Loadout.Models;
using Loadout.Models.Updates;
using Loadout.Platform.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>
/// Checks the configured release source and installs what it offers
/// (spec section 79).
/// <para>
/// Never updates without being asked. Replacing the binary somebody is about to
/// run is not something to do as a side effect of another command, and spec
/// section 79 explicitly allows automatic checks to be turned off.
/// </para>
/// </summary>
[Description("Check for a newer release and install it.")]
[CommandMeta(CommandCategory.Administration, Intent = "upgrade new version install latest", Mutates = true, RequiresNetwork = true)]
public sealed class UpdateCommand : AsyncCommand<UpdateCommand.Settings>
{
    private readonly IUpdateService _updates;
    private readonly IAnsiConsole _console;
    private readonly IPlatformPaths _paths;
    private readonly IProcessInspector _processes;
    private readonly UpdateFinisher _finisher;
    private readonly TimeProvider _time;

    public UpdateCommand(
        IUpdateService updates,
        IAnsiConsole console,
        IPlatformPaths paths,
        IProcessInspector processes,
        UpdateFinisher finisher,
        TimeProvider time)
    {
        _updates = updates;
        _console = console;
        _paths = paths;
        _processes = processes;
        _finisher = finisher;
        _time = time;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--check")]
        [Description("Report what is available and change nothing.")]
        public bool CheckOnly { get; init; }

        [CommandOption("--yes")]
        [Description("Install without asking for confirmation.")]
        public bool Yes { get; init; }

        [CommandOption("--restart-daemon")]
        [Description(
            "Hand the install to the running daemon: it restarts on the new version once the runs it started "
            + "have finished, putting the old version back if the new one does not start. How Studio updates.")]
        public bool RestartDaemon { get; init; }

        [CommandOption("--now")]
        [Description("With --restart-daemon, restart at once, ending any run it started, rather than waiting for them.")]
        public bool Now { get; init; }

        [CommandOption("--roll-back")]
        [Description(
            "Put back the version the last update replaced, kept beside the binary as .previous. "
            + "For a copy unpacked from a release archive; the MSI keeps no previous version.")]
        public bool RollBack { get; init; }

        /// <summary>The plan the update helper carries out, given by the daemon handing over.</summary>
        [CommandOption("--finish <PLAN>", IsHidden = true)]
        public string? Finish { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        if (settings.Finish is { Length: > 0 } plan)
        {
            return await FinishAsync(plan, cancellationToken).ConfigureAwait(false);
        }

        if (settings.Now && !settings.RestartDaemon)
        {
            return output.Fail("--now says when the daemon restarts, so it needs --restart-daemon.", ExitCode.InvalidArguments);
        }

        if (settings.RollBack)
        {
            return await RollBackAsync(output, settings, cancellationToken).ConfigureAwait(false);
        }

        if (settings.Offline)
        {
            return output.Fail(
                "Cannot check for updates while --offline is set.", ExitCode.InvalidArguments);
        }

        var checkResult = await _updates.CheckAsync().ConfigureAwait(false);
        if (checkResult.Failed)
        {
            return output.Fail(checkResult);
        }

        var check = checkResult.Value!;

        if (output.IsJson && !(settings.RestartDaemon && check.IsNewer))
        {
            output.WriteJson(new
            {
                current = check.CurrentVersion,
                available = check.AvailableVersion,
                updateAvailable = check.IsNewer,
                notes = check.Notes,
                channel = Channel(_updates.Channel),
                canRollBack = CanRollBack(),
            });

            // Checking is not a failure whatever it finds, so this stays zero
            // and the caller reads updateAvailable.
            return CommandOutput.Success();
        }

        output.WriteLine($"Installed  {Markup.Escape(check.CurrentVersion)}");

        if (check.AvailableVersion is null)
        {
            output.WriteLine(
                "[dim]The release source has no build for this platform.[/]");

            return CommandOutput.Success();
        }

        output.WriteLine($"Available  {Markup.Escape(check.AvailableVersion)}");

        if (!check.IsNewer)
        {
            output.WriteLine("[green]Already up to date.[/]");
            return CommandOutput.Success();
        }

        if (check.Notes is { Length: > 0 })
        {
            output.WriteBlankLine();
            output.WriteLine(Markup.Escape(check.Notes));
        }

        if (settings.CheckOnly)
        {
            output.WriteBlankLine();
            output.WriteLine("[dim]Install it with:[/] loadout update");

            return CommandOutput.Success();
        }

        if (!settings.Yes)
        {
            if (!settings.AllowsPrompting)
            {
                // Swapping the binary out from under a script that did not ask
                // for it would be a genuinely bad surprise.
                return output.Fail(
                    "Updating replaces the executable, so it needs confirmation. Pass --yes, "
                    + "or --check to see what is available.",
                    ExitCode.InvalidArguments);
            }

            if (!_console.Confirm(
                $"Install {Markup.Escape(check.AvailableVersion)}?", defaultValue: false))
            {
                output.WriteLine("[dim]Cancelled.[/]");
                return CommandOutput.Success();
            }
        }

        if (settings.RestartDaemon)
        {
            return await HandToDaemonAsync(output, settings, check, cancellationToken).ConfigureAwait(false);
        }

        if (settings.DryRun)
        {
            output.WriteLine(
                $"[bold]Would download and install[/] {Markup.Escape(check.AvailableVersion)}. "
                + "Nothing was downloaded and nothing was changed.");

            return CommandOutput.Success();
        }

        output.WriteLine("[dim]Downloading and verifying...[/]");

        var applyResult = await _updates.ApplyAsync(check).ConfigureAwait(false);
        if (applyResult.Failed)
        {
            return output.Fail(applyResult);
        }

        output.WriteLine($"[green]Updated[/] to {Markup.Escape(check.AvailableVersion)}.");
        output.WriteLine(
            $"[dim]The previous binary was kept at {Markup.Escape(applyResult.Value!)}[/]");

        Daemon(output);

        return CommandOutput.Success();
    }

    /// <summary>
    /// Says that a running daemon is still the old version, and what
    /// restarting it ends.
    /// </summary>
    /// <remarks>
    /// The update leaves it running, sessions and all, which is right: it is
    /// not this command's to stop. But it goes on serving the old version
    /// until somebody restarts it, and the restart ends whatever it hosts for
    /// Studio, so both are said here rather than found out later.
    /// </remarks>
    private void Daemon(CommandOutput output)
    {
        if (DaemonNote.Live(_paths, _processes) is not { } running)
        {
            return;
        }

        output.WriteLine(running.Hosted > 0
            ? $"[yellow]The daemon is still running the previous version,[/] with {running.Hosted} "
              + $"session{(running.Hosted == 1 ? string.Empty : "s")} for Studio that "
              + $"{(running.Hosted == 1 ? "ends" : "end")} when it restarts. Restart it when you are ready: loadout daemon restart"
            : "[dim]The daemon is still running the previous version. Restart it with:[/] loadout daemon restart");
    }

    /// <summary>
    /// Verifies the update now and leaves installing it to the daemon's
    /// restart, which waits for the runs it started unless told not to.
    /// </summary>
    /// <remarks>
    /// Verified here rather than by the helper, so a bad download is reported
    /// to whoever asked while the daemon is still up to show it, rather than in
    /// a log line written while it was down.
    /// </remarks>
    private async Task<int> HandToDaemonAsync(CommandOutput output, Settings settings, UpdateCheck check, CancellationToken ct)
    {
        if (DaemonNote.Live(_paths, _processes) is not { } running)
        {
            return output.Fail(
                "No daemon is running to restart. Update this copy with: loadout update",
                ExitCode.InvalidArguments);
        }

        var channel = _updates.Channel;

        if (settings.DryRun)
        {
            output.WriteLine(
                $"[bold]Would download and verify[/] {Markup.Escape(check.AvailableVersion!)}, then restart the daemon "
                + $"(process {running.Pid}) on it"
                + (settings.Now ? " at once" : " once the runs it started have finished")
                + (channel == InstallChannel.Installer ? ", running the MSI while it is down." : ".")
                + " Nothing was downloaded and nothing was changed.");

            return CommandOutput.Success();
        }

        if (!output.IsJson)
        {
            output.WriteLine("[dim]Downloading and verifying...[/]");
        }

        var staged = await _updates.StageAsync(check, channel, ct).ConfigureAwait(false);

        if (staged.Failed)
        {
            return output.Fail(staged);
        }

        await new UpdatePlan(
            UpdateKind.Install,
            channel,
            check.CurrentVersion,
            check.AvailableVersion,
            staged.Value!,
            _updates.Executable,
            _time.GetUtcNow()).WriteAsync(UpdatePlan.PathFor(_paths), ct).ConfigureAwait(false);

        await DaemonControl.StopAsync(_paths, new DaemonStopRequest(settings.Now, Restart: true), ct).ConfigureAwait(false);

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                current = check.CurrentVersion,
                available = check.AvailableVersion,
                channel = Channel(channel),
                daemon = running.Pid,
                restarting = true,
                now = settings.Now,
            });

            return CommandOutput.Success();
        }

        output.WriteLine($"[green]+[/] Verified {Markup.Escape(check.AvailableVersion!)}. The daemon restarts on it "
            + (settings.Now
                ? "now, ending any run it started."
                : "once the runs it started have finished, and starts nothing new meanwhile.")
            + (running.Hosted > 0
                ? $" The {running.Hosted} session{(running.Hosted == 1 ? string.Empty : "s")} it hosts for Studio "
                  + $"{(running.Hosted == 1 ? "ends" : "end")} when it does."
                : string.Empty));
        output.WriteLine(channel == InstallChannel.Installer
            ? "[dim]The MSI runs while it is down; how that went is written to the daemon's log.[/]"
            : "[dim]If the new version does not start, the old one is put back and started instead.[/]");

        return CommandOutput.Success();
    }

    /// <summary>Puts back the binary the last update kept.</summary>
    private async Task<int> RollBackAsync(CommandOutput output, Settings settings, CancellationToken ct)
    {
        if (_updates.Channel == InstallChannel.Installer)
        {
            return output.Fail(
                "This copy was installed by the MSI, which keeps no previous version to roll back to. "
                + "Install the version you want by running its MSI from the release page.",
                ExitCode.InvalidArguments);
        }

        var executable = _updates.Executable;
        var previous = InstallChannels.PreviousOf(executable);

        if (!File.Exists(previous))
        {
            return output.Fail(
                $"There is nothing to roll back to: no previous binary at {previous}. Each update keeps one.",
                ExitCode.InvalidArguments);
        }

        var running = settings.RestartDaemon ? DaemonNote.Live(_paths, _processes) : null;

        if (settings.RestartDaemon && running is null)
        {
            return output.Fail(
                "No daemon is running to restart. Roll this copy back with: loadout update --roll-back",
                ExitCode.InvalidArguments);
        }

        if (!settings.Yes)
        {
            if (!settings.AllowsPrompting)
            {
                return output.Fail(
                    "Rolling back replaces the executable, so it needs confirmation. Pass --yes.",
                    ExitCode.InvalidArguments);
            }

            if (!_console.Confirm($"Put back {Markup.Escape(previous)}?", defaultValue: false))
            {
                output.WriteLine("[dim]Cancelled.[/]");
                return CommandOutput.Success();
            }
        }

        if (settings.DryRun)
        {
            output.WriteLine($"[bold]Would put back[/] {Markup.Escape(previous)}"
                + (running is not null ? $" and restart the daemon (process {running.Pid}) on it" : string.Empty)
                + ". Nothing was changed.");

            return CommandOutput.Success();
        }

        if (running is not null)
        {
            var current = typeof(UpdateCommand).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

            await new UpdatePlan(UpdateKind.RollBack, InstallChannel.Archive, current, null, null, executable, _time.GetUtcNow())
                .WriteAsync(UpdatePlan.PathFor(_paths), ct).ConfigureAwait(false);

            await DaemonControl.StopAsync(_paths, new DaemonStopRequest(settings.Now, Restart: true), ct).ConfigureAwait(false);

            if (output.IsJson)
            {
                output.WriteJson(new { rolledBack = false, restarting = true, daemon = running.Pid, now = settings.Now });

                return CommandOutput.Success();
            }

            output.WriteLine("[green]+[/] The daemon rolls back to the previous version as it restarts, "
                + (settings.Now ? "now." : "once the runs it started have finished."));

            return CommandOutput.Success();
        }

        var restored = UpdateFiles.Restore(executable);

        if (restored.Failed)
        {
            return output.Fail(restored);
        }

        if (output.IsJson)
        {
            output.WriteJson(new { rolledBack = true, restarting = false });

            return CommandOutput.Success();
        }

        output.WriteLine("[green]Rolled back[/] to the previous version. Running it next time runs that one.");
        Daemon(output);

        return CommandOutput.Success();
    }

    /// <summary>The helper's part, started by the daemon as it hands over.</summary>
    /// <remarks>
    /// Written to the daemon's log, where the daemon's own lines about the
    /// update are, because nothing else is watching: the daemon that started
    /// it has gone, and it was started in the background.
    /// </remarks>
    private async Task<int> FinishAsync(string plan, CancellationToken ct)
    {
        using var log = DaemonLog.Open(DaemonLog.PathFor(_paths));

        return await _finisher.RunAsync(plan, log, ct).ConfigureAwait(false)
            ? CommandOutput.Success()
            : (int)ExitCode.GeneralFailure;
    }

    /// <summary>Whether a roll back is possible: an archive copy with the binary it replaced kept.</summary>
    private bool CanRollBack() =>
        _updates.Channel == InstallChannel.Archive
        && File.Exists(InstallChannels.PreviousOf(_updates.Executable));

    private static string Channel(InstallChannel channel) =>
        channel == InstallChannel.Installer ? "installer" : "archive";
}
