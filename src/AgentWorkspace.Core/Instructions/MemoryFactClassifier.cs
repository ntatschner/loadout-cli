using System.Text.RegularExpressions;

namespace AgentWorkspace.Core.Instructions;

/// <summary>Why a candidate fact is not worth keeping.</summary>
public enum FactVerdict
{
    /// <summary>Worth keeping: a standing claim about how something is or must be.</summary>
    Durable,

    /// <summary>An account of a change that was made, which the repository history already holds.</summary>
    ChangeLog,

    /// <summary>True on the day it was written and misleading afterwards.</summary>
    TimeSensitive,

    /// <summary>Session chatter, tool output or an error message.</summary>
    Noise,

    /// <summary>Too short to carry a fact.</summary>
    TooShort,

    /// <summary>Long enough to be a document rather than a fact.</summary>
    TooLong,

    /// <summary>
    /// Well-formed but making no standing claim, so there is nothing in it a
    /// future session could rely on.
    /// </summary>
    NoAssertion,
}

/// <summary>
/// Decides whether a candidate is a durable fact or something that will rot.
/// <para>
/// This is the check that keeps memory worth loading. Memory that accumulates
/// unfiltered becomes a pile of half-true statements which still cost a session
/// to read, which is worse than having none: the same price, and it misleads.
/// The three things that do the damage are accounts of changes (the repository
/// history already holds those, and they read as present tense forever),
/// statements dated to the moment they were written, and session chatter.
/// </para>
/// </summary>
public static partial class MemoryFactClassifier
{
    /// <summary>Below this a line cannot carry a fact worth the write.</summary>
    private const int MinimumLength = 40;

    /// <summary>Above this it is a document, and belongs in the workspace as one.</summary>
    private const int MaximumLength = 1200;

    /// <summary>Classifies one candidate fact.</summary>
    public static FactVerdict Classify(string? text)
    {
        var value = (text ?? string.Empty).Trim();

        if (value.Length < MinimumLength)
        {
            return FactVerdict.TooShort;
        }

        if (value.Length > MaximumLength)
        {
            return FactVerdict.TooLong;
        }

        if (Noise().IsMatch(value))
        {
            return FactVerdict.Noise;
        }

        if (TimeSensitive().IsMatch(value))
        {
            return FactVerdict.TimeSensitive;
        }

        // Checked before the positive test, because a change-log line often
        // contains a durable-sounding verb: "added a check so the build fails
        // when the schema drifts" asserts something, but what it asserts is
        // that a commit happened.
        if (ChangeLog().IsMatch(value))
        {
            return FactVerdict.ChangeLog;
        }

        return Assertion().IsMatch(value) || Subject().IsMatch(value)
            ? FactVerdict.Durable
            : FactVerdict.NoAssertion;
    }

    /// <summary>A sentence explaining the verdict, for a finding or a warning.</summary>
    public static string Explain(FactVerdict verdict) => verdict switch
    {
        FactVerdict.Durable => "records a standing fact.",

        FactVerdict.ChangeLog =>
            "reads as an account of a change rather than a standing fact. The repository history "
            + "already records what changed; memory should say how things are.",

        FactVerdict.TimeSensitive =>
            "is dated to the moment it was written, so it will be misleading within weeks. "
            + "State the rule rather than the current value.",

        FactVerdict.Noise =>
            "looks like session output or chatter rather than a fact worth carrying forward.",

        FactVerdict.TooShort =>
            $"is under {MinimumLength} characters, which is rarely enough to state a fact "
            + "unambiguously.",

        FactVerdict.TooLong =>
            $"is over {MaximumLength} characters. That is a document; put it in the workspace "
            + "and reference it.",

        _ =>
            "makes no standing claim, so a later session has nothing to rely on. Say what is "
            + "true, what is required, or why something is the way it is.",
    };

    /// <summary>
    /// Verbs of the form "somebody did something", which describe an event.
    /// <para>
    /// The past tense is the tell. "The API returns 404 for a missing project"
    /// stays true; "changed the API to return 404" describes a moment.
    /// </para>
    /// </summary>
    [GeneratedRegex(
        @"(?i)^\W*(?:we\s+|i\s+)?(?:added|updated|created|renamed|removed|deleted|fixed|refactored|"
        + @"implemented|migrated|bumped|introduced|replaced|moved|merged|reverted|split|extracted|"
        + @"wired|hooked|patched|tweaked|cleaned|rewrote|switched|upgraded|downgraded|installed|"
        + @"configured|enabled|disabled|committed|pushed|released|shipped)\b"
        + @"|\bnow (?:has|have|supports|uses|returns|includes|lives|works)\b"
        + @"|\bcompletes the implementation\b",
        RegexOptions.None, 1000)]
    private static partial Regex ChangeLog();

    [GeneratedRegex(
        @"(?i)\b(?:for now|temporarily|currently|as of (?:today|now|this week)|at the time of writing|"
        + @"until we|will be replaced|next is \d+|so far)\b|\bTODO\b|\bFIXME\b",
        RegexOptions.None, 1000)]
    private static partial Regex TimeSensitive();

    [GeneratedRegex(
        @"(?i)^\s*(?:at [\w.]+\(|npm ERR!|error TS\d+|Traceback|\s*File "".*"", line \d+)"
        + @"|\b(?:let me|i'?ll|i will|i'?m going to|let's)\b"
        + @"|\b\d+%\s*(?:complete|done)\b",
        RegexOptions.None, 1000)]
    private static partial Regex Noise();

    /// <summary>
    /// Words that make a standing claim: how something behaves, what it needs,
    /// what must not happen, or why.
    /// </summary>
    [GeneratedRegex(
        @"(?i)\b(?:is|are|was designed|lives?\s+(?:in|under|at|alongside)|requires?|depends? on|"
        + @"must|must not|never|always|cannot|only|because|root cause|responsible for|enforces?|"
        + @"expects?|assumes?|defaults? to|returns?|throws?|fails? when|breaks? when|owns?|"
        + @"handles?|survives?)\b",
        RegexOptions.None, 1000)]
    private static partial Regex Assertion();

    /// <summary>
    /// Nouns that name the kind of knowledge worth keeping, for facts phrased
    /// without an obvious assertion verb.
    /// </summary>
    [GeneratedRegex(
        @"(?i)\b(?:convention|rule|policy|constraint|invariant|contract|gotcha|caveat|limitation|"
        + @"known issue|trade-?off|rationale|decision|architecture|design)\b",
        RegexOptions.None, 1000)]
    private static partial Regex Subject();
}
