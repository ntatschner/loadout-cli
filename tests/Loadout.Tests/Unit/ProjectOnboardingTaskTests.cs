using Loadout.Core.Projects;
using Loadout.Models.Tasks;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

public sealed class ProjectOnboardingTaskTests
{
    [Theory]
    [InlineData(null, OnboardingTurn.Onboard)]
    [InlineData("", OnboardingTurn.Onboard)]
    [InlineData("  onboard this project ", OnboardingTurn.Onboard)]
    [InlineData("fix the upload retry", OnboardingTurn.Remind)]
    public void A_pending_onboarding_takes_a_session_only_when_it_came_with_no_task_of_its_own(
        string? task, OnboardingTurn expected) =>
        ProjectOnboardingTask.TurnFor(pending: true, task, attended: true).Should().Be(expected);

    [Fact]
    public void A_team_node_is_never_turned_into_onboarding()
    {
        // A node's task is somebody else's brief. Replacing it would throw the
        // brief away, and nobody is at the keyboard to be reminded.
        ProjectOnboardingTask.TurnFor(pending: true, task: null, attended: false)
            .Should().Be(OnboardingTurn.None);
    }

    [Fact]
    public void Nothing_pending_changes_nothing() =>
        ProjectOnboardingTask.TurnFor(pending: false, task: null, attended: true)
            .Should().Be(OnboardingTurn.None);

    [Theory]
    [InlineData(TaskState.Open, true)]
    [InlineData(TaskState.Doing, true)]
    [InlineData(TaskState.Blocked, true)]
    [InlineData(TaskState.Done, false)]
    [InlineData(TaskState.Dropped, false)]
    public void Onboarding_is_pending_until_it_is_done_or_skipped(TaskState state, bool pending) =>
        ProjectOnboardingTask.Pending([new TaskItem { Id = ProjectOnboardingTask.Id, State = state }])
            .Should().Be(pending);

    [Fact]
    public void An_onboarding_session_keeps_a_mode_somebody_asked_for_and_names_the_skill_once()
    {
        var (task, mode, named) = ProjectOnboardingTask.Onboarding(
            "review", [ProjectOnboardingTask.Skill, "language.csharp"]);

        task.Should().Be(ProjectOnboardingTask.Title);
        mode.Should().Be("review");
        named.Should().BeEquivalentTo([ProjectOnboardingTask.Skill, "language.csharp"]);
    }

    [Fact]
    public void An_onboarding_session_with_no_mode_asked_for_investigates() =>
        ProjectOnboardingTask.Onboarding(null, null).Mode.Should().Be("investigate");
}
