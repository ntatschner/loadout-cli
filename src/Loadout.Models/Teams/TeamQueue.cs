namespace Loadout.Models.Teams;

/// <summary>
/// One team's queue of fully specified tasks, worked through one run at a time.
/// </summary>
/// <remarks>
/// <para>
/// One per team rather than one per project: the team is what decides how a
/// run behaves and how many go at once, and each item names the project it is
/// for, so one team can work through several projects' tasks in a night.
/// </para>
/// <para>
/// Machine-local, for the reason <see cref="TeamSchedule"/> is. A queue that
/// travelled with the workspace would be drained by every machine that cloned
/// it, and two machines starting the same task fight over its branch.
/// </para>
/// </remarks>
public sealed class TeamQueue
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>What is waiting to run, in the order it runs.</summary>
    public List<QueueItem> Items { get; set; } = [];

    /// <summary>What the queue has started, newest last.</summary>
    /// <remarks>
    /// Kept so the queue can say what happened overnight, honour 'after' for a
    /// piece whose earlier piece has already run, and count what a window has
    /// spent. Trimmed to the newest few dozen when written.
    /// </remarks>
    public List<QueueRun> Runs { get; set; } = [];

    /// <summary>The limits and grants a person set for it.</summary>
    public QueueSettings Settings { get; set; } = new();

    /// <summary>
    /// When the window the daemon is working in opened, or null when none is open.
    /// </summary>
    /// <remarks>
    /// Written when the daemon first finds a window open and cleared when it
    /// has said what the window did, so a summary is sent once per window and
    /// a daemon restarted mid-window carries on counting from the same start.
    /// </remarks>
    public DateTimeOffset? WindowOpened { get; set; }
}

/// <summary>One task waiting in a queue.</summary>
public sealed class QueueItem
{
    /// <summary>The task's id on its project's list.</summary>
    public string Task { get; set; } = string.Empty;

    /// <summary>The project whose list the task is on.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// A task this one waits for, on the same project, or empty for none.
    /// </summary>
    /// <remarks>
    /// So a plan split into pieces runs in order. It is satisfied once that
    /// task's queued run has met its done-when, or the task is marked done.
    /// </remarks>
    public string After { get; set; } = string.Empty;

    /// <summary>Who added it.</summary>
    public string AddedBy { get; set; } = string.Empty;

    /// <summary>When it was added.</summary>
    public DateTimeOffset AddedUtc { get; set; }

    /// <summary>
    /// The run it was put back after, when it was re-queued from the Blocked
    /// section, so the run it becomes carries on from that one. Empty otherwise.
    /// </summary>
    public string Follows { get; set; } = string.Empty;

    /// <summary>
    /// The pull request mode for this task alone - <c>draft</c>, <c>ready</c>
    /// or <c>none</c> - or empty to take the queue's or the project's.
    /// </summary>
    public string PrMode { get; set; } = string.Empty;
}

/// <summary>Where a run the queue started got to.</summary>
public enum QueueRunState
{
    /// <summary>Started and not yet ended.</summary>
    Running,

    /// <summary>Waiting at a gate or a question nobody has answered.</summary>
    Held,

    /// <summary>Ended with every done-when met.</summary>
    Met,

    /// <summary>Ended any other way: blocked, over budget, stopped, or could not start.</summary>
    Failed,
}

/// <summary>One run the queue started, and how it went.</summary>
public sealed class QueueRun
{
    public string Task { get; set; } = string.Empty;

    public string Project { get; set; } = string.Empty;

    /// <summary>The run's id, or empty when it never got one.</summary>
    public string RunId { get; set; } = string.Empty;

    public QueueRunState State { get; set; } = QueueRunState.Running;

    /// <summary>Why it ended as it did, in a line. Empty while it is running.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>What it spent, in US dollars, as far as is known.</summary>
    public decimal CostUsd { get; set; }

    public DateTimeOffset StartedUtc { get; set; }

    public DateTimeOffset? EndedUtc { get; set; }

    /// <summary>Who started it: a person's name for 'run next', or the daemon.</summary>
    public string StartedBy { get; set; } = string.Empty;

    /// <summary>The process running it, when the daemon started it as one; zero otherwise.</summary>
    public int Pid { get; set; }

    /// <summary>When that process started, so a reused process id is not mistaken for it.</summary>
    public DateTimeOffset? PidStarted { get; set; }

    /// <summary>The worktree it worked in, removed once it has ended; empty when there was none.</summary>
    public string Worktree { get; set; } = string.Empty;

    /// <summary>The branch its work is on.</summary>
    public string Branch { get; set; } = string.Empty;

    /// <summary>
    /// The pull request mode its queued item asked for, carried over because
    /// the item leaves the queue when the run starts. Empty when it asked for none.
    /// </summary>
    public string PrMode { get; set; } = string.Empty;

    /// <summary>The pull request it opened, when it opened one.</summary>
    public string PullRequest { get; set; } = string.Empty;

    /// <summary>Whether what happened after it ended - the pull request, the task - has been done.</summary>
    public bool Landed { get; set; }

    /// <summary>When the pull request was last looked at to see whether it has been merged.</summary>
    public DateTimeOffset? MergeChecked { get; set; }
}

/// <summary>What a person allowed a queue, and its limits.</summary>
public sealed class QueueSettings
{
    /// <summary>
    /// The most a window may spend, as <c>team run --usd</c> takes a figure, or empty for no cap of its own.
    /// </summary>
    public string WindowUsd { get; set; } = string.Empty;

    /// <summary>The most runs a window may start, or null for no cap of its own.</summary>
    public int? WindowRuns { get; set; }

    /// <summary>How many runs may wait at a gate at once before the queue stops starting more.</summary>
    public int HeldCap { get; set; } = 2;

    /// <summary>
    /// Whether a queued run that meets its done-when may push its branch and open a draft pull request.
    /// </summary>
    /// <remarks>
    /// The grant queues had before <see cref="PrMode"/>, still read as
    /// <c>draft</c> when <see cref="PrMode"/> is empty so a queue set up then
    /// keeps working. Setting the mode clears it.
    /// </remarks>
    public bool DraftPullRequests { get; set; }

    /// <summary>
    /// Whether a queued run that meets its done-when pushes its branch and
    /// opens a pull request: <c>draft</c>, <c>ready</c> or <c>none</c>, or
    /// empty to take the project's.
    /// </summary>
    /// <remarks>
    /// Kept here where it can be seen and turned off again. Nothing else
    /// outward is granted by it: merges and every other outward action still
    /// wait for a person.
    /// </remarks>
    public string PrMode { get; set; } = string.Empty;
}
