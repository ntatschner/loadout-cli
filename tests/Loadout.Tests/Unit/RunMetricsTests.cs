using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The numbers that change a decision, as against the ones that decorate a
/// page.
/// </summary>
/// <remarks>
/// <para>
/// What a run has cost is already on the page and nobody acts on it. What it
/// is costing, and where that ends up if it uses the rounds it has left, is
/// the number somebody stops a run over — so it is worked out here rather than
/// left for a person to do in their head at three in the morning.
/// </para>
/// <para>
/// Projected from rounds rather than from the clock. A run does not spend
/// evenly through time — it spends while a node is up and nothing while the
/// lead thinks — but it does spend roughly per round, because a round is what
/// buys nodes.
/// </para>
/// </remarks>
public sealed class RunMetricsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static RunSummary Run(
        decimal cost = 1.00m,
        int rounds = 2,
        int limit = 5,
        decimal? budget = null,
        DateTimeOffset? finished = null,
        IReadOnlyList<RunNode>? nodes = null,
        IReadOnlyList<RunTurn>? turns = null,
        IReadOnlyList<RunRound>? timeline = null,
        int quiet = 0,
        int conflicts = 0) =>
        new(
            "20260918-1200-aaaa", "d", "docs-crew", "goal", "autonomous",
            Noon, finished, finished is null ? null : "done", cost, rounds,
            nodes ?? [], [], [], limit, null, null, null, budget, quiet, turns, timeline, conflicts);

    [Fact]
    public void A_run_says_what_it_is_costing_and_not_only_what_it_has_cost()
    {
        // Ten minutes in, a pound spent: ten pence a minute.
        var money = RunMetrics.Money(Run(cost: 1.00m), Noon.AddMinutes(10));

        money.Spent.Should().Be(1.00m);
        money.PerMinute.Should().Be(0.10m);
    }

    [Fact]
    public void And_where_that_ends_up_if_it_uses_every_round_it_has()
    {
        // Two rounds of five have cost a pound, so five rounds cost two-fifty.
        var money = RunMetrics.Money(Run(cost: 1.00m, rounds: 2, limit: 5), Noon.AddMinutes(10));

        money.Projected.Should().Be(2.50m);
    }

    [Fact]
    public void A_run_that_will_go_past_its_budget_says_so_before_it_does()
    {
        // The whole point. Two pounds fifty projected against a two pound cap,
        // said while there is still something to be done about it.
        var money = RunMetrics.Money(
            Run(cost: 1.00m, rounds: 2, limit: 5, budget: 2.00m), Noon.AddMinutes(10));

        money.Overrunning.Should().BeTrue();
        money.Budget.Should().Be(2.00m);
    }

    [Fact]
    public void A_run_inside_its_budget_says_nothing_about_it()
    {
        RunMetrics.Money(Run(cost: 1.00m, rounds: 2, limit: 5, budget: 10.00m), Noon.AddMinutes(10))
            .Overrunning.Should().BeFalse();
    }

    [Fact]
    public void A_run_that_has_finished_projects_nothing()
    {
        // There are no rounds left to project, and a projection on a finished
        // run is a number that can only mislead.
        var money = RunMetrics.Money(
            Run(cost: 1.00m, rounds: 2, limit: 5, finished: Noon.AddMinutes(10)), Noon.AddHours(3));

        money.Projected.Should().BeNull();

        // And its rate is measured over the time it actually ran, not over
        // however long ago it was.
        money.PerMinute.Should().Be(0.10m);
    }

    [Fact]
    public void A_run_with_no_round_limit_projects_nothing_rather_than_guessing()
    {
        RunMetrics.Money(Run(cost: 1.00m, rounds: 2, limit: 0), Noon.AddMinutes(10))
            .Projected.Should().BeNull();
    }

    [Fact]
    public void A_run_a_second_old_does_not_report_a_rate_of_infinity()
    {
        // Divided by however long it has been going, which starts at nothing.
        var money = RunMetrics.Money(Run(cost: 0.10m), Noon);

        money.PerMinute.Should().BeLessThan(10m);
    }

    [Theory]
    [InlineData(0.20, "$0.20 a minute")]
    [InlineData(0.004, "$0.24 an hour")]
    public void A_rate_is_said_in_units_somebody_has_a_feel_for(double perMinute, string expected)
    {
        // "$0.0041 a minute" is a number nobody has a feel for.
        RunMetrics.Rate((decimal)perMinute).Should().Be(expected);
    }

    [Fact]
    public void Where_the_time_went_is_a_question_about_rounds_first()
    {
        var run = Run(timeline:
        [
            new RunRound(1, Noon, Noon.AddMinutes(2), 2),
            new RunRound(2, Noon.AddMinutes(2), Noon.AddMinutes(9), 1),
            new RunRound(3, Noon.AddMinutes(9)),
        ]);

        var read = RunMetrics.For(run, Noon.AddMinutes(10));

        read.Rounds.Should().HaveCount(3);
        read.Rounds[0].Seconds.Should().Be(120);
        read.Rounds[1].Seconds.Should().Be(420);

        // The one still going is measured against the clock and says so.
        read.Rounds[2].Seconds.Should().Be(60);
        read.Rounds[2].Running.Should().BeTrue();
    }

    [Fact]
    public void The_shapes_a_run_goes_wrong_in_are_counted_separately()
    {
        // Separately because they mean different things: refusals are about a
        // role's permissions, rejected reports about what the role was asked
        // for, quiet rounds about the lead.
        var run = Run(
            nodes:
            [
                new RunNode("lead", "role.project-lead", "done", 2, 0.1m, Noon, Denials: 3),
                new RunNode("implementer/1", "role.implementer", "done", 5, 0.2m, Noon, Denials: 1),
            ],
            turns:
            [
                new RunTurn(Noon, "lead", 1, 1, 2, 0.1m, 3, true, "done", "accepted"),
                new RunTurn(Noon, "implementer/1", 1, 1, 5, 0.1m, 1, true, "done", "rejected"),
                new RunTurn(Noon, "implementer/1", 1, 2, 5, 0.1m, 0, true, "done", "accepted"),
            ],
            quiet: 1,
            conflicts: 2);

        var went = RunMetrics.Went(run);

        went.Denials.Should().Be(4);
        went.Rejected.Should().Be(1);
        went.Retried.Should().Be(1, "the second attempt is the one that says a first went wrong");
        went.QuietRounds.Should().Be(1);
        went.Conflicts.Should().Be(2);
        went.Any.Should().BeTrue();
    }

    [Fact]
    public void A_run_that_went_well_says_nothing_about_trouble()
    {
        RunMetrics.Went(Run(turns:
            [new RunTurn(Noon, "lead", 1, 1, 2, 0.1m, 0, true, "done", "accepted")]))
            .Any.Should().BeFalse();
    }
}
