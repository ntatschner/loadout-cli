using FluentAssertions;
using Loadout.Core.Tasks;
using Loadout.Models.Tasks;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Ranking tasks for "easy" and "needs work first" from the fields tasks have.
/// </summary>
public sealed class TaskScorerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private static TaskItem Task(
        string id,
        string title = "Do a thing",
        int ageDays = 1,
        string detail = "",
        int doneWhen = 0,
        bool blockedOnce = false)
    {
        var task = new TaskItem
        {
            Id = id,
            Title = title,
            Detail = detail,
            DeclaredUtc = Now.AddDays(-ageDays),
            DoneWhen = Enumerable.Range(1, doneWhen).Select(n => $"condition {n}").ToList(),
        };

        task.History.Add(new TaskEvent { State = TaskState.Open, Utc = Now.AddDays(-ageDays) });

        if (blockedOnce)
        {
            task.History.Add(new TaskEvent { State = TaskState.Blocked, Utc = Now.AddDays(-ageDays + 0.5) });
            task.History.Add(new TaskEvent { State = TaskState.Open, Utc = Now.AddDays(-ageDays + 0.6) });
        }

        return task;
    }

    private static IReadOnlyList<RankedTask> Rank(TaskPick pick, params TaskItem[] tasks) =>
        TaskScorer.Rank(tasks.Select(task => ("p", task)), pick, Now);

    [Fact]
    public void Easy_puts_a_short_task_with_a_done_when_ahead_of_a_long_one_without()
    {
        var ranked = Rank(
            TaskPick.Easy,
            Task("big", detail: new string('x', 3000)),
            Task("small", detail: "One line.", doneWhen: 1));

        ranked.Select(one => one.Task.Id).Should().Equal("small", "big");
        ranked[0].Reasons.Should().Contain(reason => reason.Contains("done-when")).And.Contain("short description");
        ranked[1].Reasons.Should().Contain("long description, likely a larger piece of work");
    }

    [Fact]
    public void Easy_counts_against_a_task_that_has_been_blocked()
    {
        var ranked = Rank(TaskPick.Easy, Task("stuck", blockedOnce: true), Task("clear"));

        ranked[0].Task.Id.Should().Be("clear");
        ranked[1].Reasons.Should().Contain("has been blocked before");
    }

    [Fact]
    public void Needs_work_first_puts_an_old_defect_ahead_of_a_new_feature()
    {
        var ranked = Rank(
            TaskPick.NeedsWorkFirst,
            Task("feature", "Add a shiny panel", ageDays: 2),
            Task("defect", "Fix the crash on resume", ageDays: 30));

        ranked[0].Task.Id.Should().Be("defect");
        ranked[0].Reasons.Should().Contain("waiting 30 days").And.Contain("reads as a defect ('fix')");
    }

    [Fact]
    public void A_defect_word_must_be_a_whole_word()
    {
        TaskScorer.Score("p", Task("t", "Prefix the names"), TaskPick.NeedsWorkFirst, Now)
            .Reasons.Should().NotContain(reason => reason.StartsWith("reads as a defect", StringComparison.Ordinal));
    }

    [Fact]
    public void Age_is_capped_so_one_ancient_task_does_not_outrank_everything()
    {
        var ancient = TaskScorer.Score("p", Task("old", ageDays: 400), TaskPick.NeedsWorkFirst, Now);

        ancient.Score.Should().Be(4);
    }

    [Fact]
    public void Ties_go_to_the_task_waiting_longest_then_by_id()
    {
        var ranked = Rank(TaskPick.Any, Task("b", ageDays: 3), Task("a", ageDays: 3), Task("c", ageDays: 9));

        ranked.Select(one => one.Task.Id).Should().Equal("c", "a", "b");
    }
}
