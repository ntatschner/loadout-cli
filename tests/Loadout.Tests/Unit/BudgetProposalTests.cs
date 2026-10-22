using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Core.Triggers;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>The budget and cap a rule is offered, from what its team's runs actually spent.</summary>
public sealed class BudgetProposalTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_team_with_no_finished_runs_is_offered_the_starter_figure_labelled_as_one()
    {
        var proposal = BudgetProposal.For("reviewer", [], [Run("alpha", 9m, "done", 1)]);

        proposal.Should().Be(new BudgetProposal(2m, 10m, 0, 0, 0, true, false));
        proposal.Working().Should().Contain("starter");
    }

    [Fact]
    public void The_figure_is_the_90th_percentile_rounded_up_and_one_runaway_does_not_set_it()
    {
        // Nineteen ordinary runs and one that went long.
        var runs = Enumerable.Range(1, 19).Select(i => Run("reviewer", 1m + (i * 0.07m), "done", i))
            .Append(Run("reviewer", 40m, "failed", 20));

        var proposal = BudgetProposal.For("reviewer", [], runs);

        // Sorted, the 18th of 20 is 2.26: rounded up to 3, where rounding to
        // the nearest would say 2.
        proposal.BudgetUsd.Should().Be(3m);
        proposal.CapUsd.Should().Be(15m);
        proposal.Counted.Should().Be(20);
        proposal.Working().Should().NotContain("here", "a rule watching every project has nothing to split");
    }

    [Theory]
    [InlineData("stopped")]
    [InlineData("needs-decision")]
    public void Runs_that_say_nothing_about_what_the_work_costs_are_left_out(string outcome)
    {
        var proposal = BudgetProposal.For("reviewer", [], [Run("reviewer", 30m, outcome, 1), Run("reviewer", 2m, "done", 2)]);

        proposal.BudgetUsd.Should().Be(2m);
        proposal.Counted.Should().Be(1);
    }

    [Fact]
    public void The_rules_own_projects_come_first_and_the_newest_runs_elsewhere_make_up_the_number()
    {
        var runs = new List<RunSummary>();
        runs.AddRange(Enumerable.Range(1, 12).Select(i => Run("reviewer", 1m, "done", i, "loadout-cli")));
        runs.AddRange(Enumerable.Range(1, 30).Select(i => Run("reviewer", i <= 22 ? 1m : 9m, "done", i, "starstats")));

        var proposal = BudgetProposal.For("reviewer", ["loadout-cli"], runs);

        proposal.Here.Should().Be(12);
        proposal.Elsewhere.Should().Be(8);
        proposal.BudgetUsd.Should().Be(9m, "the eight newest elsewhere are the expensive ones, and they make the top decile");
        proposal.Working().Should().Contain("12 here, 8 elsewhere");
    }

    [Fact]
    public void Runs_that_ran_out_make_the_figure_a_floor_and_say_how_many()
    {
        var proposal = BudgetProposal.For(
            "reviewer", [], [Run("reviewer", 5m, "limited", 1), Run("reviewer", 5m, "limited", 2), Run("reviewer", 1m, "done", 3)]);

        proposal.Limited.Should().Be(2);
        proposal.BudgetUsd.Should().Be(5m, "labelled, not raised");
        proposal.Working().Should().Contain("A floor: 2 of them");
    }

    private static RunSummary Run(string team, decimal cost, string outcome, int minutes, string? project = null) =>
        new(
            $"20261022-{minutes:0000}-a001",
            string.Empty,
            team,
            "goal",
            "autonomous",
            Now.AddHours(-1),
            Now.AddMinutes(minutes),
            outcome,
            cost,
            1,
            [],
            [],
            [],
            Project: project,
            Outcome: outcome);
}
