using Loadout.Models.Teams;

namespace Loadout.Core.Teams;

/// <summary>What one criterion needs, and who said so.</summary>
/// <param name="Needs">What meeting it takes.</param>
/// <param name="By">
/// <c>lead</c> where the lead tagged it, <c>keyword</c> where the lead left it
/// untagged and the wording gave it away, or <c>default</c> where neither said
/// and it is taken to be something the run can do.
/// </param>
/// <param name="Handover">What the lead said the person has to do, or null.</param>
public sealed record CriterionTag(CriterionNeeds Needs, string By, string? Handover = null)
{
    /// <summary>Whether the run can meet it without a push, CI or a person.</summary>
    public bool Local => Needs == CriterionNeeds.Local;
}

/// <summary>
/// How a criterion's tag is spelt, and the keyword rule that tags one the lead
/// did not.
/// </summary>
/// <remarks>
/// <para>
/// The lead tags each criterion as it gives its readings, and a person sees
/// the tags when they agree them. The keyword rule is for the run nobody is
/// watching: a criterion that says CI, a push or a check by hand is one no run
/// can meet on its own, however the lead read it.
/// </para>
/// <para>
/// Words matched by hand rather than by a regular expression, for the reason
/// <c>TeamRunner.Unnumbered</c> gives: this runs over text a person typed, and
/// a pattern over that has already cost this codebase a timeout once.
/// </para>
/// </remarks>
public static class CriterionTags
{
    /// <summary>The word a tag is written as.</summary>
    public static string Spell(CriterionNeeds needs) => needs switch
    {
        CriterionNeeds.Local => "local",
        CriterionNeeds.Push => "needs-push",
        CriterionNeeds.Ci => "needs-ci",
        CriterionNeeds.Person => "needs-person",
        _ => throw new ArgumentOutOfRangeException(nameof(needs), needs, null),
    };

    /// <summary>Reads a tag back from the word it was written as.</summary>
    public static bool TryParse(string? word, out CriterionNeeds needs)
    {
        needs = CriterionNeeds.Local;

        foreach (var known in Enum.GetValues<CriterionNeeds>())
        {
            if (string.Equals(word?.Trim(), Spell(known), StringComparison.OrdinalIgnoreCase))
            {
                needs = known;

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The tag a criterion gets: the lead's where it gave one, else the
    /// keyword rule's, else local.
    /// </summary>
    public static CriterionTag Of(string criterion, CriterionNeeds? said, string? handover = null)
    {
        ArgumentNullException.ThrowIfNull(criterion);

        var what = handover?.Trim() is { Length: > 0 } some ? some : null;

        if (said is { } tagged)
        {
            return new CriterionTag(tagged, "lead", what);
        }

        return Guess(criterion) is { } guessed
            ? new CriterionTag(guessed, "keyword", what)
            : new CriterionTag(CriterionNeeds.Local, "default", what);
    }

    /// <summary>
    /// What the wording of a criterion says it needs, or null where it says
    /// nothing a run could not do.
    /// </summary>
    /// <remarks>
    /// A person's check first, then CI, then a push: a criterion asking for
    /// "CI, then a check by hand" is not met when CI goes green, and CI needs
    /// the push anyway. Kept to phrases rather than single words where a word
    /// alone means something else as often: "push notification" is a feature,
    /// and "pr explain" is a command.
    /// </remarks>
    public static CriterionNeeds? Guess(string criterion)
    {
        ArgumentNullException.ThrowIfNull(criterion);

        var words = Words(criterion);

        if (Has(words, "by", "hand") || Has(words, "hand", "check") || Has(words, "hand", "checked")
            || Has(words, "manually") || Has(words, "manual", "check"))
        {
            return CriterionNeeds.Person;
        }

        if (Has(words, "ci") || Has(words, "continuous", "integration") || Has(words, "github", "actions"))
        {
            return CriterionNeeds.Ci;
        }

        if ((Has(words, "pushed") || Has(words, "a", "push") || Has(words, "push", "the") || Has(words, "push", "its")
                || Has(words, "push", "it") || Has(words, "pull", "request") || Has(words, "pull", "requests")
                || Has(words, "a", "pr") || Has(words, "the", "pr") || Has(words, "draft", "pr"))
            && !Has(words, "push", "notification") && !Has(words, "push", "notifications"))
        {
            return CriterionNeeds.Push;
        }

        return null;
    }

    /// <summary>
    /// What the person has to do about a criterion the run cannot meet: what
    /// the lead said, else what its tag means.
    /// </summary>
    public static string ToDo(CriterionTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (tag.Handover is { Length: > 0 } said)
        {
            return said;
        }

        return tag.Needs switch
        {
            CriterionNeeds.Push => "Push the run's work and open a pull request.",
            CriterionNeeds.Ci => "Push the run's work so CI runs, and see that it passes.",
            CriterionNeeds.Person => "Check it by hand.",
            _ => "Nothing: the run can meet this itself.",
        };
    }

    // Lower-cased runs of letters and digits, so "CI," and "(CI)" are both "ci".
    private static List<string> Words(string text)
    {
        var words = new List<string>();
        var start = -1;

        for (var i = 0; i <= text.Length; i++)
        {
            var inWord = i < text.Length && char.IsLetterOrDigit(text[i]);

            if (inWord && start < 0)
            {
                start = i;
            }
            else if (!inWord && start >= 0)
            {
                words.Add(text[start..i].ToLowerInvariant());
                start = -1;
            }
        }

        return words;
    }

    private static bool Has(List<string> words, params string[] phrase)
    {
        for (var i = 0; i + phrase.Length <= words.Count; i++)
        {
            var all = true;

            for (var j = 0; j < phrase.Length && all; j++)
            {
                all = string.Equals(words[i + j], phrase[j], StringComparison.Ordinal);
            }

            if (all)
            {
                return true;
            }
        }

        return false;
    }
}
