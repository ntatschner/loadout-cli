using Loadout.Models.Diagnostics;
using Loadout.Models.Policies;

namespace Loadout.Core.Policies;

/// <summary>
/// Turns a repository policy report into the diagnostic findings everything
/// else in the launcher already speaks.
/// <para>
/// The policy check had its own result type and every consumer projected it
/// again, differently. The doctor summarised it into three findings and
/// attached no fix to the one that matters, so <c>loadout doctor --fix</c>
/// reported nine committed agent files as an error it could do nothing about
/// while <c>loadout drift --fix</c> offered to untrack the same nine. One
/// projection, in one place, is what stops the two answers drifting apart.
/// </para>
/// </summary>
public static class PolicyDiagnostics
{
    /// <summary>The category these findings are filed under.</summary>
    public const string Category = "Repository";

    /// <summary>
    /// Describes a policy report as diagnostic findings, fixes included.
    /// </summary>
    /// <param name="report">What the policy check found.</param>
    /// <param name="repository">
    /// The repository the findings belong to, carried on each remedy so a fix
    /// knows what to act on.
    /// </param>
    public static IReadOnlyList<DiagnosticCheck> Describe(PolicyReport report, string repository)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);

        var checks = new List<DiagnosticCheck>
        {
            report.Violations.Count == 0
                ? DiagnosticCheck.Ok(Category, "Agent files", "none tracked")
                : DiagnosticCheck.Error(
                    Category,
                    "Agent files",
                    $"{report.Violations.Count} tracked: "
                    + string.Join(", ", report.Violations.Take(5).Select(v => v.Path))
                    + (report.Violations.Count > 5 ? ", and more" : string.Empty),

                    // The fix the doctor was missing. Removing them from the
                    // index leaves every one on disk and does not touch
                    // history, so there is no reason to report this and then
                    // decline to act on it.
                    new Remedy(
                        RemedyKind.UntrackAgentFiles,
                        $"Take {report.Violations.Count} committed agent file(s) out of the index",
                        repository)),
        };

        if (report.Warnings.Count > 0)
        {
            // Untracked and unignored: one "git add ." away from becoming the
            // finding above. Worth saying, not worth calling the repository
            // non-compliant over, and the fix for it is the excludes rather
            // than the index.
            checks.Add(DiagnosticCheck.Warn(
                Category,
                "Untracked agent files",
                $"{report.Warnings.Count} present and not ignored: "
                + string.Join(", ", report.Warnings.Take(5).Select(w => w.Path))));
        }

        checks.Add(report.HasPreCommitHook && !report.HookNeedsUpgrade
            ? DiagnosticCheck.Ok(Category, "Pre-commit protection", "installed")
            : DiagnosticCheck.Warn(
                Category,
                "Pre-commit protection",
                report.HookNeedsUpgrade
                    ? "installed, but written by an older version of the launcher"
                    : "not installed in this clone; hooks are per-clone",
                new Remedy(
                    RemedyKind.InstallPreCommitHook,
                    "Install the pre-commit hook in this clone",
                    repository)));

        checks.Add(report.HasGlobalExcludes
            ? DiagnosticCheck.Ok(Category, "Global excludes", "configured")
            : DiagnosticCheck.Warn(
                Category,
                "Global excludes",
                "no global exclude file is configured, so agent files are only kept out of "
                + "repositories that ignore them individually",
                new Remedy(
                    RemedyKind.RepairGlobalExcludes,
                    "Write the global exclude file and point Git at it")));

        return checks;
    }
}
