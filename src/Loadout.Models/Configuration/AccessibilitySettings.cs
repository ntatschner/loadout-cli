namespace Loadout.Models.Configuration;

/// <summary>
/// How this person wants to be written to and asked.
/// </summary>
/// <remarks>
/// <para>
/// In <c>config.yaml</c> because it is the person's own preference, the same
/// on every project they work on and never anybody else's business. It does
/// not travel with the workspace.
/// </para>
/// <para>
/// Every setting is named for the demand it reduces rather than the condition
/// somebody has. A person should not have to declare a diagnosis to a
/// configuration file to get shorter sentences, and the settings are useful to
/// people who would not claim one. The presets exist so the settings can be
/// found at all, and each is documented as the bundle it sets; anything a
/// preset sets can be changed on its own afterwards.
/// </para>
/// <para>
/// Nothing here is detected. A person turns it on, and the first line of
/// accessible output says which profile is active, so they can see it took.
/// </para>
/// </remarks>
public sealed class AccessibilitySettings
{
    /// <summary>
    /// A bundle of settings under a name people already use, or "none".
    /// </summary>
    /// <remarks>
    /// Applied first, then anything set explicitly wins over it. So a person
    /// can take the dyslexia bundle and still ask for technical vocabulary.
    /// </remarks>
    public string Preset { get; set; } = AccessibilityPresets.None;

    public AccessibilityQuestions Questions { get; set; } = new();

    public AccessibilityOutput Output { get; set; } = new();

    public AccessibilityDisplay Display { get; set; } = new();
}

/// <summary>The bundles, by the names people look for.</summary>
public static class AccessibilityPresets
{
    public const string None = "none";

    public const string ScreenReader = "screen-reader";

    public const string LowVision = "low-vision";

    public const string ColourBlind = "colour-blind";

    public const string Dyslexia = "dyslexia";

    public const string Adhd = "adhd";

    public const string PlainLanguage = "plain-language";

    /// <summary>Every preset, in the order they are documented.</summary>
    public static IReadOnlyList<string> All { get; } =
        [None, ScreenReader, LowVision, ColourBlind, Dyslexia, Adhd, PlainLanguage];
}

/// <summary>How the agent puts a question.</summary>
/// <remarks>
/// One question at a time is the most strongly supported rule in every
/// guideline this was drawn from: GOV.UK's one thing per page, the NHS
/// "form as a conversation", and COGA. The rest of this says what a question
/// looks like when it arrives.
/// </remarks>
public sealed class AccessibilityQuestions
{
    /// <summary>choices, written or mixed.</summary>
    public string Style { get; set; } = "choices";

    /// <summary>One question per message, and wait for the answer.</summary>
    public bool OneAtATime { get; set; } = true;

    /// <summary>A sentence on why the question is being asked, before it.</summary>
    public bool Why { get; set; } = true;

    /// <summary>on-request, always or never: how much a question is unpacked.</summary>
    public string Explain { get; set; } = "on-request";

    /// <summary>Label the option the agent would choose, with its downside. Never pre-selected.</summary>
    public bool Recommend { get; set; } = true;

    /// <summary>Offer "I don't know", "none of these" or "explain this" as a real answer.</summary>
    public bool UnsureOption { get; set; } = true;

    /// <summary>Say "question 2 of 4" when more than one is coming.</summary>
    public bool Progress { get; set; } = true;
}

/// <summary>What the agent's own prose looks like.</summary>
public sealed class AccessibilityOutput
{
    /// <summary>concise, standard or full.</summary>
    public string Verbosity { get; set; } = "standard";

    /// <summary>plain, mixed or technical.</summary>
    public string Technicality { get; set; } = "mixed";

    /// <summary>Anything longer than a screen starts with one to three lines saying what it says.</summary>
    public bool SummaryFirst { get; set; } = true;

    /// <summary>Split a sentence longer than this many words.</summary>
    public int Sentences { get; set; } = 25;

    /// <summary>At most this many sentences in a paragraph, holding one idea.</summary>
    public int Paragraph { get; set; } = 5;

    /// <summary>numbered or prose: how instructions are given.</summary>
    public string Steps { get; set; } = "numbered";

    /// <summary>bold-only or any: italics, underline and capitals read badly aloud and on the page.</summary>
    public string Emphasis { get; set; } = "bold-only";

    /// <summary>One word for one thing, and every abbreviation expanded the first time.</summary>
    public bool SameWord { get; set; } = true;

    /// <summary>Restate what will happen before anything that cannot be undone or that costs money.</summary>
    public bool ConfirmBeforeIrreversible { get; set; } = true;

    /// <summary>
    /// Bold the first half of each word in the agent's own prose.
    /// </summary>
    /// <remarks>
    /// Offered, and off by default, with the evidence stated wherever it is
    /// documented: every controlled study of this finds no reading gain and
    /// one finds it slower. It is here because people preferred styled text
    /// even where it did not help them, and preference is a fair reason to
    /// offer something somebody can switch off. It is refused under the
    /// screen-reader preset, where bold markup is read aloud on every word.
    /// </remarks>
    public bool Bionic { get; set; }
}

/// <summary>What Loadout itself draws, and what it asks the agent not to draw.</summary>
public sealed class AccessibilityDisplay
{
    /// <summary>full, sixteen or none. Only the sixteen ANSI colours are the person's own to remap.</summary>
    public string Colour { get; set; } = "full";

    /// <summary>Avoid red and green as a pair; blue and orange survive every common deficiency.</summary>
    public bool ColourSafe { get; set; }

    /// <summary>unicode or ascii. A screen reader reads box drawing aloud, character by character.</summary>
    public string Glyphs { get; set; } = "unicode";

    /// <summary>full, reduced or none.</summary>
    public string Motion { get; set; } = "full";

    /// <summary>allowed or never. A spinner or an in-place edit is re-read on every frame.</summary>
    public string Redraw { get; set; } = "allowed";

    /// <summary>tables or lists.</summary>
    public string Tables { get; set; } = "tables";

    /// <summary>arrows or numbered.</summary>
    public string Menus { get; set; } = "arrows";

    /// <summary>
    /// full or text.
    /// </summary>
    /// <remarks>
    /// No terminal toolkit has a screen-reader provider on Windows or macOS,
    /// so the full-screen launcher cannot be announced and saying otherwise
    /// would be a silent gap. Under text, the same commands are offered as a
    /// numbered menu through the same parser.
    /// </remarks>
    public string Launcher { get; set; } = "full";

    /// <summary>Ring the terminal bell when input is needed.</summary>
    public bool Bell { get; set; }
}
