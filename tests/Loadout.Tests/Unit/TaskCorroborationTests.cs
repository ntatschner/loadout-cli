using FluentAssertions;
using Loadout.Core.Tasks;
using Loadout.Models.Tasks;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What the record does and does not back up.
/// </summary>
/// <remarks>
/// The limit worth stating before any of the rules: this can say a claim is
/// unsupported, and can never say a claim is wrong. Work happens that leaves no
/// commit, and commits happen that name nothing. Everything here is an
/// observation somebody can dismiss in a second if they know better.
/// </remarks>
public sealed class TaskCorroborationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static TaskItem Task(string id, TaskState state, DateTimeOffset declared) =>
        new() { Id = id, Title = id, State = state, DeclaredBy = "someone", DeclaredUtc = declared };

    private static CommitSummary Commit(DateTimeOffset when, string subject = "some work") =>
        new("0123456789abcdef", when, subject);

    [Fact]
    public void Called_done_with_nothing_committed_since_is_worth_saying()
    {
        var found = TaskCorroboration.Check(
            [Task("a", TaskState.Done, Now.AddHours(-1))],
            [Commit(Now.AddHours(-5))],
            Now);

        found.Should().ContainSingle().Which.TaskId.Should().Be("a");
    }

    [Fact]
    public void Called_done_with_something_committed_since_is_left_alone()
    {
        TaskCorroboration.Check(
            [Task("a", TaskState.Done, Now.AddHours(-5))],
            [Commit(Now.AddHours(-1))],
            Now)
            .Should().BeEmpty();
    }

    [Fact]
    public void A_commit_at_the_moment_of_the_claim_counts()
    {
        // Declaring done and committing in the same breath is the ordinary way
        // this happens, and a strict comparison would flag every one of them.
        TaskCorroboration.Check(
            [Task("a", TaskState.Done, Now.AddHours(-1))],
            [Commit(Now.AddHours(-1))],
            Now)
            .Should().BeEmpty();
    }

    [Fact]
    public void A_commit_that_never_names_the_task_still_counts()
    {
        // Deliberately not matched on the message. "Committed under a message
        // that did not name the item" is the overwhelmingly common case, not a
        // problem — flagging it would make this report mostly noise, and a
        // report that is mostly noise stops being read.
        TaskCorroboration.Check(
            [Task("add-the-widget", TaskState.Done, Now.AddHours(-5))],
            [Commit(Now.AddHours(-1), "tidy up the parser")],
            Now)
            .Should().BeEmpty();
    }

    [Fact]
    public void The_observation_says_it_may_be_right()
    {
        var found = TaskCorroboration.Check(
            [Task("a", TaskState.Done, Now.AddHours(-1))], [], Now);

        // The wording is the feature. Anything that reads as a verdict invites
        // arguing with the tool instead of correcting the record.
        found.Single().Detail.Should().Contain("may be right");
    }

    [Fact]
    public void A_task_left_in_progress_for_a_fortnight_is_mentioned()
    {
        TaskCorroboration.Check(
            [Task("a", TaskState.Doing, Now - TaskCorroboration.Stale)], [], Now)
            .Should().ContainSingle();
    }

    [Fact]
    public void A_task_in_progress_since_yesterday_is_not()
    {
        TaskCorroboration.Check(
            [Task("a", TaskState.Doing, Now.AddDays(-1))], [], Now)
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(TaskState.Open)]
    [InlineData(TaskState.Blocked)]
    [InlineData(TaskState.Dropped)]
    public void States_that_claim_nothing_are_never_flagged(TaskState state)
    {
        // Only a claim can be unsupported. Open, blocked and dropped assert
        // nothing about work having happened, so there is nothing to check.
        TaskCorroboration.Check(
            [Task("a", state, Now.AddYears(-1))], [], Now)
            .Should().BeEmpty();
    }

    [Fact]
    public void Nothing_recorded_produces_nothing_to_say()
    {
        TaskCorroboration.Check([], [], Now).Should().BeEmpty();
    }

    [Theory]
    [InlineData("before/the-refactor")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData(".hidden")]
    public void An_id_that_is_really_a_path_is_refused(string id) =>
        TaskIds.Rejection(id).Should().NotBeNull();

    [Theory]
    [InlineData("add-the-widget")]
    [InlineData("v2.1")]
    [InlineData("a")]
    public void An_ordinary_id_is_accepted(string id) =>
        TaskIds.Rejection(id).Should().BeNull();
}
