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

/// <summary>Where a task came from, and when it left.</summary>
public sealed class TaskMove
{
    /// <summary>The project's slug, or empty for the workspace-wide list.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>When it was moved.</summary>
    public DateTimeOffset Utc { get; set; }
}
