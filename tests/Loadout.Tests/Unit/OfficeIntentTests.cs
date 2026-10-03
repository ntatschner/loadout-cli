using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What the office shows for a node: the light on its desk, where the person
/// is and how they are sitting.
/// </summary>
/// <remarks>
/// Worked out on the page until now, in two places that disagreed about
/// whether a node was waiting, and neither of which knew a run had ended.
/// </remarks>
public sealed class OfficeIntentTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    private static RunNode Node(string name, string state) =>
        new(name, "role." + name, state, 1, 0m, At);

    private static PendingAsk Question(string node) =>
        new("a", node, "role." + node, "Bash", "ls", At);

    private static RunSummary Run(
        IReadOnlyList<RunNode> nodes,
        IReadOnlyList<PendingAsk>? gates = null,
        bool finished = false) =>
        new("r", "d", "team", "goal", "supervised", At, finished ? At.AddHours(1) : null, null, 0m, 1,
            nodes, [], [], Gates: gates);

    [Theory]
    [InlineData("working")]
    [InlineData("failed")]
    [InlineData("done")]
    public void A_question_on_a_node_wins_whatever_it_last_reported(string state)
    {
        var asked = Node("strategist", state);
        var run = Run([Node("lead", "blocked"), asked], [Question("strategist")]);

        OfficeIntent.For(run, asked).Should().Be(
            new OfficeIntent(OfficeLamp.Waiting, OfficePlace.Desk, OfficePose.Idle, "?"));
    }

    [Fact]
    public void A_working_node_types_at_its_desk()
    {
        var worker = Node("implementer", "working");

        OfficeIntent.For(Run([Node("lead", "blocked"), worker]), worker).Should().Be(
            new OfficeIntent(OfficeLamp.Working, OfficePlace.Desk, OfficePose.Type, null));
    }

    [Fact]
    public void Nobody_types_in_a_run_that_has_ended()
    {
        var stray = Node("implementer", "working");

        OfficeIntent.For(Run([Node("lead", "done"), stray], finished: true), stray).Should().Be(
            new OfficeIntent(OfficeLamp.Done, OfficePlace.Gone, OfficePose.Idle, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_failed_node_stays_slumped_at_its_desk_after_the_run(bool finished)
    {
        var broken = Node("implementer", "failed");

        OfficeIntent.For(Run([Node("lead", "blocked"), broken], finished: finished), broken).Should().Be(
            new OfficeIntent(OfficeLamp.Failed, OfficePlace.Desk, OfficePose.Slump, "!"));
    }

    [Theory]
    [InlineData("done")]
    [InlineData("ended")]
    public void A_finished_node_has_gone_home(string state)
    {
        var finished = Node("reviewer", state);

        OfficeIntent.For(Run([Node("lead", "blocked"), finished]), finished).Place
            .Should().Be(OfficePlace.Gone);
    }

    [Fact]
    public void A_lead_whose_workers_are_busy_sits_and_watches()
    {
        var lead = Node("lead", "blocked");

        OfficeIntent.For(Run([lead, Node("implementer", "working")]), lead).Should().Be(
            new OfficeIntent(OfficeLamp.Quiet, OfficePlace.Desk, OfficePose.Sit, null));
    }

    [Theory]
    [InlineData("lead", "blocked")]
    [InlineData("implementer", "blocked")]
    [InlineData("implementer", "needs-decision")]
    public void Only_a_node_nothing_is_asking_of_is_free(string name, string state)
    {
        var idle = Node(name, state);
        var nodes = name == "lead" ? new[] { idle, Node("reviewer", "done") } : [Node("lead", "working"), idle];

        OfficeIntent.For(Run(nodes), idle).Should().Be(
            new OfficeIntent(OfficeLamp.Quiet, OfficePlace.Free, OfficePose.Idle, null));
    }
}
