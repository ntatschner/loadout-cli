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

    /// <summary>Who said so: an agent name, or whoever was at the keyboard.</summary>
    public string DeclaredBy { get; set; } = string.Empty;

    /// <summary>When they said it.</summary>
    public DateTimeOffset DeclaredUtc { get; set; }

    /// <summary>Anything worth adding. Empty is normal.</summary>
    public string Note { get; set; } = string.Empty;
}
