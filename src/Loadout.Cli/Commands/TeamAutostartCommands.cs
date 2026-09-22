using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Agents;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Starts the daemon when this person logs in.
/// </summary>
/// <remarks>
/// <para>
/// Nothing fires a schedule unless the daemon is running, and a machine that
/// has restarted since somebody started it looks exactly like a machine where
/// everything is fine. <c>doctor</c> says so; this is the answer to it.
/// </para>
/// <para>
/// Per user and never for the machine, so it needs no administrator rights and
/// can always be undone by whoever set it.
/// </para>
/// </remarks>
[Description("Start the team daemon when you log in.")]
[CommandMeta(CommandCategory.Integration,
    Intent = "autostart login startup daemon schedule boot", Mutates = true)]
public sealed class TeamAutostartEnableCommand : AsyncCommand<GlobalSettings>
{
    private readonly IAutostart _autostart;
    private readonly IAnsiConsole _console;

    public TeamAutostartEnableCommand(IAutostart autostart, IAnsiConsole console)
    {
        _autostart = autostart;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (LauncherInvocation.Current() is not { Length: > 0 } launcher)
        {
            return output.Fail(
                "This launcher cannot work out how it was started, so it cannot ask to be started again.",
                ExitCode.GeneralFailure);
        }

        var command = $"{launcher} team daemon";

        if (settings.DryRun)
        {
            // The whole of what it would do, named. A preview of something
            // that writes into a login folder has to say which file.
            output.WriteLine($"Would write [bold]{Markup.Escape(_autostart.Describe())}[/]");
            output.WriteLine($"  running: {Markup.Escape(command)}");
            output.WriteLine("Nothing was written.");

            return CommandOutput.Success();
        }

        var installed = await _autostart.InstallAsync(command, cancellationToken).ConfigureAwait(false);

        if (installed.Failed)
        {
            return output.Fail(installed);
        }

        output.WriteLine($"[green]+[/] The daemon will start when you log in.");
        output.WriteLine($"  [dim]{Markup.Escape(_autostart.Describe())}[/]");
        output.WriteBlankLine();

        // Said rather than done. Starting it here would be a second thing this
        // command does, and somebody running it to set up a machine they are
        // about to restart does not want a daemon starting now.
        output.WriteLine("[dim]It is not running yet. Start it now with: loadout team daemon[/]");

        // Where the page is, which nothing used to say. The daemon prints its
        // address once, and at login it prints it into a window nobody is
        // looking at - so somebody who enabled this and then logged in had a
        // dashboard running and no way to reach it. The port and the token are
        // both new every start, so there is nothing to write down either.
        output.WriteLine(
            "[dim]Its dashboard gets a fresh address every start. Ask where it is with: "
            + "loadout team dashboard[/]");

        output.WriteLine("[dim]Undo with: loadout team autostart disable[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Stops the daemon starting at login.</summary>
[Description("Stop the team daemon starting when you log in.")]
[CommandMeta(CommandCategory.Integration,
    Intent = "autostart disable remove login startup daemon", Mutates = true)]
public sealed class TeamAutostartDisableCommand : AsyncCommand<GlobalSettings>
{
    private readonly IAutostart _autostart;
    private readonly IAnsiConsole _console;

    public TeamAutostartDisableCommand(IAutostart autostart, IAnsiConsole console)
    {
        _autostart = autostart;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.DryRun)
        {
            output.WriteLine($"Would remove [bold]{Markup.Escape(_autostart.Describe())}[/]. Nothing was removed.");

            return CommandOutput.Success();
        }

        var gone = await _autostart.UninstallAsync(cancellationToken).ConfigureAwait(false);

        if (gone.Failed)
        {
            return output.Fail(gone);
        }

        // Nothing is stopped. A daemon already running stays running, because
        // stopping somebody's running process is not what "do not start at
        // login" asked for.
        output.WriteLine("[green]+[/] The daemon will not start at login.");
        output.WriteLine("[dim]One already running is still running.[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Whether the daemon starts at login.</summary>
[Description("Say whether the team daemon starts when you log in.")]
[CommandMeta(CommandCategory.Integration, Intent = "autostart show status login startup daemon")]
public sealed class TeamAutostartShowCommand : AsyncCommand<GlobalSettings>
{
    private readonly IAutostart _autostart;
    private readonly IAnsiConsole _console;

    public TeamAutostartShowCommand(IAutostart autostart, IAnsiConsole console)
    {
        _autostart = autostart;
        _console = console;
    }

    /// <inheritdoc />
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var installed = _autostart.IsInstalled();

        if (installed.Failed)
        {
            return Task.FromResult(output.Fail(installed));
        }

        if (output.IsJson)
        {
            output.WriteJson(new { enabled = installed.Value, entry = _autostart.Describe() });

            return Task.FromResult(CommandOutput.Success());
        }

        output.WriteLine(installed.Value
            ? "[green]on[/]  the daemon starts when you log in"
            : "[dim]off[/] the daemon starts only when you start it");

        output.WriteLine($"     [dim]{Markup.Escape(_autostart.Describe())}[/]");

        return Task.FromResult(CommandOutput.Success());
    }
}
