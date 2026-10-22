using FluentAssertions;
using Loadout.Cli.Commands;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What <c>idea accept</c> says about done-when, every time, since accepting
/// without <c>--done-when</c> is itself a confirmation of the plan's.
/// </summary>
public sealed class IdeaAcceptDoneWhenTests
{
    [Fact]
    public void Accepting_without_any_says_it_confirms_the_plans_own()
    {
        IdeaAcceptCommand.Confirming(null, ["a", "b"], would: false)
            .Should().Be("Accepting confirms the plan's 2 done-when, which the task is now held to.");
    }

    [Fact]
    public void Accepting_with_some_says_they_replace_the_plans()
    {
        IdeaAcceptCommand.Confirming(["x", " "], ["x"], would: true)
            .Should().Be("Would hold the task to the 1 done-when you gave, in place of the plan's.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Accepting_with_none_warns_that_a_queue_will_refuse_it(bool given)
    {
        IdeaAcceptCommand.Confirming(given ? [] : null, [], would: false)
            .Should().Contain("no done-when, so a team queue will refuse it");
    }

    [Fact]
    public void Clearing_the_plans_done_when_is_said_as_none_not_as_the_plans()
    {
        IdeaAcceptCommand.Confirming([], ["a", "b"], would: false)
            .Should().Contain("no done-when");
    }

    [Fact]
    public void No_done_when_with_done_when_given_is_none_and_otherwise_null_or_the_list()
    {
        new IdeaAcceptCommand.Settings { NoDoneWhen = true }.DoneWhenGiven.Should().BeEmpty();
        new IdeaAcceptCommand.Settings().DoneWhenGiven.Should().BeNull();
        new IdeaAcceptCommand.Settings { DoneWhen = ["a"] }.DoneWhenGiven.Should().Equal("a");
    }
}
