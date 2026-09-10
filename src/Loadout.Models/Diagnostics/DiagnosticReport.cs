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

    /// <summary>
    /// Remove committed agent files from the index, leaving them on disk.
    /// Target is the repository path.
    /// </summary>
    UntrackAgentFiles,

    /// <summary>
    /// Delete a session marker no running session accounts for. Target is the
    /// marker's path.
    /// </summary>
    /// <remarks>
    /// A file rather than a state to repair, and deleting it is the whole fix.
    /// It exists so an installer can tell that closing the launcher would end
    /// somebody's session; one left behind by a launcher that was killed
    /// refuses every install until it goes.
    /// </remarks>
    ClearStaleSessionMarker,

    /// <summary>
    /// Put something the project already holds in front of its sessions. Target
    /// is the project and what to carry, written <c>slug=key</c>.
    /// </summary>
    /// <remarks>
    /// Two things in one string, which the other kinds do not need. They apply
    /// to a thing — a repository, a project, a marker — and this applies to a
    /// thing and which of its switches. Encoding both keeps <c>Remedy</c> the
    /// same shape for every kind, and the alternative is a field that means
    /// nothing to the other five.
    /// </remarks>
    CarryProjectContext,
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

    /// <summary>
    /// Something worth turning on that is not turned on, with the change that
    /// would do it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Info rather than a warning, and that is the whole point of it being its
    /// own factory. <c>Overall</c> is the worst severity present and decides
    /// the verdict, so one warning turns a machine where everything works into
    /// a DEGRADED one. Not having opted into an optional feature is not a
    /// degradation, and a report that says it is teaches people to skim past
    /// the warnings that matter.
    /// </para>
    /// <para>
    /// It still carries a remedy, because <c>Remedies</c> collects them at
    /// every severity. So a suggestion is offered by <c>--fix</c> exactly as a
    /// repair is, and inherits the same preview, the same single question and
    /// the same re-check afterwards.
    /// </para>
    /// </remarks>
    public static DiagnosticCheck Suggest(
        string category,
        string name,
        string detail,
        Remedy remedy) =>
        new(category, name, DiagnosticSeverity.Info, detail, remedy);

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

    /// <summary>
    /// Remedies for something that is actually wrong, as opposed to something
    /// optional that is switched off.
    /// </summary>
    /// <remarks>
    /// Split out so a report can offer both without calling both the same
    /// thing. "Put right" is the wrong verb for turning on a feature nobody
    /// asked for, and a count that mixes the two tells you neither how much is
    /// broken nor how much is available.
    /// </remarks>
    public IReadOnlyList<Remedy> Repairs =>
        Checks
            .Where(c => c.Severity != DiagnosticSeverity.Info)
            .Select(c => c.Remedy)
            .OfType<Remedy>()
            .DistinctBy(r => (r.Kind, r.Target))
            .ToList();

    /// <summary>Remedies for something optional that is available and off.</summary>
    public IReadOnlyList<Remedy> Suggestions =>
        Checks
            .Where(c => c.Severity == DiagnosticSeverity.Info)
            .Select(c => c.Remedy)
            .OfType<Remedy>()
            .DistinctBy(r => (r.Kind, r.Target))
            .Where(suggestion => !Repairs.Any(
                repair => repair.Kind == suggestion.Kind && repair.Target == suggestion.Target))
            .ToList();

    /// <summary>The single word printed at the end of the report.</summary>
    public string Verdict => Overall switch
    {
        DiagnosticSeverity.Error => "UNHEALTHY",
        DiagnosticSeverity.Warning => "DEGRADED",
        _ => "HEALTHY",
    };
}
