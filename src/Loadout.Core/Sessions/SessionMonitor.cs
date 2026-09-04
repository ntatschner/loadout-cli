namespace Loadout.Core.Sessions;

/// <summary>What a running session appears to be doing.</summary>
public enum SessionState
{
    /// <summary>Something was written recently enough to call it active.</summary>
    Working,

    /// <summary>Nothing has been written for a while.</summary>
    Idle,

    /// <summary>
    /// The transcript could not be found or read, so there is no answer.
    /// </summary>
    /// <remarks>
    /// Deliberately not the same as idle. Neither agent publishes its transcript
    /// format, so a session whose file cannot be located is one this cannot see
    /// — and reporting that as "idle" would tell somebody their agent had
    /// stopped when it may be working perfectly well.
    /// </remarks>
    Unknown,
}

/// <summary>A running session, with how long it has been running and how quiet it is.</summary>
/// <param name="Session">The registry entry.</param>
/// <param name="LastMessageAt">When the transcript last changed, when that could be read.</param>
/// <param name="Elapsed">How long since the launcher started it.</param>
/// <param name="Quiet">How long since anything was written, or null when unknown.</param>
/// <param name="State">What that adds up to.</param>
public sealed record SessionActivity(
    RunningSession Session,
    DateTimeOffset? LastMessageAt,
    TimeSpan Elapsed,
    TimeSpan? Quiet,
    SessionState State);

/// <summary>
/// Describes a running session from what is already written down.
/// </summary>
/// <remarks>
/// <para>
/// Passive by construction. The registry says what was started and the agent's
/// own transcript says when it last said anything; nothing here attaches to a
/// console, reads another process's memory or drives a terminal. Doing any of
/// that on this machine once took out every live session on it, and a monitor
/// that can break the thing it is monitoring is not one worth having.
/// </para>
/// <para>
/// Best effort, like the session listing next door and for the same reason:
/// neither transcript format is a published contract. Every figure here is
/// derived from a file's last write time, which is the one thing about somebody
/// else's format that cannot change out from under this.
/// </para>
/// </remarks>
public static class SessionMonitor
{
    /// <summary>
    /// How long without a word before a session counts as idle.
    /// </summary>
    /// <remarks>
    /// Long enough that thinking, a build or a long tool call does not trip it,
    /// short enough to be worth telling somebody about. It is not a timeout:
    /// nothing is stopped, and a session that goes quiet for an hour and then
    /// carries on is reported as working again the moment it does.
    /// </remarks>
    public static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(5);

    /// <summary>What a running session looks like right now.</summary>
    /// <param name="session">The registry entry.</param>
    /// <param name="lastMessageAt">When its transcript last changed, if that is known.</param>
    /// <param name="now">The current time.</param>
    /// <param name="idleAfter">Override for the quiet threshold.</param>
    public static SessionActivity Describe(
        RunningSession session,
        DateTimeOffset? lastMessageAt,
        DateTimeOffset now,
        TimeSpan? idleAfter = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        var threshold = idleAfter ?? IdleAfter;

        // Clamped, because a clock that went backwards is not a session that
        // started in the future. A negative age shown to somebody reads as a
        // bug in the launcher, which is exactly what it would be.
        var elapsed = Longest(now - session.StartedAt);

        if (lastMessageAt is not { } last)
        {
            return new SessionActivity(session, null, elapsed, null, SessionState.Unknown);
        }

        var quiet = Longest(now - last);

        return new SessionActivity(
            session,
            last,
            elapsed,
            quiet,
            quiet >= threshold ? SessionState.Idle : SessionState.Working);
    }

    /// <summary>Never less than nothing.</summary>
    private static TimeSpan Longest(TimeSpan span) => span < TimeSpan.Zero ? TimeSpan.Zero : span;

    /// <summary>
    /// A duration as somebody would say it.
    /// </summary>
    /// <remarks>
    /// Coarse on purpose. Nobody reading a session list needs seconds, and
    /// "1h 4m" is read at a glance where "01:04:37" has to be parsed.
    /// Rounded down rather than to nearest, so ninety seconds reads as "1m"
    /// the way somebody would say it, and consistently with the hour and day
    /// branches below.
    /// </remarks>
    public static string Spoken(TimeSpan span) => span.TotalMinutes switch
    {
        < 1 => "just now",
        < 60 => $"{(int)span.TotalMinutes}m",
        < 24 * 60 => $"{(int)span.TotalHours}h {span.Minutes}m",
        _ => $"{(int)span.TotalDays}d {span.Hours}h",
    };
}
