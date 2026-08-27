using Loadout.Models.Results;

namespace Loadout.Core.Usage;

/// <summary>
/// Token counts for one agent, in one directory, on one day, from one model.
/// </summary>
/// <remarks>
/// Aggregated this finely and no finer. Per-message rows would let somebody ask
/// anything at all, at the cost of holding every message in memory; these four
/// keys answer the questions worth asking — what did this project cost, what
/// did today cost, which model did the work — and collapse a hundred thousand
/// transcript lines into a few hundred rows.
/// </remarks>
/// <param name="Agent">Which agent spent it.</param>
/// <param name="Directory">Where the session was working, before any project attribution.</param>
/// <param name="Day">The day it was spent, in UTC.</param>
/// <param name="Model">The model that answered, or "unknown" when none was recorded.</param>
/// <param name="Totals">The counts themselves.</param>
public sealed record UsageBucket(
    string Agent,
    string Directory,
    DateOnly Day,
    string Model,
    UsageTotals Totals);

/// <summary>
/// Whether a set of totals can be believed.
/// </summary>
/// <remarks>
/// <para>
/// This exists because counting and listing want opposite things from a
/// malformed transcript. <see cref="Sessions.ISessionHistory"/> is deliberately
/// best-effort — one unreadable file must not cost somebody the other forty —
/// and for building a menu that is right.
/// </para>
/// <para>
/// For arithmetic it is a trap. Neither transcript format is a published
/// contract, and Claude Code's documentation says outright that it changes
/// between versions. A best-effort reader meeting a renamed field does not
/// fail: it counts zero and returns a total that looks entirely reasonable.
/// Nothing on screen would say otherwise, and a confident wrong number is worse
/// than no number.
/// </para>
/// <para>
/// So every skipped file and every unrecognised record is counted, and a report
/// that had to skip anything says so rather than quietly printing less than
/// happened.
/// </para>
/// </remarks>
/// <param name="FilesRead">Transcripts opened and understood.</param>
/// <param name="FilesSkipped">Transcripts that could not be opened or read at all.</param>
/// <param name="RecordsCounted">Accounting records that contributed to the totals.</param>
/// <param name="RecordsRepeated">
/// Records discarded as repeats of ones already counted. Expected, not a fault:
/// both agents write the same accounting more than once.
/// </param>
/// <param name="RecordsUnrecognised">
/// Records that carried usage but no field this build knows how to read. The
/// signal that a format has moved underneath us.
/// </param>
public sealed record UsageIntegrity(
    int FilesRead = 0,
    int FilesSkipped = 0,
    int RecordsCounted = 0,
    int RecordsRepeated = 0,
    int RecordsUnrecognised = 0)
{
    /// <summary>Nothing read yet.</summary>
    public static readonly UsageIntegrity Empty = new();

    /// <summary>Whether everything found was understood.</summary>
    public bool IsComplete => FilesSkipped == 0 && RecordsUnrecognised == 0;

    /// <summary>
    /// What to tell somebody when it is not complete, or null when it is.
    /// </summary>
    public string? Caveat
    {
        get
        {
            if (IsComplete)
            {
                return null;
            }

            var parts = new List<string>(2);

            if (RecordsUnrecognised > 0)
            {
                parts.Add(
                    $"{RecordsUnrecognised:N0} records carried usage this build could not read, "
                    + "which usually means an agent changed its transcript format");
            }

            if (FilesSkipped > 0)
            {
                parts.Add($"{FilesSkipped:N0} transcripts could not be read");
            }

            return "These totals are incomplete: " + string.Join("; ", parts) + ".";
        }
    }

    public static UsageIntegrity operator +(UsageIntegrity a, UsageIntegrity b) => new(
        a.FilesRead + b.FilesRead,
        a.FilesSkipped + b.FilesSkipped,
        a.RecordsCounted + b.RecordsCounted,
        a.RecordsRepeated + b.RecordsRepeated,
        a.RecordsUnrecognised + b.RecordsUnrecognised);

    /// <summary>Named alternative to the operator, which analysers ask for.</summary>
    public static UsageIntegrity Add(UsageIntegrity left, UsageIntegrity right) => left + right;
}

/// <summary>Everything one agent's history had to say, and how much of it was understood.</summary>
/// <param name="Buckets">The counts, grouped.</param>
/// <param name="Integrity">Whether to believe them.</param>
public sealed record UsageScan(IReadOnlyList<UsageBucket> Buckets, UsageIntegrity Integrity);

/// <summary>
/// Reads one agent's record of what it spent.
/// </summary>
public interface IUsageHistory
{
    /// <summary>The agent name, matching what the launcher calls it.</summary>
    string Agent { get; }

    /// <summary>True when this agent has left any accounting on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Every count this agent recorded on or after <paramref name="since"/>.
    /// </summary>
    /// <param name="since">Earliest day to include, in UTC.</param>
    /// <param name="ct">Cancels a scan of a large history.</param>
    Task<OperationResult<UsageScan>> ScanAsync(DateOnly since, CancellationToken ct = default);
}
