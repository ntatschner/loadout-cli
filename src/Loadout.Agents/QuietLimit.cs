namespace Loadout.Agents;

/// <summary>
/// How long a headless turn may go without the agent writing anything before
/// it is given up on as silent.
/// </summary>
/// <remarks>
/// <para>
/// A turn with no limit waits for the agent for as long as it takes, which is
/// right for an agent that is working and wrong for one that has hung: a run
/// whose lead hung sat as running for ever, because nothing else in it could
/// tell the two apart.
/// </para>
/// <para>
/// The clock is read from <see cref="Time"/> and waited on through
/// <see cref="Wait"/>, so a test can move twenty minutes in a moment.
/// </para>
/// </remarks>
/// <param name="Limit">How long the agent may be silent.</param>
/// <param name="Time">The clock silence is measured by.</param>
public sealed record QuietLimit(TimeSpan Limit, TimeProvider Time)
{
    /// <summary>
    /// Whether the agent is waiting on a person, for as long as it is. The
    /// timer stops while this says yes and starts again at full length once
    /// it says no: an agent blocked on somebody's answer is not silent, and
    /// however long they take must never cost it its turn.
    /// </summary>
    public Func<bool>? Held { get; init; }

    /// <summary>How often the timer looks at the clock and at <see cref="Held"/>.</summary>
    public TimeSpan Glance { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Waits for a span of time. Left alone it is a delay on <see cref="Time"/>;
    /// a test gives it one that moves its own clock on instead.
    /// </summary>
    public Func<TimeSpan, CancellationToken, Task>? Wait { get; init; }

    internal Task WaitAsync(TimeSpan span, CancellationToken ct) =>
        Wait?.Invoke(span, ct) ?? Task.Delay(span, Time, ct);
}
