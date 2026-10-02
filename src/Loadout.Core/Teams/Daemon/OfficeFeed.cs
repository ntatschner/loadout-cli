namespace Loadout.Core.Teams.Daemon;

/// <summary>
/// What the office shows, read on one clock and handed to every page
/// listening, each time it changes.
/// </summary>
/// <remarks>
/// <para>
/// One reader however many pages are open. The run stream that already exists
/// re-reads its journal for every page holding it open; the office would be
/// that for every run on the machine, twice a second, per page. Here the
/// reading happens once and each page waits for the next version.
/// </para>
/// <para>
/// A whole snapshot each time rather than a change against the last, because
/// a page that reconnects then needs nothing but the latest message, and the
/// snapshot is a few kilobytes on a loopback connection. It is only read while
/// somebody is listening, and stops when the last page goes.
/// </para>
/// </remarks>
public sealed class OfficeFeed : IDisposable
{
    private readonly Func<string> _read;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stopping = new();

    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? _latest;
    private long _sequence;
    private int _listening;
    private Task? _loop;

    /// <summary>A feed over a snapshot, read no more often than the interval.</summary>
    /// <param name="read">Reads the snapshot as it stands now.</param>
    /// <param name="interval">How long to wait between reads.</param>
    public OfficeFeed(Func<string> read, TimeSpan interval)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _interval = interval;
    }

    /// <summary>How many pages are waiting on it, for tests and diagnostics.</summary>
    public int Listening => Volatile.Read(ref _listening);

    /// <summary>Whether its loop is still reading, for tests to wait on rather than guess.</summary>
    internal bool Looking
    {
        get
        {
            lock (_gate)
            {
                return _loop is not null;
            }
        }
    }

    /// <summary>
    /// The first snapshot newer than the one a page already has.
    /// </summary>
    /// <param name="after">The sequence number the page last had; 0 for none.</param>
    /// <param name="ct">Ends the wait when the page goes.</param>
    /// <returns>The snapshot and its sequence number.</returns>
    public async Task<(long Sequence, string Snapshot)> NextAsync(long after, CancellationToken ct)
    {
        Interlocked.Increment(ref _listening);

        try
        {
            while (true)
            {
                Task waiting;

                lock (_gate)
                {
                    if (_latest is not null && _sequence > after)
                    {
                        return (_sequence, _latest);
                    }

                    // Started by the first page to ask, rather than with the
                    // server, so a dashboard nobody is looking at reads nothing.
                    if (_loop is null)
                    {
                        _loop = Task.Run(() => LoopAsync(_stopping.Token), CancellationToken.None);
                    }

                    waiting = _changed.Task;
                }

                await waiting.WaitAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _listening);
        }
    }

    /// <summary>Read once now, and tell whoever is waiting if it changed.</summary>
    /// <remarks>Public so a test can drive the feed without a clock.</remarks>
    public void Look()
    {
        string now;

        try
        {
            now = _read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A journal being written as it was read. The next look gets it.
            return;
        }

        TaskCompletionSource? tell = null;

        lock (_gate)
        {
            if (string.Equals(now, _latest, StringComparison.Ordinal))
            {
                return;
            }

            _latest = now;
            _sequence++;
            tell = _changed;
            _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        tell.TrySetResult();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        // One look straight away, so a page that has just connected is not
        // kept waiting a whole interval for its first picture.
        Look();

        // Carries on while anybody is listening. The last page going ends it,
        // after one more interval, and the next page to arrive starts it again.
        while (true)
        {
            try
            {
                await Task.Delay(_interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Look();

            // Decided under the same lock a new page takes to see whether a
            // loop is running. A page counts itself before it takes the lock,
            // so either this sees it and carries on, or it sees no loop and
            // starts one; never a loop that stops just after a page relied on it.
            lock (_gate)
            {
                if (Listening == 0)
                {
                    _loop = null;

                    return;
                }
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }
}
