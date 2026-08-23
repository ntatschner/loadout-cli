namespace Loadout.Models.Instructions;

/// <summary>How serious an instruction finding is.</summary>
public enum RuleFindingSeverity
{
    Info,
    Warning,

    /// <summary>Something actively wrong, such as an instruction file that no longer exists.</summary>
    Error,
}

/// <summary>One thing worth saying about a project's instruction layer.</summary>
/// <param name="Rule">The rule it concerns, or null when it is about the core instructions.</param>
/// <param name="Severity">How serious it is.</param>
/// <param name="Kind">Short machine-readable category, for JSON consumers.</param>
/// <param name="Detail">What is wrong, in a sentence.</param>
public sealed record RuleFinding(
    string? Rule,
    RuleFindingSeverity Severity,
    string Kind,
    string Detail);

/// <summary>
/// The result of auditing the instruction layer.
/// <para>
/// The instruction layer is the one thing every session pays for before it has
/// read a line of code, and it decays in ways nobody notices: a rule that
/// declares globs and also declares itself always-apply looks scoped in the
/// listing and is not, and a line duplicated between the core file and a rule
/// is paid for twice while reading as emphasis.
/// </para>
/// </summary>
/// <param name="Slug">Project audited.</param>
/// <param name="Rules">Rules found.</param>
/// <param name="Findings">Everything worth reporting.</param>
/// <param name="Budget">What the layer costs.</param>
public sealed record RuleAudit(
    string Slug,
    IReadOnlyList<RuleDocument> Rules,
    IReadOnlyList<RuleFinding> Findings,
    InstructionBudget Budget)
{
    public IEnumerable<RuleFinding> Errors =>
        Findings.Where(f => f.Severity == RuleFindingSeverity.Error);

    public IEnumerable<RuleFinding> Warnings =>
        Findings.Where(f => f.Severity == RuleFindingSeverity.Warning);

    /// <summary>The word printed at the end of the report.</summary>
    public string Verdict => Errors.Any()
        ? "ACTION REQUIRED"
        : Warnings.Any() ? "NEEDS ATTENTION" : "HEALTHY";
}
