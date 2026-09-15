namespace Loadout.Models.Teams;

/// <summary>
/// A team as its file describes it: which roles, how they are wired, and
/// the rules a run of it follows.
/// </summary>
/// <remarks>
/// <para>
/// Mutable classes with settable properties because that is what the YAML
/// deserialiser needs, like the project manifest. A team lives in a file:
/// shipped inside the launcher, in the workspace under <c>global/teams</c>,
/// or under one project, later replacing earlier by name, which is the same
/// layering the specialists use and for the same reason.
/// </para>
/// <para>
/// Nothing here can raise what a node may do on a machine. A team declares
/// what its nodes need; the machine's own configuration decides what they
/// get, and a team that needs more is refused before anything starts.
/// </para>
/// </remarks>
public sealed class TeamDefinition
{
    /// <summary>The name a person runs it by. Lowercase, hyphenated.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>One or two sentences on what a run of it does.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>The node that reports to the person. Must be a key of <see cref="Nodes"/>.</summary>
    public string Lead { get; set; } = string.Empty;

    /// <summary>Every node, keyed by the name the run uses for it.</summary>
    public Dictionary<string, TeamNode> Nodes { get; set; } = new(StringComparer.Ordinal);

    /// <summary>How a run behaves.</summary>
    public TeamRules Rules { get; set; } = new();

    /// <summary>What may start a run without a person typing the command.</summary>
    public List<TeamTrigger> Triggers { get; set; } = [];

    /// <summary>
    /// A shape to copy rather than a team to run. <c>team new --from</c>
    /// offers it; <c>team run</c> refuses it and says so.
    /// </summary>
    public bool Template { get; set; }
}

/// <summary>One node of a team.</summary>
public sealed class TeamNode
{
    /// <summary>The role the node plays, by specialist id, such as <c>role.reviewer</c>.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>The agent to run it on, or empty for the project's default.</summary>
    public string Agent { get; set; } = string.Empty;

    /// <summary>
    /// The model this node's agent should use, or empty to let the project
    /// and then the agent decide.
    /// </summary>
    /// <remarks>
    /// Per node, because a team is where the shape of the work is known and
    /// the same posture is not the same job: a reviewer reading one diff and
    /// a release validator running every check both review, and only one of
    /// them is worth a large model. Written as the agent spells it.
    /// </remarks>
    public string Model { get; set; } = string.Empty;

    /// <summary>Nodes this one may request. Only meaningful on a lead.</summary>
    public List<string> Delegates { get; set; } = [];

    /// <summary>Whether each instance works in its own git worktree.</summary>
    public bool Worktree { get; set; }

    /// <summary>How many instances may run at once. One unless the team says more.</summary>
    public int Parallel { get; set; } = 1;

    /// <summary>Values passed to a parameterised role, such as a department.</summary>
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>How a run behaves.</summary>
public sealed class TeamRules
{
    /// <summary>manual, supervised or autonomous.</summary>
    public string Autonomy { get; set; } = "supervised";

    /// <summary>What a run may spend.</summary>
    public TeamBudget Budget { get; set; } = new();

    /// <summary>What the run holds for a decision.</summary>
    public TeamGates Gates { get; set; } = new();

    /// <summary>Conditions that end a run: goal_met, budget_spent, no_progress_2_rounds.</summary>
    public List<string> StopWhen { get; set; } = [];
}

/// <summary>What a run may spend, in three currencies.</summary>
public sealed class TeamBudget
{
    /// <summary>Across the whole run.</summary>
    public decimal? Usd { get; set; }

    /// <summary>Per node session.</summary>
    public int? TurnsPerNode { get; set; }

    /// <summary>Across the whole run, as a duration such as <c>4h</c> or <c>90m</c>.</summary>
    public string? WallClock { get; set; }
}

/// <summary>What the run holds for a decision.</summary>
public sealed class TeamGates
{
    /// <summary>
    /// How outward actions are handled: <c>ask</c> holds every one for the
    /// person. Anything else is refused: a team file may not allow an outward
    /// action by itself; a run allows named ones per node.
    /// </summary>
    public string Outward { get; set; } = "ask";

    /// <summary>Nodes whose decisions a change needs before it reaches the main branch.</summary>
    public List<string> Merge { get; set; } = [];

    /// <summary>
    /// Outward actions the team allows its nodes in autonomous mode, each
    /// named exactly. Empty means none. Honoured only where the machine's
    /// own configuration lets a team allow any.
    /// </summary>
    public List<string> OutwardAllowedWhenAutonomous { get; set; } = [];
}

/// <summary>What may start a run on its own.</summary>
public sealed class TeamTrigger
{
    /// <summary>A cron expression, or empty.</summary>
    public string Schedule { get; set; } = string.Empty;

    /// <summary>An event name such as <c>task.blocked</c>, or empty.</summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>Roles to run with permission checks off, honoured only where the machine allows bypass on triggered runs.</summary>
    public List<string> Bypass { get; set; } = [];
}
