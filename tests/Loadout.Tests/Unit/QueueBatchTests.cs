using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Teams;
using Loadout.Models.Tasks;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Putting an approved batch at the front of a team's queue, so the cap it
/// was approved under is spent on it and nothing else.
/// </summary>
public sealed class QueueBatchTests
{
    private static TaskItem Task(string id, bool doneWhen = true) => new()
    {
        Id = id,
        Title = $"Do {id}",
        DoneWhen = doneWhen ? ["it is done"] : [],
    };

    private static QueueItem Item(string id, string project = "web") => new() { Task = id, Project = project };

    private static readonly IReadOnlyList<TaskItem> Web = [Task("a"), Task("b"), Task("c"), Task("loose", doneWhen: false)];

    private static IReadOnlyList<TaskItem>? Lists(string project) => project == "web" ? Web : null;

    [Fact]
    public void The_batch_goes_to_the_front_in_its_own_order_and_what_was_waiting_follows_in_its_own()
    {
        var queue = new TeamQueue { Items = [Item("waiting-1"), Item("b"), Item("waiting-2")] };

        QueueBatch.ToFront(queue, [Item("c"), Item("b"), Item("a")], Lists).Should().BeNull();

        queue.Items.Select(item => item.Task).Should().Equal("c", "b", "a", "waiting-1", "waiting-2");
    }

    [Fact]
    public void A_task_already_queued_is_moved_rather_than_added_twice()
    {
        var queued = Item("b");
        var queue = new TeamQueue { Items = [Item("waiting"), queued] };

        QueueBatch.ToFront(queue, [Item("b")], Lists);

        queue.Items.Should().HaveCount(2);
        queue.Items[0].Should().BeSameAs(queued);
    }

    [Fact]
    public void A_task_the_queue_would_refuse_changes_nothing_and_says_why()
    {
        var queue = new TeamQueue { Items = [Item("waiting")] };

        var refused = QueueBatch.ToFront(queue, [Item("a"), Item("loose")], Lists);

        refused.Should().Contain("'loose' has no done-when");
        queue.Items.Select(item => item.Task).Should().Equal("waiting");
    }

    [Fact]
    public void A_task_with_a_run_going_is_refused()
    {
        var queue = new TeamQueue { Runs = [new QueueRun { Task = "a", Project = "web", State = QueueRunState.Held }] };

        QueueBatch.ToFront(queue, [Item("a")], Lists).Should().Contain("already has a queued run going");
    }

    [Fact]
    public void A_list_that_could_not_be_read_is_a_refusal_not_a_crash()
    {
        QueueBatch.ToFront(new TeamQueue(), [Item("x", "api")], Lists).Should().Be("api's task list could not be read.");
    }

    [Theory]
    [InlineData(25, 10, 15)]
    [InlineData(25, 30, 0)]
    public void What_is_left_of_a_cap_never_goes_below_zero(decimal cap, decimal spent, decimal left)
    {
        QueueBatch.Left(cap, spent).Should().Be(left);
    }

    [Theory]
    [InlineData("web/fix-login", "web", "fix-login")]
    [InlineData(" web / fix-login ", "web", "fix-login")]
    public void A_task_is_named_as_project_and_id(string given, string project, string task)
    {
        TeamQueueRunNowCommand.Split(given).Should().Be((project, task));
    }

    [Theory]
    [InlineData("fix-login")]
    [InlineData("/fix-login")]
    [InlineData("web/")]
    public void Anything_else_is_not_a_task(string given)
    {
        TeamQueueRunNowCommand.Split(given).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("lots")]
    public void Running_a_batch_needs_a_cap_said_out_loud(string? usd)
    {
        new TeamQueueRunNowCommand.Settings { Team = "bug-hunt", Tasks = ["web/a"], Usd = usd }
            .Validate().Successful.Should().BeFalse();
    }

    [Theory]
    [InlineData("25")]
    [InlineData("none")]
    public void A_figure_or_none_is_a_cap(string usd)
    {
        new TeamQueueRunNowCommand.Settings { Team = "bug-hunt", Tasks = ["web/a"], Usd = usd }
            .Validate().Successful.Should().BeTrue();
    }
}
