using Spectre.Console;

namespace Loadout.Tui;

/// <summary>
/// Every command, searchable, for the ones no menu should carry.
/// <para>
/// The grouped screens hold what somebody does often. This holds everything, so
/// the launcher is never a subset of the command line — reindexing memory or
/// restoring a backup does not deserve a permanent place in a menu, but it does
/// deserve to be reachable without leaving.
/// </para>
/// <para>
/// Built from the command registry rather than from a list kept here, so a
/// command added tomorrow appears without anyone remembering to add it.
/// </para>
/// </summary>
internal sealed class CommandPalette
{
    private readonly IAnsiConsole _console;
    private readonly ICommandCatalogue _catalogue;
    private readonly TuiScreen _screen;

    internal CommandPalette(IAnsiConsole console, ICommandCatalogue catalogue, TuiScreen screen)
    {
        _console = console;
        _catalogue = catalogue;
        _screen = screen;
    }

    /// <summary>
    /// Shows the palette and runs what is chosen.
    /// </summary>
    /// <param name="project">
    /// Project to run the command against, so a chosen command acts on where
    /// somebody already was rather than asking again.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    internal async Task RunAsync(string? project, CancellationToken ct)
    {
        _screen.Begin("All commands", project);

        var entries = _catalogue.Commands
            .OrderBy(e => e.Group, StringComparer.Ordinal)
            .ThenBy(e => e.Path, StringComparer.Ordinal)
            .ToList();

        if (entries.Count == 0)
        {
            _console.MarkupLine("[dim]No commands are registered.[/]");

            return;
        }

        var back = "Back";

        var choices = entries.Select(Label).Append(back).ToList();

        var chosen = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("Which command?")
                .PageSize(_screen.PageSize)

                // Fifty-odd commands is too many to arrow through, and the
                // title claimed this was searchable before it was.
                .EnableSearch()
                .SearchPlaceholderText("[dim]type to filter[/]")
                .AddChoices(choices));

        if (chosen == back)
        {
            return;
        }

        var entry = entries[choices.IndexOf(chosen)];

        if (!entry.Runnable)
        {
            _console.WriteLine();
            _console.MarkupLine(
                $"[yellow]{Markup.Escape(entry.Path)}[/] belongs on the command line: "
                + $"[dim]{Markup.Escape(entry.TerminalOnly!)}.[/]");
            _console.WriteLine();
            _console.MarkupLine($"[dim]Run it there as: loadout {Markup.Escape(entry.Path)}[/]");
            _console.WriteLine();

            return;
        }

        await ExecuteAsync(entry, project, ct).ConfigureAwait(false);
    }

    /// <summary>Runs one command and waits, so its output can be read.</summary>
    private async Task ExecuteAsync(CatalogueEntry entry, string? project, CancellationToken ct)
    {
        _screen.Begin(entry.Path, project);

        // Passed rather than prompted for. Somebody who opened the palette from
        // a project meant that project, and asking again would be the launcher
        // forgetting where it was.
        var arguments = project is { Length: > 0 } && WantsProject(entry)
            ? new[] { "--project", project }
            : [];

        try
        {
            var exitCode = await _catalogue.RunAsync(entry.Path, arguments, ct).ConfigureAwait(false);

            _console.WriteLine();

            if (exitCode != 0)
            {
                _console.MarkupLine($"[dim]Finished with exit code {exitCode}.[/]");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A command that throws must not take the launcher with it: whoever
            // ran it is still in the middle of something else.
            _console.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
        }

        _console.WriteLine();
        _console.MarkupLine("[dim]Press Enter to go back.[/]");
        _console.Input.ReadKey(intercept: true);
    }

    /// <summary>
    /// Whether a command takes <c>--project</c>. Read from the command line's
    /// own convention rather than from a list: every command that acts on one
    /// project accepts it, and the rest ignore what they are not given.
    /// </summary>
    private static bool WantsProject(CatalogueEntry entry) =>
        entry.Group is "memory" or "rules" or "mcp" or "profile" or "project"
        || entry.Path is "drift" or "sessions" or "resume" or "handoff" or "migrate";

    /// <summary>One line in the palette: what it is, and what it does.</summary>
    private static string Label(CatalogueEntry entry)
    {
        var name = entry.Path.PadRight(22);

        var description = entry.Runnable
            ? entry.Description
            : $"{entry.Description} (command line only)";

        return $"{name} {description}".TrimEnd();
    }
}
