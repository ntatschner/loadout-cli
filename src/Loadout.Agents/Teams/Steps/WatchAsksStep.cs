using Loadout.Core.Teams;

namespace Loadout.Agents.Teams.Steps;

/// <summary>
/// Watches a run's directory for the permission questions its nodes cannot
/// answer themselves, for as long as the run lasts.
/// </summary>
internal sealed class WatchAsksStep
{
    private readonly TimeProvider _time;

    public WatchAsksStep(TimeProvider time)
    {
        _time = time;
    }

    /// <summary>
    /// Carries a node's unanswerable permission question up to the person, for
    /// as long as the run lasts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The node is stopped inside a turn this process is awaiting, so the
    /// question cannot come back up the way everything else does. It arrives as
    /// a file in the run's directory, written by the answerer — a different
    /// process, started by the agent — and the answer goes back the same way.
    /// </para>
    /// <para>
    /// Both the question and the answer are journalled, because "the run was
    /// allowed to do that" and "somebody allowed it" are different sentences to
    /// whoever reads it afterwards, and only one of them is true here.
    /// </para>
    /// </remarks>
    public async Task RunAsync(
        RunContext run,
        JournalWriter journal,
        string directory,
        ITeamConsole console,
        CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Questions noted and not yet answered, for the console that answers
        // into this directory. Kept rather than awaited: awaiting one held the
        // loop for the whole five minutes of somebody's patience, and every
        // other node's question went unnoted until it let go.
        var outstanding = new Dictionary<string, PendingAsk>(StringComparer.Ordinal);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                foreach (var ask in NodePermissions.Pending(directory))
                {
                    if (!seen.Add(ask.Id))
                    {
                        continue;
                    }

                    await journal
                        .WriteAsync("node.asked", ask.Node, new { tool = ask.Tool, target = ask.Target }, ct)
                        .ConfigureAwait(false);

                    if (console.AnswersInPlace)
                    {
                        // The question is already where this console reads it.
                        // Asking would put it in the same queue a second time,
                        // under a second id, and whoever answered one would
                        // leave the other on the list.
                        outstanding[ask.Id] = ask;
                        continue;
                    }

                    var answer = await console.PermitAsync(ask, ct).ConfigureAwait(false);

                    // Written down before the node is told, not after: the
                    // answer releases it, and a node that finished at once
                    // could end the run - and stop this watcher - before the
                    // entry was made, leaving an answer acted on and never
                    // recorded.
                    await journal
                        .WriteAsync(
                            "node.answered",
                            ask.Node,
                            new { tool = ask.Tool, allowed = answer.Allowed, chosen = answer.Chosen },
                            ct)
                        .ConfigureAwait(false);

                    await NodePermissions.AnswerAsync(directory, ask.Id, answer, ct).ConfigureAwait(false);
                }

                // Whatever has been answered since the last glance.
                await NoteAnsweredAsync(directory, outstanding, journal, ct).ConfigureAwait(false);

                await Task.Delay(NodePermissions.Glance, _time, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The run ended. Anything still waiting is answered by the
            // answerer's own patience running out, which says so.
        }

        // One last look, whatever stopped the watch: an answer given since the
        // last glance released a node that may have finished the run before
        // the next one, and what it acted on still goes down. Not cancellable,
        // because the run being over is exactly when this matters.
        await NoteAnsweredAsync(directory, outstanding, journal, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Journals every outstanding question somebody has answered in the run
    /// directory since the last look, and forgets it.
    /// </summary>
    /// <remarks>
    /// Pending stops returning a question once its answer is there, so what
    /// has gone from that list is what somebody decided - and it is their
    /// answer that gets journalled, not one made up here.
    /// </remarks>
    private static async Task NoteAnsweredAsync(
        string directory,
        Dictionary<string, PendingAsk> outstanding,
        JournalWriter journal,
        CancellationToken ct)
    {
        foreach (var (id, ask) in outstanding.ToList())
        {
            if (NodePermissions.Answered(directory, id) is not { } said)
            {
                continue;
            }

            outstanding.Remove(id);

            await journal
                .WriteAsync(
                    "node.answered",
                    ask.Node,
                    new { tool = ask.Tool, allowed = said.Allowed, reason = said.Reason, chosen = said.Chosen },
                    ct)
                .ConfigureAwait(false);
        }
    }
}

/// <summary>
/// A background watch that stops when the run leaves, however it leaves.
/// </summary>
/// <remarks>
/// Its own type because <see cref="TeamRunner.RunAsync"/> returns from six places, and
/// a cancel written at each of them is five places to forget it. Disposing
/// a token source does not cancel it, which is the trap this exists to
/// close.
/// </remarks>
internal sealed class Watching : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop;
    private readonly Task _task;

    private Watching(CancellationTokenSource stop, Task task)
    {
        _stop = stop;
        _task = task;
    }

    public static Watching Start(bool wanted, Func<CancellationToken, Task> watch, CancellationToken ct)
    {
        var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);

        return new Watching(stop, wanted ? watch(stop.Token) : Task.CompletedTask);
    }

    /// <summary>Stops the watch and waits for it to finish; again does nothing more.</summary>
    public async Task StopAsync()
    {
        if (!_stop.IsCancellationRequested)
        {
            await _stop.CancelAsync().ConfigureAwait(false);
        }

        try
        {
            await _task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);

        _stop.Dispose();
    }
}
