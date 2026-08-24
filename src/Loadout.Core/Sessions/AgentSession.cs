using Loadout.Models.Results;

namespace Loadout.Core.Sessions;

/// <summary>
/// A past agent conversation that can be picked up again.
/// </summary>
/// <param name="Agent">Which agent owns it, and therefore which one can resume it.</param>
/// <param name="SessionId">The identifier that agent resumes by.</param>
/// <param name="Title">A human-readable name, when the agent recorded one.</param>
/// <param name="Directory">Where the session was working.</param>
/// <param name="Branch">The branch it was on, when the agent recorded one.</param>
/// <param name="LastActive">When it was last written to, which is what recency means here.</param>
/// <param name="TranscriptPath">The file it was read from, so a person can go and look.</param>
/// <param name="ProjectSlug">The registered project it belongs to, filled in afterwards.</param>
public sealed record AgentSession(
    string Agent,
    string SessionId,
    string? Title,
    string Directory,
    string? Branch,
    DateTimeOffset LastActive,
    string TranscriptPath,
    string? ProjectSlug = null)
{
    /// <summary>
    /// What to show in a list: the recorded title, or the directory it ran in
    /// when there is none. Never the raw identifier — a UUID tells nobody
    /// which conversation it was.
    /// </summary>
    public string Label => Title is { Length: > 0 }
        ? Title
        : Path.GetFileName(Directory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar)) is { Length: > 0 } folder
            ? folder
            : Directory;
}

/// <summary>
/// Reads one agent's session history.
/// <para>
/// Each agent stores its own conversations in its own layout, and neither
/// format is a published contract — they are read here as the artefacts they
/// are. That makes every implementation best-effort by definition: a file it
/// cannot understand is skipped rather than failing the listing, because one
/// unreadable transcript must not cost somebody the other forty.
/// </para>
/// </summary>
public interface ISessionHistory
{
    /// <summary>The agent name, matching what the launcher calls it.</summary>
    string Agent { get; }

    /// <summary>True when this agent stores history on this machine at all.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// The most recent sessions, newest first.
    /// </summary>
    /// <param name="limit">How many to return; readers stop early rather than parsing everything.</param>
    /// <param name="ct">Cancels a scan of a large history.</param>
    Task<OperationResult<IReadOnlyList<AgentSession>>> ListAsync(
        int limit,
        CancellationToken ct = default);
}
