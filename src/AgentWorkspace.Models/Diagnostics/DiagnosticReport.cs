namespace AgentWorkspace.Models.Diagnostics;

/// <summary>Severity of a preflight or doctor finding (spec section 59).</summary>
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>One diagnostic finding.</summary>
/// <param name="Category">Grouping shown as a heading, e.g. <c>Git</c> or <c>Agents</c>.</param>
/// <param name="Name">What was checked.</param>
/// <param name="Severity">How badly it failed, if it did.</param>
/// <param name="Detail">What was found. Redacted before display.</param>
public sealed record DiagnosticCheck(
    string Category,
    string Name,
    DiagnosticSeverity Severity,
    string Detail)
{
    public static DiagnosticCheck Ok(string category, string name, string detail) =>
        new(category, name, DiagnosticSeverity.Info, detail);

    public static DiagnosticCheck Warn(string category, string name, string detail) =>
        new(category, name, DiagnosticSeverity.Warning, detail);

    public static DiagnosticCheck Error(string category, string name, string detail) =>
        new(category, name, DiagnosticSeverity.Error, detail);
}

/// <summary>The full result of <c>agentctl doctor</c> (spec section 60).</summary>
public sealed record DiagnosticReport(IReadOnlyList<DiagnosticCheck> Checks)
{
    /// <summary>Worst severity present, which decides the overall verdict and the exit code.</summary>
    public DiagnosticSeverity Overall => Checks.Count == 0
        ? DiagnosticSeverity.Info
        : Checks.Max(c => c.Severity);

    /// <summary>The single word printed at the end of the report.</summary>
    public string Verdict => Overall switch
    {
        DiagnosticSeverity.Error => "UNHEALTHY",
        DiagnosticSeverity.Warning => "DEGRADED",
        _ => "HEALTHY",
    };
}
