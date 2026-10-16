using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams.Daemon;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Ends every browser's sign-in to the dashboard on this machine.</summary>
/// <remarks>
/// <para>
/// The key the cookies are signed with is replaced, so every cookie and every
/// unused sign-in link stops working at once, including in a daemon that is
/// already running: it reads the key again when the file changes.
/// </para>
/// <para>
/// A link with the token in it still works, because the token belongs to the
/// running daemon rather than to the sign-in, and goes when the daemon stops.
/// </para>
/// </remarks>
[Description("Sign every browser out of the dashboard on this machine. Open it again with: loadout dashboard --open")]
[CommandMeta(CommandCategory.Start, Intent = "dashboard sign out log out signout logout cookie revoke browser", Mutates = true)]
public sealed class DashboardSignOutCommand : AsyncCommand<GlobalSettings>
{
    private readonly IPlatformPaths _paths;
    private readonly IAnsiConsole _console;

    public DashboardSignOutCommand(IPlatformPaths paths, IAnsiConsole console)
    {
        _paths = paths;
        _console = console;
    }

    /// <inheritdoc />
    public override Task<int> ExecuteAsync(
        CommandContext context,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.DryRun)
        {
            output.WriteLine("Would sign every browser out of the dashboard on this machine. Nothing was changed.");

            return Task.FromResult(CommandOutput.Success());
        }

        SignIn.ForMachine(_paths).Rotate();

        if (output.IsJson)
        {
            output.WriteJson(new { signedOut = true });

            return Task.FromResult(CommandOutput.Success());
        }

        output.WriteLine("[green]+[/] Signed every browser out of the dashboard on this machine.");
        output.WriteLine("[dim]Open it again with: loadout dashboard --open[/]");

        return Task.FromResult(CommandOutput.Success());
    }
}
