using Loadout.Models.Teams;

namespace Loadout.Core.Teams;

/// <summary>One criterion a run handed to a person, and what they have to do.</summary>
/// <param name="Criterion">The criterion, in the run's words.</param>
/// <param name="Needs">What meeting it takes.</param>
/// <param name="ToDo">What the person has to do, in a sentence.</param>
/// <param name="By">Who tagged it: <c>lead</c> or <c>keyword</c>.</param>
public sealed record HandoverItem(string Criterion, CriterionNeeds Needs, string ToDo, string By)
{
    /// <summary>Who checked it by hand and said it is met, or null while nobody has.</summary>
    public string? CheckedBy { get; init; }

    /// <summary>When they did.</summary>
    public DateTimeOffset? CheckedAt { get; init; }

    /// <summary>Whether somebody has said it is met.</summary>
    public bool Checked => CheckedBy is not null;
}

/// <summary>What a run handed to a person when nothing left was something it could do.</summary>
/// <param name="Items">Each criterion handed over, in the run's order.</param>
/// <param name="Branches">Where the run's work is waiting.</param>
public sealed record RunHandover(IReadOnlyList<HandoverItem> Items, IReadOnlyList<string> Branches)
{
    /// <summary>Whether every criterion handed over has since been checked.</summary>
    public bool Settled => Items.Count > 0 && Items.All(one => one.Checked);

    /// <summary>The criteria still waiting on somebody.</summary>
    public IReadOnlyList<HandoverItem> Open => [.. Items.Where(one => !one.Checked)];
}

/// <summary>
/// When a run should stop and hand over rather than take another round, and
/// what it hands over.
/// </summary>
/// <remarks>
/// <para>
/// Pure, so the rule is a table test. A run with only criteria left that need
/// a push, CI or somebody's own check cannot get any closer by spending: run
/// 20261023-1740-2677 had its three local criteria met and two of that kind
/// left, stopped blocked, and was picked up twice more to find nothing to do,
/// the last time for $6.92.
/// </para>
/// <para>
/// The coordinator decides this rather than the lead, because a lead told to
/// report it would be one more instruction a model can miss, and the one run
/// that needed it did.
/// </para>
/// </remarks>
public static class RunHandovers
{
    /// <summary>How a handed-over run's ending begins, which is what it is known by.</summary>
    public const string EndedOpening = "needs-decision: handed over";

    /// <summary>
    /// What to hand over, or null where something left is the run's to do.
    /// </summary>
    /// <param name="outstanding">The criteria not met yet, in the run's order.</param>
    /// <param name="tags">What each criterion needs, by the criterion. One missing is tagged by the keyword rule.</param>
    /// <remarks>
    /// Null when nothing is outstanding, because that run is done, and when any
    /// outstanding criterion is local, because that one the run can still work
    /// on.
    /// </remarks>
    public static IReadOnlyList<HandoverItem>? Decide(
        IReadOnlyList<string> outstanding,
        IReadOnlyDictionary<string, CriterionTag> tags)
    {
        ArgumentNullException.ThrowIfNull(outstanding);
        ArgumentNullException.ThrowIfNull(tags);

        if (outstanding.Count == 0)
        {
            return null;
        }

        var items = new List<HandoverItem>();

        foreach (var criterion in outstanding)
        {
            var tag = tags.TryGetValue(criterion, out var said) ? said : CriterionTags.Of(criterion, null);

            if (tag.Local)
            {
                return null;
            }

            items.Add(new HandoverItem(criterion, tag.Needs, CriterionTags.ToDo(tag), tag.By));
        }

        return items;
    }

    /// <summary>The ending a handed-over run records.</summary>
    public static string Ended(IReadOnlyList<HandoverItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return $"{EndedOpening} {items.Count} criterion(s) the run cannot meet itself";
    }

    /// <summary>
    /// The handover of a run that ended by handing over and has since had
    /// every criterion in it checked by a person, or null.
    /// </summary>
    /// <remarks>
    /// Such a run is judged again without a lead turn when it is picked up:
    /// nothing in it is left for the lead to do.
    /// </remarks>
    public static RunHandover? Settled(RunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return !run.Running && IsHandover(run.Ended) && run.Handover is { Settled: true } settled ? settled : null;
    }

    /// <summary>Whether an ending is a handover.</summary>
    public static bool IsHandover(string? ended) =>
        ended?.Trim().StartsWith(EndedOpening, StringComparison.OrdinalIgnoreCase) == true;
}
