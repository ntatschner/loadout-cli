using Loadout.Core.Teams;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Sessions;

/// <summary>
/// Catches up on the team nodes that ran before the launcher wrote their
/// conversations down.
/// </summary>
public interface IHeadlessBackfill
{
    /// <summary>Does the catching up, the first time it is asked on this machine.</summary>
    Task RunOnceAsync(CancellationToken ct = default);
}

/// <summary>
/// Reads every run journal still on the machine for the sessions its nodes
/// reported, writes each into the ledger as a headless session, and puts the
/// transcripts of runs that have ended where Claude's picker does not look.
/// </summary>
/// <remarks>
/// <para>
/// The ledger only learns of a node's conversation from the moment it was
/// taught to listen, so every run before that would have stayed in every
/// resume list until it aged out. The journals of those runs already name
/// each node's session on every turn it reported, so nothing has to be
/// guessed from what a conversation says.
/// </para>
/// <para>
/// Once per machine, marked by a file under the state directory: a second
/// pass would find nothing new, since everything since writes its own lines.
/// A pass interrupted before the mark is simply done again, and writing a
/// session down twice is harmless because the reader keeps a set.
/// </para>
/// <para>
/// Idea rounds and dump splits from before are not caught: nothing recorded
/// their sessions anywhere. Runs whose journals were pruned are not either.
/// </para>
/// </remarks>
internal sealed class HeadlessBackfill : IHeadlessBackfill
{
    /// <summary>The agent every node ran, because it is the only one that can run headless.</summary>
    private const string Agent = "claude";

    private readonly IRunJournal _journal;
    private readonly ILaunchLedger _ledger;
    private readonly IHeadlessTranscripts _transcripts;
    private readonly string _mark;
    private bool _done;

    public HeadlessBackfill(
        IRunJournal journal,
        ILaunchLedger ledger,
        IHeadlessTranscripts transcripts,
        IPlatformPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _journal = journal;
        _ledger = ledger;
        _transcripts = transcripts;
        _mark = Path.Combine(paths.Paths.State, "headless", "backfilled");
    }

    /// <inheritdoc />
    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        if (_done || File.Exists(_mark))
        {
            _done = true;

            return;
        }

        foreach (var run in _journal.List(int.MaxValue))
        {
            ct.ThrowIfCancellationRequested();

            var events = _journal.Read(run);

            if (events.Failed)
            {
                continue;
            }

            var sessions = events.Value!
                .Where(entry => entry.Kind == "node.turn" && entry.Node is { Length: > 0 })
                .Select(entry => entry.Text("session"))
                .OfType<string>()
                .Where(session => session.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (sessions.Count == 0)
            {
                continue;
            }

            // A run still going has nodes that may yet be resumed and are
            // certainly still writing. Their lines are written now, so the
            // lists drop them; the transcripts are left for the run to finish.
            var ended = _journal.Summarise(run) is { Succeeded: true } summary && !summary.Value!.Running;

            foreach (var session in sessions)
            {
                await _ledger.RecordHeadlessSessionAsync(session, Agent, $"node of run {run}", null, ct)
                    .ConfigureAwait(false);

                if (ended)
                {
                    await _transcripts.PutAwayAsync(Agent, session, ct).ConfigureAwait(false);
                }
            }
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_mark)!);
            await File.WriteAllTextAsync(_mark, "1", ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Done again next time, which finds the same sessions and costs a
            // second read of the journals.
        }

        _done = true;
    }
}
