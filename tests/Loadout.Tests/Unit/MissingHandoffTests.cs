using FluentAssertions;
using Loadout.Agents;
using Loadout.Core.Context;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Saying when a session ended without leaving anything for the next one.
/// </summary>
/// <remarks>
/// <para>
/// The consuming half of a handoff has always worked: one is compiled into the
/// next session's context. The producing half depends on somebody thinking of it
/// at the end of a session, which is the moment the work is finished and nobody
/// wants admin — and the exit policy then commits and pushes that absence
/// without comment.
/// </para>
/// <para>
/// It says rather than does. Writing one automatically would produce a document
/// with nothing in it, which is worse than none: the next session is handed
/// something that says nothing and believes it has been handed over to.
/// </para>
/// </remarks>
public sealed class MissingHandoffTests
{
    private static readonly DateTimeOffset Started = new(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_session_that_worked_for_a_while_and_left_nothing_is_told_so()
    {
        var warning = AgentLauncher.MissingHandoffWarning(
            "starstats", Started, Started.AddMinutes(40), latest: null);

        warning.Should().NotBeNull();
        warning.Should().Contain("loadout handoff create starstats");
    }

    [Fact]
    public void A_session_that_left_one_is_not_nagged()
    {
        var warning = AgentLauncher.MissingHandoffWarning(
            "starstats",
            Started,
            Started.AddMinutes(40),
            new HandoffDocument("2026-02-01", "handoffs/2026-02-01.md", Started.AddMinutes(35)));

        warning.Should().BeNull();
    }

    [Fact]
    public void A_handoff_from_a_previous_session_does_not_count_as_this_one_s()
    {
        // It is already in the context this session was given. Treating it as
        // sufficient would stop the reminder the first time anybody wrote one
        // and never bring it back.
        var warning = AgentLauncher.MissingHandoffWarning(
            "starstats",
            Started,
            Started.AddMinutes(40),
            new HandoffDocument("old", "handoffs/old.md", Started.AddDays(-3)));

        warning.Should().NotBeNull();
    }

    [Fact]
    public void A_session_too_short_to_have_worked_anything_out_is_left_alone()
    {
        // Somebody who opened a session and closed it has nothing to hand over,
        // and a nag after every one of those is a nag nobody reads.
        var warning = AgentLauncher.MissingHandoffWarning(
            "starstats", Started, Started.AddSeconds(20), latest: null);

        warning.Should().BeNull();
    }

    [Fact]
    public void The_boundary_falls_on_the_side_of_saying_nothing()
    {
        AgentLauncher.MissingHandoffWarning(
            "starstats", Started, Started.AddMinutes(5).AddSeconds(-1), latest: null)
            .Should().BeNull();

        AgentLauncher.MissingHandoffWarning(
            "starstats", Started, Started.AddMinutes(5), latest: null)
            .Should().NotBeNull();
    }
}
