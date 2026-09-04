using Loadout.Models.Instructions;

namespace Loadout.Core.Instructions;

/// <summary>One specialist that only one of two configurations composes.</summary>
/// <param name="Id">The specialist.</param>
/// <param name="Reason">Why the side that has it reached for it.</param>
/// <param name="EstimatedTokens">What it is estimated to cost.</param>
public sealed record InstructionChange(string Id, string Reason, int EstimatedTokens);

/// <summary>
/// What changes between two ways of asking the same question.
/// </summary>
/// <param name="Added">Composed by the second and not the first.</param>
/// <param name="Removed">Composed by the first and not the second.</param>
/// <param name="Kept">How many both had, which is usually most of them.</param>
/// <param name="TokensBefore">What the first was estimated at.</param>
/// <param name="TokensAfter">What the second was estimated at.</param>
public sealed record InstructionDiff(
    IReadOnlyList<InstructionChange> Added,
    IReadOnlyList<InstructionChange> Removed,
    int Kept,
    int TokensBefore,
    int TokensAfter)
{
    /// <summary>What the change costs, or saves when it is negative.</summary>
    public int TokenDelta => TokensAfter - TokensBefore;

    /// <summary>Whether the two configurations compose the same thing.</summary>
    public bool IsSame => Added.Count == 0 && Removed.Count == 0;

    /// <summary>
    /// The difference between two resolutions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>instructions explain</c> answers what one configuration loads. What it
    /// could not answer is what a change to that configuration costs, and
    /// reading two full listings side by side to work it out is exactly the sort
    /// of comparison a person does badly: forty lines are the same in both, and
    /// the three that differ are the whole question.
    /// </para>
    /// <para>
    /// Ordered by cost rather than by name. Somebody diffing configurations is
    /// usually trying to get under a budget, and the specialist worth looking at
    /// first is the expensive one.
    /// </para>
    /// </remarks>
    public static InstructionDiff Between(EffectiveInstructions before, EffectiveInstructions after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var first = before.Selected.ToDictionary(
            selection => selection.Specialist.Id, StringComparer.OrdinalIgnoreCase);

        var second = after.Selected.ToDictionary(
            selection => selection.Specialist.Id, StringComparer.OrdinalIgnoreCase);

        return new InstructionDiff(
            Changes(after.Selected.Where(selection => !first.ContainsKey(selection.Specialist.Id))),
            Changes(before.Selected.Where(selection => !second.ContainsKey(selection.Specialist.Id))),
            first.Keys.Count(id => second.ContainsKey(id)),
            before.Budget.EstimatedTokens,
            after.Budget.EstimatedTokens);
    }

    private static IReadOnlyList<InstructionChange> Changes(
        IEnumerable<SpecialistSelection> selections) =>
        selections
            .Select(selection => new InstructionChange(
                selection.Specialist.Id,
                selection.Reason,
                selection.Specialist.EstimatedTokens))
            .OrderByDescending(change => change.EstimatedTokens)
            .ThenBy(change => change.Id, StringComparer.Ordinal)
            .ToList();
}
