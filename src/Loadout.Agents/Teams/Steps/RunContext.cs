namespace Loadout.Agents.Teams.Steps;

/// <summary>
/// One run's own state, made once the run knows who is watching it and handed
/// to whatever needs it, rather than kept on the runner between runs.
/// </summary>
internal sealed class RunContext
{
    public RunContext(bool asking, string? onTimeout)
    {
        Asking = asking;
        OnTimeout = onTimeout;
    }

    /// <summary>
    /// Whether this run may put a node's unmatched call to the person.
    /// </summary>
    /// <remarks>
    /// Read when each node's policy is written. A fact about the run rather
    /// than about any one node.
    /// </remarks>
    public bool Asking { get; }

    /// <summary>What a timed question does when nobody answers, for this run.</summary>
    public string? OnTimeout { get; }

    /// <summary>
    /// Which questions the timer has already sent back to the lead, by
    /// <c>TeamRunner.KeyOf</c>, so think-again-once sends each back only once.
    /// </summary>
    public HashSet<string> SentBackByTimer { get; } = new(StringComparer.Ordinal);
}
