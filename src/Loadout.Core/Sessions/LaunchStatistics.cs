using Loadout.Models.Agents;

namespace Loadout.Core.Sessions;

/// <summary>How often one specialist was composed, and what it weighs now.</summary>
/// <param name="Id">The specialist.</param>
/// <param name="Launches">Launches it was composed into.</param>
/// <param name="TokensNow">
/// What it is estimated to cost as the library stands today, which is not
/// necessarily what it cost then. The ledger records what a whole composition
/// was estimated at rather than each part, so a per-specialist figure taken from
/// history would have to be invented. This one is measured, and is only a guide
/// to what dropping the specialist would save from here on.
/// </param>
public sealed record SpecialistUsage(string Id, int Launches, int TokensNow);

/// <summary>What was launched in one mode.</summary>
/// <param name="Mode">The mode, or "none" for launches that named none.</param>
/// <param name="Launches">How many launches took that posture.</param>
/// <param name="EstimatedTokens">
/// The context Loadout put in front of the agent, added up.
/// </param>
/// <remarks>
/// Not spend, and the distinction is the whole reason this is separate from
/// 'usage'. What the agents record is per day, per directory and per model, so
/// a day in which somebody reviewed and then implemented cannot be split
/// between the two — and a mode column in a spend report would be a number
/// somebody would act on and nothing could support. This counts what the
/// launcher itself decided, which it knows exactly.
/// </remarks>
public sealed record ModeUsage(string Mode, int Launches, long EstimatedTokens);

/// <summary>
/// What the ledger says about the specialist library.
/// </summary>
/// <remarks>
/// <para>
/// The question the library could never answer about itself: of the specialists
/// that ship, which ones does anybody's work actually reach? A specialist that
/// has never been composed is either wrong about when it applies or covers work
/// nobody here does, and both are worth knowing and neither is visible from the
/// file.
/// </para>
/// <para>
/// Every figure here comes from records of launches that happened. None of it is
/// modelled, projected or inferred from what a launch would compose today.
/// </para>
/// </remarks>
/// <param name="Launches">Launches in the window.</param>
/// <param name="NeverClosed">
/// Launches with no ending recorded. Not the same as sessions running now — a
/// killed session and a live one leave the same trace here, and the registry is
/// what tells them apart.
/// </param>
/// <param name="EstimatedTokens">What those launches' instructions were estimated at, added up.</param>
/// <param name="Loaded">Specialists that were composed, most often first.</param>
/// <param name="NeverLoaded">Specialists in the library that no launch reached.</param>
/// <param name="LibrarySize">How many specialists the library holds.</param>
/// <param name="Modes">
/// Launches grouped by the posture they took, with the context each was given.
/// Never spend: see <see cref="ModeUsage"/>.
/// </param>
public sealed record LaunchStatistics(
    int Launches,
    int NeverClosed,
    long EstimatedTokens,
    IReadOnlyList<SpecialistUsage> Loaded,
    IReadOnlyList<string> NeverLoaded,
    int LibrarySize,
    IReadOnlyList<ModeUsage>? Modes = null)
{
    /// <summary>Nothing recorded yet, which is the state on the day this ships.</summary>
    public static readonly LaunchStatistics Empty = new(0, 0, 0, [], [], 0, []);

    /// <summary>
    /// Adds up a window of launches against the library as it stands.
    /// </summary>
    /// <param name="records">Launches to count, in any order.</param>
    /// <param name="library">
    /// Every specialist that could have been composed, and what each is
    /// estimated at now. A specialist a launch recorded but the library no
    /// longer holds is still counted: it was composed, whatever has happened to
    /// it since, and dropping it would make the history quietly disagree with
    /// itself.
    /// </param>
    public static LaunchStatistics From(
        IReadOnlyList<LaunchRecord> records,
        IReadOnlyDictionary<string, int> library)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(library);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in records.SelectMany(record => record.Specialists.Distinct(
            StringComparer.OrdinalIgnoreCase)))
        {
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }

        var loaded = counts
            .Select(entry => new SpecialistUsage(
                entry.Key,
                entry.Value,
                library.GetValueOrDefault(entry.Key)))
            .OrderByDescending(usage => usage.Launches)
            .ThenBy(usage => usage.Id, StringComparer.Ordinal)
            .ToList();

        var neverLoaded = library.Keys
            .Where(id => !counts.ContainsKey(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return new LaunchStatistics(
            records.Count,
            records.Count(record => !record.IsComplete),
            records.Sum(record => (long)record.EstimatedTokens),
            loaded,
            neverLoaded,
            library.Count,
            ByMode(records));
    }

    /// <summary>Launches grouped by the posture they were started in.</summary>
    /// <remarks>
    /// Ordered by how much was launched rather than alphabetically, because the
    /// question this answers is which posture the work is actually done in, and
    /// the answer should be the first line.
    /// </remarks>
    private static IReadOnlyList<ModeUsage> ByMode(IReadOnlyList<LaunchRecord> records) =>
    [
        .. records
            .GroupBy(record => record.Mode is { Length: > 0 } mode ? mode : "none",
                StringComparer.Ordinal)
            .Select(group => new ModeUsage(
                group.Key,
                group.Count(),
                group.Sum(record => (long)record.EstimatedTokens)))
            .OrderByDescending(mode => mode.Launches)
            .ThenBy(mode => mode.Mode, StringComparer.Ordinal),
    ];
}
