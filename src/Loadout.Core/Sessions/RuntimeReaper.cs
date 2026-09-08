using Loadout.Models.Results;

namespace Loadout.Core.Sessions;

/// <summary>
/// Removes per-launch runtime directories that no session is using any more.
/// </summary>
/// <remarks>
/// <para>
/// A launch writes its compiled context, its MCP configuration and its settings
/// into a directory of its own, and the launcher deletes that directory when
/// the agent exits. That covers every session that ends. It covers none of the
/// ones that do not: a killed agent, a closed terminal, a machine turned off.
/// Those leave the directory behind, and nothing has ever collected them —
/// twenty-seven of them going back three weeks on the machine this was written
/// on, each still holding the instructions and memory index that session was
/// given.
/// </para>
/// <para>
/// The whole difficulty is telling a directory nobody will come back for from
/// one a session is reading right now. Deleting a live session's context is a
/// far worse outcome than leaving a stale directory alone, so this errs the
/// other way twice over: nothing is removed unless it is older than a grace
/// period, and nothing is removed that is newer than the oldest session still
/// running.
/// </para>
/// <para>
/// The second guard is what makes this safe without recording which directory
/// belongs to which session. A session running since Tuesday protects
/// everything written since Tuesday, including its own, whether or not the
/// launcher that started it knew about any of this — which matters because the
/// sessions running when this ships were started by a launcher that did not.
/// </para>
/// </remarks>
public interface IRuntimeReaper
{
    /// <summary>
    /// Removes what can safely be removed, and says how many that was.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<OperationResult<int>> ReapAsync(CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class RuntimeReaper : IRuntimeReaper
{
    /// <summary>
    /// How long a directory is left alone regardless of anything else.
    /// </summary>
    /// <remarks>
    /// A launch creates its directory before it registers the session that
    /// holds it, and the two are milliseconds apart — but a reaper running in
    /// that gap on another launch would delete a context that is about to be
    /// read. A day costs nothing: these are leftovers measured in weeks.
    /// </remarks>
    private static readonly TimeSpan Grace = TimeSpan.FromDays(1);

    private readonly Platform.Abstractions.IPlatformPaths _paths;
    private readonly ISessionRegistry _sessions;
    private readonly TimeProvider _time;

    public RuntimeReaper(
        Platform.Abstractions.IPlatformPaths paths,
        ISessionRegistry sessions,
        TimeProvider time)
    {
        _paths = paths;
        _sessions = sessions;
        _time = time;
    }

    /// <inheritdoc />
    public async Task<OperationResult<int>> ReapAsync(CancellationToken ct = default)
    {
        var root = _paths.Paths.Runtime;

        if (!Directory.Exists(root))
        {
            return OperationResult<int>.Ok(0);
        }

        var running = await _sessions.ListAsync(ct).ConfigureAwait(false);

        // The registry answers who is alive, checking a recorded identifier
        // against the moment that process started, so a reused identifier
        // cannot pass for a session of ours.
        var cutoff = _time.GetUtcNow() - Grace;

        foreach (var session in running)
        {
            if (session.StartedAt < cutoff)
            {
                cutoff = session.StartedAt;
            }
        }

        var removed = 0;

        foreach (var directory in SafelyEnumerate(root))
        {
            ct.ThrowIfCancellationRequested();

            if (LastTouched(directory) is not { } touched || touched >= cutoff)
            {
                continue;
            }

            if (Remove(directory))
            {
                removed++;
            }
        }

        return OperationResult<int>.Ok(removed);
    }

    private static IEnumerable<string> SafelyEnumerate(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// When a directory was last written, or null when that cannot be read.
    /// </summary>
    /// <remarks>
    /// The written time rather than the name. The name carries a timestamp and
    /// parsing it would work, but a directory whose name does not parse would
    /// then be either skipped forever or deleted on a guess, and neither is a
    /// good answer to "I do not recognise this".
    /// </remarks>
    private static DateTimeOffset? LastTouched(string directory)
    {
        try
        {
            var written = Directory.GetLastWriteTimeUtc(directory);

            return written == default ? null : new DateTimeOffset(written, TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool Remove(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Held open, or not ours to delete. Leaving it is the same outcome
            // as before this existed, and failing a launch over housekeeping
            // would be a poor trade.
            return false;
        }
    }
}
