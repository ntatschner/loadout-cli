using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Everything the launcher can do, grouped by what it is for.
/// <para>
/// <c>--help</c> lists twenty-six commands alphabetically, which tells somebody
/// who already knows the names where to look and tells a newcomer nothing about
/// where to start. This is the same list arranged by task, generated from the
/// metadata each command declares, so it cannot fall out of step with what is
/// registered.
/// </para>
/// <para>
/// Deliberately a command rather than a replacement for <c>--help</c>. Spectre
/// renders help through a provider that cannot be extended, only substituted
/// wholesale, so grouping the root listing would mean reimplementing usage and
/// option rendering for every command in the tool — a great deal of surface to
/// get wrong for one improvement, and it would change the output of every
/// <c>--help</c> anybody has ever piped somewhere.
/// </para>
/// </summary>
[Description("List everything loadout can do, grouped by what it is for.")]
[CommandMeta(CommandCategory.Administration,
    Intent = "help commands list what can i do getting started",
    Example = "--search backup")]
public sealed class CommandsCommand : Command<CommandsCommand.Settings>
{
    private readonly IAnsiConsole _console;

    public CommandsCommand(IAnsiConsole console) => _console = console;

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--search <TEXT>")]
        [Description("Only commands matching this, by name or by what they are for.")]
        public string? Search { get; init; }

        [CommandOption("--all")]
        [Description("Include sub-commands, not just the top-level ones.")]
        public bool All { get; init; }
    }

    /// <inheritdoc />
    public override int Execute(CommandContext context, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        // Registering the parser is what fills the catalogue, so this reads the
        // same list the command line itself was built from.
        Program.CommandNames();

        var matching = Program.RegisteredCommands()
            .Where(entry => settings.All || !entry.Path.Contains(' ', StringComparison.Ordinal))
            .Where(entry => entry.Matches(settings.Search ?? string.Empty))
            .ToList();

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                commands = matching.Select(entry => new
                {
                    path = entry.Path,
                    description = entry.Description,
                    category = entry.Category,
                    intent = entry.Intent,
                    mutates = entry.Mutates,
                    requiresNetwork = entry.RequiresNetwork,
                    terminalOnly = entry.TerminalOnly,
                    example = entry.Example,
                }),
            });

            return CommandOutput.Success();
        }

        if (matching.Count == 0)
        {
            output.WriteLine(
                $"[yellow]Nothing matches '{Markup.Escape(settings.Search ?? string.Empty)}'.[/]");

            return CommandOutput.Success();
        }

        // Ordered by the categories themselves rather than alphabetically, so
        // the list opens with starting work and ends with housekeeping.
        foreach (var category in CommandCategory.All)
        {
            var group = matching
                .Where(entry => string.Equals(entry.Category, category, StringComparison.Ordinal))
                .OrderBy(entry => entry.Path, StringComparer.Ordinal)
                .ToList();

            if (group.Count == 0)
            {
                continue;
            }

            output.WriteBlankLine();
            output.WriteLine($"[bold]{Markup.Escape(category.ToUpperInvariant())}[/]");

            foreach (var entry in group)
            {
                // The mark says whether choosing this changes anything, before
                // somebody runs it to find out.
                var mark = entry.TerminalOnly is { Length: > 0 } ? "[dim]·[/]"
                    : entry.Mutates ? "[yellow]![/]"
                    : "[dim] [/]";

                output.WriteLine(
                    $"  {mark} [bold]{Markup.Escape(entry.Path)}[/]  "
                    + $"[dim]{Markup.Escape(entry.Description)}[/]");
            }
        }

        output.WriteBlankLine();
        output.WriteLine("[dim]![/] [dim]changes files or configuration[/]");
        output.WriteLine("[dim]·[/] [dim]runs in a terminal only[/]");
        output.WriteBlankLine();
        output.WriteLine("[dim]Search by what you want, not what it is called:[/] "
            + "loadout commands --search undo");

        return CommandOutput.Success();
    }
}
