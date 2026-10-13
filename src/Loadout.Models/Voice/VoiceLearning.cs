namespace Loadout.Models.Voice;

/// <summary>A rule a learn run proposed and nobody has decided on yet.</summary>
public sealed class VoiceProposal
{
    /// <summary>The rule as proposed, with its citations.</summary>
    public VoiceRule Rule { get; set; } = new();

    /// <summary>The run that proposed it.</summary>
    public string Run { get; set; } = string.Empty;

    /// <summary>
    /// Whether it was short of the evidence threshold, and so is put to the
    /// person as a question rather than as a proposal.
    /// </summary>
    public bool Asked { get; set; }
}

/// <summary>A rule the person turned down, as it was worded when they did.</summary>
public sealed class VoiceRejection
{
    /// <summary>The rule's id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Its statement. Proposed again in other words, it is a different proposal and is asked again.</summary>
    public string Statement { get; set; } = string.Empty;
}

/// <summary>How a run's cap came to be what it was.</summary>
public enum VoiceCapSetting
{
    /// <summary>The stored cap was used as it was.</summary>
    Kept,

    /// <summary>The person accepted the estimate plus the margin.</summary>
    Accepted,

    /// <summary>The person typed their own figure.</summary>
    Typed,

    /// <summary>The person declined, so nothing was spent.</summary>
    Declined,
}

/// <summary>How a run ended.</summary>
public enum VoiceRunOutcome
{
    /// <summary>Everything was mined and reviewed.</summary>
    Complete,

    /// <summary>It stopped at its cap. It never counts towards promotion or demotion.</summary>
    Partial,

    /// <summary>Nothing was mined; only proposals carried from an earlier run were reviewed. Counts towards nothing.</summary>
    ReviewOnly,
}

/// <summary>A run's spending limit: whichever is reached first.</summary>
public sealed class VoiceCap
{
    /// <summary>Model calls.</summary>
    public int Calls { get; set; }

    /// <summary>Estimated cost, in US dollars.</summary>
    public decimal Usd { get; set; }

    /// <summary>The estimate it was set against, in US dollars.</summary>
    public decimal Estimate { get; set; }

    /// <summary>When it was set.</summary>
    public DateTimeOffset Set { get; set; }
}

/// <summary>One learn run, as the history keeps it.</summary>
public sealed class VoiceRun
{
    /// <summary>Its id, from when it started.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The guide definition it ran against.</summary>
    public string Guide { get; set; } = string.Empty;

    /// <summary>How it ended.</summary>
    public VoiceRunOutcome Outcome { get; set; }

    /// <summary>How its cap was set.</summary>
    public VoiceCapSetting Cap { get; set; }

    /// <summary>Model calls made.</summary>
    public int Calls { get; set; }

    /// <summary>What they cost, as the agent reported it.</summary>
    public decimal CostUsd { get; set; }

    /// <summary>Pieces of writing it read.</summary>
    public int Evidence { get; set; }

    /// <summary>Rules put to the person, carried ones included.</summary>
    public int Proposed { get; set; }

    /// <summary>Rules accepted, as proposed or edited.</summary>
    public int Accepted { get; set; }

    /// <summary>Rules rejected.</summary>
    public int Rejected { get; set; }

    /// <summary>Proposals left for the next run.</summary>
    public int Carried { get; set; }

    /// <summary>
    /// How the accepted rules did on held-out samples, or null when the run has
    /// no score. A partial or review-only run never has one, and that is shown
    /// as no score rather than a score from a smaller sample.
    /// </summary>
    public VoiceScore? Score { get; set; }

    /// <summary>When it finished.</summary>
    public DateTimeOffset Finished { get; set; }
}

/// <summary>
/// Everything about one person's running of a guide for one profile: kept on
/// this machine, never beside a shared profile.
/// </summary>
public sealed class VoiceLearnState
{
    /// <summary>The confirmed cap, or null before the first run has asked.</summary>
    public VoiceCap? Cap { get; set; }

    /// <summary>Proposals left unreviewed, offered at the start of the next run.</summary>
    public List<VoiceProposal> Carried { get; set; } = [];

    /// <summary>Rules the person rejected, so they are not put to them again unchanged.</summary>
    public List<VoiceRejection> Rejected { get; set; } = [];

    /// <summary>Every run, oldest first.</summary>
    public List<VoiceRun> Runs { get; set; } = [];
}

/// <summary>
/// How a run's accepted rules did against writing they were not learned from.
/// Recorded from the first release and acted on from the second, so promotion
/// has history to go on from its first day.
/// </summary>
public sealed class VoiceScore
{
    /// <summary>Pieces of the person's own writing held out from mining.</summary>
    public int Own { get; set; }

    /// <summary>Edit pairs read: descriptions the person rewrote when they merged.</summary>
    public int Edits { get; set; }

    /// <summary>Held-out pieces rewritten by a model to break a rule, so a rule the history never breaks can be tested.</summary>
    public int Variants { get; set; }

    /// <summary>Each accepted rule's result.</summary>
    public List<VoiceRuleScore> Rules { get; set; } = [];
}

/// <summary>One rule's agreement on held-out samples, counted by where each sample came from.</summary>
/// <remarks>
/// Counted by source rather than pooled, because the sources mean different
/// things: the person's own writing shows a rule does not fire where it should
/// not, an edit shows it firing where the person agreed it should, and a
/// variant shows it firing where a model thought it should. Release 2
/// promotes on the pool and on a floor of edit samples.
/// </remarks>
public sealed class VoiceRuleScore
{
    /// <summary>The rule's id.</summary>
    public string Rule { get; set; } = string.Empty;

    /// <summary>Its check type, or null for a judged rule, which is scored by a model's verdicts.</summary>
    public string? Check { get; set; }

    /// <summary>Held-out pieces of the person's own writing it applied to.</summary>
    public int Own { get; set; }

    /// <summary>Of those, how many it passed, as it should.</summary>
    public int OwnAgreed { get; set; }

    /// <summary>Edit pairs it had an opinion on: it fired on one side or both.</summary>
    public int Edit { get; set; }

    /// <summary>Of those, how many it fired on the draft and not on what was kept.</summary>
    public int EditAgreed { get; set; }

    /// <summary>Variants written to break it.</summary>
    public int Generated { get; set; }

    /// <summary>Of those, how many it caught.</summary>
    public int GeneratedAgreed { get; set; }

    /// <summary>Agreement across every sample, from 0 to 1, or null when it could not be scored.</summary>
    public double? Agreement { get; set; }

    /// <summary>
    /// Whether this run, taken alone, met the guide's promotion thresholds.
    /// Nothing acts on it yet.
    /// </summary>
    public bool Meets { get; set; }

    /// <summary>Why it has no agreement, when it has none.</summary>
    public string? Unscored { get; set; }
}
