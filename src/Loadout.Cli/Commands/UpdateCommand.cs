using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Updates;
using Loadout.Models;
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

    public UpdateCommand(IUpdateService updates, IAnsiConsole console)
    {
        _updates = updates;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--check")]
        [Description("Report what is available and change nothing.")]
        public bool CheckOnly { get; init; }

        [CommandOption("--yes")]
        [Description("Install without asking for confirmation.")]
        public bool Yes { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(_console, settings);

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

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                current = check.CurrentVersion,
                available = check.AvailableVersion,
                updateAvailable = check.IsNewer,
                notes = check.Notes,
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

        output.WriteLine("[dim]Downloading and verifying...[/]");

        var applyResult = await _updates.ApplyAsync(check).ConfigureAwait(false);
        if (applyResult.Failed)
        {
            return output.Fail(applyResult);
        }

        output.WriteLine($"[green]Updated[/] to {Markup.Escape(check.AvailableVersion)}.");
        output.WriteLine(
            $"[dim]The previous binary was kept at {Markup.Escape(applyResult.Value!)}[/]");

        return CommandOutput.Success();
    }
}
