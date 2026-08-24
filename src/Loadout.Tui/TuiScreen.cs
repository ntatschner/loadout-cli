using Spectre.Console;

namespace Loadout.Tui;

/// <summary>
/// The frame every screen is drawn in.
/// <para>
/// Before this, each screen appended to whatever the last one had left behind.
/// The title scrolled away, a long project list pushed everything above it out
/// of view, and no two screens began in the same place. This gives every screen
/// one entry point: clear, draw the title, then draw the content.
/// </para>
/// <para>
/// Redrawing is conditional on the terminal actually supporting it. Where it
/// does not — a pipe, a redirected stream, a test console — the frame degrades
/// to plain sequential output rather than emitting escape sequences into
/// something that will print them literally.
/// </para>
/// </summary>
internal sealed class TuiScreen
{
    /// <summary>
    /// Rows kept back from a list for the title, the prompt and the margin
    /// around them, so a list never grows into the frame that contains it.
    /// </summary>
    private const int Furniture = 9;

    /// <summary>
    /// Shortest list worth paging. Below this the scrolling costs more
    /// attention than the space it saves.
    /// </summary>
    private const int ShortestPage = 5;

    /// <summary>
    /// Longest list to show at once. A page taller than this is a wall, and
    /// the search-as-you-type in a Spectre prompt is the better way through a
    /// long list anyway.
    /// </summary>
    private const int LongestPage = 18;

    /// <summary>Switch to the alternate buffer, keeping the primary intact.</summary>
    private const string EnterAlternateScreen = "\u001b[?1049h";

    /// <summary>Switch back, restoring whatever was on screen before.</summary>
    private const string LeaveAlternateScreen = "\u001b[?1049l";

    private readonly IAnsiConsole _console;

    internal TuiScreen(IAnsiConsole console) => _console = console;

    /// <summary>
    /// Whether the screen can be redrawn in place. False for a pipe or a
    /// captured console, where clearing would either do nothing or destroy the
    /// output somebody is reading.
    /// </summary>
    /// <remarks>
    /// Read from the console it was given rather than from the process's own
    /// standard output. Spectre already works out both capabilities when it
    /// builds a profile, and consulting the static Console instead would make
    /// this answer depend on something the caller cannot influence — including
    /// under a test runner, where output is always redirected.
    /// </remarks>
    private bool CanRedraw =>
        _console.Profile.Capabilities.Ansi
        && _console.Profile.Capabilities.Interactive;

    /// <summary>
    /// Begins a screen. Clears where it can, then draws the title, so every
    /// screen starts at the top of the terminal rather than below the last one.
    /// </summary>
    /// <param name="title">Name of the screen.</param>
    /// <param name="subtitle">One line of context, such as which project this is.</param>
    internal void Begin(string title, string? subtitle = null)
    {
        if (CanRedraw)
        {
            _console.Clear();
        }
        else
        {
            // Without a clear, a blank line is what separates this screen from
            // the one before it.
            _console.WriteLine();
        }

        _console.Write(new Rule($"[bold]{Markup.Escape(title)}[/]").LeftJustified());

        if (subtitle is { Length: > 0 })
        {
            _console.MarkupLine($"[dim]{Markup.Escape(subtitle)}[/]");
        }

        _console.WriteLine();
    }

    /// <summary>
    /// How many rows a list may occupy before it scrolls within itself.
    /// <para>
    /// Derived from the terminal rather than fixed, so a tall window shows more
    /// and a short one still leaves room for the title and the prompt. This is
    /// what stops a list of twenty projects pushing the heading off the screen.
    /// </para>
    /// </summary>
    internal int PageSize
    {
        get
        {
            var available = _console.Profile.Height - Furniture;

            return Math.Clamp(available, ShortestPage, LongestPage);
        }
    }

    /// <summary>
    /// Runs the launcher inside the terminal's alternate screen where one
    /// exists, so the session leaves the scrollback exactly as it found it.
    /// </summary>
    /// <remarks>
    /// A launcher that scrolls a person's terminal history away to draw a menu
    /// has taken something it cannot give back. Where there is no alternate
    /// screen the action simply runs as it always did.
    /// </remarks>
    internal async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!CanRedraw)
        {
            return await action().ConfigureAwait(false);
        }

        // Written directly rather than through Spectre's wrapper, which only
        // takes a synchronous action: driving async work through it would mean
        // blocking on it. Two escape sequences, and a finally that restores the
        // primary screen whatever happens - leaving somebody in the alternate
        // buffer would look like their terminal had lost its history.
        _console.Write(new ControlCode(EnterAlternateScreen));

        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            _console.Write(new ControlCode(LeaveAlternateScreen));
        }
    }
}
