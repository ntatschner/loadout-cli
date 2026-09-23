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
/// <param name="Goal">
/// What the team exists for, standing, or null where its file says nothing.
/// Not the run's goal, which is the task: a node reads both, and they answer
/// different questions.
/// </param>
/// <param name="Declarations">
/// Rules this team follows whatever the run, or null for none. Prose in a
/// brief, read by a model - a declaration can never raise what a node may do,
/// which is decided before anything starts.
/// </param>
/// <param name="TeamDirectory">
/// Where this team keeps what it makes, across every run of it, or null where
/// it has none. The same path for every node of every run, so a declaration
/// that says "register it in the team's directory" names one place rather than
/// one per node.
/// </param>
/// <param name="Delegates">
/// The nodes this node may request, with their roles, or null for a node
/// that may request none. Only a lead has any. Named here because the first
/// real run's lead asked for "implementer/1" from a role file's example,
/// the team called the node "implementer", and the run refused it twice
/// without the lead ever being told what the names were.
/// </param>
/// <param name="Criteria">
/// The run's criteria: what it is being judged on, whole, however narrow this
/// node's own job is.
/// </param>
/// <remarks>
/// <paramref name="Criteria"/> is the run's, not this node's. Every node is
/// told them for the same reason every node is told the team's standing goal: a
/// worker given a narrow job still needs to know what the run is being judged
/// on. Answering for them is the lead's alone, and the check that enforces that
/// is in ReportCheck.
/// </remarks>
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
    [property: JsonPropertyName("parameters")] IReadOnlyDictionary<string, string>? Parameters = null,
    [property: JsonPropertyName("delegates")] IReadOnlyList<BriefDelegate>? Delegates = null,
    [property: JsonPropertyName("goal")] string? Goal = null,
    [property: JsonPropertyName("declarations")] IReadOnlyList<string>? Declarations = null,
    [property: JsonPropertyName("team_directory")] string? TeamDirectory = null,
    [property: JsonPropertyName("criteria")] IReadOnlyList<string>? Criteria = null)
{
    /// <summary>The version of this shape. Read before anything else, so an older reader can say it does not understand.</summary>
    [JsonPropertyName("contract")]
    public string Contract { get; init; } = Version;

    /// <summary>The contract this record implements.</summary>
    public const string Version = "brief/1";
}

/// <summary>A node a lead may request, as the lead is told about it.</summary>
/// <param name="Node">The name to use in a request. Instances of a parallel node are named <c>name/1</c>, <c>name/2</c>.</param>
/// <param name="Role">The role it plays.</param>
/// <param name="Deliverable">What kind of thing it hands back.</param>
/// <param name="Parallel">How many instances may run at once.</param>
public sealed record BriefDelegate(
    [property: JsonPropertyName("node")] string Node,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("deliverable")] string? Deliverable,
    [property: JsonPropertyName("parallel")] int Parallel);

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
