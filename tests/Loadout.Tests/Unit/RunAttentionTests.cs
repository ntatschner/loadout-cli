using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What the person running these teams needs to look at.
/// </summary>
/// <remarks>
/// <para>
/// The property every one of these tests is really about: <strong>a reason has
/// to clear itself</strong>. Temporal flags a workflow after five consecutive
/// failed tasks and unflags it on the first success, and that is what stops
/// such a list becoming a pile nobody reads. So for each reason there is a
/// pair: the state that raises it, and the state that takes it away.
/// </para>
/// <para>
/// A rail that only grows is worse than no rail. It teaches somebody to skim
/// the one thing that was meant to be unskimmable.
/// </para>
/// </remarks>
public sealed class RunAttentionTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    private static RunSummary Run(
        bool running = true,
        IReadOnlyList<RunNode>? nodes = null,
        IReadOnlyList<PendingAsk>? gates = null,
        int quietRounds = 0,
        decimal cost = 0m,
        decimal? budget = null,
        DateTimeOffset? started = null) =>
        new(
            "20260917-1200-aaaa",
            Directory: "d",
            Team: "iterating-project",
            Goal: "do the thing",
            Autonomy: "supervised",
            Started: started ?? Noon.AddMinutes(-10),
            Finished: running ? null : Noon,
            Ended: running ? null : "done",
            CostUsd: cost,
            Rounds: 2,
            Nodes: nodes ?? [],
            Merged: [],
            Branches: [],
            RoundLimit: 5,
            Gates: gates,
            BudgetUsd: budget,
            QuietRounds: quietRounds);

    private static RunNode Node(
        string name = "implementer/1",
        int turns = 2,
        string state = "running",
        DateTimeOffset? lastSeen = null,
        DateTimeOffset? started = null) =>
        new(
            name,
            "role.implementer",
            state,
            turns,
            CostUsd: 0.10m,
            LastSeen: lastSeen ?? Noon,
            Started: started ?? Noon.AddMinutes(-4));

    private static PendingAsk Gate() =>
        new("g1", "implementer/1", "role.implementer", "Bash", "dotnet test", Noon);

    [Fact]
    public void A_run_that_wants_nothing_is_not_in_the_rail()
    {
        RunAttention.For(Run(nodes: [Node()]), Noon).Should().BeEmpty();
    }

    [Fact]
    public void A_finished_run_wants_nothing_however_it_ended()
    {
        // Whatever was true while it ran stopped being true when it stopped.
        // A rail full of yesterday is the failure this is designed against.
        var stuck = Run(
            running: false,
            gates: [Gate()],
            quietRounds: 3,
            cost: 99m,
            budget: 1m,
            nodes: [Node(turns: 1, lastSeen: Noon.AddHours(-3))]);

        RunAttention.For(stuck, Noon).Should().BeEmpty();
    }

    [Fact]
    public void A_question_puts_it_in_the_rail_and_answering_takes_it_out()
    {
        var asking = RunAttention.For(Run(gates: [Gate()]), Noon);

        asking.Should().ContainSingle().Which.Kind.Should().Be(AttentionKind.Asking);
        asking[0].Detail.Should().Contain("dotnet test");
        asking[0].Clears.Should().Be("you answer it");

        // Answering removes the file, so the next read has no gate at all.
        RunAttention.For(Run(gates: []), Noon).Should().BeEmpty();
    }

    [Fact]
    public void A_question_the_lead_wrote_reaches_the_rail_with_one_question_mark()
    {
        var asking = RunAttention.For(Run(gates: [Gate() with { Asked = "Merge now?" }]), Noon);

        // This is the sentence that goes out to somebody's phone, so it is
        // worth being exact about rather than merely containing the words.
        asking.Should().ContainSingle().Which.Detail.Should().Be("Merge now?");
    }

    [Fact]
    public void A_round_that_asked_for_nothing_is_worth_saying_before_the_next_one_ends_it()
    {
        var stuck = RunAttention.For(Run(quietRounds: 1), Noon);

        stuck.Should().ContainSingle().Which.Kind.Should().Be(AttentionKind.GoingNowhere);

        // The useful part: it says what happens next if nothing changes.
        stuck[0].Detail.Should().Contain("stops itself");

        // And a round that asked for something resets it.
        RunAttention.For(Run(quietRounds: 0), Noon).Should().BeEmpty();
    }

    [Fact]
    public void A_node_silent_for_far_longer_than_it_usually_takes_is_mentioned()
    {
        // Two turns over four minutes: two minutes each. Silent for ten.
        var quiet = Run(nodes:
        [
            Node(turns: 2, started: Noon.AddMinutes(-14), lastSeen: Noon.AddMinutes(-10)),
        ]);

        var said = RunAttention.For(quiet, Noon);

        said.Should().ContainSingle().Which.Kind.Should().Be(AttentionKind.Quiet);
        said[0].Clears.Should().Be("it says anything at all");
    }

    [Fact]
    public void And_stops_being_mentioned_the_moment_it_speaks()
    {
        var spoke = Run(nodes:
        [
            Node(turns: 2, started: Noon.AddMinutes(-14), lastSeen: Noon.AddSeconds(-5)),
        ]);

        RunAttention.For(spoke, Noon).Should().BeEmpty();
    }

    [Fact]
    public void A_node_that_is_merely_slow_is_left_alone()
    {
        // Turns average two minutes; silent for five. Slower than usual and
        // nowhere near three times it. Crying wolf here would cost the rail
        // its meaning.
        var slow = Run(nodes:
        [
            Node(turns: 2, started: Noon.AddMinutes(-9), lastSeen: Noon.AddMinutes(-5)),
        ]);

        RunAttention.For(slow, Noon).Should().BeEmpty();
    }

    [Fact]
    public void A_node_with_fast_turns_is_not_reported_after_seconds()
    {
        // Four turns in sixteen seconds: four seconds each. Three times that
        // is twelve, which would be absurd, so the floor applies instead.
        var brisk = Run(nodes:
        [
            Node(turns: 4, started: Noon.AddSeconds(-46), lastSeen: Noon.AddSeconds(-30)),
        ]);

        RunAttention.For(brisk, Noon).Should().BeEmpty();
    }

    [Fact]
    public void A_node_on_its_first_turn_is_never_called_quiet()
    {
        // Nothing is known yet about how long this one takes, so there is no
        // average to be three times.
        var first = Run(nodes:
        [
            Node(turns: 0, started: Noon.AddMinutes(-30), lastSeen: Noon.AddMinutes(-30)),
        ]);

        RunAttention.For(first, Noon).Should().BeEmpty();
    }

    [Theory]
    [InlineData("done")]
    [InlineData("reported")]
    [InlineData("failed")]
    public void A_node_that_has_finished_is_not_quiet_it_is_finished(string state)
    {
        var over = Run(nodes:
        [
            Node(turns: 2, state: state, started: Noon.AddMinutes(-14), lastSeen: Noon.AddHours(-2)),
        ]);

        RunAttention.For(over, Noon).Should().BeEmpty();
    }

    [Fact]
    public void A_run_that_has_spent_its_budget_says_so()
    {
        var spent = RunAttention.For(Run(cost: 5.10m, budget: 5m), Noon);

        spent.Should().ContainSingle().Which.Kind.Should().Be(AttentionKind.Spending);
        spent[0].Detail.Should().Contain("5.10");
    }

    [Fact]
    public void A_run_with_no_budget_is_never_reported_for_spending()
    {
        // Nothing to be over. Reporting it would be inventing a threshold
        // nobody set.
        RunAttention.For(Run(cost: 99m, budget: null), Noon).Should().BeEmpty();
    }

    [Fact]
    public void A_run_comfortably_within_its_budget_is_left_alone()
    {
        RunAttention.For(Run(cost: 0.50m, budget: 5m), Noon).Should().BeEmpty();
    }

    [Fact]
    public void Several_reasons_are_all_reported_rather_than_the_first_one()
    {
        // Somebody looking at the rail wants to know everything that is wrong
        // with a run, not the first thing this happened to check.
        var bad = Run(
            gates: [Gate()],
            quietRounds: 1,
            cost: 6m,
            budget: 5m,
            nodes: [Node(turns: 2, started: Noon.AddMinutes(-14), lastSeen: Noon.AddMinutes(-10))]);

        RunAttention.For(bad, Noon).Select(r => r.Kind).Should().BeEquivalentTo(
        [
            AttentionKind.Asking,
            AttentionKind.GoingNowhere,
            AttentionKind.Quiet,
            AttentionKind.Spending,
        ]);
    }

    [Fact]
    public void Every_reason_says_what_would_clear_it()
    {
        // The rule the whole design rests on. A reason that cannot say how it
        // goes away is a reason that never will.
        var bad = Run(
            gates: [Gate()],
            quietRounds: 1,
            cost: 6m,
            budget: 5m,
            nodes: [Node(turns: 2, started: Noon.AddMinutes(-14), lastSeen: Noon.AddMinutes(-10))]);

        RunAttention.For(bad, Noon).Should().AllSatisfy(reason =>
            reason.Clears.Should().NotBeNullOrWhiteSpace());
    }
}
