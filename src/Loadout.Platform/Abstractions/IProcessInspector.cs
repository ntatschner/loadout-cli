namespace Loadout.Platform.Abstractions;

/// <summary>
/// Answers whether a process is still there.
/// <para>
/// Separate from <see cref="IProcessLauncher"/>, which starts things. This one
/// starts nothing and touches nothing: it is asked about a process somebody else
/// recorded, which is the only question a record of running sessions can ask
/// without becoming a thing that drives them.
/// </para>
/// </summary>
public interface IProcessInspector
{
    /// <summary>The identifier of the process asking.</summary>
    int CurrentProcessId { get; }

    /// <summary>When the process asking was started.</summary>
    DateTimeOffset CurrentProcessStartedAt { get; }

    /// <summary>
    /// Whether a process with this identifier, started at this moment, is still
    /// running.
    /// </summary>
    /// <remarks>
    /// The start time is half the question rather than decoration. Process
    /// identifiers are reused, and on a machine that has been up for a while
    /// they are reused quickly, so an identifier alone would report somebody
    /// else's process as a session of ours that is still going. A record that
    /// confidently reports a dead session as live is worse than no record: it
    /// is the sort of instrument that has to be checked against a case whose
    /// answer is already known before anything is believed of it.
    /// </remarks>
    /// <param name="processId">The identifier that was recorded.</param>
    /// <param name="startedAt">When the process bearing it was recorded as starting.</param>
    bool IsRunning(int processId, DateTimeOffset startedAt);
}
