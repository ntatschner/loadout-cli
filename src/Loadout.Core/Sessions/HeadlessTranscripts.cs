using Loadout.Platform.Abstractions;

namespace Loadout.Core.Sessions;

/// <summary>
/// Moves the transcripts of headless conversations out of the agent's own
/// folder once they are over, and back again before one is resumed.
/// </summary>
public interface IHeadlessTranscripts
{
    /// <summary>
    /// Moves a finished conversation's transcript, and the folder of the same
    /// name beside it, into the launcher's own store.
    /// </summary>
    /// <returns>
    /// True when it was moved; false when there was nothing to move, the agent
    /// keeps no transcripts this knows how to move, or the file would not let
    /// go. Never throws: a transcript left where it was is a nuisance, not a
    /// failed run.
    /// </returns>
    Task<bool> PutAwayAsync(string agent, string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Puts a transcript moved by <see cref="PutAwayAsync"/> back where the
    /// agent looks for it, so the conversation can be resumed.
    /// </summary>
    /// <returns>True when it was put back; false when it was never moved.</returns>
    bool Restore(string agent, string sessionId);
}

/// <summary>
/// Claude Code's half of keeping headless conversations out of sight.
/// </summary>
/// <remarks>
/// <para>
/// Loadout's own lists leave headless sessions out by reading the ledger, but
/// Claude Code's <c>/resume</c> picker and <c>claude --resume</c> read its
/// projects folder directly, and the only way out of a list nobody here
/// draws is not to be in the folder it reads. So a finished node's
/// <c>&lt;id&gt;.jsonl</c>, and the <c>&lt;id&gt;/</c> folder of subagent
/// transcripts and tool results beside it, are moved to
/// <c>headless/transcripts/&lt;project folder&gt;/</c> under the state
/// directory, keeping the name of the folder they came from so that putting
/// one back puts it exactly where it was.
/// </para>
/// <para>
/// Found by identifier one level under the projects folder rather than by
/// encoding the working directory into a folder name. The encoding is Claude's
/// and unpublished, a node in a worktree runs somewhere other than the
/// repository, and an identifier is unique on its own.
/// </para>
/// <para>
/// Only Claude, because only Claude has a headless protocol here. Another
/// agent's sessions are left alone and this reports that nothing was moved.
/// </para>
/// </remarks>
internal sealed class HeadlessTranscripts : IHeadlessTranscripts
{
    private const string Claude = "claude";

    /// <summary>
    /// How many times to try a move a file is still holding. On Windows an
    /// agent's handle can outlive its process by a moment.
    /// </summary>
    private const int Attempts = 5;

    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(200);

    private readonly IEnvironmentProvider _environment;
    private readonly string _store;

    public HeadlessTranscripts(IEnvironmentProvider environment, IPlatformPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _environment = environment;
        _store = Path.Combine(paths.Paths.State, "headless", "transcripts");
    }

    private string Projects => Agents.AgentHome.ClaudeProjects(_environment);

    /// <inheritdoc />
    public async Task<bool> PutAwayAsync(string agent, string sessionId, CancellationToken ct = default)
    {
        if (!Applies(agent, sessionId) || Find(Projects, sessionId) is not { } folder)
        {
            return false;
        }

        var into = Path.Combine(_store, Path.GetFileName(folder));

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                Directory.CreateDirectory(into);
                Move(folder, into, sessionId);

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == Attempts)
                {
                    return false;
                }

                await Task.Delay(Pause, ct).ConfigureAwait(false);
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool Restore(string agent, string sessionId)
    {
        if (!Applies(agent, sessionId) || Find(_store, sessionId) is not { } kept)
        {
            return false;
        }

        try
        {
            var into = Path.Combine(Projects, Path.GetFileName(kept));

            Directory.CreateDirectory(into);
            Move(kept, into, sessionId);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The agent will say it cannot find the conversation, which is the
            // truth and more use than an exception from here.
            return false;
        }
    }

    private static bool Applies(string agent, string sessionId) =>
        string.Equals(agent, Claude, StringComparison.OrdinalIgnoreCase)

        // An identifier is a file name here, so one that could climb out of
        // the folder it names is refused rather than followed.
        && sessionId is { Length: > 0 }
        && sessionId.IndexOfAny(['/', '\\']) < 0
        && sessionId is not ("." or "..");

    /// <summary>The folder one level under <paramref name="root"/> holding the session's transcript.</summary>
    private static string? Find(string root, string sessionId)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        try
        {
            return Directory
                .EnumerateDirectories(root)
                .FirstOrDefault(folder => File.Exists(Path.Combine(folder, sessionId + ".jsonl")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Moves the transcript and its folder from one project folder to another,
    /// replacing what is there. What is there is always the older copy: a
    /// conversation is only ever in one place at a time, and the one being
    /// moved is the one the agent last wrote.
    /// </summary>
    private static void Move(string from, string into, string sessionId)
    {
        var file = sessionId + ".jsonl";

        // The folder first. If the file then refuses, a retry finds the
        // transcript where it was and moves the rest of it.
        var folder = Path.Combine(from, sessionId);

        if (Directory.Exists(folder))
        {
            var target = Path.Combine(into, sessionId);

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.Move(folder, target);
        }

        File.Move(Path.Combine(from, file), Path.Combine(into, file), overwrite: true);
    }
}
