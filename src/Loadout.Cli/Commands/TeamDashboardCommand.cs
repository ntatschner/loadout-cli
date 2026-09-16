using System.ComponentModel;
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
/// Serves a page showing what the teams on this machine are doing.
/// </summary>
/// <remarks>
/// <para>
/// A run already has three views: the command that started it prints as it
/// goes, <c>team status</c> reads the journal back, and <c>team log --follow</c>
/// watches one. This is the fourth, and the one that answers "what is
/// happening" without somebody asking again every thirty seconds - and the
/// only one that shows every run at once.
/// </para>
/// <para>
/// It reads and nothing else. Approving a gate or stopping a run from a page
/// has to go through the same parser somebody would type at, which is the rule
/// the launcher already follows; and there is nothing yet on the other end to
/// receive it, because a run is a process its own command is holding. That
/// arrives with the daemon, which will serve this same page.
/// </para>
/// </remarks>
[Description("Serve a page on this machine showing what the team runs are doing. Reads only; nothing can be changed from it.")]
[CommandMeta(CommandCategory.Start, Intent = "team dashboard web page browser watch runs live")]
public sealed class TeamDashboardCommand : AsyncCommand<TeamDashboardCommand.Settings>
{
    private readonly IRunJournal _journal;
    private readonly IAnsiConsole _console;
    private readonly IApplicationLauncher _opener;
    private readonly ReadingProfile _reading;

    public TeamDashboardCommand(
        IRunJournal journal,
        IAnsiConsole console,
        IApplicationLauncher opener,
        ReadingProfile reading)
    {
        _journal = journal;
        _console = console;
        _opener = opener;
        _reading = reading;
    }

    /// <summary>
    /// Stops the server when whatever started it closes its input.
    /// </summary>
    /// <remarks>
    /// Only where the input is a pipe. A person at a terminal has Ctrl+C and
    /// has not asked for their keyboard to be read; a script has closed the
    /// pipe precisely to say it is finished.
    /// </remarks>
    internal static async Task Ends(CancellationTokenSource stopping)
    {
        if (!Console.IsInputRedirected)
        {
            return;
        }

        await Task.Run(async () =>
        {
            try
            {
                await Console.In.ReadToEndAsync(stopping.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
            }

            await stopping.CancelAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--port <PORT>")]
        [Description("The port to listen on. One the machine chooses when omitted.")]
        public int Port { get; init; }

        [CommandOption("--open")]
        [Description("Open the page in a browser once it is listening.")]
        public bool Open { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        using var server = new DashboardServer(_journal);

        if (settings.DryRun)
        {
            output.WriteLine(
                "[dim]Dry run: nothing was served.[/] A dashboard would listen on "
                + (settings.Port == 0 ? "a port this machine chose" : $"port {settings.Port}")
                + ", on 127.0.0.1 only, with a token in its address.");

            return CommandOutput.Success();
        }

        var started = server.Start(settings.Port);

        if (started.Failed)
        {
            return output.Fail(started);
        }

        if (output.IsJson)
        {
            // For whatever started this: the address is the only thing worth
            // having, and it carries the token.
            output.WriteJson(new { address = server.Address, port = new Uri(server.Address).Port });
        }
        else
        {
            output.WriteLine($"[bold]{Markup.Escape(server.Address)}[/]");
            output.WriteLine(
                "[dim]On this machine only, and only with that token. It reads; nothing can be "
                + "changed from it.[/]");
            output.WriteLine(Console.IsInputRedirected
                ? "[dim]It stops when whatever started it closes its input.[/]"
                : "[dim]Press Ctrl+C to stop.[/]");
        }

        if (settings.Open && output.CanOpenAWindow)
        {
            // Asked for, never assumed. A command that opened a browser
            // because it felt like it is one somebody runs once.
            await _opener.OpenUrlAsync(server.Address, cancellationToken).ConfigureAwait(false);
        }

        _reading.Ring(_console);

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // A server started by something other than a person ends when that
        // something lets go. The same contract the MCP server has, and the
        // reason it matters is not theoretical: the contract test that runs
        // every registered command ran this one, and a command that serves
        // until Ctrl+C held the whole suite for twenty minutes.
        var ending = Ends(stopping);

        try
        {
            await server.ListenAsync(stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C, or the input ending, which are the two ways this ends.
        }

        await ending.ConfigureAwait(false);

        return (int)ExitCode.Success;
    }
}
