using Spectre.Console;

namespace Loadout.Tui;

/// <summary>
/// The launcher for somebody who cannot see a full-screen one: the same
/// commands, as a numbered menu, run through the same parser.
/// </summary>
/// <remarks>
/// <para>
/// No terminal toolkit has a screen-reader provider on Windows or macOS. The
/// full-screen launcher draws its list, moves a highlight through it and
/// repaints the rows it leaves, and none of that is announced to anything. It
/// is keyboard-operable and unreadable, and offering it to somebody using a
/// screen reader would be a promise this cannot keep.
/// </para>
/// <para>
/// So under a profile that asks for a text launcher, this is what
/// <c>loadout</c> with no arguments opens instead. Not a smaller set of
/// commands and not a second implementation of any of them: the catalogue is
/// the same one the full-screen launcher shows, and running one hands it back
/// to the same parser the command line uses. A person on this path and a
/// person on the other are using the same program.
/// </para>
/// </remarks>
public sealed class TextLauncher
{
    /// <summary>What is typed to leave.</summary>
    private const string Quit = "Quit";

    private readonly ICommandCatalogue _catalogue;
    private readonly IAnsiConsole _console;
    private readonly ReadingProfile _reading;

    public TextLauncher(ICommandCatalogue catalogue, IAnsiConsole console, ReadingProfile reading)
    {
        _catalogue = catalogue;
        _console = console;
        _reading = reading;
    }

    /// <summary>Whether this is the launcher the person asked for.</summary>
    public bool IsWanted =>
        _reading.Profile is { } profile
        && string.Equals(profile.Display.Launcher, "text", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Offers the commands until somebody leaves, and returns the exit code of
    /// the last one run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Grouped by the category each command declares about itself, in the
    /// order the categories are declared, so the list reads as a list of jobs
    /// rather than of internal names. Everything is on one page: there is no
    /// scrolling to hear, and a person who wants a particular command can read
    /// down to it or leave and type it.
    /// </para>
    /// <para>
    /// A command that needs an argument is told to the person rather than
    /// asked for here. Guessing which of a project, a path and a name a
    /// command wanted would be a second implementation of every command's own
    /// parsing, which is the thing this exists not to be.
    /// </para>
    /// </remarks>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var last = 0;

        while (!ct.IsCancellationRequested)
        {
            var choices = new List<string>();

            foreach (var group in _catalogue.Commands
                .Where(command => command.TerminalOnly is null)
                .GroupBy(command => command.Category))
            {
                foreach (var command in group)
                {
                    choices.Add(command.Path);
                }
            }

            choices.Add(Quit);

            var chosen = _reading.Ask(
                _console,
                "What would you like to do? The commands are:",
                choices,
                path => path == Quit ? "Quit" : Describe(path));

            if (chosen == Quit)
            {
                return last;
            }

            var entry = _catalogue.Commands.First(command => command.Path == chosen);

            if (entry.RequiredArgument is { Length: > 0 } needed)
            {
                // Said, not asked for. What a command wants is the command's
                // own business, and a second parser here would drift from the
                // first the day somebody adds an option.
                _console.MarkupLine(
                    $"{Markup.Escape(entry.Path)} needs {Markup.Escape(needed)}. "
                    + $"Run it with: loadout {Markup.Escape(entry.Example.Length > 0 ? entry.Example : entry.Path)}");

                continue;
            }

            last = await _catalogue.RunAsync(chosen, [], ct).ConfigureAwait(false);

            _console.WriteLine();
        }

        return last;
    }

    private string Describe(string path)
    {
        var entry = _catalogue.Commands.FirstOrDefault(command => command.Path == path);

        return entry is null || entry.Description.Length == 0
            ? path
            : $"{path} - {entry.Description}";
    }
}
