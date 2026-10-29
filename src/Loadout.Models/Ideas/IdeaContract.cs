namespace Loadout.Models.Ideas;

/// <summary>One field of a plan, as the current contract asks for it.</summary>
/// <param name="Name">The field's name in <see cref="IdeaSchema.Version3"/>.</param>
/// <param name="Present">Whether a plan has it.</param>
/// <param name="Fillable">
/// Whether a backfill pass can write it into a plan that lacks it. A field
/// that is not is still listed, so a plan missing it is still told apart, and
/// is fixed by refining the idea.
/// </param>
public sealed record IdeaPlanField(string Name, Func<IdeaPlan, bool> Present, bool Fillable);

/// <summary>
/// What the current idea contract asks a plan to carry, field by field, so a
/// plan proposed under an earlier one can be found and brought up to date.
/// </summary>
/// <remarks>
/// <para>
/// A second statement of what <see cref="IdeaSchema.Version3"/> says, kept
/// beside it on purpose: reading the schema string to learn which fields
/// matter would be cheap to get wrong and dear to run on every list. A test
/// reads the schema's plan properties and fails when one is not here, so a
/// new contract field cannot be added without saying how to check for it.
/// </para>
/// <para>
/// <c>additions</c> may rightly be empty and <c>project</c> is always written,
/// so neither is ever missing; they are here for the test's sake and to say so.
/// </para>
/// </remarks>
public static class IdeaContract
{
    /// <summary>The name of the done-when field, the one fill a pass makes for both ideas and tasks.</summary>
    public const string DoneWhen = "doneWhen";

    /// <summary>Every plan field in the contract, in the schema's order.</summary>
    public static IReadOnlyList<IdeaPlanField> PlanFields { get; } =
    [
        new("title", plan => plan.Title.Trim().Length > 0, Fillable: false),
        new("layers", plan => plan.Layers.Count > 0, Fillable: false),
        new("additions", _ => true, Fillable: false),
        new("project", _ => true, Fillable: false),
        new(DoneWhen, plan => plan.DoneWhen.Any(one => one.Trim().Length > 0), Fillable: true),
    ];

    /// <summary>The fields the plan lacks, in the schema's order.</summary>
    public static IReadOnlyList<IdeaPlanField> Missing(IdeaPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return [.. PlanFields.Where(field => !field.Present(plan))];
    }
}
