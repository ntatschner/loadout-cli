using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

// Disambiguated because System.Attribute is in scope everywhere.
using Ink = Terminal.Gui.Drawing.Attribute;

namespace Loadout.Tui.Terminal;

/// <summary>
/// What the launcher looks like.
/// </summary>
/// <remarks>
/// <para>
/// Until this existed the launcher set no colours at all — every one on screen
/// was Terminal.Gui's stock scheme, which is why it looked like a toolkit
/// sample: a solid cyan bar across the top, a light grey slab for the filter,
/// bright blue borders, and a yellow selection competing with all of them.
/// Nothing was wrong with any of it individually. Together, everything shouted
/// and so nothing stood out.
/// </para>
/// <para>
/// The palette is small on purpose. One ground, one raised surface for things
/// that sit above it, two weights of text, a quiet border, and a single accent
/// that means "this is where you are". Colours that carry meaning rather than
/// decoration — a blocked project, a warning — are separate from the accent
/// and used nowhere else, which is what stops a screen full of colour from
/// meaning nothing.
/// </para>
/// <para>
/// None of it is load-bearing. The launcher says everything in words and marks
/// as well, so it reads the same in a terminal with no colour at all: readiness
/// carries a label, not just a hue. That rule came first and this is decoration
/// on top of it, which is the only order that works.
/// </para>
/// </remarks>
internal static class LauncherTheme
{
    private const string Ground = "#12141A";
    private const string Raised = "#1B2029";
    private const string Text = "#C6CCD8";
    private const string Bright = "#EDF1F7";
    private const string Dim = "#6B7383";
    private const string Border = "#333B49";
    private const string Selected = "#39435A";
    private const string Accent = "#E0A458";
    private const string Warn = "#D9736A";

    /// <summary>
    /// Applies the palette to the schemes every screen draws from.
    /// </summary>
    /// <remarks>
    /// Applied by name to the schemes Terminal.Gui already uses rather than by
    /// setting colours on each view. A view asks for "Menu" or "Dialog" and
    /// gets whatever those mean; changing what they mean changes everything at
    /// once, and a screen added later is themed without anybody remembering to
    /// theme it.
    /// </remarks>
    /// <summary>Replaces one of the toolkit's own schemes.</summary>
    /// <remarks>
    /// The name is asked for rather than written down, because the names are
    /// the toolkit's. Spelling one by hand is how the first attempt themed
    /// "TopLevel", which does not exist: the call succeeded, added a scheme
    /// nothing draws with, and the borders stayed cyan.
    /// </remarks>
    private static void Set(Schemes scheme, Scheme colours) =>
        SchemeManager.AddScheme(
            SchemeManager.SchemesToSchemeName(scheme)
                ?? throw new InvalidOperationException($"Terminal.Gui has no scheme named {scheme}."),
            colours);

    internal static void Apply()
    {
        // The body of the application: frames, lists, labels.
        Set(Schemes.Base, new Scheme
        {
            // Borders, titles and body text all come from Normal, because a
            // border is not a view in this toolkit and cannot be given a
            // scheme of its own. So the hierarchy cannot come from role — it
            // has to come from state, and Normal has to be a weight that suits
            // a project's name and the box drawn round it equally.
            Normal = new Ink(Text, Ground),

            // The accent, and the only place it appears at rest: the letter
            // you would press. An accent that never shows is not an accent.
            HotNormal = new Ink(Accent, Ground),

            // Where you are. One warm row against a screen of grey, which is
            // the whole of the colour design: everything else recedes so this
            // does not have to shout to be found.
            // Ground on accent, not accent on slate. The first attempt put
            // the warm colour on the text and left the bar behind it grey,
            // and amber on slate is two mid-tones fighting: the selected row
            // was the least readable line on the screen, which is precisely
            // backwards.
            Focus = new Ink(Ground, Accent),
            HotFocus = new Ink(Ground, Accent),
            Active = new Ink(Ground, Accent),
            HotActive = new Ink(Ground, Accent),

            Highlight = new Ink(Accent, Ground),
            Editable = new Ink(Text, Raised),
            ReadOnly = new Ink(Dim, Ground),
            Disabled = new Ink(Dim, Ground),
        });

        // Borders and titles. This is the one the launcher was missing: the
        // cyan frames everywhere came from here, and the first attempt themed
        // a scheme called "TopLevel" that does not exist — Terminal.Gui's
        // names are Base, Accent, Dialog, Menu and Error, so that call added a
        // scheme nothing asked for and the frames stayed exactly as they were.
        //
        // Quiet at rest and accented when focused, so the panel you are in is
        // the one with the warm border. That is colour saying something rather
        // than colour being present.
        Set(Schemes.Accent, new Scheme
        {
            Normal = new Ink(Border, Ground),
            HotNormal = new Ink(Accent, Ground),
            Focus = new Ink(Accent, Ground),
            HotFocus = new Ink(Bright, Ground),
            Active = new Ink(Accent, Ground),
            HotActive = new Ink(Bright, Ground),
            Highlight = new Ink(Accent, Ground),
            Disabled = new Ink(Dim, Ground),
        });

        // Raised rather than reversed. The stock menu is a solid bar of cyan
        // the full width of the screen, which is the loudest thing on a screen
        // whose loudest thing should be the project you are about to open.
        Set(Schemes.Menu, new Scheme
        {
            Normal = new Ink(Text, Raised),
            HotNormal = new Ink(Accent, Raised),
            Focus = new Ink(Bright, Selected),
            HotFocus = new Ink(Accent, Selected),
            Disabled = new Ink(Dim, Raised),
        });

        Set(Schemes.Dialog, new Scheme
        {
            Normal = new Ink(Text, Raised),
            HotNormal = new Ink(Accent, Raised),
            Focus = new Ink(Bright, Selected),
            HotFocus = new Ink(Accent, Selected),
            Disabled = new Ink(Dim, Raised),
        });

        // The one place colour is allowed to be loud, because it is the one
        // place it is carrying something that cannot wait.
        Set(Schemes.Error, new Scheme
        {
            Normal = new Ink(Warn, Raised),
            HotNormal = new Ink(Bright, Raised),
            Focus = new Ink(Raised, Warn),
            HotFocus = new Ink(Bright, Warn),
            Disabled = new Ink(Dim, Raised),
        });
    }
}
