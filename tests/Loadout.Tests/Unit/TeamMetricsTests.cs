using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Models.Results;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a machine has learned about its own team file.
/// </summary>
/// <remarks>
/// <para>
/// One run refusing twenty tool calls is a bad afternoon. Every run of one role
/// refusing twenty is a role whose permissions are written wrong, and the
/// second only shows up once the first is counted across runs.
/// </para>
/// <para>
/// Split by model as well as by role, because the question worth answering is
/// not "what does the reviewer cost" but "what does it cost on this model
/// rather than that one, and does it finish".
/// </para>
/// </remarks>
public sealed class TeamMetricsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void One_role_across_several_runs_is_one_row()
    {
        var read = TeamMetrics.Across(new Runs(
            Run("a", Node("reviewer/1", "role.reviewer", 0.20m, denials: 3)),
            Run("b", Node("reviewer/1", "role.reviewer", 0.30m, denials: 5))));

        read.Should().ContainSingle();
        read[0].Runs.Should().Be(2);
        read[0].CostUsd.Should().Be(0.50m);
        read[0].Denials.Should().Be(8);
        read[0].Each.Should().Be(0.25m);
        read[0].Refusals.Should().Be(4);
    }

    [Fact]
    public void The_same_role_on_a_different_model_is_a_different_row()
    {
        // The whole reason the model is written down: "the reviewer costs
        // twenty pence" is not a fact anybody can act on until it says which
        // model it cost that on.
        var read = TeamMetrics.Across(new Runs(
            Run("a", Node("reviewer/1", "role.reviewer", 0.60m, model: "claude-opus-5")),
            Run("b", Node("reviewer/1", "role.reviewer", 0.20m, model: "claude-haiku-4-5"))));

        read.Should().HaveCount(2);
        read.Select(one => one.Model).Should().BeEquivalentTo(["claude-opus-5", "claude-haiku-4-5"]);
    }

    [Fact]
    public void Runs_that_never_said_which_model_are_grouped_apart_rather_than_guessed_at()
    {
        var read = TeamMetrics.Across(new Runs(
            Run("a", Node("reviewer/1", "role.reviewer", 0.60m, model: "claude-opus-5")),
            Run("b", Node("reviewer/1", "role.reviewer", 0.20m))));

        read.Single(one => one.Model is null).Runs.Should().Be(1);
    }

    [Fact]
    public void The_dearest_role_is_first_because_it_is_the_one_somebody_acts_on()
    {
        // A table sorted by name buries the expensive row wherever the
        // alphabet happens to put it.
        var read = TeamMetrics.Across(new Runs(Run(
            "a",
            Node("aardvark/1", "role.aardvark", 0.10m),
            Node("zebra/1", "role.zebra", 9.00m))));

        read[0].Role.Should().Be("role.zebra");
    }

    [Fact]
    public void How_often_a_roles_reports_are_taken_is_counted_from_its_own_turns()
    {
        var run = Run("a", Node("reviewer/1", "role.reviewer", 0.20m)) with
        {
            Exchanges =
            [
                new RunTurn(Noon, "reviewer/1", 1, 1, 2, 0.1m, 0, true, "done", "accepted"),
                new RunTurn(Noon, "reviewer/1", 1, 2, 2, 0.1m, 0, true, "done", "rejected"),

                // Somebody else's turn, which is not this role's business.
                new RunTurn(Noon, "lead", 1, 1, 2, 0.1m, 0, true, "done", "accepted"),
            ],
        };

        var read = TeamMetrics.Across(new Runs(run));
        var reviewer = read.Single(one => one.Role == "role.reviewer");

        reviewer.Accepted.Should().Be(1);
        reviewer.Rejected.Should().Be(1);
        reviewer.Accepting.Should().Be(0.5);
    }

    [Fact]
    public void A_turn_that_never_reported_counts_as_neither()
    {
        // A turn that did not finish is a different thing from one whose report
        // was refused, and rolling them together would make a role look worse
        // than it is at the one job the number is about.
        var run = Run("a", Node("reviewer/1", "role.reviewer", 0.20m)) with
        {
            Exchanges = [new RunTurn(Noon, "reviewer/1", 1, 1, 2, 0.1m, 0, Completed: false)],
        };

        var reviewer = TeamMetrics.Across(new Runs(run)).Single();

        reviewer.Accepted.Should().Be(0);
        reviewer.Rejected.Should().Be(0);
        reviewer.Accepting.Should().BeNull("it made no report either way");
    }

    [Fact]
    public void A_machine_with_no_runs_has_nothing_to_say_rather_than_failing()
    {
        TeamMetrics.Across(new Runs()).Should().BeEmpty();
    }

    [Fact]
    public void A_run_that_will_not_read_is_skipped_rather_than_stopping_the_rest()
    {
        // One unreadable journal among forty is ordinary; losing the other
        // thirty-nine over it is not.
        var read = TeamMetrics.Across(new Runs(Run("a", Node("lead", "role.project-lead", 0.10m)))
        {
            Broken = "b",
        });

        read.Should().ContainSingle();
    }

    private static RunNode Node(
        string name,
        string role,
        decimal cost,
        int denials = 0,
        string? model = null) =>
        new(name, role, "done", 2, cost, Noon.AddMinutes(1),
            Denials: denials, Started: Noon, Model: model);

    private static RunSummary Run(string id, params RunNode[] nodes) =>
        new(id, "d", "docs-crew", "goal", "autonomous", Noon, Noon.AddMinutes(5), "done",
            nodes.Sum(node => node.CostUsd), 1, nodes, [], []);

    private sealed class Runs(params RunSummary[] runs) : IRunJournal
    {
        /// <summary>A run whose journal will not read, if a test wants one.</summary>
        public string? Broken { get; init; }

        public IReadOnlyList<string> List(int limit = 20) =>
            [.. runs.Select(run => run.RunId), .. Broken is null ? Array.Empty<string>() : [Broken]];

        public OperationResult<IReadOnlyList<RunEvent>> Read(string runId) =>
            OperationResult<IReadOnlyList<RunEvent>>.Ok([]);

        public OperationResult<RunSummary> Summarise(string runId) =>
            runs.FirstOrDefault(run => run.RunId == runId) is { } found
                ? OperationResult<RunSummary>.Ok(found)
                : OperationResult<RunSummary>.Fail("that one will not read", ExitCode.GeneralFailure);

        public string DirectoryOf(string runId) => "C:/runs/" + runId;

        /// <summary>
        /// Nothing here deletes anything: these tests read runs.
        /// </summary>
        /// <remarks>
        /// A double that quietly reported success would be asserting its own
        /// opinion about a thing with no undo. Forgetting a run is asserted
        /// against a real disk, in RunForgettingTests.
        /// </remarks>
        public OperationResult<RunForgotten> Forget(string runId, bool force = false) =>
            throw new NotSupportedException("these tests read runs, they do not delete them");

    }
}
