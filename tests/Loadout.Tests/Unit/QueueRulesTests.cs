using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Teams;
using Loadout.Models.Tasks;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What may join a team's queue, and what it runs next.
/// </summary>
/// <remarks>
/// The whole policy is a pure function so it can be tested here without a
/// daemon or a runner: a queue that refuses the wrong thing, or starts the
/// wrong thing, does it at three in the morning where nobody sees it.
/// </remarks>
public sealed class QueueRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 1, 0, 0, TimeSpan.Zero);

    private static TaskItem Ready(string id, TaskState state = TaskState.Open) => new()
    {
        Id = id,
        Title = $"Do {id}",
        State = state,
        DoneWhen = ["the suite passes"],
    };

    private static QueueItem Item(string task, string after = "") =>
        new() { Task = task, Project = "website", After = after };

    private static TeamQueue Queue(params QueueItem[] items) => new() { Items = [.. items] };

    private static Func<string, IReadOnlyList<TaskItem>?> Lists(params TaskItem[] tasks) =>
        project => project == "website" ? tasks : null;

    [Fact]
    public void A_task_with_a_title_and_a_done_when_gets_in()
    {
        QueueRules.Admission(Item("a"), [Ready("a")], Queue()).Should().BeNull();
    }

    [Fact]
    public void A_task_with_no_done_when_is_refused_and_told_how_to_give_it_one()
    {
        var bare = new TaskItem { Id = "a", Title = "Do a" };

        QueueRules.Admission(Item("a"), [bare], Queue()).Should()
            .Contain("has no done-when")
            .And.Contain("loadout task declare a open --done-when");
    }

    [Fact]
    public void Done_when_that_is_only_blank_counts_as_none()
    {
        var blank = new TaskItem { Id = "a", Title = "Do a", DoneWhen = ["  ", ""] };

        QueueRules.Admission(Item("a"), [blank], Queue()).Should().Contain("has no done-when");
    }

    [Fact]
    public void A_task_with_no_title_is_refused_because_a_run_would_have_no_goal()
    {
        var untitled = new TaskItem { Id = "a", DoneWhen = ["x"] };

        QueueRules.Admission(Item("a"), [untitled], Queue()).Should().Contain("no title");
    }

    [Fact]
    public void A_missing_task_is_refused()
    {
        QueueRules.Admission(Item("ghost"), [Ready("a")], Queue()).Should().Contain("There is no task 'ghost'");
    }

    [Fact]
    public void An_idea_is_refused_until_it_is_accepted()
    {
        var idea = Ready("a");
        idea.Kind = TaskKind.Idea;

        QueueRules.Admission(Item("a"), [idea], Queue()).Should().Contain("is an idea");
    }

    [Theory]
    [InlineData(TaskState.Done, "done")]
    [InlineData(TaskState.Dropped, "dropped")]
    public void A_finished_task_is_refused(TaskState state, string word)
    {
        QueueRules.Admission(Item("a"), [Ready("a", state)], Queue()).Should().Contain($"is {word}");
    }

    [Fact]
    public void After_itself_is_refused()
    {
        QueueRules.Admission(Item("a", after: "A"), [Ready("a")], Queue()).Should().Contain("can't wait for itself");
    }

    [Fact]
    public void After_a_task_that_is_not_on_the_list_is_refused()
    {
        QueueRules.Admission(Item("a", after: "ghost"), [Ready("a")], Queue()).Should().Contain("which is not on website's list");
    }

    [Fact]
    public void After_a_dropped_task_is_refused_because_it_would_wait_for_ever()
    {
        QueueRules.Admission(Item("a", after: "b"), [Ready("a"), Ready("b", TaskState.Dropped)], Queue())
            .Should().Contain("which was dropped");
    }

    [Fact]
    public void After_that_closes_a_loop_is_refused_and_names_the_loop()
    {
        var queue = Queue(Item("b", after: "c"), Item("c", after: "a"));

        QueueRules.Admission(Item("a", after: "b"), [Ready("a"), Ready("b"), Ready("c")], queue)
            .Should().Contain("makes a loop").And.Contain("a after c after b after a");
    }

    [Fact]
    public void After_that_only_chains_is_not_a_loop()
    {
        var queue = Queue(Item("b", after: "c"), Item("c"));

        QueueRules.Admission(Item("a", after: "b"), [Ready("a"), Ready("b"), Ready("c")], queue).Should().BeNull();
    }

    [Fact]
    public void The_first_item_in_is_the_first_to_start()
    {
        var decision = QueueRules.Next(Queue(Item("a"), Item("b")), Lists(Ready("a"), Ready("b")), window: null);

        decision.Verdict.Should().Be(QueueVerdict.Start);
        decision.Item!.Task.Should().Be("a");
    }

    [Fact]
    public void An_item_that_no_longer_qualifies_is_passed_over_with_its_reason_and_the_next_starts()
    {
        // Admission again at the start, because a task can be finished or
        // stripped of its done-when between being queued and being run.
        var decision = QueueRules.Next(
            Queue(Item("a"), Item("b")), Lists(Ready("a", TaskState.Done), Ready("b")), window: null);

        decision.Item!.Task.Should().Be("b");
        decision.Skipped.Should().ContainSingle().Which.Reason.Should().Contain("is done");
    }

    [Fact]
    public void A_project_whose_list_cannot_be_read_is_passed_over_with_a_reason()
    {
        var queue = Queue(new QueueItem { Task = "x", Project = "elsewhere" }, Item("a"));

        var decision = QueueRules.Next(queue, Lists(Ready("a")), window: null);

        decision.Item!.Task.Should().Be("a");
        decision.Skipped.Single().Reason.Should().Contain("elsewhere's task list could not be read");
    }

    [Fact]
    public void Only_one_run_goes_at_a_time()
    {
        var queue = Queue(Item("b"));
        queue.Runs.Add(new QueueRun { Task = "a", Project = "website", State = QueueRunState.Running });

        var decision = QueueRules.Next(queue, Lists(Ready("b")), window: null);

        decision.Verdict.Should().Be(QueueVerdict.Wait);
        decision.Reason.Should().Contain("'a' is still running");
    }

    [Fact]
    public void Held_runs_up_to_the_cap_stop_the_queue()
    {
        var queue = Queue(Item("c"));
        queue.Runs.Add(new QueueRun { Task = "a", Project = "website", State = QueueRunState.Held });
        queue.Runs.Add(new QueueRun { Task = "b", Project = "website", State = QueueRunState.Held });

        var decision = QueueRules.Next(queue, Lists(Ready("c")), window: null);

        decision.Verdict.Should().Be(QueueVerdict.Stop);
        decision.Reason.Should().Contain("2 run(s) are waiting for an answer");
    }

    [Fact]
    public void Held_runs_under_the_cap_let_the_next_start()
    {
        var queue = Queue(Item("c"));
        queue.Runs.Add(new QueueRun { Task = "a", Project = "website", State = QueueRunState.Held });

        QueueRules.Next(queue, Lists(Ready("c")), window: null).Verdict.Should().Be(QueueVerdict.Start);
    }

    [Fact]
    public void A_window_stops_at_its_cap_on_runs_counting_only_its_own()
    {
        var queue = Queue(Item("c"));
        queue.Runs.Add(new QueueRun { Task = "old", Project = "website", State = QueueRunState.Met, StartedUtc = Now.AddDays(-1) });
        queue.Runs.Add(new QueueRun { Task = "a", Project = "website", State = QueueRunState.Met, StartedUtc = Now.AddMinutes(5) });

        QueueRules.Next(queue, Lists(Ready("c")), new QueueWindow(Now, null, 2)).Verdict.Should().Be(QueueVerdict.Start);

        queue.Runs.Add(new QueueRun { Task = "b", Project = "website", State = QueueRunState.Failed, StartedUtc = Now.AddMinutes(30) });

        var decision = QueueRules.Next(queue, Lists(Ready("c")), new QueueWindow(Now, null, 2));
        decision.Verdict.Should().Be(QueueVerdict.Stop);
        decision.Reason.Should().Contain("started 2 run(s), its cap");
    }

    [Fact]
    public void A_window_stops_at_its_cap_on_spending()
    {
        var queue = Queue(Item("c"));
        queue.Runs.Add(new QueueRun { Task = "a", Project = "website", State = QueueRunState.Met, StartedUtc = Now, CostUsd = 6m });
        queue.Runs.Add(new QueueRun { Task = "b", Project = "website", State = QueueRunState.Failed, StartedUtc = Now, CostUsd = 4.5m });

        var decision = QueueRules.Next(queue, Lists(Ready("c")), new QueueWindow(Now, 10m, null));

        decision.Verdict.Should().Be(QueueVerdict.Stop);
        decision.Reason.Should().Contain("$10.50 of its $10.00");
    }

    [Fact]
    public void A_person_asking_by_hand_is_not_held_to_the_window_caps()
    {
        var queue = Queue(Item("c"));
        queue.Runs.Add(new QueueRun { Task = "a", Project = "website", State = QueueRunState.Met, StartedUtc = Now, CostUsd = 50m });

        QueueRules.Next(queue, Lists(Ready("c")), window: null).Verdict.Should().Be(QueueVerdict.Start);
    }

    [Fact]
    public void An_item_waits_for_the_task_it_comes_after_and_the_one_behind_it_starts()
    {
        var decision = QueueRules.Next(
            Queue(Item("b", after: "a"), Item("c")), Lists(Ready("a"), Ready("b"), Ready("c")), window: null);

        decision.Item!.Task.Should().Be("c");
        decision.Skipped.Single().Reason.Should().Contain("'b' waits for 'a', which has not run yet");
    }

    [Fact]
    public void An_item_starts_once_the_task_it_comes_after_has_met_its_done_when()
    {
        var queue = Queue(Item("b", after: "a"));
        queue.Runs.Add(new QueueRun { Task = "a", Project = "website", State = QueueRunState.Met });

        QueueRules.Next(queue, Lists(Ready("a", TaskState.Blocked), Ready("b")), window: null).Item!.Task.Should().Be("b");
    }

    [Fact]
    public void An_item_starts_once_the_task_it_comes_after_is_done_by_any_route()
    {
        QueueRules.Next(Queue(Item("b", after: "a")), Lists(Ready("a", TaskState.Done), Ready("b")), window: null)
            .Item!.Task.Should().Be("b");
    }

    [Fact]
    public void An_item_after_a_failed_run_waits_and_says_what_failed()
    {
        var queue = Queue(Item("b", after: "a"));
        queue.Runs.Add(new QueueRun { Task = "a", Project = "website", State = QueueRunState.Failed, Reason = "over budget" });

        var decision = QueueRules.Next(queue, Lists(Ready("a", TaskState.Blocked), Ready("b")), window: null);

        decision.Verdict.Should().Be(QueueVerdict.Wait);
        decision.Reason.Should().Contain("whose run failed (over budget)");
    }

    [Fact]
    public void An_empty_queue_stops()
    {
        QueueRules.Next(Queue(), Lists(), window: null).Should()
            .Match<QueueDecision>(one => one.Verdict == QueueVerdict.Stop && one.Reason == "The queue is empty.");
    }

    [Fact]
    public void The_plan_runs_a_split_plan_in_order_and_stops_at_the_window_cap()
    {
        var queue = Queue(Item("two", after: "one"), Item("one"), Item("three", after: "two"), Item("bare"));
        queue.Settings.WindowRuns = 2;

        var bare = new TaskItem { Id = "bare", Title = "Bare" };
        var plan = TeamQueuePlanCommand.Plan(queue, Lists(Ready("one"), Ready("two"), Ready("three"), bare), Now);

        plan.Order.Select(one => one.Task).Should().Equal("one", "two");
        plan.Stops.Should().Contain("its cap");
    }

    [Fact]
    public void The_plan_names_what_it_would_leave_and_why()
    {
        var bare = new TaskItem { Id = "bare", Title = "Bare" };
        var plan = TeamQueuePlanCommand.Plan(Queue(Item("one"), Item("bare")), Lists(Ready("one"), bare), Now);

        plan.Order.Select(one => one.Task).Should().Equal("one");
        plan.Left.Should().ContainSingle().Which.Reason.Should().Contain("has no done-when");
    }
}
