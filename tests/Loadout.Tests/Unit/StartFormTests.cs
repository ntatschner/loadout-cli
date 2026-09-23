using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Teams.Daemon;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The command line the dashboard's start form stands for.
/// </summary>
/// <remarks>
/// <para>
/// The page implements nothing: it types the command somebody would have typed.
/// That is only true while every box on the form reaches the command line, and
/// twice now one has not. The model was read from its box and dropped, so a run
/// started from the page took whatever the team file pinned however carefully
/// somebody had chosen otherwise — and nothing failed, because a field that is
/// read and discarded looks exactly like a field that works.
/// </para>
/// <para>
/// Asserted against the argument list rather than by running anything. Whether
/// the parser accepts these options is a different question, answered by the
/// contract tests against the built binary; this one is about whether they are
/// passed at all.
/// </para>
/// </remarks>
public sealed class StartFormTests
{
    private static StartRequest Filled() => new(
        "iterating-project",
        "Add --since to loadout usage.",
        Project: "demo",
        Rounds: 4,
        Autonomy: "supervised",
        Criteria: ["the option exists", "  ", "a test covers it"],
        Model: "opus",
        Agent: "claude",
        TakeRecommendationAfter: "30m");

    [Fact]
    public void Every_box_on_the_form_reaches_the_command_line()
    {
        var typed = DashboardActions.Starting(Filled());

        typed.Should().StartWith(["iterating-project", "Add --since to loadout usage."]);

        typed.Should().ContainInConsecutiveOrder("--project", "demo");
        typed.Should().ContainInConsecutiveOrder("--rounds", "4");
        typed.Should().ContainInConsecutiveOrder("--autonomy", "supervised");
        typed.Should().ContainInConsecutiveOrder("--model", "opus");
        typed.Should().ContainInConsecutiveOrder("--agent", "claude");
        typed.Should().ContainInConsecutiveOrder("--take-recommendation-after", "30m");

        // One option per criterion, because a criterion is a sentence and
        // sentences contain commas.
        typed.Should().ContainInConsecutiveOrder("--done-when", "the option exists");
        typed.Should().ContainInConsecutiveOrder("--done-when", "a test covers it");

        // Nobody is at the browser to answer a prompt in the daemon's console.
        typed.Should().Contain("--non-interactive");
    }

    [Fact]
    public void A_blank_criterion_is_dropped_rather_than_passed()
    {
        // A criterion nothing could report a verdict on would refuse every done
        // for ever, and a box people type into acquires empty lines.
        DashboardActions.Starting(Filled())
            .Count(one => one == "--done-when")
            .Should().Be(2);
    }

    [Fact]
    public void An_empty_form_asks_for_nothing_it_was_not_given()
    {
        var typed = DashboardActions.Starting(new StartRequest("docs-crew", "make the docs true"));

        typed.Should().NotContain("--project");
        typed.Should().NotContain("--model");
        typed.Should().NotContain("--agent");
        typed.Should().NotContain("--autonomy");
        typed.Should().NotContain("--done-when");
        typed.Should().NotContain("--take-recommendation-after");

        // And no round cap, which is what an empty box means now: the team's
        // budget and two rounds without progress are what stop it.
        typed.Should().NotContain("--rounds");
    }

    [Fact]
    public void A_round_cap_of_zero_is_no_cap_rather_than_a_cap_of_none()
    {
        // The form sends null for an empty box, but a zero reaching here from
        // anywhere else must mean the same thing rather than '--rounds 0',
        // which the runner would read as no cap anyway and the parser would
        // accept as a number.
        DashboardActions.Starting(new StartRequest("docs-crew", "goal", Rounds: 0))
            .Should().NotContain("--rounds");
    }
}
