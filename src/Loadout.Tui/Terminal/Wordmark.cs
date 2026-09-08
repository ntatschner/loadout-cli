using System.Text;

namespace Loadout.Tui.Terminal;

/// <summary>
/// The name, drawn large, with the letters riding a wave.
/// <para>
/// Kept as plain string generation rather than drawing routines so the shape of
/// every frame can be asserted without a terminal anywhere in sight. An
/// animation is exactly the sort of thing that is never tested and then turns
/// out to render as a column of spaces on somebody else's machine.
/// </para>
/// </summary>
internal static class Wordmark
{
    /// <summary>Rows in one letter, before any wave is applied.</summary>
    private const int LetterHeight = 5;

    /// <summary>
    /// How far a letter may ride above the baseline. Two rows is enough to read
    /// as a wave and small enough to fit a short terminal.
    /// </summary>
    internal const int Amplitude = 2;

    /// <summary>Blank columns between letters.</summary>
    private const int Spacing = 1;

    /// <summary>Full height of a frame, wave included.</summary>
    internal static int Height => LetterHeight + Amplitude;

    /// <summary>
    /// How far the wave advances per frame, and how far the phase shifts from
    /// one letter to the next. The second is what makes it travel along the
    /// word rather than every letter bobbing together.
    /// </summary>
    private const double StepPhase = 0.55;

    private const double LetterPhase = 0.75;

    private static readonly string[] Dash =
    [
        "     ",
        "     ",
        "█████",
        "     ",
        "     ",
    ];

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['-'] = Dash,
        ['L'] = ["█    ", "█    ", "█    ", "█    ", "█████"],
        ['O'] = ["█████", "█   █", "█   █", "█   █", "█████"],
        ['A'] = ["█████", "█   █", "█████", "█   █", "█   █"],
        ['D'] = ["████ ", "█   █", "█   █", "█   █", "████ "],
        ['U'] = ["█   █", "█   █", "█   █", "█   █", "█████"],
        ['T'] = ["█████", "  █  ", "  █  ", "  █  ", "  █  "],
    };

    /// <summary>The word, as it is drawn.</summary>
    internal const string Word = "-LOADOUT-";

    /// <summary>
    /// One frame of the animation, as rows of text.
    /// </summary>
    /// <param name="step">
    /// Which frame. Any integer: the wave repeats, and negative steps are as
    /// valid as positive ones.
    /// </param>
    internal static IReadOnlyList<string> Frame(int step)
    {
        var letters = Word.ToCharArray();

        // How far each letter has ridden up, this frame.
        var lift = new int[letters.Length];

        for (var i = 0; i < letters.Length; i++)
        {
            var wave = Math.Sin((step * StepPhase) - (i * LetterPhase));

            // sin gives -1..1; this maps it onto whole rows of 0..Amplitude.
            lift[i] = (int)Math.Round((Amplitude / 2.0) * (1 + wave), MidpointRounding.AwayFromZero);
        }

        var rows = new List<string>(Height);

        for (var row = 0; row < Height; row++)
        {
            var line = new StringBuilder();

            for (var i = 0; i < letters.Length; i++)
            {
                if (i > 0)
                {
                    line.Append(' ', Spacing);
                }

                var glyph = Glyphs.TryGetValue(letters[i], out var found) ? found : Dash;

                // The letter sits Amplitude rows down at rest and rises from
                // there, so the tallest lift still lands inside the frame.
                var within = row - (Amplitude - lift[i]);

                line.Append(within >= 0 && within < LetterHeight
                    ? glyph[within]
                    : new string(' ', glyph[0].Length));
            }

            rows.Add(line.ToString());
        }

        return rows;
    }

    /// <summary>One frame as a single string, for a view that takes text.</summary>
    internal static string FrameText(int step) =>
        string.Join(Environment.NewLine, Frame(step));

    /// <summary>Width of a frame, which does not change between them.</summary>
    internal static int Width =>
        (Word.Length * Dash[0].Length) + ((Word.Length - 1) * Spacing);

    /// <summary>
    /// A short bar that fills and empties, for saying that something is being
    /// read without claiming to know how far along it is.
    /// </summary>
    /// <remarks>
    /// A progress bar would be a lie here: reading a repository's state gives
    /// no way to know how much of it is left.
    /// </remarks>
    /// <param name="step">Which frame. Any integer: the wave repeats.</param>
    /// <param name="width">How many columns the bar fills.</param>
    /// <param name="glyphs">
    /// The levels to draw the bar in, lowest first. <see cref="Bars"/> unless
    /// the terminal cannot draw them.
    /// </param>
    internal static string Pulse(int step, int width = 12, string? glyphs = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);

        var blocks = glyphs is { Length: > 1 } ? glyphs : Bars;

        var bar = new StringBuilder(width);

        for (var i = 0; i < width; i++)
        {
            var wave = Math.Sin((step * StepPhase) - (i * 0.6));
            var level = (int)Math.Round((blocks.Length - 1) / 2.0 * (1 + wave));

            bar.Append(blocks[Math.Clamp(level, 0, blocks.Length - 1)]);
        }

        return bar.ToString();
    }

    /// <summary>
    /// The bar as a wave of rising blocks, which is how it is meant to look.
    /// </summary>
    internal const string Bars = " ▁▂▃▄▅▆▇█";

    /// <summary>
    /// The same wave in shades, for a console whose font has no eighth blocks.
    /// </summary>
    /// <remarks>
    /// A photograph of a plain Windows console showed <see cref="Bars"/> as a
    /// row of boxes with only the half and full blocks legible: the stock
    /// console font has the code page 437 blocks — the halves, the full block
    /// and the three shades — and none of the eighths. The headless tests
    /// could not see it, because a missing glyph is decided by the font long
    /// after the character has left this program. The bars were preferred
    /// where they can be drawn, so this is the fallback rather than the rule.
    /// </remarks>
    internal const string Shades = " ░▒▓█";

    /// <summary>
    /// Which form the reading indicator takes on this terminal.
    /// </summary>
    /// <remarks>
    /// Decided from what the terminal is rather than from what it says it can
    /// do. The toolkit's legacy-console flag was tried first and is false on
    /// a Windows 11 console, which speaks the escape sequences perfectly well
    /// and still has no glyph for an eighth block. Windows Terminal ships a
    /// font that has them and marks its children with <c>WT_SESSION</c>;
    /// every other host on Windows is assumed to be the plain console, whose
    /// font is the one photographed. Elsewhere the bars are safe.
    /// </remarks>
    /// <param name="onWindows">Whether this is Windows at all.</param>
    /// <param name="inWindowsTerminal">Whether Windows Terminal is the host.</param>
    internal static string PulseGlyphsFor(bool onWindows, bool inWindowsTerminal) =>
        onWindows && !inWindowsTerminal ? Shades : Bars;
}
