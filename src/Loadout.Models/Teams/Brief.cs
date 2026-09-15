using System.Text.Json.Serialization;

namespace Loadout.Models.Teams;

/// <summary>
/// What a node is given: the one document that is allowed to tell it what
/// to do. Contract <c>brief/1</c>.
/// </summary>
/// <remarks>
/// Rendered as the node's prompt and attached as JSON beside it. Every field
/// name here is the field name on the wire, because a role's instructions
/// name them and a model reading "constraints.outward_allowed" must find a
/// key spelled exactly that.
/// </remarks>
/// <param name="Run">The run this brief belongs to.</param>
/// <param name="Node">The node being briefed, unique within the run.</param>
/// <param name="Parent">The node whose request produced this brief, or null for the lead.</param>
/// <param name="Role">The role the node plays, by specialist id.</param>
/// <param name="Task">What to do, written so a stranger could act on it.</param>
/// <param name="Deliverable">What kind of thing the report must carry.</param>
/// <param name="Inputs">Artefacts the node needs, by reference: a plan file, another node's report, a commit.</param>
/// <param name="Constraints">How far the node may go.</param>
/// <param name="DoneWhen">The conditions under which the work is finished, each one something a test or a command can show.</param>
/// <param name="Parameters">Values a team file passes to a parameterised role, such as a department.</param>
public sealed record Brief(
    [property: JsonPropertyName("run")] string Run,
    [property: JsonPropertyName("node")] string Node,
    [property: JsonPropertyName("parent")] string? Parent,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("task")] string Task,
    [property: JsonPropertyName("deliverable")] DeliverableKind Deliverable,
    [property: JsonPropertyName("inputs")] IReadOnlyList<string> Inputs,
    [property: JsonPropertyName("constraints")] BriefConstraints Constraints,
    [property: JsonPropertyName("done_when")] IReadOnlyList<string> DoneWhen,
    [property: JsonPropertyName("parameters")] IReadOnlyDictionary<string, string>? Parameters = null)
{
    /// <summary>The version of this shape. Read before anything else, so an older reader can say it does not understand.</summary>
    [JsonPropertyName("contract")]
    public string Contract { get; init; } = Version;

    /// <summary>The contract this record implements.</summary>
    public const string Version = "brief/1";
}

/// <summary>How far a node may go.</summary>
/// <param name="Mode">The posture the node runs in: advise, investigate, implement, review or coordinate.</param>
/// <param name="BudgetUsd">What the node may spend, or null for the team's default.</param>
/// <param name="MaxTurns">How many exchanges with the model the node may take, or null for the team's default.</param>
/// <param name="Worktree">The git worktree the node works in, or null to work in the repository itself.</param>
/// <param name="OutwardAllowed">Outward actions this node may take, each named exactly. Empty means none.</param>
/// <param name="Home">A throwaway home directory for roles that run documented commands, or null.</param>
public sealed record BriefConstraints(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("budget_usd")] decimal? BudgetUsd = null,
    [property: JsonPropertyName("max_turns")] int? MaxTurns = null,
    [property: JsonPropertyName("worktree")] string? Worktree = null,
    [property: JsonPropertyName("outward_allowed")] IReadOnlyList<string>? OutwardAllowed = null,
    [property: JsonPropertyName("home")] string? Home = null);

/// <summary>What kind of thing a node hands back.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeliverableKind>))]
public enum DeliverableKind
{
    [JsonStringEnumMemberName("commit")] Commit,
    [JsonStringEnumMemberName("branch")] Branch,
    [JsonStringEnumMemberName("file")] File,
    [JsonStringEnumMemberName("document")] Document,
    [JsonStringEnumMemberName("plan")] Plan,
    [JsonStringEnumMemberName("decision")] Decision,
    [JsonStringEnumMemberName("answer")] Answer,
}
