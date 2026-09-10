using Loadout.Core.Tasks;
using Loadout.Models.Diagnostics;
using Loadout.Models.Tasks;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Whether a project writing down what it is working on, and showing it to
/// nobody, gets told so.
/// <para>
/// A project carries its open tasks into a session only when it is asked to,
/// and most are never asked. So one session records where something stands, the
/// next is not shown it, and nothing about either looks wrong. That is the case
/// worth speaking up about — and the empty record next to it is the case worth
/// staying quiet about, because a suggestion that fires on nothing is one
/// people learn to skim past.
/// </para>
/// </summary>
public sealed class TaskSuggestionTests
{
    private static TaskItem Task(TaskState state) =>
        new() { Id = "t1", Title = "something", State = state, DeclaredBy = "claude" };

    [Fact]
    public void A_project_with_work_waiting_is_offered_the_switch_that_shows_it()
    {
        var check = TaskContextDiagnosticContributor.Suggestion(
            "storefront-api",
            [Task(TaskState.Open), Task(TaskState.Doing)]);

        check.Should().NotBeNull("two tasks are recorded and no session is shown either");

        check!.Detail.Should().Contain("2 task(s)");

        check.Remedy.Should().NotBeNull("the whole point is that it can be turned on from here");
        check.Remedy!.Kind.Should().Be(RemedyKind.CarryProjectContext);
        check.Remedy.Target.Should().Be("storefront-api=tasks");
    }

    [Fact]
    public void It_is_a_suggestion_rather_than_a_warning()
    {
        var check = TaskContextDiagnosticContributor.Suggestion("demo", [Task(TaskState.Open)]);

        // The verdict is the worst severity in the report. A project that has
        // not opted into an optional feature must not make a working machine
        // read as DEGRADED, or the warnings that do matter stop being read.
        check!.Severity.Should().Be(DiagnosticSeverity.Info);

        new DiagnosticReport([check]).Verdict.Should().Be("HEALTHY");
    }

    [Fact]
    public void A_suggestion_is_counted_apart_from_a_repair()
    {
        var suggestion = TaskContextDiagnosticContributor.Suggestion("demo", [Task(TaskState.Open)])!;

        var repair = DiagnosticCheck.Warn(
            "Repository",
            "Pre-commit protection",
            "not installed in this clone",
            new Remedy(RemedyKind.InstallPreCommitHook, "Install it", "/repo"));

        var report = new DiagnosticReport([suggestion, repair]);

        report.Repairs.Should().ContainSingle("one thing here is actually wrong");
        report.Suggestions.Should().ContainSingle("one thing here is available and off");

        // Both still reachable by --fix; only the counting differs.
        report.Remedies.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(TaskState.Done)]
    [InlineData(TaskState.Dropped)]
    public void Work_nobody_has_to_act_on_earns_no_suggestion(TaskState state)
    {
        TaskContextDiagnosticContributor.Suggestion("demo", [Task(state)])
            .Should().BeNull("a finished record is not one being ignored");
    }

    [Fact]
    public void An_empty_record_earns_no_suggestion()
    {
        TaskContextDiagnosticContributor.Suggestion("demo", [])
            .Should().BeNull("there is nothing to show anybody");
    }
}
