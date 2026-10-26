using System.Text;
using Loadout.Models;
using Loadout.Models.Results;

namespace Loadout.Core.Teams;

/// <summary>
/// Adds lines to an ended run's journal from outside the run, after reading it
/// under the same hold, so what is written is decided on what is there.
/// </summary>
/// <remarks>
/// The journal is held shut to other writers while it is read and written, so
/// two people acting on one run at once cannot both be told they did.
/// </remarks>
internal static class RunJournalAppend
{
    /// <summary>One line to add: its kind and what it carries.</summary>
    internal sealed record Line(string Kind, object Data);

    /// <param name="journal">Where the run is.</param>
    /// <param name="runId">The run.</param>
    /// <param name="at">When, for every line written.</param>
    /// <param name="decide">
    /// Given the run as its journal reads now, the lines to add or why not.
    /// </param>
    public static OperationResult<RunSummary> Append(
        IRunJournal journal,
        string runId,
        DateTimeOffset at,
        Func<RunSummary, OperationResult<IReadOnlyList<Line>>> decide)
    {
        if (!RunJournal.Names(runId))
        {
            return OperationResult<RunSummary>.Fail($"'{runId}' is not a run identifier.", ExitCode.InvalidArguments);
        }

        var directory = journal.DirectoryOf(runId);
        var path = Path.Combine(directory, "journal.jsonl");

        if (!File.Exists(path))
        {
            return OperationResult<RunSummary>.Fail($"No run named '{runId}' on this machine.", ExitCode.ProjectNotFound);
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

            var run = RunJournal.Fold(runId, directory, events);
            var decided = decide(run);

            if (decided.Failed)
            {
                return OperationResult<RunSummary>.Fail(decided.Error!, decided.ExitCode);
            }

            // A journal whose last line was cut short gets its own line, so
            // this one is not glued to the end of it.
            var text = new StringBuilder(file.Length > 0 && LastByte(file) != '\n' ? "\n" : string.Empty);

            foreach (var one in decided.Value!)
            {
                text.Append(RunJournalLine.Serialize(at, runId, null, one.Kind, one.Data)).Append('\n');
            }

            file.Seek(0, SeekOrigin.End);
            var bytes = Encoding.UTF8.GetBytes(text.ToString());
            file.Write(bytes, 0, bytes.Length);
            file.Flush(flushToDisk: true);

            return OperationResult<RunSummary>.Ok(run);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<RunSummary>.Fail($"The run's journal could not be written: {ex.Message}");
        }
    }

    private static int LastByte(FileStream file)
    {
        file.Seek(-1, SeekOrigin.End);

        return file.ReadByte();
    }
}
