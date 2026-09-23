using System.Text.Json.Serialization;

namespace Loadout.Models.Teams;

/// <summary>
/// What a node hands back: the one document a run reads from it. Contract
/// <c>report/1</c>.
/// </summary>
/// <remarks>
/// <para>
/// Produced by the agent as structured output against <see cref="ReportSchema"/>,
/// so the shape is enforced before the launcher sees it; the rules the shape
/// cannot express, such as "done needs evidence", are checked afterwards
/// and the report is returned to the node once if they fail.
/// </para>
/// <para>
/// What the model must not be trusted to report about itself is not here.
/// Cost, turns, duration and the session id come from the agent's own
/// result event.
/// </para>
/// </remarks>
/// <param name="Node">The node reporting, as its brief named it.</param>
/// <param name="Status">How the work ended.</param>
/// <param name="Summary">What happened, in the node's own words, past tense.</param>
/// <param name="Deliverables">What was produced, by reference.</param>
/// <param name="Evidence">What shows the deliverables do what the brief asked.</param>
/// <param name="OutwardTaken">Outward actions the node took. Must be within what the brief allowed.</param>
/// <param name="Blocker">What stopped the work and what would unblock it. Required when blocked.</param>
/// <param name="Questions">Decisions the node may not make. Required when a decision is needed.</param>
/// <param name="Requests">Nodes a lead asks the coordinator to brief. Never spawns anything itself.</param>
/// <param name="OutwardRequested">Outward actions the node wanted and was not allowed, each named with its command.</param>
/// <param name="Next">What should happen next, in a sentence.</param>
/// <param name="Coverage">
/// Where each of the run's criteria got to, one entry per criterion. Asked of
/// the lead and of nothing else: a worker is accountable for its own brief, and
/// the lead is the node that answers for the goal.
/// </param>
/// <param name="ProposedDoneWhen">
/// What the lead thinks this run should be judged on, when nobody said.
/// <para>
/// Asked for in the lead's first brief, and only when the run was started with
/// no criteria of its own. Such a run used to be held to one criterion - "the
/// goal is met" - which the lead wrote its own verdict on, so nothing it could
/// report was ever wrong: a run that produced a design document for a goal
/// somebody expected code from said done, cited itself, and was right by the
/// only rule it had.
/// </para>
/// <para>
/// A proposal and not a decision. It goes in front of a person before a single
/// worker is briefed, and what comes back is what the run is held to.
/// </para>
/// </param>
/// <param name="GoalUnderstood">
/// What the lead took the goal to mean, in a sentence, so a person can see the
/// run was working to what they asked rather than to a nearby thing that was
/// easier. Optional, as the coverage's own reading is.
/// </param>
public sealed record Report(
    [property: JsonPropertyName("node")] string Node,
    [property: JsonPropertyName("status")] ReportStatus Status,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("deliverables")] IReadOnlyList<ReportDeliverable> Deliverables,
    [property: JsonPropertyName("evidence")] IReadOnlyList<ReportEvidence> Evidence,
    [property: JsonPropertyName("outward_taken")] IReadOnlyList<string> OutwardTaken,
    [property: JsonPropertyName("blocker")] ReportBlocker? Blocker = null,
    [property: JsonPropertyName("questions")] IReadOnlyList<ReportQuestion>? Questions = null,
    [property: JsonPropertyName("requests")] IReadOnlyList<ReportRequest>? Requests = null,
    [property: JsonPropertyName("outward_requested")] IReadOnlyList<string>? OutwardRequested = null,
    [property: JsonPropertyName("next")] string? Next = null,
    [property: JsonPropertyName("coverage")] IReadOnlyList<ReportCoverage>? Coverage = null,
    [property: JsonPropertyName("proposed_done_when")] IReadOnlyList<string>? ProposedDoneWhen = null,
    [property: JsonPropertyName("goal_understood")] string? GoalUnderstood = null)
{
    /// <summary>The version of this shape.</summary>
    [JsonPropertyName("contract")]
    public string Contract { get; init; } = Version;

    /// <summary>The contract this record implements.</summary>
    public const string Version = "report/1";
}

/// <summary>How a node's work ended.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReportStatus>))]
public enum ReportStatus
{
    [JsonStringEnumMemberName("done")] Done,
    [JsonStringEnumMemberName("blocked")] Blocked,
    [JsonStringEnumMemberName("failed")] Failed,
    [JsonStringEnumMemberName("needs-decision")] NeedsDecision,
}

/// <summary>
/// Where one of the run's criteria got to, as the lead reports it.
/// </summary>
/// <remarks>
/// <para>
/// A run's goal used to be one sentence and one claim: the lead said
/// <c>done</c> and nothing argued. That is fine while somebody is watching and
/// is the whole of the check on an autonomous run, which is the case where
/// nobody is.
/// </para>
/// <para>
/// So a run may carry criteria, and a lead reporting done has to say what
/// became of each. <c>met</c> needs a reason that cites something: the rule
/// that <c>done</c> needs evidence which passed already exists one level down,
/// on a worker's report, and this is the same rule at the level of the goal.
/// </para>
/// </remarks>
/// <param name="Criterion">The criterion, repeated back exactly as the run gave it.</param>
/// <param name="Verdict">met, unmet, or not-attempted.</param>
/// <param name="Because">
/// What shows it: which node, which report, which evidence. Required for met,
/// because a claim with nothing behind it is what this exists to stop.
/// </param>
/// <param name="Understood">
/// What the lead took the criterion to mean, in its own words. A criterion is
/// a sentence somebody wrote in a hurry, and a verdict on it is only worth as
/// much as the reading it was given: "the tests pass" read as "the new test
/// passes" can be met while the suite is red. Optional, because runs written
/// before it was asked for have none.
/// </param>
public sealed record ReportCoverage(
    [property: JsonPropertyName("criterion")] string Criterion,
    [property: JsonPropertyName("verdict")] CoverageVerdict Verdict,
    [property: JsonPropertyName("because")] string? Because = null,
    [property: JsonPropertyName("understood")] string? Understood = null);

/// <summary>What became of one criterion.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CoverageVerdict>))]
public enum CoverageVerdict
{
    /// <summary>Done, and something shows it.</summary>
    [JsonStringEnumMemberName("met")] Met,

    /// <summary>Attempted and not achieved. An honest answer, and it blocks a done.</summary>
    [JsonStringEnumMemberName("unmet")] Unmet,

    /// <summary>
    /// Nothing was done about it.
    /// </summary>
    /// <remarks>
    /// Its own verdict rather than folded into unmet, because the two want
    /// different things next: an unmet criterion was tried and needs a
    /// different approach, and one never attempted needs somebody to notice
    /// that a whole area of the goal was missed. That second case is the one a
    /// run of several rounds loses quietly.
    /// </remarks>
    [JsonStringEnumMemberName("not-attempted")] NotAttempted,
}

/// <summary>Something a node produced, by reference.</summary>
public sealed record ReportDeliverable(
    [property: JsonPropertyName("kind")] DeliverableKind Kind,
    [property: JsonPropertyName("ref")] string Ref,
    [property: JsonPropertyName("note")] string? Note = null);

/// <summary>Something that shows the work does what was asked.</summary>
public sealed record ReportEvidence(
    [property: JsonPropertyName("kind")] EvidenceKind Kind,
    [property: JsonPropertyName("ref")] string Ref,
    [property: JsonPropertyName("result")] EvidenceResult Result,
    [property: JsonPropertyName("note")] string? Note = null);

/// <summary>What kind of thing a piece of evidence is.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EvidenceKind>))]
public enum EvidenceKind
{
    [JsonStringEnumMemberName("test")] Test,
    [JsonStringEnumMemberName("command")] Command,
    [JsonStringEnumMemberName("observation")] Observation,
    [JsonStringEnumMemberName("review")] Review,
}

/// <summary>What a piece of evidence showed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EvidenceResult>))]
public enum EvidenceResult
{
    [JsonStringEnumMemberName("pass")] Pass,
    [JsonStringEnumMemberName("fail")] Fail,
    [JsonStringEnumMemberName("n/a")] NotApplicable,
}

/// <summary>What stopped the work, and what would let it continue.</summary>
public sealed record ReportBlocker(
    [property: JsonPropertyName("what")] string What,
    [property: JsonPropertyName("unblocked_by")] string UnblockedBy);

/// <summary>A decision the node may not make.</summary>
public sealed record ReportQuestion(
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("options")] IReadOnlyList<string> Options,
    [property: JsonPropertyName("recommendation")] string Recommendation);

/// <summary>A lead asking for a node to be briefed.</summary>
public sealed record ReportRequest(
    [property: JsonPropertyName("node")] string Node,
    [property: JsonPropertyName("task")] string Task,
    [property: JsonPropertyName("deliverable")] DeliverableKind Deliverable,
    [property: JsonPropertyName("inputs")] IReadOnlyList<string>? Inputs = null);
