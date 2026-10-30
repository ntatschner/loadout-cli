namespace Loadout.Models.Tasks;

/// <summary>Where a piece of work stands.</summary>
public enum TaskState
{
    /// <summary>Not started.</summary>
    Open,

    /// <summary>Being worked on.</summary>
    Doing,

    /// <summary>Someone said it is finished.</summary>
    /// <remarks>
    /// Said, not established. Every state here is a claim with a name and a
    /// date on it, and this is the one worth corroborating, because it is the
    /// one people act on.
    /// </remarks>
    Done,

    /// <summary>Waiting on something else.</summary>
    Blocked,

    /// <summary>Decided against.</summary>
    Dropped,
}

/// <summary>What sort of thing a task is.</summary>
public enum TaskKind
{
    /// <summary>Work somebody could pick up.</summary>
    Task,

    /// <summary>
    /// Something not yet shaped into work: dropped in to be fleshed out, and
    /// kept out of what a session is told until it has been.
    /// </summary>
    /// <remarks>
    /// A task rather than a list of its own, because an idea turns into one:
    /// accepting it keeps the id and the history and changes the kind, where a
    /// separate list would have to copy it across and keep the two in step.
    /// </remarks>
    Idea,
}

/// <summary>What the work is about, from a set Loadout reasons about.</summary>
/// <remarks>
/// Closed rather than a free tag, because later work keys checks on it: a
/// release task on a project with no release workflow is worth saying something
/// about, and that can only be said of a type every surface agrees on.
/// </remarks>
public enum TaskType
{
    /// <summary>Something that is not a project yet.</summary>
    NewProject,

    /// <summary>Something the project does not do yet.</summary>
    Feature,

    /// <summary>Something the project does wrong.</summary>
    BugFix,

    /// <summary>How the project is built, tested and checked.</summary>
    CiCd,

    /// <summary>How the project ships.</summary>
    Release,

    /// <summary>What is written about the project.</summary>
    Docs,

    /// <summary>Upkeep: dependencies, tidying, renaming, removing.</summary>
    Maintenance,
}

/// <summary>Who gave a task its type.</summary>
public enum TaskTypeSource
{
    /// <summary>Guessed from its words and where it was captured.</summary>
    Rule,

    /// <summary>Revised by the agent refining it.</summary>
    Agent,

    /// <summary>Chosen by a person, and left alone until they hand it back.</summary>
    Person,
}

/// <summary>
/// One piece of work, and who said what about it when.
/// </summary>
/// <remarks>
/// <para>
/// Kept apart from memory because the two answer different questions. A memory
/// is something that stays true — how this machine behaves, what broke last
/// time. A task is true today and stops being true, and mixing the two would
/// fill the durable store with things that expire.
/// </para>
/// <para>
/// Every state carries who declared it and when, because a bare status is an
/// assertion and a dated attributed one can be checked. That is the whole basis
/// on which anything here can be corroborated.
/// </para>
/// </remarks>
public sealed class TaskItem
{
    /// <summary>Short identifier, unique within the project.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>What the work is, in a line.</summary>
    public string Title { get; set; } = string.Empty;

    public TaskState State { get; set; } = TaskState.Open;

    /// <summary>Work, or an idea still being shaped.</summary>
    public TaskKind Kind { get; set; } = TaskKind.Task;

    /// <summary>What the work is about, or null for a task recorded before there were types.</summary>
    /// <remarks>
    /// Every task added since has one, guessed when nobody gives it. A list
    /// recorded earlier is typed by <c>task classify</c> when somebody asks,
    /// never as a side effect of reading it.
    /// </remarks>
    public TaskType? Type { get; set; }

    /// <summary>Who gave it <see cref="Type"/>, or null when it has none.</summary>
    /// <remarks>
    /// Kept so a guess and a choice are told apart: a rule's or the agent's
    /// guess can be revised, and a person's choice is not touched by either.
    /// </remarks>
    public TaskTypeSource? TypeBy { get; set; }

    /// <summary>Who said so: an agent name, or whoever was at the keyboard.</summary>
    public string DeclaredBy { get; set; } = string.Empty;

    /// <summary>When they said it.</summary>
    public DateTimeOffset DeclaredUtc { get; set; }

    /// <summary>Anything worth adding, as a short aside. Empty is normal.</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>The full description, opened on demand. Empty is normal.</summary>
    /// <remarks>
    /// Apart from the title so that a list can be read at a glance. A title
    /// that is really a paragraph swamps every row it appears in, and cutting
    /// it on screen loses the end of it; here nothing is lost and nothing has
    /// to be cut.
    /// </remarks>
    public string Detail { get; set; } = string.Empty;

    /// <summary>What must be true for the work to count as finished, one condition each. Empty is normal.</summary>
    /// <remarks>
    /// On the task rather than on whatever runs it, so the finish line travels
    /// with the work: a team run for the task is held to these, and a team's
    /// queue refuses a task without any, because nobody is there to judge an
    /// unattended run that has no stated finish line.
    /// </remarks>
    public List<string> DoneWhen { get; set; } = [];

    /// <summary>
    /// A done-when a backfill pass asked an agent for and nobody has confirmed
    /// yet, or what went wrong asking; null when there is neither.
    /// </summary>
    /// <remarks>
    /// Apart from <see cref="DoneWhen"/> because nothing unconfirmed may become
    /// a finish line: a team's queue reads <see cref="DoneWhen"/>, and an
    /// unattended run is held to it. It goes when the gap does, so declaring
    /// the task doing, done or dropped, or writing a done-when by hand, throws
    /// it away; going from blocked back to open keeps it.
    /// </remarks>
    public TaskProposal? Proposal { get; set; }

    /// <summary>Every state declared for it, oldest first, added to and never rewritten.</summary>
    /// <remarks>
    /// <see cref="DeclaredBy"/> and <see cref="DeclaredUtc"/> stay as the latest
    /// claim, so whatever reads only those keeps working. A task recorded before
    /// this existed starts with none, and gains its last claim as the first
    /// entry when it is next declared.
    /// </remarks>
    public List<TaskEvent> History { get; set; } = [];

    /// <summary>The list it was on before it was last moved, or null when it never has been.</summary>
    /// <remarks>
    /// Kept on the task rather than left behind on the list it came from, so a
    /// list only ever holds work that is its own: a session, a count or a team
    /// reading it never meets a row that is somewhere else now. The trace the
    /// old list shows is worked out from this, and lasts as long as the task.
    /// </remarks>
    public TaskMove? Moved { get; set; }
}

/// <summary>One dated, attributed claim about where a task stood.</summary>
public sealed class TaskEvent
{
    public TaskState State { get; set; } = TaskState.Open;

    /// <summary>Who said so.</summary>
    public string By { get; set; } = string.Empty;

    /// <summary>When they said it.</summary>
    public DateTimeOffset Utc { get; set; }

    /// <summary>What else changed, when something did. Empty is normal.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>A done-when proposed for a task and held until a person confirms or edits it.</summary>
/// <remarks>
/// Either conditions waiting for review or a failure to retry, never both: a
/// pass that gets an answer clears the failure, and a pass skips a task whose
/// conditions are waiting.
/// </remarks>
public sealed class TaskProposal
{
    /// <summary>The conditions proposed, or empty when asking for them went wrong.</summary>
    public List<string> DoneWhen { get; set; } = [];

    /// <summary>The agent's sentence or two on how it chose them.</summary>
    public string Why { get; set; } = string.Empty;

    /// <summary>When they were proposed, or when asking last went wrong.</summary>
    public DateTimeOffset Utc { get; set; }

    /// <summary>What went wrong the last time a pass asked, or empty when nothing did.</summary>
    public string Error { get; set; } = string.Empty;
}

/// <summary>Where a task came from, and when it left.</summary>
public sealed class TaskMove
{
    /// <summary>The project's slug, or empty for the workspace-wide list.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>When it was moved.</summary>
    public DateTimeOffset Utc { get; set; }
}
