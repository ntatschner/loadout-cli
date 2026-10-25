using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Loadout.Core.Teams;
using Loadout.Models.Teams;

namespace Loadout.Core.Events;

/// <summary>One reason something wants you, as it is true at one look.</summary>
/// <param name="Key">
/// What makes it the same reason at the next look: the run, the kind and what
/// it is about, never its wording, which for a silence changes every minute.
/// </param>
/// <param name="Level">How much it wants you.</param>
/// <param name="Source">What it is about.</param>
/// <param name="Kind">Which reason.</param>
/// <param name="Title">The line the history shows.</param>
/// <param name="Detail">More of it, where there is more.</param>
/// <param name="Project">The project, where there is one.</param>
/// <param name="Run">The run, for a run's reason.</param>
/// <param name="Session">The hosted session, for a session's reason.</param>
public sealed record Observed(
    string Key,
    string Level,
    string Source,
    string Kind,
    string Title,
    string? Detail = null,
    string? Project = null,
    string? Run = null,
    string? Session = null);

/// <summary>What the recorder saw last, kept between passes and between daemons.</summary>
/// <param name="Outstanding">Every reason still true, by its key, as the event that recorded it appearing.</param>
/// <param name="Journals">How many of each run's journal entries have been copied across.</param>
/// <param name="Sessions">The hosted sessions already seen, so each one's start is recorded once.</param>
public sealed record RecorderState(
    IReadOnlyDictionary<string, UserEvent> Outstanding,
    IReadOnlyDictionary<string, int> Journals,
    IReadOnlyList<string> Sessions)
{
    /// <summary>A first start.</summary>
    public static RecorderState Empty { get; } = new(
        new Dictionary<string, UserEvent>(StringComparer.Ordinal),
        new Dictionary<string, int>(StringComparer.Ordinal),
        []);
}

/// <summary>A hosted session as one look sees it.</summary>
/// <param name="Id">The daemon's id for it.</param>
/// <param name="Title">What it is called on the page.</param>
/// <param name="Project">The project it runs in, where it runs in one.</param>
/// <param name="Needs"><c>waiting</c>, <c>approval</c> or null, from <c>SessionSignals.Needs</c>.</param>
public sealed record SessionSeen(string Id, string Title, string? Project, string? Needs);

/// <summary>
/// Turns what is true now into what happened: a reason appearing, a reason
/// clearing, and the milestones and answers around them.
/// </summary>
/// <remarks>
/// <para>
/// Every reason a run or a session wants you is worked out fresh from its
/// current state and never kept (see <see cref="RunAttention"/>), which is
/// right for a rail and leaves nothing to look back over. This compares one
/// look with the last and writes the difference down: an <c>appeared</c>
/// event for each reason that is new, a <see cref="EventKinds.Cleared"/> event
/// pointing at it for each that has gone. So the history is exactly as
/// self-clearing as the rail, and "resolved 4 minutes later" comes free.
/// </para>
/// <para>
/// Milestones and what you did (a run starting or finishing, a question
/// answered, a merge decided) are already written down in each run's journal,
/// so they are copied across rather than inferred, and they are there for
/// days the daemon was not running. Reasons are not: nothing watches while no
/// daemon does, which is why its own start and stop are recorded, so a gap
/// reads as a gap rather than as nothing having happened.
/// </para>
/// <para>
/// Pure over its inputs and the clock, so appearing, staying, clearing and
/// coming back can be argued with in a table of tests.
/// </para>
/// </remarks>
public static class EventRecorder
{
    /// <summary>The runs a pass looks at, newest first: as many as the notices read.</summary>
    public const int Runs = 30;

    /// <summary>One pass: what to append and what to remember.</summary>
    public static (IReadOnlyList<UserEvent> Events, RecorderState State) Pass(
        RecorderState before,
        IReadOnlyList<Observed> now,
        IReadOnlyList<UserEvent> copied,
        IReadOnlyDictionary<string, int> journals,
        IReadOnlyList<string> sessions,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(now);

        var events = new List<UserEvent>(copied);
        var outstanding = new Dictionary<string, UserEvent>(StringComparer.Ordinal);

        foreach (var reason in now.DistinctBy(one => one.Key, StringComparer.Ordinal))
        {
            if (before.Outstanding.TryGetValue(reason.Key, out var still))
            {
                outstanding[reason.Key] = still;

                continue;
            }

            var appeared = new UserEvent(
                IdOf(reason.Key, at),
                at,
                reason.Level,
                reason.Source,
                EventDirections.ToMe,
                reason.Kind,
                reason.Title,
                reason.Detail,
                reason.Project,
                reason.Run,
                reason.Session);

            outstanding[reason.Key] = appeared;
            events.Add(appeared);
        }

        foreach (var (key, was) in before.Outstanding.Where(one => !outstanding.ContainsKey(one.Key)))
        {
            events.Add(Clearing(was, key, at));
        }

        return (events, new RecorderState(outstanding, journals, sessions));
    }

    /// <summary>What clearing an event writes: the event's own particulars, pointing back at it.</summary>
    public static UserEvent Clearing(UserEvent was, string key, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(was);

        return new UserEvent(
            IdOf(key + "/cleared", at),
            at,
            EventLevels.Info,
            was.Source,
            EventDirections.ToMe,
            EventKinds.Cleared,
            was.Title,
            Project: was.Project,
            Run: was.Run,
            Session: was.Session,
            Clears: was.Id);
    }

    /// <summary>Why one run wants you, keyed so a reason is the same reason until it stops being true.</summary>
    public static IEnumerable<Observed> Of(RunSummary run, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);

        foreach (var reason in RunAttention.For(run, now))
        {
            var (kind, level) = reason.Kind switch
            {
                AttentionKind.Asking => (EventKinds.Asking, EventLevels.Needs),
                AttentionKind.GoingNowhere => (EventKinds.GoingNowhere, EventLevels.Problem),
                AttentionKind.Quiet => (EventKinds.Quiet, EventLevels.Problem),
                _ => (EventKinds.Spending, EventLevels.Problem),
            };

            var title = reason.Kind switch
            {
                AttentionKind.Asking => $"{run.Team} asks: {reason.Detail}",
                AttentionKind.GoingNowhere => $"{run.Team} is going nowhere",
                AttentionKind.Quiet => $"{run.Team}: {reason.About ?? "a node"} has gone quiet",
                _ => $"{run.Team} is spending too fast",
            };

            yield return new Observed(
                $"run/{run.RunId}/{kind}/{reason.About ?? string.Empty}",
                level,
                EventSources.Run,
                kind,
                title,
                reason.Kind == AttentionKind.Asking ? null : reason.Detail,
                run.Project,
                run.RunId);
        }
    }

    /// <summary>Why one hosted session wants you.</summary>
    public static Observed? Of(SessionSeen session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.Needs switch
        {
            "approval" => new Observed(
                $"session/{session.Id}/approval", EventLevels.Needs, EventSources.Session, EventKinds.Approval,
                $"{session.Title} needs approval", Project: session.Project, Session: session.Id),
            "waiting" => new Observed(
                $"session/{session.Id}/waiting", EventLevels.Needs, EventSources.Session, EventKinds.Waiting,
                $"{session.Title} is waiting for you", Project: session.Project, Session: session.Id),
            _ => null,
        };
    }

    /// <summary>A session seen for the first time: one you started, from Studio, the only place that hosts them.</summary>
    public static UserEvent Started(SessionSeen session, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new UserEvent(
            IdOf($"session/{session.Id}/started", at), at, EventLevels.Info, EventSources.Session, EventDirections.ByMe,
            EventKinds.SessionStarted, $"You started {session.Title}", Project: session.Project, Session: session.Id);
    }

    /// <summary>
    /// What a run's journal says happened from entry <paramref name="from"/> on
    /// that belongs in the history.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each takes its id from the run and its place in the journal, so copying
    /// the same entry twice writes the same id, and a reader can tell.
    /// </para>
    /// <para>
    /// An answer counts as yours unless the run wrote it itself, closing or
    /// retiring a question nobody answered. A merge decision counts only on a
    /// run that was not autonomous, because an autonomous run decides its own.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<UserEvent> Copied(RunSummary run, IReadOnlyList<RunEvent> entries, int from)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(entries);

        var copied = new List<UserEvent>();

        for (var index = Math.Max(0, from); index < entries.Count; index++)
        {
            var entry = entries[index];
            var id = $"j-{run.RunId}-{index.ToString(CultureInfo.InvariantCulture)}";

            UserEvent Make(string level, string direction, string kind, string title, string? detail = null) =>
                new(id, entry.At, level, EventSources.Run, direction, kind, title, detail, run.Project, run.RunId);

            switch (entry.Kind)
            {
                case "run.started":
                    copied.Add(Make(
                        EventLevels.Info, EventDirections.ToMe, EventKinds.RunStarted,
                        $"{run.Team} started" + (run.Project is { Length: > 0 } on ? $" on {on}" : string.Empty),
                        run.Goal));
                    break;

                case "run.finished":
                    var outcome = RunOutcomes.From(entry.Text("outcome") ?? entry.Text("ended")) ?? RunOutcome.Unrecorded;

                    copied.Add(Make(
                        outcome switch
                        {
                            RunOutcome.Done => EventLevels.Done,
                            RunOutcome.Stopped => EventLevels.Info,

                            // Somebody ending a dead run has dealt with it;
                            // the problem was the crash.
                            RunOutcome.Abandoned => EventLevels.Info,
                            _ => EventLevels.Problem,
                        },
                        EventDirections.ToMe,
                        EventKinds.RunFinished,
                        $"{run.Team} finished: {RunOutcomes.Spell(outcome)}",
                        entry.Text("error")));
                    break;

                case "node.answered" when entry.Text("by") is not (NodePermissions.Ended or NodePermissions.Retired):
                    copied.Add(Make(
                        EventLevels.Info, EventDirections.ByMe, EventKinds.Answered,
                        $"You {(entry.Yes("allowed") ? "allowed" : "refused")} {entry.Node ?? "a node"} "
                            + (entry.Word("tool") ?? "what it asked"),
                        entry.Word("chosen")));
                    break;

                case "gate.decided" when !string.Equals(run.Autonomy, "autonomous", StringComparison.OrdinalIgnoreCase):
                    copied.Add(Make(
                        EventLevels.Info, EventDirections.ByMe, EventKinds.GateDecided,
                        $"You {(entry.Yes("allowed") ? "allowed" : "refused")} merging {entry.Word("branch") ?? "a branch"}"));
                    break;
            }
        }

        return copied;
    }

    /// <summary>The daemon starting or stopping, which is when watching starts and stops.</summary>
    public static UserEvent Daemon(bool started, DateTimeOffset at) =>
        new(
            IdOf(started ? "daemon/started" : "daemon/stopped", at),
            at,
            EventLevels.Info,
            EventSources.Daemon,
            EventDirections.ToMe,
            started ? EventKinds.DaemonStarted : EventKinds.DaemonStopped,
            started
                ? "The daemon started, and began watching"
                : "The daemon stopped. Nothing is watched until it starts again");

    /// <summary>A notice going out somewhere else, or failing to.</summary>
    public static UserEvent Notice(string where, string title, bool landed, string? project, string? run, DateTimeOffset at) =>
        new(
            IdOf($"notice/{run}/{title}/{landed}", at),
            at,
            landed ? EventLevels.Info : EventLevels.Problem,
            EventSources.Notice,
            EventDirections.ToMe,
            landed ? EventKinds.NoticeSent : EventKinds.NoticeFailed,
            landed ? $"Told {where}: {title}" : $"Could not tell {where}: {title}",
            Project: project,
            Run: run);

    /// <summary>An id from what it is and when, so the same pass writing the same thing twice writes one id.</summary>
    public static string IdOf(string key, DateTimeOffset at)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key + "@" + at.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)));

        return "e-" + Convert.ToHexStringLower(bytes)[..12];
    }
}
