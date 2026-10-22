using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models.Tasks;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// How the queue reads a run it started: ended, held for a person, or gone
/// with its process. And what it says about them afterwards.
/// </summary>
public sealed class QueueSettlingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 3, 0, 0, TimeSpan.Zero);

    private static QueueRun Run(string runId = "", int minutesAgo = 1) => new()
    {
        Task = "login",
        Project = "website",
        RunId = runId,
        StartedUtc = Now.AddMinutes(-minutesAgo),
    };

    internal static RunSummary Summary(
        string runId,
        string? ended,
        bool finished,
        IReadOnlyList<PendingAsk>? gates = null,
        string task = "login",
        int startedMinutesAgo = 1,
        decimal cost = 1.5m) =>
        new(
            runId,
            Directory: "dir",
            Team: "builders",
            Goal: "Fix the login",
            Autonomy: "autonomous",
            Started: Now.AddMinutes(-startedMinutesAgo),
            Finished: finished ? Now : null,
            Ended: ended,
            CostUsd: cost,
            Rounds: 2,
            Nodes: [],
            Merged: [],
            Branches: [],
            Project: "website",
            Gates: gates,
            Task: task);

    private static PendingAsk Gate() =>
        new("g1", "lead", "project-lead", "Bash", "git push", Now, Kind: "permission");

    [Fact]
    public void A_finished_run_whose_lead_said_done_met_its_done_when()
    {
        var standing = QueueSettling.Standing(Run("r1"), Summary("r1", "done", finished: true), alive: false, Now);

        standing.Should().Be(new QueueStanding(QueueRunState.Met, "r1", "every done-when met", 1.5m));
    }

    [Theory]
    [InlineData("blocked")]
    [InlineData("over budget")]
    public void A_finished_run_ending_any_other_way_failed_with_that_reason(string ended)
    {
        var standing = QueueSettling.Standing(Run("r1"), Summary("r1", ended, finished: true), alive: null, Now);

        standing.State.Should().Be(QueueRunState.Failed);
        standing.Reason.Should().Be(ended);
    }

    [Fact]
    public void A_finished_run_that_never_said_how_it_ended_failed()
    {
        QueueSettling.Standing(Run("r1"), Summary("r1", null, finished: true), alive: null, Now)
            .Reason.Should().Contain("without saying how it ended");
    }

    [Fact]
    public void A_run_waiting_on_a_gate_is_held_and_says_what_it_asks()
    {
        var standing = QueueSettling.Standing(Run("r1"), Summary("r1", null, finished: false, gates: [Gate()]), alive: true, Now);

        standing.State.Should().Be(QueueRunState.Held);
        standing.Reason.Should().StartWith("waiting for an answer");
    }

    [Fact]
    public void A_run_going_with_nothing_asked_is_running()
    {
        QueueSettling.Standing(Run("r1"), Summary("r1", null, finished: false), alive: true, Now)
            .State.Should().Be(QueueRunState.Running);
    }

    [Fact]
    public void A_run_whose_process_died_mid_run_failed_rather_than_going_for_ever()
    {
        var standing = QueueSettling.Standing(Run("r1"), Summary("r1", null, finished: false, gates: [Gate()]), alive: false, Now);

        standing.State.Should().Be(QueueRunState.Failed);
        standing.Reason.Should().Be("stopped with the process running it");
    }

    [Fact]
    public void A_process_gone_before_any_journal_failed_at_once()
    {
        QueueSettling.Standing(Run(), summary: null, alive: false, Now).Reason.Should().Be("the run stopped before it started");
    }

    [Fact]
    public void A_run_with_no_process_and_no_journal_is_given_time_then_taken_as_never_started()
    {
        QueueSettling.Standing(Run(minutesAgo: 5), summary: null, alive: null, Now).State.Should().Be(QueueRunState.Running);
        QueueSettling.Standing(Run(minutesAgo: 11), summary: null, alive: null, Now).Reason.Should().Be("the run never started");
    }

    [Fact]
    public void A_run_without_an_id_is_found_by_its_task_among_runs_started_after_it_and_not_the_newest()
    {
        var before = Summary("old", "done", finished: true, startedMinutesAgo: 30);
        var other = Summary("other-task", null, finished: false, task: "docs", startedMinutesAgo: 0);
        var mine = Summary("mine", null, finished: false, startedMinutesAgo: 0);

        QueueSettling.Find(Run(minutesAgo: 1), [other, mine, before])!.RunId.Should().Be("mine");
    }

    [Fact]
    public void A_run_with_an_id_is_found_by_it()
    {
        QueueSettling.Find(Run("old"), [Summary("old", "done", true, startedMinutesAgo: 30)])!.RunId.Should().Be("old");
    }

    [Fact]
    public void The_branch_names_the_task_safely_and_when()
    {
        QueueSettling.Branch(new QueueItem { Task = "Fix Login!" }, Now).Should().Be("queue/fix-login--20261022-0300");
    }

    [Fact]
    public void The_pull_request_body_carries_the_confirmed_done_when_and_how_the_lead_read_each()
    {
        var task = new TaskItem { Id = "login", Title = "Fix the login", DoneWhen = ["the suite passes", "a wrong password says so"] };
        var run = Summary("r1", "done", finished: true) with
        {
            Readings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["the suite passes"] = "met" },
        };

        var body = QueueSettling.Body(task, run, "builders");

        body.Should().Contain("## Done when")
            .And.Contain("- the suite passes - the lead read it as met")
            .And.Contain("- a wrong password says so\n")
            .And.Contain("team builders' queue, run r1")
            .And.Contain("Nothing reaches the base branch until a person merges it.");
    }

    [Fact]
    public void The_window_summary_says_what_each_run_came_to_and_what_was_spent()
    {
        var runs = new List<QueueRun>
        {
            new() { Task = "a", State = QueueRunState.Met, PullRequest = "https://example.test/pull/7", CostUsd = 2m },
            new() { Task = "b", State = QueueRunState.Met, Branch = "queue/b", CostUsd = 1m },
            new() { Task = "c", State = QueueRunState.Held, Reason = "waiting for an answer: push?", CostUsd = 0.5m },
            new() { Task = "d", State = QueueRunState.Failed, Reason = "over budget", RunId = "r9", CostUsd = 3m },
        };

        var (title, detail) = QueueSettling.Summary("builders", runs, [new QueueItem { Task = "e" }]);

        title.Should().Be("builders' queue window: 4 run(s), $6.50");
        detail.Should().Contain("a: done-when met, draft pull request https://example.test/pull/7")
            .And.Contain("b: done-when met, on branch queue/b (no pull request)")
            .And.Contain("c: waiting for you - waiting for an answer: push?")
            .And.Contain("d: blocked - over budget (run r9)")
            .And.Contain("1 still in the queue.");
    }

    [Fact]
    public void A_window_that_ran_nothing_says_so()
    {
        QueueSettling.Summary("builders", [], []).Title.Should().Contain("closed with nothing run");
    }
}
