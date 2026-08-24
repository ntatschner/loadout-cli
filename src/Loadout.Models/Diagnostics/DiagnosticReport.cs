namespace Loadout.Models.Diagnostics;

/// <summary>Severity of a preflight or doctor finding (spec section 59).</summary>
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>Something the launcher can put right by itself.</summary>
public enum RemedyKind
{
    /// <summary>Install the per-clone pre-commit hook. Target is the repository path.</summary>
    InstallPreCommitHook,

    /// <summary>Write the global exclude file and point Git at it. No target.</summary>
    RepairGlobalExcludes,

    /// <summary>Bring machine-local agent memory into the workspace. Target is the project slug.</summary>
    ImportProjectMemory,
}

/// <summary>
/// A fix attached to a finding.
/// <para>
/// Named rather than carried as a delegate because this lives in the models
/// layer, which holds no logic: the check says what would fix it, and the
/// remediation service in core knows how. That also keeps every fix a fixed,
/// reviewable set rather than arbitrary code arriving from a contributor.
/// </para>
/// </summary>
/// <param name="Kind">Which fix applies.</param>
/// <param name="Description">What it will do, in the words shown before it is agreed to.</param>
/// <param name="Target">What it applies to, when the fix needs one.</param>
public sealed record Remedy(RemedyKind Kind, string Description, string? Target = null);

/// <summary>One diagnostic finding.</summary>
/// <param name="Category">Grouping shown as a heading, e.g. <c>Git</c> or <c>Agents</c>.</param>
/// <param name="Name">What was checked.</param>
/// <param name="Severity">How badly it failed, if it did.</param>
/// <param name="Detail">What was found. Redacted before display.</param>
/// <param name="Remedy">
/// How to put it right, when the launcher can do it itself. Absent on findings
/// that need a person to decide something.
/// </param>
public sealed record DiagnosticCheck(
    string Category,
    string Name,
    DiagnosticSeverity Severity,
    string Detail,
    Remedy? Remedy = null)
{
    public static DiagnosticCheck Ok(string category, string name, string detail) =>
        new(category, name, DiagnosticSeverity.Info, detail);

    public static DiagnosticCheck Warn(
        string category,
        string name,
        string detail,
        Remedy? remedy = null) =>
        new(category, name, DiagnosticSeverity.Warning, detail, remedy);

    public static DiagnosticCheck Error(
        string category,
        string name,
        string detail,
        Remedy? remedy = null) =>
        new(category, name, DiagnosticSeverity.Error, detail, remedy);
}

/// <summary>The full result of <c>loadout doctor</c> (spec section 60).</summary>
public sealed record DiagnosticReport(IReadOnlyList<DiagnosticCheck> Checks)
{
    /// <summary>Worst severity present, which decides the overall verdict and the exit code.</summary>
    public DiagnosticSeverity Overall => Checks.Count == 0
        ? DiagnosticSeverity.Info
        : Checks.Max(c => c.Severity);

    /// <summary>
    /// Findings the launcher can put right itself, in the order they were
    /// found. Deduplicated by kind and target, because the same repository can
    /// be reported on by more than one check.
    /// </summary>
    public IReadOnlyList<Remedy> Remedies =>
        Checks
            .Select(c => c.Remedy)
            .OfType<Remedy>()
            .DistinctBy(r => (r.Kind, r.Target))
            .ToList();

    /// <summary>The single word printed at the end of the report.</summary>
    public string Verdict => Overall switch
    {
        DiagnosticSeverity.Error => "UNHEALTHY",
        DiagnosticSeverity.Warning => "DEGRADED",
        _ => "HEALTHY",
    };
}
