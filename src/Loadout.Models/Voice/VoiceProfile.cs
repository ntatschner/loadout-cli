namespace Loadout.Models.Voice;

/// <summary>
/// What a guide learned about how somebody writes: the accepted rules, each
/// with the evidence it was accepted on.
/// </summary>
/// <remarks>
/// The source of truth. Anything else made from it, such as the specialist a
/// launch loads, is rendered from this and never edited by hand. Nothing about
/// who marked what, or what a run cost, is kept here: a codebase's profile is
/// shared, and that is one person's business.
/// </remarks>
public sealed class VoiceProfile
{
    /// <summary>The guide definition it was learned against, such as <c>prose/1</c>.</summary>
    public string Guide { get; set; } = string.Empty;

    /// <summary>When it last changed.</summary>
    public DateTimeOffset? Updated { get; set; }

    /// <summary>The accepted rules.</summary>
    public List<VoiceRule> Rules { get; set; } = [];
}

/// <summary>One accepted rule.</summary>
public sealed class VoiceRule
{
    /// <summary>Stable id, unique within the profile. A more specific layer replaces a rule by id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The rule as the person accepted it, in a sentence.</summary>
    public string Statement { get; set; } = string.Empty;

    /// <summary>
    /// How to check it mechanically, or null for a judged rule, which reports
    /// in review but can never be checked here.
    /// </summary>
    public VoiceCheck? Check { get; set; }

    /// <summary>The surfaces it applies to. Empty means all the guide's surfaces.</summary>
    public List<string> Surfaces { get; set; } = [];

    /// <summary>The evidence it was accepted on.</summary>
    public List<VoiceCitation> Citations { get; set; } = [];

    /// <summary>When it was accepted.</summary>
    public DateTimeOffset? Accepted { get; set; }
}

/// <summary>A mechanical check, of one of the types the guide definition names.</summary>
public sealed class VoiceCheck
{
    /// <summary>The check type, such as <c>avoid-phrase</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Words or phrases, for the types that take them.</summary>
    public List<string> Phrases { get; set; } = [];

    /// <summary>Disfavoured form to preferred form, for <c>prefer-spelling</c>.</summary>
    public Dictionary<string, string> Spellings { get; set; } = [];

    /// <summary>A limit, for the types that take one.</summary>
    public int? Limit { get; set; }

    /// <summary>A pattern that must not match, for <c>regex</c>. Never gates.</summary>
    public string? Pattern { get; set; }

    /// <summary>
    /// The value chosen, for the checks that are settings rather than learned:
    /// a format preset for <c>date-format</c>, a policy for <c>unowned-estimate</c>.
    /// </summary>
    public string? Value { get; set; }
}

/// <summary>Why a rule was accepted: one piece of the person's writing.</summary>
public sealed class VoiceCitation
{
    /// <summary>The kind of source, such as <c>squash</c>, <c>mark</c> or <c>interview</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Where it came from: a commit, a file, a corpus.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>A short excerpt, redacted before it was stored.</summary>
    public string Excerpt { get; set; } = string.Empty;

    /// <summary>
    /// The hash of the text when it was cited, so a citation whose text has
    /// since changed can be told apart from one that still holds.
    /// </summary>
    public string? Hash { get; set; }
}
