using FluentAssertions;
using Loadout.Core.Tasks;
using Loadout.Core.Teams;
using Loadout.Models.Tasks;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Whether a team can be run for a task, what its lead is told, and the id a
/// task added from the dashboard is given.
/// </summary>
public sealed class TaskRunsTests
{
    private static readonly TaskItem[] List =
    [
        new() { Id = "fix-login", Title = "Fix the login", State = TaskState.Open },
        new() { Id = "stuck", Title = "Stuck", State = TaskState.Blocked },
        new() { Id = "shipped", Title = "Shipped", State = TaskState.Done },
        new() { Id = "abandoned", Title = "Abandoned", State = TaskState.Dropped },
        new() { Id = "a-thought", Title = "A thought", Kind = TaskKind.Idea },
    ];

    [Theory]
    [InlineData("fix-login")]
    [InlineData("FIX-LOGIN")]
    [InlineData("stuck")]
    public void A_task_still_to_be_done_can_be_run(string id)
    {
        TaskRuns.Rejection(id, "website", List).Should().BeNull();
    }

    [Fact]
    public void A_misspelt_id_is_refused_rather_than_declared_into_existence()
    {
        TaskRuns.Rejection("fix-logn", "website", List).Should()
            .Contain("There is no task 'fix-logn' on website's list")
            .And.Contain("loadout task list --project website");
    }

    [Fact]
    public void An_idea_is_refused_until_it_has_been_accepted()
    {
        TaskRuns.Rejection("a-thought", "website", List).Should().Contain("is an idea").And.Contain("Accept it first");
    }

    [Theory]
    [InlineData("shipped", "done")]
    [InlineData("abandoned", "dropped")]
    public void A_finished_task_is_refused_and_told_how_to_reopen_it(string id, string state)
    {
        TaskRuns.Rejection(id, "website", List).Should()
            .Contain($"is {state}")
            .And.Contain($"loadout task declare {id} open --project website");
    }

    [Fact]
    public void The_lead_is_given_the_title_with_the_note_beneath()
    {
        TaskRuns.Goal(new TaskItem { Id = "x", Title = " Fix the login ", Note = " After the cookie change. " })
            .Should().Be("Fix the login\n\nAfter the cookie change.");

        TaskRuns.Goal(new TaskItem { Id = "x", Title = "Fix the login" }).Should().Be("Fix the login");
        TaskRuns.Goal(new TaskItem { Id = "untitled" }).Should().Be("untitled", "a goal is never empty");
    }

    [Fact]
    public void An_added_task_is_named_from_its_title_and_numbered_past_any_taken()
    {
        TaskIds.From("Fix the login page, please!", []).Should().Be("fix-the-login-page-please");
        TaskIds.From("Fix the login", ["fix-the-login", "FIX-THE-LOGIN-2"]).Should().Be("fix-the-login-3");
        TaskIds.From("!!!", []).Should().Be("task");
        TaskIds.Rejection(TaskIds.From("A very long title that goes on and on for many words indeed", []))
            .Should().BeNull("whatever it makes is an id the list accepts");
    }

    [Fact]
    public void The_task_a_run_was_started_for_is_read_back_from_its_journal()
    {
        // Which is how a resume, told only the run, goes on moving that task.
        var summary = RunJournal.Fold("r1", "dir",
        [
            new RunEvent(DateTimeOffset.UnixEpoch, null, "run.started",
                System.Text.Json.JsonSerializer.SerializeToElement(new { team = "t", goal = "g", task = "fix-login" })),
        ]);

        summary.Task.Should().Be("fix-login");

        RunJournal.Fold("r2", "dir",
        [
            new RunEvent(DateTimeOffset.UnixEpoch, null, "run.started",
                System.Text.Json.JsonSerializer.SerializeToElement(new { team = "t", goal = "g" })),
        ]).Task.Should().BeNull("a run of its own was started for no task");
    }
}
