using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What this machine lets a team allow its nodes.
/// </summary>
/// <remarks>
/// <para>
/// A team file lives in a workspace anybody on the team can push to, and one of
/// the teams that ships asks to push a tag in an autonomous run. Until this
/// existed, that list went straight into the node's brief on any machine: the
/// file asked and nothing answered.
/// </para>
/// <para>
/// So the whole of the decision is here, as a function over two lists, and the
/// case worth being certain about is the empty one — a machine that has said
/// nothing has granted nothing.
/// </para>
/// </remarks>
public sealed class TeamCeilingTests
{
    [Fact]
    public void A_machine_that_has_agreed_to_nothing_grants_nothing()
    {
        // The default, and the one that matters: a fresh machine does not push
        // anything unattended because a file somebody else edited said it could.
        var decision = TeamCeiling.Decide(["git push --tags"], granted: []);

        decision.Allowed.Should().BeEmpty();
        decision.Refused.Should().ContainSingle().Which.Should().Be("git push --tags");
        decision.IsShort.Should().BeTrue();
    }

    [Fact]
    public void A_machine_that_agreed_to_exactly_that_grants_it()
    {
        var decision = TeamCeiling.Decide(["git push --tags"], ["git push --tags"]);

        decision.Allowed.Should().ContainSingle().Which.Should().Be("git push --tags");
        decision.IsShort.Should().BeFalse();
    }

    [Fact]
    public void Agreeing_to_one_action_does_not_agree_to_a_longer_one_starting_the_same_way()
    {
        // Exactly, never by prefix. "git push" must not carry "git push
        // --force" with it, and a machine that meant both can say both.
        var decision = TeamCeiling.Decide(["git push --force"], ["git push"]);

        decision.Refused.Should().ContainSingle().Which.Should().Be("git push --force");
    }

    [Fact]
    public void What_was_agreed_and_what_was_not_are_both_reported()
    {
        var decision = TeamCeiling.Decide(
            ["git push --tags", "gh release create"],
            ["git push --tags"]);

        decision.Allowed.Should().BeEquivalentTo(["git push --tags"]);
        decision.Refused.Should().BeEquivalentTo(["gh release create"]);

        // One refusal is enough to stop the run. A run allowed half of what it
        // was built to do would fail at the other half, minutes in.
        decision.IsShort.Should().BeTrue();
    }

    [Fact]
    public void A_team_that_asks_for_nothing_is_short_of_nothing()
    {
        TeamCeiling.Decide([], ["git push --tags"]).IsShort.Should().BeFalse();
        TeamCeiling.Decide(null, null).Should().Be(TeamCeiling.Nothing);
    }

    [Fact]
    public void The_refusal_names_the_action_and_says_what_to_do_about_it()
    {
        var said = TeamCeiling.Explain(
            "release-crew", TeamCeiling.Decide(["git push --tags"], granted: []));

        said.Should().Contain("release-crew");
        said.Should().Contain("git push --tags");

        // Both ways out, because refusing without one is a dead end: agree to
        // it here, or run it in a posture where you are asked instead.
        said.Should().Contain("team-outward-allowed");
        said.Should().Contain("--autonomy supervised");
    }

    [Fact]
    public void Spacing_around_an_action_does_not_change_what_it_is()
    {
        TeamCeiling.Decide([" git push --tags "], ["git push --tags"])
            .Allowed.Should().ContainSingle().Which.Should().Be("git push --tags");
    }
}
