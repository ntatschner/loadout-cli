using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a picked-up run may spend and how many rounds it may take, and the
/// cases where it would only stop again.
/// </summary>
public sealed class RunResumeSettleTests
{
    private static RunSummary Ended(decimal spent, int rounds, int limit = 0) =>
        new("r", "d", "team", "goal", "supervised",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "budget spent", spent, rounds, [], [], [],
            RoundLimit: limit);

    [Fact]
    public void A_run_that_spent_its_budget_needs_more_before_it_is_picked_up()
    {
        var (_, _, refused) = TeamResumeCommand.Settle(Ended(25.10m, 3), UsdCap.Of(25m), usd: default, moreRounds: 0);

        refused.Should().Contain("--usd");
    }

    [Fact]
    public void A_new_budget_below_what_it_has_spent_is_refused()
    {
        var (_, _, refused) = TeamResumeCommand.Settle(Ended(25.10m, 3), UsdCap.Of(25m), usd: UsdCap.Of(20m), moreRounds: 0);

        refused.Should().Contain("more than it has spent");
    }

    [Fact]
    public void More_money_picks_it_up_with_the_new_budget()
    {
        var (budget, rounds, refused) = TeamResumeCommand.Settle(Ended(25.10m, 3), UsdCap.Of(25m), usd: UsdCap.Of(40m), moreRounds: 0);

        refused.Should().BeNull();
        budget.Usd.Should().Be(40m);
        rounds.Should().Be(0, "a run with no round limit keeps having none");
    }

    [Fact]
    public void A_run_out_of_rounds_needs_more_rounds()
    {
        TeamResumeCommand.Settle(Ended(5m, 5, limit: 5), UsdCap.Of(25m), usd: default, moreRounds: 0)
            .Refused.Should().Contain("--rounds");
    }

    [Fact]
    public void More_rounds_are_counted_from_where_it_got_to()
    {
        TeamResumeCommand.Settle(Ended(5m, 5, limit: 5), UsdCap.Of(25m), usd: default, moreRounds: 3)
            .Rounds.Should().Be(8);
    }

    [Fact]
    public void A_run_with_money_and_rounds_left_is_picked_up_as_it_was()
    {
        var (budget, rounds, refused) = TeamResumeCommand.Settle(Ended(5m, 2, limit: 5), UsdCap.Of(25m), usd: default, moreRounds: 0);

        refused.Should().BeNull();
        budget.Usd.Should().Be(25m);
        rounds.Should().Be(5);
    }

    [Fact]
    public void A_run_that_spent_its_budget_may_be_picked_up_with_no_cap()
    {
        var (budget, _, refused) = TeamResumeCommand.Settle(Ended(25.10m, 3), UsdCap.Of(25m), usd: UsdCap.NoCap, moreRounds: 0);

        refused.Should().BeNull();
        budget.Should().Be(UsdCap.NoCap);
    }

    [Fact]
    public void A_run_with_no_cap_is_never_refused_for_what_it_has_spent()
    {
        TeamResumeCommand.Settle(Ended(500m, 3), UsdCap.NoCap, usd: default, moreRounds: 0)
            .Refused.Should().BeNull();
    }
}
