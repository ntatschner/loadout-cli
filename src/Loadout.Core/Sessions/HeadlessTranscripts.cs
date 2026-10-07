using System.Text.Json;
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
/// <para>
/// Kept as long as Claude would have kept them. Claude deletes a transcript
/// once it has gone <c>cleanupPeriodDays</c> without being written to, thirty
/// by default, and one moved out of its folder is out of reach of that. So the
/// same rule is applied here, with Claude's own setting when there is one,
/// each time something is put away.
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

    /// <summary>Claude's own default for how long a transcript is kept.</summary>
    private const int DefaultKeepDays = 30;

    private readonly IEnvironmentProvider _environment;
    private readonly TimeProvider _time;
    private readonly string _store;

    public HeadlessTranscripts(IEnvironmentProvider environment, IPlatformPaths paths, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _environment = environment;
        _time = time;
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

        Prune();

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

    /// <summary>
    /// Deletes what Claude would have deleted by now: every transcript in the
    /// store not written to for its cleanup period, with its folder.
    /// </summary>
    /// <remarks>
    /// Never throws. One that cannot be deleted is tried again next time, and
    /// a store that is tidied late is a disk a little fuller, not a failure.
    /// </remarks>
    internal void Prune()
    {
        if (!Directory.Exists(_store))
        {
            return;
        }

        var cutoff = _time.GetUtcNow().UtcDateTime - TimeSpan.FromDays(KeepDays());

        try
        {
            // A project folder each, and the transcripts directly in it: the
            // subagent files below belong to one of those and go with it.
            foreach (var transcript in Directory.EnumerateDirectories(_store)
                .SelectMany(project => Directory.EnumerateFiles(project, "*.jsonl", SearchOption.TopDirectoryOnly))
                .ToList())
            {
                if (File.GetLastWriteTimeUtc(transcript) >= cutoff)
                {
                    continue;
                }

                try
                {
                    var folder = Path.Combine(
                        Path.GetDirectoryName(transcript)!,
                        Path.GetFileNameWithoutExtension(transcript));

                    if (Directory.Exists(folder))
                    {
                        Directory.Delete(folder, recursive: true);
                    }

                    File.Delete(transcript);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Tried again the next time something is put away.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The store could not be listed; nothing is lost by waiting.
        }
    }

    /// <summary>
    /// How many days Claude keeps a transcript on this machine: its own
    /// setting when one is written down, and its default otherwise.
    /// </summary>
    private int KeepDays()
    {
        try
        {
            var settings = Agents.AgentHome.ClaudeSettings(_environment);

            if (File.Exists(settings))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(settings));

                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("cleanupPeriodDays", out var days)
                    && days.TryGetInt32(out var kept)
                    && kept > 0)
                {
                    return kept;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // A settings file that cannot be read keeps Claude's default.
        }

        return DefaultKeepDays;
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

            MoveFolder(folder, target);
        }

        // Copies and deletes by itself when the two are on different volumes.
        File.Move(Path.Combine(from, file), Path.Combine(into, file), overwrite: true);
    }

    /// <summary>
    /// Moves a folder, across volumes as well as within one.
    /// </summary>
    /// <remarks>
    /// <see cref="Directory.Move"/> refuses to cross a volume, and the state
    /// directory and a moved <c>CLAUDE_CONFIG_DIR</c> can sit on different
    /// drives. Copying is the fallback only when the move could not have
    /// worked, so a folder that is merely held open on the same volume still
    /// fails and is retried rather than half-copied. On Windows that is told
    /// by the roots; elsewhere a mount point does not show in the path, so any
    /// failure to move is taken as the other volume and copied instead.
    /// </remarks>
    private static void MoveFolder(string from, string to)
    {
        try
        {
            Directory.Move(from, to);
        }
        catch (IOException) when (!OperatingSystem.IsWindows() || !SameRoot(from, to))
        {
            CopyThenDelete(from, to);
        }
    }

    private static bool SameRoot(string left, string right) => string.Equals(
        Path.GetPathRoot(Path.GetFullPath(left)),
        Path.GetPathRoot(Path.GetFullPath(right)),
        StringComparison.OrdinalIgnoreCase);

    /// <summary>Copies a folder and everything under it, then deletes the original.</summary>
    internal static void CopyThenDelete(string from, string to)
    {
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        }

        Directory.CreateDirectory(to);

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
        }

        Directory.Delete(from, recursive: true);
    }
}
