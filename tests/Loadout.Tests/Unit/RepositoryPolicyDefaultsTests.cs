using Loadout.Models.Policies;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What the default policy considers agent tooling.
/// <para>
/// A name missing from this list is the worst way for this tool to be wrong.
/// It does not fail: it reports "No agent tooling files are tracked" and calls
/// the repository compliant, which is a clean bill of health for exactly the
/// thing the launcher exists to prevent. That happened with .serena, in a
/// repository that had .serena/project.yml committed.
/// </para>
/// </summary>
public sealed class RepositoryPolicyDefaultsTests
{
    private static bool Forbids(string pattern) =>
        RepositoryPolicy.CreateDefault().Forbidden.Contains(pattern, StringComparer.Ordinal);

    [Theory]
    [InlineData(".claude/**")]
    [InlineData(".codex/**")]
    [InlineData(".cursor/**")]
    [InlineData(".windsurf/**")]
    [InlineData(".continue/**")]
    [InlineData(".roo/**")]
    [InlineData(".serena/**")]
    [InlineData(".ai/**")]
    [InlineData(".agent/**")]
    [InlineData("CLAUDE.md")]
    public void The_agents_we_know_about_are_forbidden_by_default(string pattern)
    {
        Forbids(pattern).Should().BeTrue($"'{pattern}' is agent state and does not belong in an application repository");
    }

    [Fact]
    public void A_project_may_still_version_its_own_agent_instructions()
    {
        // AGENTS.md is named in the spec as the example of a file a project may
        // legitimately choose to version, so the default must not fight it.
        Forbids("AGENTS.md").Should().BeFalse();
    }

    [Fact]
    public void Nothing_is_exempted_by_default()
    {
        // Allowed is the escape hatch for a project that disagrees, and it
        // starts empty so the disagreement has to be deliberate.
        RepositoryPolicy.CreateDefault().Allowed.Should().BeEmpty();
    }
}
