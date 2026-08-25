using Loadout.Core.Projects;
using Loadout.Models.Projects;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Where the line sits between "worth fixing" and "cannot work".
/// <para>
/// That line is the whole value of the state. Promoting every warning to
/// Blocked would make a list where everything is blocked, which says no more
/// than a list with no states at all — and trains people to ignore the one
/// project that really is.
/// </para>
/// </summary>
public sealed class ProjectReadinessTests
{
    private static ProjectOverview Overview(
        bool guarded = true,
        int trackedAgentFiles = 0,
        int pendingImports = 0,
        long bytes = 4096)
    {
        var project = new ProjectResolution(
            new ProjectRegistryEntry { Slug = "alpha", Name = "Alpha" }, "/repos/alpha", null, 0, false);

        return new ProjectOverview(
            project, "main", true, bytes, 3, 2, pendingImports, guarded, trackedAgentFiles);
    }

    [Fact]
    public void A_project_with_nothing_wrong_is_ready()
    {
        ProjectReadinessRules.Of(Overview(), isAvailableLocally: true, agentInstalled: true)
            .Should().Be(Readiness.Ready);
    }

    [Fact]
    public void A_repository_that_is_not_here_is_blocked()
    {
        // Genuinely stops a launch: there is nothing to launch against.
        ProjectReadinessRules.Of(Overview(), isAvailableLocally: false, agentInstalled: true)
            .Should().Be(Readiness.Blocked);
    }

    [Fact]
    public void An_agent_that_is_not_installed_blocks_the_project()
    {
        ProjectReadinessRules.Of(Overview(), isAvailableLocally: true, agentInstalled: false)
            .Should().Be(Readiness.Blocked);
    }

    [Theory]
    [InlineData(false, 0, 0, 4096)]      // no pre-commit protection
    [InlineData(true, 3, 0, 4096)]       // agent files committed
    [InlineData(true, 0, 2, 4096)]       // memory recorded outside the workspace
    [InlineData(true, 0, 0, 40000)]      // instructions over budget
    public void Everything_else_needs_attention_and_does_not_block(
        bool guarded, int tracked, int pending, long bytes)
    {
        // Each of these is worth fixing and none of them stops somebody
        // working. Calling any of them Blocked would be crying wolf.
        ProjectReadinessRules
            .Of(Overview(guarded, tracked, pending, bytes), true, true)
            .Should().Be(Readiness.NeedsAttention);
    }

    [Fact]
    public void A_project_whose_details_could_not_be_read_is_not_called_ready()
    {
        // Not a clean bill of health, and not a reason to refuse either.
        ProjectReadinessRules.Of(null, isAvailableLocally: true, agentInstalled: true)
            .Should().Be(Readiness.NeedsAttention);
    }

    [Theory]
    [InlineData(Readiness.Ready)]
    [InlineData(Readiness.NeedsAttention)]
    [InlineData(Readiness.Blocked)]
    [InlineData(Readiness.Unsupported)]
    public void Every_state_reads_without_colour(Readiness readiness)
    {
        // A monochrome terminal, and somebody who cannot tell red from green,
        // must get the same information as everybody else.
        ProjectReadinessRules.Label(readiness).Should().NotBeNullOrWhiteSpace();
        ProjectReadinessRules.Mark(readiness).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Being_blocked_says_which_of_the_two_reasons_it_is()
    {
        ProjectReadinessRules.Because(Readiness.Blocked, false, true)
            .Should().Contain("not on this machine");

        ProjectReadinessRules.Because(Readiness.Blocked, true, false)
            .Should().Contain("agent is not installed");
    }

    [Fact]
    public void A_project_that_has_not_been_read_yet_is_unknown_rather_than_a_warning()
    {
        ProjectReadinessRules.Provisional(isAvailableLocally: true, agentInstalled: true)
            .Should().Be(Readiness.Unknown);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void What_stops_a_launch_is_known_without_reading_the_repository(
        bool isAvailableLocally,
        bool agentInstalled)
    {
        // Both are answered by the registry alone, so a list can say what it
        // cannot start the moment it is drawn rather than once every project
        // has been visited.
        ProjectReadinessRules.Provisional(isAvailableLocally, agentInstalled)
            .Should().Be(Readiness.Blocked);
    }

    [Fact]
    public void Unknown_says_nothing_at_all_rather_than_something_neutral()
    {
        ProjectReadinessRules.Mark(Readiness.Unknown).Should().BeEmpty();
        ProjectReadinessRules.Label(Readiness.Unknown).Should().BeEmpty();
    }
}
