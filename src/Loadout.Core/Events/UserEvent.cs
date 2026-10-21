namespace Loadout.Core.Events;

/// <summary>One thing that happened which a person may want to know about, or did.</summary>
/// <param name="Id">Its own name, which a <c>cleared</c> event and the read marks point at.</param>
/// <param name="At">When it was seen. For a reason, the pass that saw it appear, so no finer than the daemon's look.</param>
/// <param name="Level">How much it wants you: one of <see cref="EventLevels.All"/>.</param>
/// <param name="Source">What it is about: one of <see cref="EventSources.All"/>.</param>
/// <param name="Direction">Whether it was said to you or done by you: one of <see cref="EventDirections.All"/>.</param>
/// <param name="Kind">What happened, in a word the page and the command can each turn into their own wording.</param>
/// <param name="Title">What happened, as one line somebody can read with nothing else in front of them.</param>
/// <param name="Detail">More of it, where there is more, such as the question a run asked.</param>
/// <param name="Project">The project it happened in, where it happened in one.</param>
/// <param name="Run">The team run it happened in, so a surface can point at it.</param>
/// <param name="Session">The hosted session it happened in, for the same reason.</param>
/// <param name="Clears">
/// For <see cref="EventKinds.Cleared"/> only: the id of the event that has
/// stopped being true. Folded into that event when read, never listed itself.
/// </param>
public sealed record UserEvent(
    string Id,
    DateTimeOffset At,
    string Level,
    string Source,
    string Direction,
    string Kind,
    string Title,
    string? Detail = null,
    string? Project = null,
    string? Run = null,
    string? Session = null,
    string? Clears = null);

/// <summary>How much an event wants you, in the words Studio's notifications already use.</summary>
public static class EventLevels
{
    /// <summary>Something is waiting on a person.</summary>
    public const string Needs = "needs";

    /// <summary>Something went wrong, or is going wrong.</summary>
    public const string Problem = "problem";

    /// <summary>Something finished as it should.</summary>
    public const string Done = "done";

    /// <summary>Worth knowing, asking nothing.</summary>
    public const string Info = "info";

    /// <summary>Every level, most wanting first, which is the order a filter writes them in.</summary>
    public static readonly IReadOnlyList<string> All = [Needs, Problem, Done, Info];
}

/// <summary>What an event is about.</summary>
public static class EventSources
{
    public const string Run = "run";
    public const string Session = "session";
    public const string Daemon = "daemon";
    public const string Notice = "notice";

    public static readonly IReadOnlyList<string> All = [Run, Session, Daemon, Notice];
}

/// <summary>Which way an event points.</summary>
public static class EventDirections
{
    /// <summary>Said to you: something asked, went wrong, or finished.</summary>
    public const string ToMe = "to-me";

    /// <summary>Done by you: an answer, a merge decision, a session started.</summary>
    public const string ByMe = "by-me";

    /// <summary>Not a direction an event has, but one a filter may ask for: both.</summary>
    public const string Any = "any";

    public static readonly IReadOnlyList<string> All = [ToMe, ByMe];
}

/// <summary>The words <see cref="UserEvent.Kind"/> takes.</summary>
public static class EventKinds
{
    public const string Asking = "asking";
    public const string GoingNowhere = "going-nowhere";
    public const string Quiet = "quiet";
    public const string Spending = "spending";
    public const string Waiting = "waiting";
    public const string Approval = "approval";
    public const string Cleared = "cleared";
    public const string RunStarted = "run-started";
    public const string RunFinished = "run-finished";
    public const string Answered = "answered";
    public const string GateDecided = "gate-decided";
    public const string SessionStarted = "session-started";
    public const string DaemonStarted = "daemon-started";
    public const string DaemonStopped = "daemon-stopped";
    public const string NoticeSent = "notice-sent";
    public const string NoticeFailed = "notice-failed";
}
