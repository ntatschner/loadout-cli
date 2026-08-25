using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;

namespace Loadout.Tui.Terminal;

/// <summary>
/// Replaces the characters Terminal.Gui decorates controls with when the stock
/// console font has no such character to draw.
/// </summary>
/// <remarks>
/// <para>
/// Terminal.Gui 2 brackets every button in <c>U+27E6</c> and <c>U+27E7</c>,
/// the mathematical white square brackets. Cascadia Mono has neither, and
/// Cascadia Mono is what a Windows console uses unless somebody has changed
/// it, so every button in the launcher renders as:
/// </para>
/// <code>
/// ⊡ Launch claude ⊡    ⊡ Resume ⊡    ⊡ Shell ⊡
/// </code>
/// <para>
/// Nothing caught this, because nothing was looking at pixels. The test
/// harness drives the ANSI driver and asserts on the text it produces, and the
/// text was right the whole time — a missing glyph is a decision made by the
/// font, long after the character has left this program. It was found by
/// photographing a real console window, and can be found again the same way:
/// <c>build/screenshot-tui.ps1</c>.
/// </para>
/// <para>
/// That script also drew a sheet of every glyph Terminal.Gui defines outside
/// the ranges a stock console font covers. Seven of them have no glyph:
/// <c>LeftBracket</c>, <c>RightBracket</c>, <c>Selected</c>, <c>Folder</c>,
/// <c>Copy</c>, <c>DottedSquare</c> and <c>Null</c>. Only the brackets are
/// substituted here, because only the brackets are on screen — the launcher
/// has no radio group and no file dialog. The other five are recorded rather
/// than pre-emptively replaced: choosing a stand-in for a glyph nothing draws
/// means guessing at what it is meant to convey, and the guess would never be
/// looked at. If a control that needs one is added, this is the list, and the
/// script is how to check.
/// </para>
/// <para>
/// Note in passing that <c>UnSelected</c> (<c>U+25CB</c>) does render while
/// <c>Selected</c> (<c>U+25C9</c>) does not, so a radio group would show a
/// legible circle for every option except the chosen one.
/// </para>
/// </remarks>
internal static class ConsoleGlyphs
{
    /// <summary>
    /// Starts the application and makes its decoration legible.
    /// </summary>
    /// <remarks>
    /// Paired with <see cref="IApplication.Init"/> rather than left as a
    /// separate call somebody has to remember at four call sites, one of which
    /// would eventually be added without it.
    /// </remarks>
    internal static void InitLegibly(this IApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.Init();

        // After Init, which is where the configuration that sets these is
        // applied. Before it, they are overwritten by the defaults again.
        MakeLegible();
    }

    /// <summary>
    /// Swaps decoration a stock console font cannot draw for plain equivalents.
    /// </summary>
    internal static void MakeLegible()
    {
        Glyphs.LeftBracket = new Rune('[');
        Glyphs.RightBracket = new Rune(']');
    }
}
