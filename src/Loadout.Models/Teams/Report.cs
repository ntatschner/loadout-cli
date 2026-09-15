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
    [property: JsonPropertyName("next")] string? Next = null)
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
