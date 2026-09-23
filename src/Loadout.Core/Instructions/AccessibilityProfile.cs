using System.Text;
using Loadout.Models.Configuration;

namespace Loadout.Core.Instructions;

/// <summary>
/// The person's accessibility settings, with their preset applied, and the
/// guidance they compile to.
/// </summary>
/// <remarks>
/// <para>
/// This is the first of the accessibility work and the widest: it reaches
/// every session of every agent from one file, without a line of adapter code.
/// What it produces is a section of the compiled context, composed after
/// everything else, because the person reading is the most specific thing
/// there is and the last thing read wins.
/// </para>
/// <para>
/// A preset is a starting point, not a mode. It is applied first and anything
/// the person set explicitly wins over it, so somebody can take the dyslexia
/// bundle and still ask for the field's own vocabulary.
/// </para>
/// <para>
/// What this deliberately does not do: choose a font, score the text for
/// readability, or guess at a person's needs from their machine. Every
/// peer-reviewed study of dyslexia fonts finds no gain in speed or accuracy,
/// readability scores measure sentence length rather than whether anybody
/// understood, and a guessed profile is one nobody asked for. What has the
/// evidence behind it is structure, and structure is what this writes.
/// </para>
/// </remarks>
public static class AccessibilityProfile
{
    /// <summary>The heading the compiled section carries.</summary>
    public const string Heading = "Who is reading this";

    /// <summary>
    /// The settings as they apply, with the preset filled in underneath
    /// anything set by hand.
    /// </summary>
    /// <remarks>
    /// Compared against a fresh default to decide what was "set by hand",
    /// which is the same thing YAML deserialisation leaves behind: a key
    /// absent from the file keeps its default, so a value that differs from
    /// the default was written by somebody.
    /// </remarks>
    public static AccessibilitySettings Resolve(AccessibilitySettings? settings)
    {
        if (settings is null)
        {
            return new AccessibilitySettings();
        }

        var preset = Preset(settings.Preset);

        if (preset is null)
        {
            return settings;
        }

        var untouched = new AccessibilitySettings();
        var resolved = new AccessibilitySettings { Preset = settings.Preset };

        // The preset first, then anything that differs from a default, which
        // is what somebody wrote down.
        Apply(resolved, preset);

        Fill(resolved.Questions, settings.Questions, untouched.Questions);
        Fill(resolved.Output, settings.Output, untouched.Output);
        Fill(resolved.Display, settings.Display, untouched.Display);

        return resolved;
    }

    /// <summary>Whether anything here changes how a session is written to.</summary>
    public static bool IsSet(AccessibilitySettings? settings)
    {
        if (settings is null)
        {
            return false;
        }

        var resolved = Resolve(settings);
        var untouched = new AccessibilitySettings();

        return !string.Equals(resolved.Preset, AccessibilityPresets.None, StringComparison.OrdinalIgnoreCase)
            || Differs(resolved.Questions, untouched.Questions)
            || Differs(resolved.Output, untouched.Output)
            || Differs(resolved.Display, untouched.Display);
    }

    /// <summary>
    /// The guidance this profile compiles to, or null when there is none.
    /// </summary>
    /// <remarks>
    /// Written as rules in the same shape the team roles use, because the same
    /// thing is being asked of a model either way: a verb it can check itself
    /// against, an observable outcome, and an instead for every prohibition.
    /// </remarks>
    public static string? Compose(AccessibilitySettings? settings)
    {
        if (!IsSet(settings))
        {
            return null;
        }

        var profile = Resolve(settings!);
        var text = new StringBuilder();

        text.AppendLine(
            $"The person you are working with has set an accessibility profile ({Describe(profile)}). "
            + "Everything below is how they have asked to be written to and asked.");
        text.AppendLine();
        text.AppendLine(
            "It applies to what you write. It never applies to what you quote: a log line, a diff, "
            + "an error message or a command's output is reproduced exactly.");
        text.AppendLine();

        Questions(text, profile);
        Writing(text, profile);
        Levels(text, profile);

        text.AppendLine("### What this does not change");
        text.AppendLine();
        text.AppendLine(
            "The work. A change is still tested, a failing test is still reported as failing with its "
            + "output, and a secret is still never printed. This changes how things are said, never "
            + "what is done or what is true.");

        return text.ToString().TrimEnd();
    }

    private static void Questions(StringBuilder text, AccessibilitySettings profile)
    {
        text.AppendLine("### Asking questions");
        text.AppendLine();

        if (profile.Questions.OneAtATime)
        {
            text.AppendLine(
                "- You MUST ask one question per message. Instead of a second question, wait for the "
                + "answer, then ask the next.");

            if (profile.Questions.Progress)
            {
                text.AppendLine(
                    "- When more than one question is coming, you MUST say which this is: \"question 2 of 4\".");
            }
        }

        if (profile.Questions.Why)
        {
            text.AppendLine("- You MUST say in one sentence why you are asking, before the question.");
        }

        switch (profile.Questions.Style)
        {
            case "written":
                text.AppendLine(
                    "- You MUST ask for a written answer and say what form it should take: a word, a "
                    + "sentence, a path, a yes or a no. Instead of a list of choices, describe what you "
                    + "need to know.");
                break;

            case "mixed":
                text.AppendLine(
                    "- You SHOULD offer choices where the answers are known and ask for a written answer "
                    + "where they are not.");
                break;

            default:
                text.AppendLine(
                    "- You MUST give the answers as a numbered list of choices, each a short phrase that "
                    + "reads correctly on its own. Where you have a question tool, use it with one "
                    + "question per call; where you do not, write the list and accept a number as the answer.");
                break;
        }

        if (profile.Questions.UnsureOption)
        {
            text.AppendLine(
                "- You MUST include a way out with every question: \"I don't know\", \"none of these\", or "
                + "\"explain this question\". Choosing it is a valid answer, and you answer it before asking again.");
        }

        if (profile.Questions.Recommend)
        {
            text.AppendLine(
                "- You MUST label the option you would choose as recommended and say its downside in a few "
                + "words. You MUST NOT choose it for the person or treat silence as agreement. Instead, wait.");
        }

        switch (profile.Questions.Explain)
        {
            case "always":
                text.AppendLine(
                    "- You MUST follow every question with two or three plain sentences on what the answer changes.");
                break;

            case "never":
                break;

            default:
                text.AppendLine(
                    "- When the person asks what a question means, you MUST explain it in plain words, with "
                    + "an example, before asking it again.");
                break;
        }

        if (profile.Output.ConfirmBeforeIrreversible)
        {
            text.AppendLine(
                "- Before anything that cannot be undone or that costs money, you MUST restate in one or two "
                + "sentences what will happen and ask once. Instead of proceeding on an earlier answer, ask "
                + "again at the point of action.");
        }

        text.AppendLine();
    }

    private static void Writing(StringBuilder text, AccessibilitySettings profile)
    {
        text.AppendLine("### Writing");
        text.AppendLine();
        text.AppendLine("- You MUST put the answer, or the thing to do, in the first sentence.");

        if (profile.Output.SummaryFirst)
        {
            text.AppendLine(
                "- Anything longer than a screen MUST start with a summary of one to three lines.");
        }

        text.AppendLine(
            $"- Sentences MUST stay under {profile.Output.Sentences} words; split a longer one. Paragraphs "
            + $"MUST stay under {profile.Output.Paragraph} sentences and hold one idea.");

        if (string.Equals(profile.Output.Steps, "numbered", StringComparison.OrdinalIgnoreCase))
        {
            text.AppendLine("- Instructions MUST be numbered steps, one action per step.");
        }

        if (profile.Output.SameWord)
        {
            text.AppendLine(
                "- You MUST use one word for one thing throughout, and expand every abbreviation the first "
                + "time it appears.");
        }

        if (string.Equals(profile.Output.Emphasis, "bold-only", StringComparison.OrdinalIgnoreCase))
        {
            text.AppendLine(
                "- Emphasis MUST be bold, sparingly. You MUST NOT use italics, underline or capitals for "
                + "emphasis. Instead, put the important words first.");
        }

        if (string.Equals(profile.Display.Tables, "lists", StringComparison.OrdinalIgnoreCase))
        {
            text.AppendLine(
                "- You MUST NOT use tables. Instead, write \"Label: value\" lines or a numbered list.");
        }

        if (string.Equals(profile.Display.Glyphs, "ascii", StringComparison.OrdinalIgnoreCase))
        {
            text.AppendLine(
                "- You MUST NOT draw diagrams, trees or boxes out of characters. Instead, describe the "
                + "structure in sentences or a nested list.");
        }

        text.AppendLine(
            "- Errors, warnings and confirmations of destructive actions MUST be given in full, at every "
            + "level of detail.");

        if (profile.Output.Bionic && !IsScreenReader(profile))
        {
            text.AppendLine(
                "- The person has asked for bionic formatting: in your own prose, you MUST bold the first "
                + "half of each word of four or more letters, rounding down, as **bio**nic **rea**ding "
                + "**for**matting **wou**ld. You MUST NOT apply it inside code, paths, commands, quoted "
                + "output, headings, list markers or links. Instead, leave those exactly as they are. If "
                + "the person says it is not helping, stop for the rest of the session and say so.");
        }

        text.AppendLine();
    }

    private static void Levels(StringBuilder text, AccessibilitySettings profile)
    {
        text.AppendLine("### Detail and technicality");
        text.AppendLine();
        text.AppendLine($"Three levels of detail, and the person has chosen **{profile.Output.Verbosity}**:");
        text.AppendLine();
        text.AppendLine("- concise: the result, what changed, what is next. No preamble.");
        text.AppendLine("- standard: the result, the reasoning in brief, the evidence.");
        text.AppendLine("- full: everything, including what was considered and rejected.");
        text.AppendLine();
        text.AppendLine(
            $"Three levels of technicality, and the person has chosen **{profile.Output.Technicality}**:");
        text.AppendLine();
        text.AppendLine("- plain: ordinary words, no jargon, every term explained the first time.");
        text.AppendLine("- mixed: technical terms where they are exact, each explained once.");
        text.AppendLine("- technical: the field's own vocabulary, no explanations.");
        text.AppendLine();
        text.AppendLine(
            "When the person says \"be brief\", \"say more\", \"less technical\", \"more technical\" or "
            + "\"explain that more simply\", you MUST change to that level for the rest of the session and "
            + "say in one line that you have. Instead of asking whether they meant it, do it; it is easy "
            + "to reverse.");
        text.AppendLine();
    }

    /// <summary>The profile in a few words, for the line that says it took.</summary>
    public static string Describe(AccessibilitySettings profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return string.Equals(profile.Preset, AccessibilityPresets.None, StringComparison.OrdinalIgnoreCase)
            ? "set by hand"
            : profile.Preset;
    }

    private static bool IsScreenReader(AccessibilitySettings profile) =>
        string.Equals(profile.Preset, AccessibilityPresets.ScreenReader, StringComparison.OrdinalIgnoreCase);

    /// <summary>The settings a named preset stands for, or null for none and for a name nobody knows.</summary>
    /// <remarks>
    /// An unknown name is treated as no preset rather than refused here. The
    /// configuration command is where a name is checked and a person is told
    /// what the names are; a launch that met one would otherwise fail over a
    /// typo in a preference.
    /// </remarks>
    public static AccessibilitySettings? Preset(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        AccessibilityPresets.ScreenReader => new AccessibilitySettings
        {
            Questions = new AccessibilityQuestions { Style = "choices", OneAtATime = true },
            Display = new AccessibilityDisplay
            {
                Redraw = "never",
                Glyphs = "ascii",
                Tables = "lists",
                Menus = "numbered",
                Launcher = "text",
                Motion = "none",
                Bell = true,
            },
        },

        AccessibilityPresets.LowVision => new AccessibilitySettings
        {
            Output = new AccessibilityOutput { Emphasis = "bold-only", SummaryFirst = true },
            Display = new AccessibilityDisplay { Colour = "sixteen", Motion = "reduced" },
        },

        AccessibilityPresets.ColourBlind => new AccessibilitySettings
        {
            Display = new AccessibilityDisplay { Colour = "sixteen", ColourSafe = true },
        },

        AccessibilityPresets.Dyslexia => new AccessibilitySettings
        {
            Output = new AccessibilityOutput
            {
                Sentences = 20,
                Paragraph = 4,
                Emphasis = "bold-only",
                SameWord = true,
                Steps = "numbered",
                Technicality = "plain",
                SummaryFirst = true,
            },
        },

        AccessibilityPresets.Adhd => new AccessibilitySettings
        {
            Questions = new AccessibilityQuestions { OneAtATime = true, Progress = true },
            Output = new AccessibilityOutput
            {
                SummaryFirst = true,
                Verbosity = "concise",
                ConfirmBeforeIrreversible = true,
            },
            Display = new AccessibilityDisplay { Motion = "reduced", Redraw = "never" },
        },

        AccessibilityPresets.PlainLanguage => new AccessibilitySettings
        {
            Questions = new AccessibilityQuestions { Explain = "always", Why = true },
            Output = new AccessibilityOutput
            {
                Technicality = "plain",
                Verbosity = "standard",
                SameWord = true,
            },
        },

        _ => null,
    };

    private static void Apply(AccessibilitySettings target, AccessibilitySettings preset)
    {
        target.Questions = Copy(preset.Questions);
        target.Output = Copy(preset.Output);
        target.Display = Copy(preset.Display);
    }

    private static AccessibilityQuestions Copy(AccessibilityQuestions from) => new()
    {
        Style = from.Style,
        OneAtATime = from.OneAtATime,
        Why = from.Why,
        Explain = from.Explain,
        Recommend = from.Recommend,
        UnsureOption = from.UnsureOption,
        Progress = from.Progress,
    };

    private static AccessibilityOutput Copy(AccessibilityOutput from) => new()
    {
        Verbosity = from.Verbosity,
        Technicality = from.Technicality,
        SummaryFirst = from.SummaryFirst,
        Sentences = from.Sentences,
        Paragraph = from.Paragraph,
        Steps = from.Steps,
        Emphasis = from.Emphasis,
        SameWord = from.SameWord,
        ConfirmBeforeIrreversible = from.ConfirmBeforeIrreversible,
        Bionic = from.Bionic,
    };

    private static AccessibilityDisplay Copy(AccessibilityDisplay from) => new()
    {
        Colour = from.Colour,
        ColourSafe = from.ColourSafe,
        Glyphs = from.Glyphs,
        Motion = from.Motion,
        Redraw = from.Redraw,
        Tables = from.Tables,
        Menus = from.Menus,
        Launcher = from.Launcher,
        Bell = from.Bell,
    };

    /// <summary>
    /// Copies across every property the person changed from its default.
    /// </summary>
    /// <remarks>
    /// By reflection rather than by hand, because a property added to the
    /// settings and forgotten here would be a preference silently ignored -
    /// which is the one failure mode a preferences file must not have.
    /// </remarks>
    private static void Fill<T>(T target, T written, T untouched)
        where T : class
    {
        foreach (var property in typeof(T).GetProperties())
        {
            if (!property.CanWrite)
            {
                continue;
            }

            var value = property.GetValue(written);

            if (!Equals(value, property.GetValue(untouched)))
            {
                property.SetValue(target, value);
            }
        }
    }

    private static bool Differs<T>(T left, T right)
        where T : class =>
        typeof(T).GetProperties()
            .Where(property => property.CanRead)
            .Any(property => !Equals(property.GetValue(left), property.GetValue(right)));
}
