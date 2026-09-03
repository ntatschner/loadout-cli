namespace Loadout.Models.Agents;

/// <summary>
/// One launch, as it was set up and as it ended.
/// </summary>
/// <remarks>
/// <para>
/// The launcher knew all of this at the moment it started an agent and then
/// threw it away. Sessions and token counts are both reconstructed afterwards
/// from the transcripts the agents write, which say what a session cost but not
/// what it was given, so the two questions that matter most about the specialist
/// library — which of them are ever chosen, and what they cost when they are —
/// had no answer at all.
/// </para>
/// <para>
/// Nothing here can be backfilled. A launch that was not recorded is gone, which
/// is the argument for writing the record before there is anything to read it.
/// </para>
/// </remarks>
/// <param name="Id">Identifies this launch, and joins its start to its end.</param>
/// <param name="StartedAt">When the agent was started.</param>
/// <param name="ProjectSlug">The project, by the name the registry knows it by.</param>
/// <param name="ProjectName">The project as a person calls it.</param>
/// <param name="Agent">Adapter that ran.</param>
/// <param name="Mode">Posture the session took, or null when no specialists were in play.</param>
/// <param name="Task">
/// What the user said they were doing. Recorded because it is the strongest
/// signal behind specialist selection, so without it a record cannot explain its
/// own contents.
/// </param>
/// <param name="TaskWithheld">
/// The name of the credential pattern that matched the task, when one did. The
/// task is then not recorded at all. A pattern name says enough to explain the
/// gap and nothing that would copy the credential into a second file.
/// </param>
/// <param name="Profile">Context profile applied, or null for the base context.</param>
/// <param name="Worktree">Working tree launched into, or null for the main one.</param>
/// <param name="Specialists">Identifiers of the specialists composed, in composition order.</param>
/// <param name="EstimatedTokens">What the composed instructions were estimated to cost.</param>
/// <param name="TokenBudget">The ceiling in force, or 0 when none was set.</param>
/// <param name="EndedAt">When the agent exited, or null if this launch never closed.</param>
/// <param name="ExitCode">
/// The agent's own exit status. Null covers two different things and the record
/// says which by whether <see cref="EndedAt"/> is set: a launch still open, and
/// one that closed without the agent ever running.
/// </param>
public sealed record LaunchRecord(
    string Id,
    DateTimeOffset StartedAt,
    string ProjectSlug,
    string ProjectName,
    string Agent,
    string? Mode,
    string? Task,
    string? TaskWithheld,
    string? Profile,
    string? Worktree,
    IReadOnlyList<string> Specialists,
    int EstimatedTokens,
    int TokenBudget,
    DateTimeOffset? EndedAt = null,
    int? ExitCode = null)
{
    /// <summary>How long the session ran, or null while it is still open.</summary>
    public TimeSpan? Duration => EndedAt is { } ended ? ended - StartedAt : null;

    /// <summary>
    /// Whether this launch was seen to finish.
    /// </summary>
    /// <remarks>
    /// A record that never closed is not a fault in the ledger. The machine was
    /// shut down, the terminal was closed, the process was killed — all ordinary,
    /// and all of them leave a start with no end. Reporting has to cope with it
    /// rather than treat it as corruption.
    /// </remarks>
    public bool IsComplete => EndedAt is not null;
}
