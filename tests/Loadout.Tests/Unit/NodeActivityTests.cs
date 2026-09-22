using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a node is doing right now, as the dashboard, <c>team status</c> and
/// the launcher's screen show it beside the node.
/// </summary>
/// <remarks>
/// The node's own state is what it last reported, which answered the wrong
/// question: a lead waiting for its strategist read "blocked", and a node
/// stopped on a permission question read "working".
/// </remarks>
public sealed class NodeActivityTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 22, 23, 0, 0, TimeSpan.Zero);

    private static RunNode Node(string name, string state) =>
        new(name, "role." + name, state, 1, 0m, At);

    private static RunSummary Run(
        IReadOnlyList<RunNode> nodes,
        IReadOnlyList<PendingAsk>? gates = null,
        bool finished = false) =>
        new("r", "d", "team", "goal", "supervised", At, finished ? At.AddHours(1) : null, null, 0m, 1,
            nodes, [], [], Gates: gates);

    [Fact]
    public void A_node_stopped_on_a_question_is_waiting_for_you_not_working()
    {
        var strategist = Node("strategist", "working");
        var run = Run(
            [Node("lead", "blocked"), strategist],
            [new PendingAsk("a", "strategist", "role.strategist", "Bash", "ls", At)]);

        run.Activity(strategist).Should().Be("waiting for you");
    }

    [Fact]
    public void A_lead_whose_workers_are_busy_is_waiting_on_them_by_name()
    {
        var lead = Node("lead", "blocked");
        var run = Run([lead, Node("strategist", "working"), Node("copywriter", "done")]);

        run.Activity(lead).Should().Be("waiting on strategist");
    }

    [Fact]
    public void A_lead_with_nobody_working_is_between_turns()
    {
        var lead = Node("lead", "blocked");

        Run([lead, Node("copywriter", "done")]).Activity(lead).Should().Be("between turns");
    }

    [Theory]
    [InlineData("working", "working")]
    [InlineData("done", "done")]
    [InlineData("ended", "done")]
    [InlineData("failed", "failed")]
    public void A_worker_says_where_it_is(string state, string shown)
    {
        var worker = Node("scheduler", state);

        Run([Node("lead", "blocked"), worker]).Activity(worker).Should().Be(shown);
    }

    [Fact]
    public void Nothing_in_a_finished_run_is_still_working_or_waiting()
    {
        var lead = Node("lead", "done");
        var stray = Node("strategist", "working");
        var run = Run([lead, stray], finished: true);

        run.Activity(stray).Should().Be("ended");
        run.Activity(lead).Should().Be("done");
    }
}
