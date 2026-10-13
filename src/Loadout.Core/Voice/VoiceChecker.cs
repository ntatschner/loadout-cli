using System.Text.RegularExpressions;
using Loadout.Models.Voice;

namespace Loadout.Core.Voice;

/// <summary>One place a piece of writing breaks an accepted rule.</summary>
/// <param name="Rule">The rule's id.</param>
/// <param name="Type">The check type that found it.</param>
/// <param name="Line">Line it is on, counting from one.</param>
/// <param name="Detail">What is wrong, in a sentence.</param>
/// <param name="Gates">
/// Whether it may fail a build. Always false for now: a rule gates only once
/// it has been promoted, and nothing is promoted until there are re-learns to
/// promote it on.
/// </param>
public sealed record VoiceFinding(string Rule, string Type, int Line, string Detail, bool Gates);

/// <summary>What checking one piece of writing came to.</summary>
/// <param name="Findings">Where it broke a rule.</param>
/// <param name="Unchecked">Rules that could not be checked here, by id, with the reason.</param>
public sealed record VoiceCheckResult(
    IReadOnlyList<VoiceFinding> Findings,
    IReadOnlyList<(string Rule, string Reason)> Unchecked);

/// <summary>
/// Checks writing against accepted rules, using only the typed checks.
/// </summary>
/// <remarks>
/// <para>
/// Deterministic on purpose, and that is the whole of why only typed checks
/// run here. A finding has to be one anybody can reproduce from the text and
/// the rule; a judged rule that needs a model to read it reports in review,
/// never here.
/// </para>
/// <para>
/// Code is not prose. Fenced blocks and inline code are blanked before any
/// check sees the text, because a rule against "color" that fires on a CSS
/// property name is a rule somebody turns off.
/// </para>
/// </remarks>
public static partial class VoiceChecker
{
    /// <summary>Words or phrases to avoid.</summary>
    public const string AvoidPhrase = "avoid-phrase";

    /// <summary>A spelling preferred over another, such as <c>colour</c> over <c>color</c>.</summary>
    public const string PreferSpelling = "prefer-spelling";

    /// <summary>Headings in sentence case rather than title case.</summary>
    public const string HeadingCase = "heading-case";

    /// <summary>Sentences no longer than a number of words.</summary>
    public const string MaxSentenceWords = "max-sentence-words";

    /// <summary>A commit subject's form: its length, a capital to start and no full stop to end.</summary>
    public const string CommitSubject = "commit-subject";

    /// <summary>A pattern that must not match. Reports, and is never allowed to gate.</summary>
    public const string Regex = "regex";

    /// <summary>The check types this version can run.</summary>
    public static readonly IReadOnlyList<string> Types =
        [AvoidPhrase, PreferSpelling, HeadingCase, MaxSentenceWords, CommitSubject, Regex];

    /// <summary>Commit subject length when the rule names none, which is what git's own tooling wraps at.</summary>
    public const int DefaultSubjectLength = 72;

    private static readonly TimeSpan PatternLimit = TimeSpan.FromMilliseconds(250);

    [GeneratedRegex(@"`[^`\n]*`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"^\s{0,3}(```|~~~)")]
    private static partial Regex Fence();

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+(.*?)\s*#*\s*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"[A-Za-z][A-Za-z'\-]*")]
    private static partial Regex Word();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBreak();

    /// <summary>
    /// Checks documentation for the docs audit: every finding a warning, never
    /// a failure.
    /// </summary>
    /// <param name="repositoryRoot">The repository the paths are relative to.</param>
    /// <param name="documents">Documents to read, relative to the root.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// The same check <c>voice check</c> runs, so the audit and the drafting
    /// prompts cannot disagree about a page. A page that cannot be read is
    /// left to the audit's own checks, which already say so.
    /// </remarks>
    public static IReadOnlyList<Instructions.DocsFinding> Audit(
        string repositoryRoot,
        IReadOnlyList<string> documents,
        IReadOnlyList<VoiceRule> rules,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(rules);

        var findings = new List<Instructions.DocsFinding>();

        if (rules.Count == 0)
        {
            return findings;
        }

        foreach (var document in documents)
        {
            ct.ThrowIfCancellationRequested();

            string text;

            try
            {
                text = File.ReadAllText(Path.Combine(repositoryRoot, document.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var finding in Check(text, VoiceGuides.Document, rules).Findings)
            {
                findings.Add(new Instructions.DocsFinding(document, finding.Line, "voice", $"{finding.Rule}: {finding.Detail}"));
            }
        }

        return findings;
    }

    /// <summary>
    /// Checks one piece of writing.
    /// </summary>
    /// <param name="text">The writing.</param>
    /// <param name="surface">What it is: <c>doc</c>, <c>commit</c> or <c>pr</c>.</param>
    /// <param name="rules">The rules in force.</param>
    public static VoiceCheckResult Check(string text, string surface, IEnumerable<VoiceRule> rules)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(rules);

        var lines = Prose(text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'));
        var findings = new List<VoiceFinding>();
        var skipped = new List<(string, string)>();

        foreach (var rule in rules)
        {
            if (rule.Surfaces.Count > 0
                && !rule.Surfaces.Contains(surface, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (rule.Check is not { } check)
            {
                skipped.Add((rule.Id, "judged rather than checkable"));

                continue;
            }

            switch (check.Type.Trim().ToLowerInvariant())
            {
                case AvoidPhrase:
                    Phrases(rule, check, lines, findings);
                    break;

                case PreferSpelling:
                    Spellings(rule, check, lines, findings);
                    break;

                case HeadingCase:
                    Headings(rule, lines, findings);
                    break;

                case MaxSentenceWords when check.Limit is > 0:
                    Sentences(rule, check.Limit.Value, lines, findings);
                    break;

                case MaxSentenceWords:
                    skipped.Add((rule.Id, "names no limit"));
                    break;

                case CommitSubject when string.Equals(surface, VoiceGuides.Commit, StringComparison.OrdinalIgnoreCase):
                    Subject(rule, check, text, findings);
                    break;

                case CommitSubject:
                    break;

                case Regex:
                    Pattern(rule, check, lines, findings, skipped);
                    break;

                default:
                    skipped.Add((rule.Id, $"'{check.Type}' is not a check type this version knows"));
                    break;
            }
        }

        return new VoiceCheckResult(
            findings.OrderBy(f => f.Line).ThenBy(f => f.Rule, StringComparer.Ordinal).ToList(),
            skipped);
    }

    /// <summary>
    /// The lines with code blanked: fenced blocks become empty lines and
    /// inline code becomes spaces, so line numbers and columns still match
    /// the original.
    /// </summary>
    internal static string[] Prose(string[] lines)
    {
        var result = new string[lines.Length];
        var fenced = false;

        for (var i = 0; i < lines.Length; i++)
        {
            if (Fence().IsMatch(lines[i]))
            {
                fenced = !fenced;
                result[i] = string.Empty;

                continue;
            }

            result[i] = fenced
                ? string.Empty
                : InlineCode().Replace(lines[i], m => new string(' ', m.Length));
        }

        return result;
    }

    private static void Phrases(VoiceRule rule, VoiceCheck check, string[] lines, List<VoiceFinding> findings)
    {
        foreach (var phrase in check.Phrases.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var pattern = Bounded(phrase);

            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in pattern.Matches(lines[i]))
                {
                    findings.Add(new VoiceFinding(
                        rule.Id, AvoidPhrase, i + 1, $"'{match.Value}': {rule.Statement}", Gates: false));
                }
            }
        }
    }

    private static void Spellings(VoiceRule rule, VoiceCheck check, string[] lines, List<VoiceFinding> findings)
    {
        foreach (var (avoided, preferred) in check.Spellings)
        {
            if (string.IsNullOrWhiteSpace(avoided))
            {
                continue;
            }

            var pattern = Bounded(avoided);

            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in pattern.Matches(lines[i]))
                {
                    findings.Add(new VoiceFinding(
                        rule.Id, PreferSpelling, i + 1,
                        $"'{match.Value}', where you write '{preferred}'.", Gates: false));
                }
            }
        }
    }

    /// <summary>
    /// Flags a heading in title case: two or more words of four letters or
    /// more after the first, every one of them capitalised.
    /// </summary>
    /// <remarks>
    /// Short words are left out of the count because "Set Up the CLI" and
    /// "Set up the CLI" differ only in words a title-case writer lower-cases
    /// anyway, and all-capital words because they are acronyms. A heading of
    /// proper nouns can still trip it; requiring two words and every one of
    /// them makes that rare, and missing a title-case heading is cheaper than
    /// flagging "Windows Sandbox".
    /// </remarks>
    private static void Headings(VoiceRule rule, string[] lines, List<VoiceFinding> findings)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (Heading().Match(lines[i]) is not { Success: true } heading)
            {
                continue;
            }

            var words = Word().Matches(heading.Groups[1].Value)
                .Select(m => m.Value)
                .Skip(1)
                .Where(w => w.Length >= 4 && !w.All(char.IsUpper))
                .ToList();

            if (words.Count >= 2 && words.All(w => char.IsUpper(w[0])))
            {
                findings.Add(new VoiceFinding(
                    rule.Id, HeadingCase, i + 1,
                    $"'{heading.Groups[1].Value}' is in title case: {rule.Statement}", Gates: false));
            }
        }
    }

    /// <summary>Counts words per sentence, a paragraph at a time, so a sentence wrapped over lines is one sentence.</summary>
    private static void Sentences(VoiceRule rule, int limit, string[] lines, List<VoiceFinding> findings)
    {
        var start = -1;
        var paragraph = new List<string>();

        void Flush()
        {
            if (paragraph.Count > 0)
            {
                foreach (var sentence in SentenceBreak().Split(string.Join(' ', paragraph)))
                {
                    var count = Word().Matches(sentence).Count;

                    if (count > limit)
                    {
                        findings.Add(new VoiceFinding(
                            rule.Id, MaxSentenceWords, start + 1,
                            $"A sentence of {count} words, over the {limit} you keep to.", Gates: false));
                    }
                }
            }

            paragraph.Clear();
            start = -1;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (line.Length == 0 || Heading().IsMatch(lines[i]) || line.StartsWith('|'))
            {
                Flush();

                continue;
            }

            if (start < 0)
            {
                start = i;
            }

            paragraph.Add(line);
        }

        Flush();
    }

    private static void Subject(VoiceRule rule, VoiceCheck check, string text, List<VoiceFinding> findings)
    {
        var subject = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')[0].TrimEnd();
        var limit = check.Limit is > 0 ? check.Limit.Value : DefaultSubjectLength;

        if (subject.Length > limit)
        {
            findings.Add(new VoiceFinding(
                rule.Id, CommitSubject, 1, $"The subject is {subject.Length} characters, over {limit}.", Gates: false));
        }

        if (subject.EndsWith('.') && !subject.EndsWith("...", StringComparison.Ordinal))
        {
            findings.Add(new VoiceFinding(
                rule.Id, CommitSubject, 1, "The subject ends with a full stop.", Gates: false));
        }

        if (subject.Length > 0 && char.IsLower(subject[0]))
        {
            findings.Add(new VoiceFinding(
                rule.Id, CommitSubject, 1, "The subject starts with a lower-case letter.", Gates: false));
        }
    }

    private static void Pattern(
        VoiceRule rule,
        VoiceCheck check,
        string[] lines,
        List<VoiceFinding> findings,
        List<(string, string)> skipped)
    {
        if (string.IsNullOrWhiteSpace(check.Pattern))
        {
            skipped.Add((rule.Id, "names no pattern"));

            return;
        }

        System.Text.RegularExpressions.Regex pattern;

        try
        {
            pattern = new System.Text.RegularExpressions.Regex(check.Pattern, RegexOptions.None, PatternLimit);
        }
        catch (ArgumentException)
        {
            skipped.Add((rule.Id, "its pattern is not a valid regular expression"));

            return;
        }

        try
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (pattern.Match(lines[i]) is { Success: true } match)
                {
                    findings.Add(new VoiceFinding(
                        rule.Id, Regex, i + 1, $"'{match.Value}': {rule.Statement}", Gates: false));
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            skipped.Add((rule.Id, "its pattern took too long to run"));
        }
    }

    /// <summary>A phrase matched as whole words, ignoring case, with any run of spaces in it matching any other.</summary>
    private static System.Text.RegularExpressions.Regex Bounded(string phrase)
    {
        var words = phrase.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(System.Text.RegularExpressions.Regex.Escape);

        return new System.Text.RegularExpressions.Regex(
            @"(?<![\w'])" + string.Join(@"\s+", words) + @"(?![\w'])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            PatternLimit);
    }
}
