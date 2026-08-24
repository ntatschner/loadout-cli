using Loadout.Core.Projects;
using Loadout.Models.Diagnostics;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Covers how a drift report is summarised and what it offers to fix.
/// <para>
/// The findings themselves come from services already covered elsewhere. What
/// is worth pinning down here is the judgement layered on top: which findings
/// count as drift, which of them the launcher will act on by itself, and — most
/// of all — which it deliberately will not.
/// </para>
/// </summary>
public sealed class DriftTests
{
    private static ProjectDrift Drift(params DiagnosticCheck[] findings) =>
        new("alpha", findings);

    [Fact]
    public void A_project_with_only_clean_findings_has_not_drifted()
    {
        var drift = Drift(
            DiagnosticCheck.Ok("alpha", "Remote", "https://example.com/alpha"),
            DiagnosticCheck.Ok("alpha", "Pre-commit protection", "installed"));

        drift.HasDrift.Should().BeFalse();
        drift.Remedies.Should().BeEmpty();
        drift.Overall.Should().Be(DiagnosticSeverity.Info);
    }

    [Fact]
    public void The_worst_finding_decides_the_verdict()
    {
        var drift = Drift(
            DiagnosticCheck.Ok("alpha", "Remote", "https://example.com/alpha"),
            DiagnosticCheck.Warn("alpha", "Memory", "1 topic outside the workspace"),
            DiagnosticCheck.Error("alpha", "Agent files", "1 committed"));

        // A committed agent file is a policy breach, and a report that averaged
        // it away with four passing checks would be worse than no report.
        drift.Overall.Should().Be(DiagnosticSeverity.Error);
        drift.HasDrift.Should().BeTrue();
    }

    [Fact]
    public void Only_findings_that_carry_a_fix_are_offered()
    {
        var drift = Drift(
            DiagnosticCheck.Error("alpha", "Agent files", "1 committed"),
            DiagnosticCheck.Warn(
                "alpha",
                "Pre-commit protection",
                "missing",
                new Remedy(RemedyKind.InstallPreCommitHook, "Install the hook", "/repo/alpha")),
            DiagnosticCheck.Warn("alpha", "Instruction budget", "102 KB always loaded"));

        // Untracking committed files rewrites the repository and splitting an
        // instruction layer is a judgement call. Neither is something to do to
        // somebody without asking, so neither carries a remedy.
        drift.Remedies.Should().ContainSingle()
            .Which.Kind.Should().Be(RemedyKind.InstallPreCommitHook);
    }

    [Fact]
    public void The_same_fix_reached_twice_is_offered_once()
    {
        var remedy = new Remedy(RemedyKind.ImportProjectMemory, "Import memory", "alpha");

        var drift = Drift(
            DiagnosticCheck.Warn("alpha", "Memory", "outside the workspace", remedy),
            DiagnosticCheck.Warn("alpha", "Memory again", "still outside", remedy));

        drift.Remedies.Should().ContainSingle();
    }

    [Theory]
    // The same repository, written the several ways different machines write it.
    [InlineData("https://github.com/n/alpha.git", "git@github.com:n/alpha.git")]
    [InlineData("https://github.com/n/alpha", "https://github.com/n/alpha.git")]
    [InlineData("ssh://git@github.com/n/alpha.git", "git@github.com:n/alpha")]
    [InlineData("https://GitHub.com/n/alpha.git", "https://github.com/n/alpha.git")]
    public void Equivalent_remotes_are_not_reported_as_drift(string recorded, string actual) =>
        Loadout.Core.Git.GitRemote.AreEquivalent(recorded, actual).Should().BeTrue();

    [Theory]
    [InlineData("https://github.com/n/alpha.git", "https://github.com/n/beta.git")]
    [InlineData("https://github.com/n/alpha.git", "https://gitlab.com/n/alpha.git")]
    [InlineData("https://github.com/n/alpha.git", "https://github.com/other/alpha.git")]
    public void Genuinely_different_remotes_are_reported(string recorded, string actual) =>
        Loadout.Core.Git.GitRemote.AreEquivalent(recorded, actual).Should().BeFalse();
}
