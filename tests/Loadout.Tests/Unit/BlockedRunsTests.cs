using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which ended runs need a person, and what they are offered first.
/// </summary>
/// <remarks>
/// Every ending a run can write, and every rule in the table, each as a row.
/// A rule moved above another changes a row here, which is the point: the
/// order is the behaviour.
/// </remarks>
public sealed class BlockedRunsTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    private static RunSummary Run(
        string? ended,
        string? outcome = null,
        bool running = false,
        string? task = "fix-login",
        string? project = "loadout-cli",
        int rounds = 3,
        int limit = 3,
        decimal cost = 4.80m,
        decimal? budget = 5.00m,
        RunResolution? resolved = null) =>
        new(
            "20261025-0900-ab12",
            "C:/runs/20261025-0900-ab12",
            "iterating-project",
            "Fix the login",
            "autonomous",
            At,
            running ? null : At.AddHours(1),
            running ? null : ended,
            cost,
            rounds,
            [],
            [],
            [],
            limit,
            project,
            BudgetUsd: budget,
            Outcome: outcome,
            Task: task)
        {
            Resolved = resolved,
        };

    [Theory]
    [InlineData("blocked", "blocked")]
    [InlineData("failed", "failed")]
    [InlineData("round limit: 3 rounds", "limited")]
    [InlineData("budget spent: 4.80 of 5.00 USD", "limited")]
    [InlineData("no progress: two rounds without a request or a finish", "failed")]
    [InlineData("the lead could not be started: no agent", "failed")]
    [InlineData("halted: the lead took an outward action its brief did not allow", "failed")]
    [InlineData("the lead reported done with 1 of 2 criteria unmet", "failed")]
    [InlineData("", "unrecorded")]
    [InlineData("abandoned: nothing was driving it", "dead")]
    public void Every_ending_short_of_done_is_held(string ended, string kind)
    {
        BlockedRuns.KindOf(Run(ended)).Should().Be(kind);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("needs-decision")]
    [InlineData("stopped by request")]
    [InlineData("stopped by the person")]
    [InlineData("stopped at a decision")]
    public void A_run_that_got_there_or_has_an_owner_is_not(string ended)
    {
        // Stopped and needs-decision already belong to somebody: the person
        // who stopped it, and the person being asked.
        BlockedRuns.KindOf(Run(ended)).Should().BeNull();
    }

    [Fact]
    public void A_run_still_going_is_held_only_when_its_process_has_gone()
    {
        BlockedRuns.KindOf(Run(null, running: true)).Should().BeNull();
        BlockedRuns.KindOf(Run(null, running: true), dead: true).Should().Be("dead");
    }

    [Fact]
    public void A_run_somebody_has_dealt_with_is_not_held_again()
    {
        var dealt = Run("blocked", resolved: new RunResolution(At, "set-aside", "nrtat"));

        BlockedRuns.KindOf(dealt).Should().BeNull();
        BlockedRuns.Judge(dealt, null).Should().BeNull();
    }

    [Fact]
    public void The_category_the_run_recorded_wins_over_its_sentence()
    {
        // Written by a current build, which settles its category as it ends.
        BlockedRuns.KindOf(Run("something nobody wrote before", outcome: "limited")).Should().Be("limited");
    }

    public static TheoryData<string, BlockedLead?, int, string, BlockedAction> Suggestions => new()
    {
        { "budget spent: 4.80 of 5.00 USD", null, 1, "budget", BlockedAction.Raise },
        { "round limit: 3 rounds", null, 1, "rounds", BlockedAction.Raise },
        { "blocked", new BlockedLead("no test database", "a connection string", []), 1, "blocked-unblock", BlockedAction.CarryOn },
        { "blocked", new BlockedLead(null, null, ["Which API version?"]), 1, "blocked-question", BlockedAction.CarryOn },
        { "blocked", null, 1, "blocked", BlockedAction.EditRequeue },
        { "the lead reported done with 1 of 2 criteria unmet", null, 1, "unmet", BlockedAction.CarryOn },
        { "no progress: two rounds without a request or a finish", null, 1, "no-progress", BlockedAction.EditRequeue },
        { "the lead could not be started: no agent", null, 1, "not-started", BlockedAction.Requeue },
        { "halted: implementer took an outward action its brief did not allow", null, 1, "halted", BlockedAction.SetAside },
        { "", null, 1, "unrecorded", BlockedAction.Requeue },
        { "failed", null, 1, "failed", BlockedAction.SetAside },

        // The loop guard: whatever the ending, the third short run in a row
        // stops suggesting more of the same.
        { "round limit: 3 rounds", null, BlockedRuns.ChainLimit, "chain", BlockedAction.SetAside },
        { "blocked", new BlockedLead("x", "y", []), BlockedRuns.ChainLimit, "chain", BlockedAction.SetAside },
        { "round limit: 3 rounds", null, BlockedRuns.ChainLimit - 1, "rounds", BlockedAction.Raise },
    };

    [Theory]
    [MemberData(nameof(Suggestions))]
    public void Each_rule_suggests_its_action(string ended, BlockedLead? lead, int chain, string rule, BlockedAction action)
    {
        var judged = BlockedRuns.Judge(Run(ended), lead, chain)!;

        judged.Suggested.Rule.Should().Be(rule);
        judged.Suggested.Action.Should().Be(action);
        judged.Offered[0].Should().Be(action, "the suggestion is offered first");
        judged.Suggested.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_dead_run_is_suggested_carrying_on()
    {
        var judged = BlockedRuns.Judge(Run(null, running: true), null, dead: true)!;

        judged.Kind.Should().Be("dead");
        judged.Suggested.Action.Should().Be(BlockedAction.CarryOn);
    }

    [Fact]
    public void The_reason_is_about_this_run()
    {
        var judged = BlockedRuns.Judge(Run("blocked"), new BlockedLead("no test database", "a connection string", []))!;

        judged.Suggested.Reason.Should().Contain("no test database").And.Contain("a connection string");

        BlockedRuns.Judge(Run("budget spent: 4.80 of 5.00 USD"), null)!
            .Suggested.Reason.Should().Contain("$4.80 of $5.00");
    }

    [Fact]
    public void Raise_is_offered_only_for_a_limited_run()
    {
        BlockedRuns.Judge(Run("round limit: 3 rounds"), null)!.Offered.Should().Contain(BlockedAction.Raise);

        foreach (var ended in new[] { "blocked", "failed", "", "no progress: x" })
        {
            var judged = BlockedRuns.Judge(Run(ended), null)!;

            judged.Offered.Should().NotContain(BlockedAction.Raise, ended);
            judged.Raise.Should().BeNull();
        }
    }

    [Fact]
    public void A_raise_starts_from_the_same_again_beside_what_was_used()
    {
        var money = BlockedRuns.Judge(Run("budget spent: 4.80 of 5.00 USD", cost: 4.80m, budget: 5.00m), null)!.Raise!;

        money.Cap.Should().Be(BlockedCap.Budget);
        money.Figure.Should().Be(5.00m);
        money.Used.Should().Be(4.80m);

        var rounds = BlockedRuns.Judge(Run("round limit: 4 rounds", rounds: 4, limit: 4), null)!.Raise!;

        rounds.Cap.Should().Be(BlockedCap.Rounds);
        rounds.Figure.Should().Be(4);
        rounds.Used.Should().Be(4);
    }

    [Fact]
    public void A_run_with_no_task_is_offered_nothing_that_needs_one()
    {
        var judged = BlockedRuns.Judge(Run("blocked", task: null), null)!;

        judged.Offered.Should().NotContain([BlockedAction.Requeue, BlockedAction.EditRequeue, BlockedAction.Drop]);
        judged.Offered.Should().Contain([BlockedAction.CarryOn, BlockedAction.SetAside]);

        // The blocked rule names edit-requeue, which is not open here, so the
        // table falls through to one that is.
        judged.Suggested.Action.Should().BeOneOf(judged.Offered);
    }

    [Fact]
    public void A_run_with_no_project_can_only_be_set_aside()
    {
        var judged = BlockedRuns.Judge(Run("round limit: 3 rounds", project: null), null)!;

        judged.Offered.Should().Equal(BlockedAction.SetAside);
        judged.Suggested.Action.Should().Be(BlockedAction.SetAside);
    }

    [Fact]
    public void Every_action_reads_back_from_how_it_is_spelt()
    {
        foreach (var action in Enum.GetValues<BlockedAction>())
        {
            BlockedActions.TryParse(BlockedActions.Spell(action), out var read).Should().BeTrue();
            read.Should().Be(action);
        }

        BlockedActions.TryParse("give up", out _).Should().BeFalse();
    }

    [Fact]
    public void Chains_follow_the_links_newest_chain_first()
    {
        RunSummary One(string id, int minute, string? follows = null) =>
            Run("blocked") with { RunId = id, Started = At.AddMinutes(minute), Follows = follows };

        var chains = BlockedRunReader.Chains(
        [
            One("a", 0),
            One("b", 10, follows: "a"),
            One("c", 20, follows: "b"),
            One("d", 5),
        ]);

        chains.Should().HaveCount(2);
        chains[0].Select(one => one.RunId).Should().Equal("a", "b", "c");
        chains[1].Select(one => one.RunId).Should().Equal("d");
    }

    [Fact]
    public void A_run_whose_owner_has_gone_reads_as_dead_and_one_still_held_does_not()
    {
        var run = Run(null, running: true);
        var gone = new Loadout.Tests.Fakes.FakeRunOwnership()
            .Script(run.Directory, new OwnerReading.Free(null));
        var held = new Loadout.Tests.Fakes.FakeRunOwnership()
            .Script(run.Directory, new OwnerReading.Held());

        Loadout.Cli.Commands.BlockedReading.Gone(run, gone, At.AddHours(2)).Should().BeTrue();
        Loadout.Cli.Commands.BlockedReading.Gone(run, held, At.AddHours(2)).Should().BeFalse();
    }

    [Fact]
    public void A_run_from_an_older_build_that_has_only_gone_quiet_is_not_called_dead()
    {
        // No owner record is a guess, not an observation: such a run is shown
        // as possibly not responding elsewhere, and closing it asks first, so
        // the Blocked section does not offer to carry on from it.
        var run = Run(null, running: true);
        var none = new Loadout.Tests.Fakes.FakeRunOwnership();

        Loadout.Cli.Commands.BlockedReading.Gone(run, none, At.AddDays(2)).Should().BeFalse();
    }

    private static RunSummary HandedOver(params bool[] ticked) =>
        Run(RunHandovers.EndedOpening + " 2 criterion(s) the run cannot meet itself", outcome: "needs-decision") with
        {
            Handover = new RunHandover(
            [
                new HandoverItem("Green on the Unix CI legs", CriterionNeeds.Ci, "Push it and see CI pass.", "keyword")
                {
                    CheckedBy = ticked.ElementAtOrDefault(0) ? "nrtat" : null,
                },
                new HandoverItem("End run checked by hand in Studio", CriterionNeeds.Person, "Press End run on a dead run.", "lead")
                {
                    CheckedBy = ticked.ElementAtOrDefault(1) ? "nrtat" : null,
                },
            ],
            ["teams-20261025-0900-ab12"]),
        };

    [Fact]
    public void A_run_that_handed_over_is_held_while_anything_is_left_to_tick()
    {
        // Its outcome is needs-decision, which is left out as already asking
        // somebody. A handover's process has exited, so nobody is being asked.
        BlockedRuns.KindOf(HandedOver()).Should().Be("handover");
        BlockedRuns.KindOf(HandedOver(true)).Should().Be("handover");
        BlockedRuns.KindOf(HandedOver(true, true)).Should().BeNull("every item is ticked, so it is judged again");
    }

    [Fact]
    public void A_run_that_handed_over_is_told_to_tick_what_is_open_and_never_offered_carrying_on()
    {
        var judged = BlockedRuns.Judge(HandedOver(true), null, chain: BlockedRuns.ChainLimit)!;

        judged.Suggested.Rule.Should().Be("handover", "a handover is not one more short run, whatever the chain");
        judged.Suggested.Reason.Should().Contain("1 criterion(s)")
            .And.Contain("End run checked by hand in Studio")
            .And.Contain("loadout team handover 20261025-0900-ab12 --check 2");
        judged.Offered.Should().NotContain(BlockedAction.CarryOn).And.NotContain(BlockedAction.Raise);
    }
}
