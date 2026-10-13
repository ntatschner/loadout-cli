namespace Loadout.Models.Voice;

/// <summary>
/// A guide's fixed shape: what it learns from, what it may write down as a
/// rule, and every threshold a run is judged by.
/// </summary>
/// <remarks>
/// <para>
/// Fixed and versioned rather than designed afresh by each run, so what a run
/// produces is something other features can rely on. A profile names the
/// definition it was learned against (<c>prose/1</c>), and a later version can
/// change a threshold without anybody's profile changing meaning underneath
/// them.
/// </para>
/// <para>
/// Every threshold lives here rather than in code for the same reason. Some
/// of them do nothing yet: the promotion, demotion and lapse fields are
/// recorded from the first release so that the release which acts on them
/// has history to act on from its first day.
/// </para>
/// </remarks>
public sealed class VoiceGuide
{
    /// <summary>What the guide learns, such as <c>prose</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The definition's version. A profile records which it was learned against.</summary>
    public int Version { get; set; } = 1;

    /// <summary>What it learns, in a sentence.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Where evidence may come from, as source kinds such as <c>squash</c> or <c>mark</c>.</summary>
    public List<string> Sources { get; set; } = [];

    /// <summary>The surfaces its rules apply to, such as <c>doc</c>, <c>commit</c> and <c>pr</c>.</summary>
    public List<string> Surfaces { get; set; } = [];

    /// <summary>The check types a rule may carry. Anything else is a judged rule, which never gates.</summary>
    public List<string> CheckTypes { get; set; } = [];

    /// <summary>How much evidence a mined rule needs before it is proposed rather than asked about.</summary>
    public VoiceEvidenceThreshold Evidence { get; set; } = new();

    /// <summary>When a rule may become a failing finding. Recorded, and acted on from release 2.</summary>
    public VoicePromotion Promotion { get; set; } = new();

    /// <summary>How a run's spending is capped.</summary>
    public VoiceCapRule Cap { get; set; } = new();

    /// <summary>How rules are scored against writing they were not learned from.</summary>
    public VoiceScoringRule Scoring { get; set; } = new();

    /// <summary>The <c>id/version</c> a profile records, such as <c>prose/1</c>.</summary>
    public string Reference => $"{Id}/{Version}";
}

/// <summary>How much evidence a mined rule needs.</summary>
public sealed class VoiceEvidenceThreshold
{
    /// <summary>Citations needed, counting only those whose marks still hold.</summary>
    public int Citations { get; set; } = 3;

    /// <summary>Distinct sources the citations must come from.</summary>
    public int Sources { get; set; } = 2;
}

/// <summary>When a rule may gate, and when it stops. Inert until release 2.</summary>
public sealed class VoicePromotion
{
    /// <summary>Agreement on held-out samples a rule must reach, from 0 to 1.</summary>
    public double Agreement { get; set; } = 0.9;

    /// <summary>Consecutive complete re-learns at or above the agreement needed to promote.</summary>
    public int ConsecutiveRuns { get; set; } = 2;

    /// <summary>Held-out samples from the person's own edits a rule needs before it can be promoted.</summary>
    public int EditFloor { get; set; } = 5;

    /// <summary>Consecutive failed re-learns before demotion is proposed.</summary>
    public int DemotionFailures { get; set; } = 2;

    /// <summary>Re-learns a lapse-only shortfall may go unconfirmed before it counts as a failure.</summary>
    public int UnconfirmedLapses { get; set; } = 3;
}

/// <summary>
/// How a learn run's spending is capped. There is no default figure: the first
/// run proposes its own estimate plus the margin and asks.
/// </summary>
public sealed class VoiceCapRule
{
    /// <summary>Added to the estimate when a cap is proposed, as a fraction.</summary>
    public double Margin { get; set; } = 0.25;

    /// <summary>A lower cap is offered only when the new proposal is below this fraction of the stored one.</summary>
    public double LowerBelow { get; set; } = 0.5;

    /// <summary>
    /// What one call is assumed to cost before there is any history to go on,
    /// in US dollars. Replaced by the average of the calls already made as soon
    /// as there are some.
    /// </summary>
    public decimal SeedCallUsd { get; set; } = 0.30m;

    /// <summary>How much writing one call reads, in characters, which sets how many calls a run needs.</summary>
    public int BatchCharacters { get; set; } = 40_000;
}

/// <summary>How a run keeps writing back to score its rules against.</summary>
public sealed class VoiceScoringRule
{
    /// <summary>
    /// The share of the person's writing never shown to the miner, from 0 to 1.
    /// Which pieces is decided by each one's reference, so the same pieces are
    /// held out on every run and a rule is never scored on what it was learned
    /// from.
    /// </summary>
    public double HoldOut { get; set; } = 0.2;
}
