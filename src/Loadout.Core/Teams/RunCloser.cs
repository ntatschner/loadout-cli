using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Teams;

namespace Loadout.Core.Teams;

/// <summary>What closing a run wrote, or would write.</summary>
/// <param name="RunId">The run.</param>
/// <param name="Team">The team that ran it.</param>
/// <param name="Ended">The nodes it said had ended, which were still "working".</param>
/// <param name="Ending">The ending it wrote: "abandoned: " and the reason.</param>
/// <param name="Liveness">What the run looked like when it was asked about.</param>
public sealed record RunClosed(
    string RunId,
    string Team,
    IReadOnlyList<string> Ended,
    string Ending,
    Liveness Liveness);

/// <summary>
/// Ends a run whose driver has gone, so the journal says what is true.
/// </summary>
/// <remarks>
/// <para>
/// A run is going until its journal says it finished, and only its runner
/// writes that. A runner that was killed leaves a run that reads as going for
/// good, and remove, prune and resume all refuse it. This writes the finish
/// the runner never did, and only once nothing is driving the run.
/// </para>
/// <para>
/// It takes the run's lock before writing and holds it until it has, so a
/// resume starting at the same moment is shut out rather than interleaved.
/// For a run from before owners were kept, taking the lock is also what
/// creates its owner file.
/// </para>
/// </remarks>
public sealed class RunCloser
{
    private readonly IRunJournal _journal;
    private readonly IRunOwnership _ownership;
    private readonly TimeProvider _time;

    public RunCloser(IRunJournal journal, IRunOwnership ownership, TimeProvider time)
    {
        _journal = journal;
        _ownership = ownership;
        _time = time;
    }

    /// <summary>The run and whether anything is driving it, read and changed nothing.</summary>
    public OperationResult<(RunSummary Run, Liveness Liveness)> Judge(string runId)
    {
        var summary = _journal.Summarise(runId);

        if (summary.Failed)
        {
            return OperationResult<(RunSummary, Liveness)>.Fail(summary.Error!, summary.ExitCode);
        }

        var run = summary.Value!;
        var owner = _ownership.Read(run.Directory);

        return OperationResult<(RunSummary, Liveness)>.Ok(
            (run, RunLiveness.Of(run, owner, Written(run.Directory), _time.GetUtcNow())));
    }

    /// <summary>
    /// What closing would write, or why it would be refused, without writing it.
    /// </summary>
    /// <param name="runId">The run.</param>
    /// <param name="confirmed">Whether somebody agreed to close a run that may still be going.</param>
    /// <param name="reason">Why, for the ending.</param>
    public OperationResult<RunClosed> Preview(string runId, bool confirmed, string reason)
    {
        var judged = Judge(runId);

        if (judged.Failed)
        {
            return OperationResult<RunClosed>.Fail(judged.Error!, judged.ExitCode);
        }

        var (run, liveness) = judged.Value;

        return Refusal(run, liveness, confirmed)
            ?? OperationResult<RunClosed>.Ok(Closing(run, liveness, reason));
    }

    /// <summary>Ends the run, if nothing is driving it.</summary>
    /// <param name="runId">The run.</param>
    /// <param name="confirmed">
    /// Whether somebody agreed to close a run that has no owner record and
    /// may be a run from an older build still going.
    /// </param>
    /// <param name="reason">Why, written after "abandoned: " in the ending.</param>
    public OperationResult<RunClosed> Close(string runId, bool confirmed, string reason)
    {
        var judged = Judge(runId);

        if (judged.Failed)
        {
            return OperationResult<RunClosed>.Fail(judged.Error!, judged.ExitCode);
        }

        var (run, liveness) = judged.Value;

        if (Refusal(run, liveness, confirmed) is { } refused)
        {
            return refused;
        }

        var taken = _ownership.Take(run.Directory);

        if (taken.Failed)
        {
            return OperationResult<RunClosed>.Fail(
                $"Something took run {runId} back while it was being closed, so it was left alone. {taken.Error}",
                ExitCode.PolicyViolation);
        }

        using (taken.Value!)
        {
            // Read again under the lock: whatever was true a moment ago, this
            // is what will be written against.
            var again = _journal.Summarise(runId);

            if (again.Failed)
            {
                return OperationResult<RunClosed>.Fail(again.Error!, again.ExitCode);
            }

            run = again.Value!;

            if (!run.Running)
            {
                return Finished(run);
            }

            var closed = Closing(run, liveness, reason);
            var at = _time.GetUtcNow();
            var lines = new List<string>();

            foreach (var node in closed.Ended)
            {
                lines.Add(RunJournalLine.Serialize(at, run.RunId, node, "node.ended", new { exit = (int?)null, by = "close" }));
            }

            lines.Add(RunJournalLine.Serialize(at, run.RunId, null, "run.finished", new
            {
                ended = closed.Ending,
                outcome = RunOutcomes.Spell(RunOutcome.Abandoned),
                cost = run.CostUsd,
                rounds = run.Rounds,
            }));

            try
            {
                File.AppendAllText(
                    Path.Combine(run.Directory, "journal.jsonl"),
                    string.Concat(lines.Select(line => line + "\n")));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return OperationResult<RunClosed>.Fail($"Could not write the end of run {runId}: {ex.Message}");
            }

            return OperationResult<RunClosed>.Ok(closed);
        }
    }

    private static OperationResult<RunClosed>? Refusal(RunSummary run, Liveness liveness, bool confirmed) =>
        liveness switch
        {
            Liveness.Finished => Finished(run),
            Liveness.Live => OperationResult<RunClosed>.Fail(
                $"Run {run.RunId} is still going: a process on this machine is driving it. "
                + $"Stop it with 'loadout team halt {run.RunId}' instead.",
                ExitCode.PolicyViolation),
            Liveness.Unreadable unreadable => OperationResult<RunClosed>.Fail(
                $"Run {run.RunId} was left alone: its owner file "
                + $"{Path.Combine(run.Directory, FileRunOwnership.FileName)} could not be read ({unreadable.Why}), "
                + "so there is no telling whether a process still drives it. Put right whatever stops it being read and try again.",
                ExitCode.GeneralFailure),
            Liveness.PossiblyNotResponding quiet when !confirmed => OperationResult<RunClosed>.Fail(
                $"Run {run.RunId} has written nothing for {Quiet(quiet.Quiet)}, but it is from a build "
                + "that kept no owner, so it may still be going. Pass --yes to close it anyway.",
                ExitCode.InvalidArguments),
            _ => null,
        };

    private static OperationResult<RunClosed> Finished(RunSummary run) =>
        OperationResult<RunClosed>.Fail(
            $"Run {run.RunId} has already finished ({run.Ended}). There is nothing to close.",
            ExitCode.InvalidArguments);

    private static RunClosed Closing(RunSummary run, Liveness liveness, string reason) =>
        new(
            run.RunId,
            run.Team,
            [.. run.Nodes.Where(node => string.Equals(node.State, "working", StringComparison.Ordinal)).Select(node => node.Node)],
            "abandoned: " + reason,
            liveness);

    /// <summary>When the journal was last written, since the fold dates only node events.</summary>
    private static DateTimeOffset Written(string directory)
    {
        var path = Path.Combine(directory, "journal.jsonl");

        return File.Exists(path)
            ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)
            : DateTimeOffset.MinValue;
    }

    /// <summary>A quiet spell as somebody would say it.</summary>
    public static string Quiet(TimeSpan quiet) =>
        quiet.TotalDays >= 1 ? $"{(int)quiet.TotalDays} day(s)"
        : quiet.TotalHours >= 1 ? $"{(int)quiet.TotalHours} hour(s)"
        : $"{Math.Max(1, (int)quiet.TotalMinutes)} minute(s)";
}
