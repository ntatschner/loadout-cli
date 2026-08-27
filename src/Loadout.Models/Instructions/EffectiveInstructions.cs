namespace Loadout.Models.Instructions;

/// <summary>
/// Why a specialist was chosen.
/// </summary>
/// <remarks>
/// Ordered by how much authority the signal carries, strongest first. The
/// resolver uses that order twice: to decide which reason to report when
/// several apply, and to decide what to give up first when the budget is
/// exceeded. Something somebody asked for by name is never dropped in favour of
/// something guessed from a file extension.
/// </remarks>
public enum SpecialistTrigger
{
    /// <summary>A hard rule that loads whatever the task.</summary>
    Foundation,

    /// <summary>The posture chosen for this launch.</summary>
    Mode,

    /// <summary>Named by the user.</summary>
    Explicit,

    /// <summary>Required by another specialist that was selected.</summary>
    Required,

    /// <summary>Matched words in the task.</summary>
    TaskSemantics,

    /// <summary>Matched a declared dependency in the repository.</summary>
    Dependency,

    /// <summary>Preferred by the project or profile, and supported by other evidence.</summary>
    ProjectPreference,

    /// <summary>Matched files in the repository.</summary>
    RepositoryEvidence,
}

/// <summary>One specialist, and the reason it is or is not in the effective set.</summary>
/// <param name="Specialist">What was selected.</param>
/// <param name="Trigger">The strongest signal that reached it.</param>
/// <param name="Reason">
/// That signal in a sentence somebody can check, such as
/// "Npgsql dependency detected". A reason nobody can verify is not an
/// explanation.
/// </param>
/// <param name="Confidence">
/// How strongly the evidence points here, from 0 to 100. Used only to order
/// what is dropped when the budget is short, never to rank what is shown.
/// </param>
public sealed record SpecialistSelection(
    SpecialistDocument Specialist,
    SpecialistTrigger Trigger,
    string Reason,
    int Confidence)
{
    /// <summary>
    /// Whether this may be dropped to fit the budget.
    /// </summary>
    /// <remarks>
    /// Foundation carries the safety rules and mode carries the posture, so
    /// neither is negotiable. Something named explicitly is not either: a user
    /// who asked for the security specialist and silently did not get it has
    /// been told something untrue about their own session.
    /// </remarks>
    public bool IsNegotiable =>
        Trigger is not (SpecialistTrigger.Foundation
            or SpecialistTrigger.Mode
            or SpecialistTrigger.Explicit);
}

/// <summary>
/// Two instructions that bear on the same subject, and which one won.
/// </summary>
/// <remarks>
/// Reported rather than resolved quietly. Overlap between specialists is
/// expected and harmless — C# and .NET will both mention async — but where the
/// narrower source deliberately contradicts the wider one, somebody reading the
/// compiled context should be able to see that it happened and why.
/// </remarks>
/// <param name="Subject">What both instructions are about.</param>
/// <param name="WinnerId">The specialist whose guidance stands.</param>
/// <param name="LoserId">The specialist that was overridden.</param>
/// <param name="Reason">Why that way round.</param>
public sealed record InstructionConflict(
    string Subject,
    string WinnerId,
    string LoserId,
    string Reason);

/// <summary>
/// What the composed instructions cost, against what was allowed.
/// </summary>
/// <param name="Bytes">Size of everything selected, which is the exact measure.</param>
/// <param name="EstimatedTokens">The same in approximate tokens.</param>
/// <param name="TokenBudget">The ceiling, or 0 when none is set.</param>
/// <param name="WarnAtPercent">Share of the budget above which it is worth saying so.</param>
public sealed record InstructionContextBudget(
    long Bytes,
    int EstimatedTokens,
    int TokenBudget,
    int WarnAtPercent)
{
    /// <summary>How much of the budget is spent, or null when there is no budget.</summary>
    public double? UsedFraction =>
        TokenBudget > 0 ? (double)EstimatedTokens / TokenBudget : null;

    /// <summary>Whether the budget is exceeded.</summary>
    public bool IsOverBudget => TokenBudget > 0 && EstimatedTokens > TokenBudget;

    /// <summary>Whether it is close enough to the ceiling to be worth mentioning.</summary>
    public bool IsNearBudget =>
        UsedFraction is { } used && used * 100 >= WarnAtPercent;
}

/// <summary>
/// Everything an agent will be told for one task, and why.
/// </summary>
/// <remarks>
/// The answer to "why did Loadout give this agent these instructions". Every
/// field exists so that question can be answered without reading the compiled
/// file: what was chosen, what was considered and passed over, what contradicted
/// what, and what it all cost.
/// </remarks>
/// <param name="Mode">The posture chosen.</param>
/// <param name="Selected">What will be composed, in composition order.</param>
/// <param name="Omitted">
/// Candidates that were considered and left out, with the reason they were
/// reached and the reason they were dropped. Shown rather than discarded: a
/// specialist that was nearly relevant is exactly what somebody wants to know
/// about when the answer disappoints them.
/// </param>
/// <param name="Conflicts">Contradictions found between selected specialists.</param>
/// <param name="Budget">What it costs.</param>
public sealed record EffectiveInstructions(
    string Mode,
    IReadOnlyList<SpecialistSelection> Selected,
    IReadOnlyList<SpecialistSelection> Omitted,
    IReadOnlyList<InstructionConflict> Conflicts,
    InstructionContextBudget Budget)
{
    public IEnumerable<SpecialistSelection> OfKind(SpecialistKind kind) =>
        Selected.Where(s => s.Specialist.Kind == kind);

    /// <summary>Specialists dropped to fit the budget, as opposed to never reached.</summary>
    public IEnumerable<SpecialistSelection> DroppedForBudget =>
        Omitted.Where(s => s.Reason.Contains("budget", StringComparison.OrdinalIgnoreCase));
}
