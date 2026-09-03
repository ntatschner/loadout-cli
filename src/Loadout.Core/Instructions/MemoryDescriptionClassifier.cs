using System.Text;

namespace Loadout.Core.Instructions;

/// <summary>Why a topic's description cannot do its job.</summary>
public enum DescriptionVerdict
{
    /// <summary>It says what the topic answers, so the index line is worth reading.</summary>
    Decidable,

    /// <summary>Too short to say anything.</summary>
    TooShort,

    /// <summary>It says the topic's own name back, so the index line carries nothing new.</summary>
    RestatesTheName,

    /// <summary>A placeholder: it describes that a note exists rather than what is in it.</summary>
    Placeholder,
}

/// <summary>
/// Decides whether a description can carry the weight the index puts on it.
/// </summary>
/// <remarks>
/// <para>
/// Only the index reaches a compiled context: one name and one line per topic.
/// Everything a session decides about whether to open a topic is decided from
/// that line, so a line that says "notes" or repeats the file name has spent a
/// session's attention and told it nothing. The topic may hold exactly the
/// answer and never be opened.
/// </para>
/// <para>
/// Three mechanical failures, and deliberately no more. Whether a description is
/// <em>accurate</em> is not checkable here and is not attempted: a wrong
/// description flagged by a regular expression would be a guess with a
/// confident face on it. These three are countable, and each has a fix the
/// author can see from the report.
/// </para>
/// <para>
/// The placeholder list is short and stays short. A long one starts refusing
/// words that are ordinary in a real description — "reference" and "details"
/// mean something when they are part of a sentence, and the length and
/// name checks already catch the cases where they are the whole of it.
/// </para>
/// </remarks>
public static class MemoryDescriptionClassifier
{
    /// <summary>
    /// Below this a line cannot say what a topic answers.
    /// </summary>
    /// <remarks>
    /// Short enough to admit a terse real description — "why the build is slow
    /// after a clean" is 38 characters — and long enough to exclude a word or
    /// two standing in for one.
    /// </remarks>
    private const int MinimumLength = 20;

    /// <summary>
    /// Descriptions that describe the existence of a note rather than its
    /// subject.
    /// </summary>
    /// <remarks>
    /// Matched as whole descriptions after normalising, never as substrings. A
    /// description containing the word "notes" is usually fine; one that is the
    /// word "notes" is not.
    /// </remarks>
    private static readonly HashSet<string> Placeholders = new(StringComparer.Ordinal)
    {
        "notes", "note", "misc", "miscellaneous", "stuff", "things", "various",
        "info", "information", "details", "other", "general", "todo", "tbd",
        "memory", "facts", "context", "background",
    };

    /// <summary>
    /// Judges a description against the name it sits beside.
    /// </summary>
    /// <param name="name">The topic name, which the index already shows.</param>
    /// <param name="description">The line being judged.</param>
    public static DescriptionVerdict Classify(string? name, string? description)
    {
        var trimmed = description?.Trim() ?? string.Empty;
        var words = Words(trimmed);

        if (words.Count == 0)
        {
            return DescriptionVerdict.TooShort;
        }

        // The specific reasons are tested before the length, because a short
        // description usually fails for a reason worth naming. "notes" is too
        // short and is also a placeholder, and only the second of those tells
        // its author what to do instead.
        if (words.Count <= 3 && words.All(Placeholders.Contains))
        {
            return DescriptionVerdict.Placeholder;
        }

        // "Recorded by an agent working on starstats" and its relatives: they
        // describe the act of writing rather than the subject written about.
        if (IsAboutTheRecording(words))
        {
            return DescriptionVerdict.Placeholder;
        }

        var fromName = Words(name);

        // The name is already on the index line. A description holding no word
        // the name does not already have doubles the line and adds nothing to
        // it, whether it is the whole name or a part of it.
        if (fromName.Count > 0 && !words.Except(fromName).Any())
        {
            return DescriptionVerdict.RestatesTheName;
        }

        return trimmed.Length < MinimumLength
            ? DescriptionVerdict.TooShort
            : DescriptionVerdict.Decidable;
    }

    /// <summary>A sentence about the note existing rather than about its subject.</summary>
    private static bool IsAboutTheRecording(IReadOnlyList<string> words) =>
        words[0] is "recorded" or "written" or "noted" or "captured"
        && words.Contains("by");

    /// <summary>What a description says, reduced to comparable words.</summary>
    private static List<string> Words(string? text)
    {
        var words = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return words;
        }

        var word = new StringBuilder();

        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                word.Append(char.ToLowerInvariant(character));

                continue;
            }

            Take(word, words);
        }

        Take(word, words);

        return words;

        static void Take(StringBuilder word, List<string> into)
        {
            if (word.Length == 0)
            {
                return;
            }

            var term = word.ToString();

            word.Clear();

            // Articles and prepositions are not what makes a description say
            // something, and counting them would let "the notes for the thing"
            // pass as five words of content.
            if (term is not ("a" or "an" or "and" or "for" or "in" or "of" or "on"
                or "or" or "the" or "to" or "with" or "that" or "is" or "it"))
            {
                into.Add(term);
            }
        }
    }

    /// <summary>What to tell somebody whose description was refused.</summary>
    public static string Explain(DescriptionVerdict verdict) => verdict switch
    {
        DescriptionVerdict.TooShort =>
            "it is too short to say what the topic answers",
        DescriptionVerdict.RestatesTheName =>
            "it says the topic's own name back, and the name is already on the index line",
        DescriptionVerdict.Placeholder =>
            "it describes that a note exists rather than what is in it",
        _ => "it is fine",
    };
}
