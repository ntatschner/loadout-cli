using Loadout.Core.Sessions.Hosting;
using Loadout.Core.Teams;

namespace Loadout.Core.Events;

/// <summary>
/// The daemon's side of the event history: looks every so often and writes
/// down what changed.
/// </summary>
/// <remarks>
/// <para>
/// On a loop of its own rather than the schedules' loop. That one waits for
/// each scheduled run to finish before it looks again, so anything on it is
/// blind for as long as a run takes, which is exactly when a run is most
/// likely to be asking something.
/// </para>
/// <para>
/// The only writer of the feed, so appends never interleave. Notices are
/// sent from the schedules' loop, so what they report comes in through
/// <see cref="Told"/> and is written under the same lock.
/// </para>
/// <para>
/// Nothing here is allowed to stop the daemon. A pass that cannot read or
/// write is skipped and the next one tries again: a history with a gap in it
/// is a smaller failure than runs nobody is watching.
/// </para>
/// </remarks>
public sealed class EventWatch
{
    /// <summary>How often it looks.</summary>
    /// <remarks>
    /// Twenty seconds: a question is on the page within a breath of being
    /// asked through the change stream, so this only decides how exactly the
    /// history dates it, and reading thirty journals three times a minute is
    /// what the notices already spend once a minute.
    /// </remarks>
    public static readonly TimeSpan Glance = TimeSpan.FromSeconds(20);

    private readonly EventStore _store;
    private readonly IRunJournal _journal;
    private readonly HostedSessions? _sessions;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();

    /// <summary>
    /// Finished runs, fully copied, by how their journal stood: not read
    /// again until it changes, which a run picked up again does.
    /// </summary>
    private readonly Dictionary<string, string> _settled = new(StringComparer.Ordinal);

    /// <summary>Notices that failed and have been recorded as failing, so a channel that is down is said once.</summary>
    private readonly HashSet<string> _failing = new(StringComparer.Ordinal);

    private RecorderState _state = RecorderState.Empty;

    public EventWatch(EventStore store, IRunJournal journal, HostedSessions? sessions, TimeProvider time)
    {
        _store = store;
        _journal = journal;
        _sessions = sessions;
        _time = time;
    }

    /// <summary>
    /// Prunes, reads back what the last daemon saw, and records that watching
    /// has started.
    /// </summary>
    /// <remarks>
    /// On the very first start everything the journals already hold is about
    /// to be copied in, a month of runs at once. It is marked read before it
    /// arrives, so the count does not open on a number that means nothing.
    /// </remarks>
    public void Start(int days)
    {
        var now = _time.GetUtcNow();

        Quietly(() =>
        {
            _store.Prune(now, days);

            if (!File.Exists(_store.MarksPath))
            {
                _store.MarkAllRead(now);
            }

            lock (_gate)
            {
                _state = _store.Recorded();
                _store.Append([EventRecorder.Daemon(started: true, now)]);
            }
        });
    }

    /// <summary>Looks until told to stop.</summary>
    public async Task WatchAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Look();

                await Task.Delay(Glance, _time, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping, which is how this ends.
        }
    }

    /// <summary>One look: what appeared, what cleared, and what the journals have said since.</summary>
    /// <returns>How many events it wrote.</returns>
    public int Look()
    {
        var written = 0;

        Quietly(() =>
        {
            var now = _time.GetUtcNow();
            var observed = new List<Observed>();
            var copied = new List<UserEvent>();
            var listed = _journal.List(EventRecorder.Runs);

            lock (_gate)
            {
                var journals = new Dictionary<string, int>(StringComparer.Ordinal);

                foreach (var id in listed)
                {
                    var seen = _state.Journals.TryGetValue(id, out var count) ? count : 0;
                    var stamp = Stamp(id);

                    if (_settled.TryGetValue(id, out var was) && was == stamp)
                    {
                        journals[id] = seen;

                        continue;
                    }

                    if (_journal.Summarise(id) is not { Succeeded: true, Value: { } run }
                        || _journal.Read(id) is not { Succeeded: true, Value: { } entries })
                    {
                        journals[id] = seen;

                        continue;
                    }

                    observed.AddRange(EventRecorder.Of(run, now));
                    copied.AddRange(EventRecorder.Copied(run, entries, seen));
                    journals[id] = entries.Count;

                    if (run.Running)
                    {
                        _settled.Remove(id);
                    }
                    else
                    {
                        _settled[id] = stamp;
                    }
                }

                var sessions = new List<string>();

                foreach (var session in _sessions?.All ?? [])
                {
                    var seen = new SessionSeen(
                        session.Id,
                        session.Name,
                        _sessions!.LaunchOf(session.Id)?.Project,
                        session.HasEnded ? null : _sessions.SignalOf(session).Needs);

                    sessions.Add(session.Id);

                    if (!_state.Sessions.Contains(session.Id, StringComparer.Ordinal))
                    {
                        copied.Add(EventRecorder.Started(seen, now));
                    }

                    if (EventRecorder.Of(seen) is { } needs)
                    {
                        observed.Add(needs);
                    }
                }

                var (events, state) = EventRecorder.Pass(_state, observed, copied, journals, sessions, now);
                var moved = events.Count > 0
                    || !journals.OrderBy(one => one.Key, StringComparer.Ordinal)
                        .SequenceEqual(_state.Journals.OrderBy(one => one.Key, StringComparer.Ordinal))
                    || !sessions.SequenceEqual(_state.Sessions, StringComparer.Ordinal);

                _store.Append(events);
                _state = state;

                if (moved)
                {
                    _store.Record(state);
                }

                written = events.Count;
            }
        });

        return written;
    }

    /// <summary>A notice went out somewhere else, or failed to.</summary>
    /// <remarks>
    /// A failure is said once while it keeps failing: the notices try again
    /// every minute, and a channel that is down for an hour is one problem,
    /// not sixty.
    /// </remarks>
    public void Told(string where, RunSummary run, Attention reason, bool landed)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(reason);

        var key = $"{run.RunId}/{reason.Kind}/{reason.About ?? reason.Detail}";

        // The notice's own title, which is what went out, or did not.
        var title = Teams.Daemon.Notifier.Title(run, reason);

        Quietly(() =>
        {
            lock (_gate)
            {
                if (landed)
                {
                    _failing.Remove(key);
                }
                else if (!_failing.Add(key))
                {
                    return;
                }

                _store.Append([EventRecorder.Notice(where, title, landed, run.Project, run.RunId, _time.GetUtcNow())]);
            }
        });
    }

    /// <summary>A last look, and the record that nothing is watched from here.</summary>
    public void Stop()
    {
        Look();

        Quietly(() =>
        {
            lock (_gate)
            {
                _store.Append([EventRecorder.Daemon(started: false, _time.GetUtcNow())]);
            }
        });
    }

    /// <summary>How a run's journal stands, as the change stream stamps it: size, time and the directory's time.</summary>
    private string Stamp(string runId)
    {
        var directory = _journal.DirectoryOf(runId);
        var journal = new FileInfo(Path.Combine(directory, "journal.jsonl"));

        return journal.Exists
            ? $"{journal.Length}|{journal.LastWriteTimeUtc.Ticks}|{Directory.GetLastWriteTimeUtc(directory).Ticks}"
            : "-";
    }

    private static void Quietly(Action work)
    {
        try
        {
            work();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tried again on the next look.
        }
    }
}
