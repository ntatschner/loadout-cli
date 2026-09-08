using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

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
        // Flat. A button with a shadow under it is drawn in two colours the
        // font has to have block glyphs for, and in the documentation image
        // the shadows read as a row of debris under every button. Nothing
        // else on the screen has depth, so a button should not either.
        Button.DefaultShadow = ShadowStyles.None;

        // The body of the application: frames, lists, labels.
        Set(Schemes.Base, new Scheme
        {
            // Body text, and by default the frame round it: a border takes
            // whatever scheme its view has unless it is handed one of its own,
            // which is what Quieten below does for the panels. Normal here is
            // therefore the weight of a project's name, not of a line.
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

        // The toolkit's Accent scheme, for anything that asks for it by name.
        // This was believed to be where the frames got their colour, and it
        // is not: reading back the colour of every cell after a draw showed
        // the frames in Base's Normal, the same weight as the text inside
        // them. A frame is coloured by handing its border a scheme, which is
        // what Quieten does below. Kept themed so that nothing asking for
        // "Accent" comes out cyan.
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

    // What follows is applied to particular views rather than by name, because
    // a frame's border is not a view with a scheme name of its own. It is drawn
    // by an adornment that takes whatever its parent has, so a frame's lines
    // were drawn in the same weight as the text inside it — every box on the
    // screen as loud as the words it was around, which is the toolkit-sample
    // look the palette above was supposed to have ended.

    /// <summary>A frame nobody is in: lines and title in the quiet grey.</summary>
    private static readonly Scheme AtRest = new()
    {
        Normal = new Ink(Dim, Ground),
        HotNormal = new Ink(Dim, Ground),
        Focus = new Ink(Accent, Ground),
        HotFocus = new Ink(Accent, Ground),
        Active = new Ink(Dim, Ground),
        HotActive = new Ink(Dim, Ground),
        Highlight = new Ink(Accent, Ground),
        Disabled = new Ink(Dim, Ground),
    };

    /// <summary>The frame you are in: lines and title in the accent.</summary>
    private static readonly Scheme Lit = new()
    {
        Normal = new Ink(Accent, Ground),
        HotNormal = new Ink(Accent, Ground),
        Focus = new Ink(Accent, Ground),
        HotFocus = new Ink(Bright, Ground),
        Active = new Ink(Accent, Ground),
        HotActive = new Ink(Accent, Ground),
        Highlight = new Ink(Accent, Ground),
        Disabled = new Ink(Dim, Ground),
    };

    /// <summary>
    /// Makes a frame quiet until the focus is inside it, and warm while it is.
    /// </summary>
    /// <remarks>
    /// The border draws its lines in its own Normal whether or not the view
    /// is focused; only the title follows the focus. So the whole scheme is
    /// swapped when the focus arrives rather than relying on the roles, which
    /// is what makes the line and the title change together. One warm frame
    /// on a screen of grey ones says where you are without a bar of colour
    /// having to.
    /// </remarks>
    internal static void Quieten(View frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var border = frame.Border?.View
            ?? throw new InvalidOperationException("The frame has no border to colour.");

        border.SetScheme(AtRest);

        frame.HasFocusChanged += (_, e) => border.SetScheme(e.NewValue ? Lit : AtRest);
    }

    /// <summary>
    /// Keeps a frame warm whether or not it has the focus, for a panel that
    /// is put up to be read rather than moved into.
    /// </summary>
    internal static void Light(View frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        (frame.Border?.View ?? throw new InvalidOperationException("The frame has no border to colour."))
            .SetScheme(Lit);
    }

    /// <summary>
    /// A surface that sits above the ground: the detail pane. Everything
    /// inside it inherits this, so a button or a list added later is on the
    /// same surface without being told.
    /// </summary>
    internal static readonly Scheme RaisedSurface = new()
    {
        Normal = new Ink(Text, Raised),
        HotNormal = new Ink(Accent, Raised),
        Focus = new Ink(Ground, Accent),
        HotFocus = new Ink(Ground, Accent),
        Active = new Ink(Ground, Accent),
        HotActive = new Ink(Ground, Accent),
        Highlight = new Ink(Accent, Raised),
        Editable = new Ink(Text, Raised),
        ReadOnly = new Ink(Dim, Raised),
        Disabled = new Ink(Dim, Raised),
    };

    /// <summary>A heading on the raised surface: bright and bold.</summary>
    internal static readonly Scheme Heading = new()
    {
        Normal = new Ink(Bright, Raised, TextStyle.Bold),
        HotNormal = new Ink(Bright, Raised, TextStyle.Bold),
        Focus = new Ink(Bright, Raised, TextStyle.Bold),
        HotFocus = new Ink(Bright, Raised, TextStyle.Bold),
        Disabled = new Ink(Dim, Raised),
    };

    /// <summary>The name of a fact, beside its value: quieter than the value.</summary>
    internal static readonly Scheme Muted = new()
    {
        Normal = new Ink(Dim, Raised),
        HotNormal = new Ink(Dim, Raised),
        Focus = new Ink(Dim, Raised),
        HotFocus = new Ink(Dim, Raised),
        Disabled = new Ink(Dim, Raised),
    };

    /// <summary>The same, on the ground rather than the raised surface.</summary>
    internal static readonly Scheme MutedOnGround = new()
    {
        Normal = new Ink(Dim, Ground),
        HotNormal = new Ink(Accent, Ground),
        Focus = new Ink(Dim, Ground),
        HotFocus = new Ink(Accent, Ground),
        Disabled = new Ink(Dim, Ground),
    };

    /// <summary>A key somebody could press, on the ground: the accent, at rest.</summary>
    internal static readonly Scheme KeyOnGround = new()
    {
        Normal = new Ink(Accent, Ground),
        HotNormal = new Ink(Accent, Ground),
        Focus = new Ink(Accent, Ground),
        HotFocus = new Ink(Accent, Ground),
        Disabled = new Ink(Dim, Ground),
    };

    /// <summary>Something wrong, said on the raised surface.</summary>
    internal static readonly Scheme Alert = new()
    {
        Normal = new Ink(Warn, Raised, TextStyle.Bold),
        HotNormal = new Ink(Warn, Raised, TextStyle.Bold),
        Focus = new Ink(Warn, Raised, TextStyle.Bold),
        HotFocus = new Ink(Warn, Raised, TextStyle.Bold),
        Disabled = new Ink(Dim, Raised),
    };
}
