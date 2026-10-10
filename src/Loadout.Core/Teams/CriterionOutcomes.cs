namespace Loadout.Core.Teams;

/// <summary>Where one of a run's criteria ended up, with everything that says why.</summary>
/// <param name="Criterion">The criterion, in the words the run gave it.</param>
/// <param name="Reading">
/// How the lead read it: its last stated reading, else what it said in its
/// verdict, else null for a run that never said.
/// </param>
/// <param name="Verdict">met, unmet or not attempted, in words; null where the lead never gave one.</param>
/// <param name="Because">What the lead said shows it.</param>
/// <param name="Source">Where it came from: project, team, lead or run.</param>
/// <param name="Delivered">What the lead cited that a worker actually handed back.</param>
/// <param name="Unfound">
/// Refs the lead cited that no worker reported. Shown rather than dropped,
/// because a lead citing work nobody handed back is exactly what somebody
/// reading the outcome should see.
/// </param>
public sealed record CriterionOutcome(
    string Criterion,
    string? Reading,
    string? Verdict,
    string? Because,
    string? Source,
    IReadOnlyList<Delivered> Delivered,
    IReadOnlyList<string> Unfound)
{
    /// <summary>Whether the lead reported it met.</summary>
    public bool Met => string.Equals(Verdict, "met", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// One summary per criterion of a run: what it said, how the lead read it,
/// the verdict, and what was delivered behind it.
/// </summary>
/// <remarks>
/// <para>
/// One model, rendered by <c>team run</c> when it ends, by <c>team status</c>
/// and on the run's page, so the three cannot disagree about what a run
/// achieved.
/// </para>
/// <para>
/// What the lead says was delivered is checked against what the workers'
/// reports say they handed back. The lead writes the coverage and can be wrong
/// about it; a ref only counts as delivered when a worker reported it.
/// </para>
/// </remarks>
public static class CriterionOutcomes
{
    /// <summary>The outcome of each criterion the run has a reading or a verdict for, in the run's order.</summary>
    /// <param name="run">The run, folded from its journal.</param>
    /// <param name="behind">What its nodes reported handing back.</param>
    public static IReadOnlyList<CriterionOutcome> Of(RunSummary run, LeftBehind behind)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(behind);

        // Matched as the lead repeats a string back: trimmed and past case. The
        // lead's own report is among the reports, and what it cites itself is
        // not a worker's delivery.
        var handedBack = behind.Delivered
            .Where(one => !string.Equals(one.Node, run.Nodes.FirstOrDefault()?.Node, StringComparison.Ordinal))
            .GroupBy(one => one.Ref.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var outcomes = new List<CriterionOutcome>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var covered in run.Coverage)
        {
            if (!seen.Add(covered.Criterion.Trim()))
            {
                continue;
            }

            var found = new List<Delivered>();
            var unfound = new List<string>();

            foreach (var cited in covered.Delivered)
            {
                if (handedBack.TryGetValue(cited.Trim(), out var delivered))
                {
                    found.Add(delivered);
                }
                else
                {
                    unfound.Add(cited);
                }
            }

            outcomes.Add(new CriterionOutcome(
                covered.Criterion,
                ReadingOf(run, covered.Criterion) ?? covered.Understood,
                covered.InWords,
                covered.Because,
                covered.Source,
                found,
                unfound));
        }

        // Read and never given a verdict: a run that stopped before the lead
        // said done. Its readings are still what it was working to.
        foreach (var (criterion, reading) in run.Readings)
        {
            if (seen.Add(criterion.Trim()))
            {
                outcomes.Add(new CriterionOutcome(criterion, reading, null, null, null, [], []));
            }
        }

        return outcomes;
    }

    private static string? ReadingOf(RunSummary run, string criterion) =>
        run.Readings.TryGetValue(criterion.Trim(), out var reading) ? reading : null;
}
