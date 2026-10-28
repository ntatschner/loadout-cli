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

    /// <summary>
    /// What this team is for, standing, as opposed to what any one run is
    /// about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A run's goal is the thing somebody typed when they started it, true of
    /// that run and no other. This is true of every run of the team, and it
    /// reaches every node's brief: a node given a narrow job still needs to
    /// know what the team is for. "Find why the disk filled" is a different
    /// job inside a team that exists to keep a system up from inside one that
    /// exists to write a report about it.
    /// </para>
    /// <para>
    /// Empty is ordinary. A team whose description says enough does not need
    /// to say it twice.
    /// </para>
    /// </remarks>
    public string Goal { get; set; } = string.Empty;

    /// <summary>
    /// What a run of this team is judged on when whoever starts it gives no
    /// done-when of their own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A run given criteria is held to those and these are ignored: the two
    /// are never merged, because somebody who wrote their own has said what
    /// done means for this run, and a list with the team's added on top would
    /// hold them to things they did not ask for.
    /// </para>
    /// <para>
    /// With these, the lead is not asked to propose criteria. The team's author
    /// has already said what done means, and a lead proposing its own on top
    /// would be asking a person to choose between two answers to a question
    /// that was settled when the team was written.
    /// </para>
    /// </remarks>
    public List<string> DoneWhen { get; set; } = [];

    /// <summary>
    /// Rules every node of this team follows, whatever the run is about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Standing instructions rather than a task: how this team works, not what
    /// it is doing today. They go into every brief, so a role's own
    /// instructions and the team's declarations are both in front of a node
    /// before it starts.
    /// </para>
    /// <para>
    /// Each one is written so somebody could check the node against it
    /// afterwards, for the same reason a done-when condition is. A declaration
    /// nobody can check is a hope.
    /// </para>
    /// <para>
    /// A declaration cannot raise what a node may do. It is prose in a brief,
    /// read by a model; what a node is permitted lives in the machine's own
    /// configuration and is decided before anything starts.
    /// </para>
    /// </remarks>
    public List<string> Declarations { get; set; } = [];

    /// <summary>
    /// Machinery the declarations lean on, by capability id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A declaration is prose and enforces nothing by itself. "Register what
    /// you work out so later runs find it" only means anything where something
    /// gives the team a shelf to put it on, a format to write it in, a role
    /// that can act on it and a gate that decides whether it may. This names
    /// that bundle.
    /// </para>
    /// <para>
    /// Separate from the declarations rather than attached to each one,
    /// because a declaration reads better as a sentence and a team relies on a
    /// capability once however many rules it writes about it.
    /// </para>
    /// <para>
    /// Empty is ordinary, and most teams want nothing here: a team whose
    /// declarations are about how to work rather than what to keep needs no
    /// machinery at all.
    /// </para>
    /// </remarks>
    public List<string> Capabilities { get; set; } = [];

    /// <summary>The node that reports to the person. Must be a key of <see cref="Nodes"/>.</summary>
    public string Lead { get; set; } = string.Empty;

    /// <summary>Every node, keyed by the name the run uses for it.</summary>
    public Dictionary<string, TeamNode> Nodes { get; set; } = new(StringComparer.Ordinal);

    /// <summary>How a run behaves.</summary>
    public TeamRules Rules { get; set; } = new();

    /// <summary>
    /// Trigger rules the team suggests for itself, which do nothing until
    /// somebody accepts one with <c>loadout trigger accept</c>.
    /// </summary>
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

    /// <summary>
    /// How long a lead's question with a recommendation waits for somebody
    /// before the recommendation is taken for them, as 30m, 2h or 1d. Empty
    /// means it waits for a person, which is the default.
    /// </summary>
    /// <remarks>
    /// Only the lead's questions, and only where the answer arrives as a file -
    /// a run started from the dashboard or the daemon. A terminal prompt blocks
    /// until it is answered and nothing here can answer it for you. Never a
    /// gate on an outward action or a merge: those carry no recommendation,
    /// and are the ones a person has to say yes to.
    /// </remarks>
    public string? TakeRecommendationAfter { get; set; }

    /// <summary>
    /// What a question does when that wait runs out: <c>recommend</c>,
    /// <c>think-again-once</c> or <c>think-again</c>. Empty means
    /// think-again-once.
    /// </summary>
    /// <remarks>
    /// A think-again goes back to the lead as a round of its own, so the round
    /// limit and the budget still bound it, including under think-again.
    /// </remarks>
    public string? OnTimeout { get; set; }

    /// <summary>
    /// How long a node may go without writing anything before its turn is
    /// taken as hung, as 20m, 1h or <c>never</c>. Empty means
    /// <see cref="DefaultQuietLimit"/>.
    /// </summary>
    /// <remarks>
    /// A turn that goes past it is killed and tried once more; a second
    /// silence marks the node stuck. Time a node spends waiting on a person's
    /// answer to its question is not counted.
    /// </remarks>
    public string? QuietLimit { get; set; }

    /// <summary>The silence a node is allowed when the team file says nothing.</summary>
    public static readonly TimeSpan DefaultQuietLimit = TimeSpan.FromMinutes(20);

    /// <summary>The silence a node is allowed, or null where the team said never.</summary>
    public TimeSpan? Quiet => TeamDuration.IsNever(QuietLimit)
        ? null
        : TeamDuration.Parse(QuietLimit) ?? DefaultQuietLimit;
}

/// <summary>What a run may spend, in three currencies.</summary>
public sealed class TeamBudget
{
    /// <summary>Across the whole run.</summary>
    public decimal? Usd { get; set; }

    /// <summary>
    /// Whether the team file said <c>usd: none</c>: no money cap, on purpose,
    /// which is not the same as saying nothing.
    /// </summary>
    public bool Uncapped { get; set; }

    /// <summary>What the team file says about money, as one value.</summary>
    public UsdCap Cap => Uncapped ? UsdCap.NoCap : Usd is { } usd && usd > 0m ? UsdCap.Of(usd) : UsdCap.Unset;

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

/// <summary>
/// A trigger rule a team suggests for itself: what to wait for and what to ask
/// it, offered to whoever uses the team and acting only once accepted.
/// </summary>
/// <remarks>
/// <para>
/// A suggestion, never a rule. A team file travels between machines and
/// repositories, and one that could start runs by being read would let
/// whoever wrote it spend somebody else's money. Accepting it makes a rule on
/// this machine, with a spend cap the person accepting it settles, and the
/// rule stays as accepted when the team's file changes: a changed suggestion
/// is shown, and taking it is a deliberate edit.
/// </para>
/// <para>
/// Replaces a placeholder of the same name that carried a cron expression, an
/// event and roles to bypass, which nothing ever read. A team file that still
/// has those keys loads as before, without them.
/// </para>
/// </remarks>
public sealed class TeamTrigger
{
    /// <summary>Its name within the team, which the accepted rule is named after.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The kind of event it waits for, such as <c>run-finished</c>.</summary>
    public string On { get; set; } = string.Empty;

    /// <summary>Fields of the event that must hold a given value.</summary>
    public Dictionary<string, string> Where { get; set; } = [];

    /// <summary>What to ask the team for. Fields of the event can be named as <c>{field}</c>.</summary>
    public string Goal { get; set; } = string.Empty;

    /// <summary>supervised or autonomous.</summary>
    public string Autonomy { get; set; } = "autonomous";

    /// <summary>What it suggests each run may spend, or null to leave it to the person accepting.</summary>
    public decimal? BudgetUsd { get; set; }

    /// <summary>What it suggests its runs may spend in the window, or null to leave it to the person accepting.</summary>
    public decimal? CapUsd { get; set; }

    /// <summary>How long a subject must go quiet before acting, such as 10m, or empty for not at all.</summary>
    public string Debounce { get; set; } = string.Empty;
}
