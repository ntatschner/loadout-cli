using System.Text;
using Loadout.Models;
using Loadout.Models.Results;

namespace Loadout.Core.Teams;

/// <summary>
/// Reads the runs on this machine that need a person to decide what happens
/// next, and writes down what they decided.
/// </summary>
/// <remarks>
/// <para>
/// The judging is <see cref="BlockedRuns"/>'s, which is pure. This is the part
/// that opens files: each run's journal, the lead's final report beside it, and
/// the runs before it on the same chain.
/// </para>
/// <para>
/// What was decided goes in the run's own journal, as a <c>run.resolved</c>
/// event, rather than in a store of its own. The journal is already the one
/// record of a run that survives a restart and travels with it into the bin
/// and back; a second record would be one more thing to keep in step.
/// </para>
/// </remarks>
public sealed class BlockedRunReader
{
    /// <summary>The most runs looked at, newest first.</summary>
    /// <remarks>
    /// Enough for weeks of runs on one machine. Every run is opened to judge
    /// it, so this is what keeps the section quick on a machine that has
    /// never pruned.
    /// </remarks>
    public const int Looked = 200;

    /// <summary>How far back a chain is followed before it is cut off.</summary>
    private const int Longest = 20;

    private readonly IRunJournal _journal;
    private readonly Func<RunSummary, bool> _dead;

    /// <param name="journal">Where the runs are.</param>
    /// <param name="dead">
    /// Whether a run its journal calls running has lost the process driving
    /// it. Nothing by default: such a run reads as still going until a check
    /// that can tell is given here.
    /// </param>
    public BlockedRunReader(IRunJournal journal, Func<RunSummary, bool>? dead = null)
    {
        _journal = journal;
        _dead = dead ?? (_ => false);
    }

    /// <summary>Every run on this machine waiting on a person's decision, newest first.</summary>
    /// <param name="project">Only this project's, or every project's for null.</param>
    public IReadOnlyList<BlockedRun> List(string? project = null)
    {
        var found = new List<BlockedRun>();

        foreach (var id in _journal.List(Looked))
        {
            if (_journal.Summarise(id) is not { Succeeded: true, Value: { } run })
            {
                continue;
            }

            if (project is { Length: > 0 } only && !string.Equals(run.Project, only, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Judge(run) is { } blocked)
            {
                found.Add(blocked);
            }
        }

        return found;
    }

    /// <summary>One run as the Blocked section shows it, or why it is not there.</summary>
    public OperationResult<BlockedRun> Find(string runId)
    {
        var summary = _journal.Summarise(runId);

        if (summary.Failed)
        {
            return OperationResult<BlockedRun>.Fail(summary.Error!, summary.ExitCode);
        }

        var run = summary.Value!;

        if (Judge(run) is { } blocked)
        {
            return OperationResult<BlockedRun>.Ok(blocked);
        }

        return OperationResult<BlockedRun>.Fail(NotBlocked(run), ExitCode.InvalidArguments);
    }

    /// <summary>Why a run is not one the Blocked section holds, worded to follow its name.</summary>
    public static string NotBlocked(RunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.Resolved is { } done)
        {
            return $"Run {run.RunId} was already dealt with: {done.Action}"
                + (done.By.Length > 0 ? $" by {done.By}" : string.Empty)
                + (done.Next is { Length: > 0 } next ? $", carried on as {next}" : string.Empty) + ".";
        }

        if (run.Running)
        {
            return $"Run {run.RunId} is still going.";
        }

        return RunOutcomes.Of(run) switch
        {
            Loadout.Models.Teams.RunOutcome.Done => $"Run {run.RunId} ended done, so there is nothing to decide.",
            Loadout.Models.Teams.RunOutcome.Stopped => $"Run {run.RunId} was stopped by a person, who already owns what happens next.",
            Loadout.Models.Teams.RunOutcome.NeedsDecision =>
                $"Run {run.RunId} is waiting on a decision; answer it with 'loadout team resume {run.RunId}'.",
            _ => $"Run {run.RunId} ended in a way the Blocked section does not hold: {run.Ended}.",
        };
    }

    /// <summary>The run as the Blocked section shows it, or null.</summary>
    public BlockedRun? Judge(RunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var dead = run.Running && _dead(run);

        if (BlockedRuns.KindOf(run, dead) is null)
        {
            return null;
        }

        return BlockedRuns.Judge(run, Lead(run), Before(run).Count + 1, dead);
    }

    /// <summary>The runs this one carries on from, oldest first, not including it.</summary>
    public IReadOnlyList<RunSummary> Before(RunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var chain = new List<RunSummary>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { run.RunId };
        var at = run;

        while (at.Follows is { Length: > 0 } previous && chain.Count < Longest && seen.Add(previous)
            && _journal.Summarise(previous) is { Succeeded: true, Value: { } earlier })
        {
            chain.Insert(0, earlier);
            at = earlier;
        }

        return chain;
    }

    /// <summary>
    /// Every run started for one task, as chains: each chain oldest first, the
    /// newest chain first.
    /// </summary>
    /// <remarks>
    /// Two runs are on one chain when the later says it follows the earlier.
    /// A run started for the task afresh begins a chain of its own.
    /// </remarks>
    public IReadOnlyList<IReadOnlyList<RunSummary>> ChainsFor(string project, string task)
    {
        var runs = _journal.List(Looked)
            .Select(_journal.Summarise)
            .Where(one => one.Succeeded)
            .Select(one => one.Value!)
            .Where(one => string.Equals(one.Task, task, StringComparison.OrdinalIgnoreCase)
                && string.Equals(one.Project, project, StringComparison.OrdinalIgnoreCase))
            .OrderBy(one => one.Started)
            .ToList();

        return Chains(runs);
    }

    /// <summary>Runs grouped into the chains their follows links make, each oldest first, newest chain first.</summary>
    public static IReadOnlyList<IReadOnlyList<RunSummary>> Chains(IReadOnlyList<RunSummary> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var byId = runs.ToDictionary(one => one.RunId, StringComparer.Ordinal);
        var next = runs
            .Where(one => one.Follows is { Length: > 0 } && byId.ContainsKey(one.Follows))
            .GroupBy(one => one.Follows!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(one => one.Started).First(), StringComparer.Ordinal);

        var chains = new List<IReadOnlyList<RunSummary>>();

        foreach (var head in runs.Where(one => one.Follows is not { Length: > 0 } || !byId.ContainsKey(one.Follows)))
        {
            var chain = new List<RunSummary> { head };
            var seen = new HashSet<string>(StringComparer.Ordinal) { head.RunId };

            while (next.TryGetValue(chain[^1].RunId, out var after) && seen.Add(after.RunId))
            {
                chain.Add(after);
            }

            chains.Add(chain);
        }

        return [.. chains.OrderByDescending(chain => chain[^1].Started)];
    }

    /// <summary>What the lead last said, from the final report beside its journal.</summary>
    public static BlockedLead? Lead(RunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var path = Path.Combine(run.Directory, "final-report.json");

        try
        {
            return File.Exists(path) && ReportReader.Read(File.ReadAllText(path)) is { Succeeded: true, Value: { } report }
                ? BlockedLead.From(report)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes what was decided into the run's journal, refusing when somebody
    /// already has.
    /// </summary>
    /// <param name="runId">The run.</param>
    /// <param name="resolution">What was decided.</param>
    /// <param name="again">
    /// Write it even though the run was dealt with already. For a run started
    /// to carry on from it, which names itself on the decision that started it.
    /// </param>
    /// <remarks>
    /// The journal is held shut while it is read and written, so two people
    /// deciding about one run at once cannot both be told they did.
    /// </remarks>
    public OperationResult Record(string runId, RunResolution resolution, bool again = false)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        if (!RunJournal.Names(runId))
        {
            return OperationResult.Fail($"'{runId}' is not a run identifier.", ExitCode.InvalidArguments);
        }

        var path = Path.Combine(_journal.DirectoryOf(runId), "journal.jsonl");

        if (!File.Exists(path))
        {
            return OperationResult.Fail($"No run named '{runId}' on this machine.", ExitCode.ProjectNotFound);
        }

        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            using var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

            var events = new List<RunEvent>();

            while (reader.ReadLine() is { } line)
            {
                if (RunJournal.Parse(line) is { } parsed)
                {
                    events.Add(parsed);
                }
            }

            var run = RunJournal.Fold(runId, _journal.DirectoryOf(runId), events);

            if (!again && run.Resolved is not null)
            {
                return OperationResult.Fail(NotBlocked(run), ExitCode.InvalidArguments);
            }

            // A journal whose last line was cut short gets its own line, so
            // this one is not glued to the end of it.
            var opening = file.Length > 0 && LastByte(file) != '\n' ? "\n" : string.Empty;

            var line2 = opening + RunJournalLine.Serialize(resolution.At, runId, null, "run.resolved", new
            {
                action = resolution.Action,
                by = resolution.By,
                answer = resolution.Answer,
                next = resolution.Next,
                cap = resolution.Cap,
                figure = resolution.Figure,
            }) + "\n";

            file.Seek(0, SeekOrigin.End);
            var bytes = Encoding.UTF8.GetBytes(line2);
            file.Write(bytes, 0, bytes.Length);
            file.Flush(flushToDisk: true);

            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"The run's journal could not be written: {ex.Message}");
        }
    }

    private static int LastByte(FileStream file)
    {
        file.Seek(-1, SeekOrigin.End);

        return file.ReadByte();
    }
}
