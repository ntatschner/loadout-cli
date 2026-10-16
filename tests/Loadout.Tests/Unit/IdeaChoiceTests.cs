using FluentAssertions;
using Loadout.Core.Ideas;
using Loadout.Models.Ideas;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which agent and model the next round of an idea asks: the one rule the
/// command and the page share, so the two never disagree about the same idea.
/// </summary>
public sealed class IdeaChoiceTests
{
    private static IdeaRecord After(string agent, string model, IdeaAttempt? failure = null) => new()
    {
        Id = "status-page",
        LastRound = new IdeaAttempt { Agent = agent, Model = model },
        Failure = failure,
    };

    [Fact]
    public void Nothing_asked_takes_the_last_round()
    {
        var choice = IdeaWork.Choose(After("codex", "gpt-5"), null, null);

        choice.Agent.Should().Be("codex");
        choice.Model.Should().Be("gpt-5");
        choice.FromLastRound.Should().BeTrue();
    }

    [Fact]
    public void Before_any_round_nothing_is_named_and_the_machine_decides()
    {
        var choice = IdeaWork.Choose(new IdeaRecord { Id = "status-page" }, null, null);

        choice.Agent.Should().BeNull();
        choice.Model.Should().BeNull();
        choice.FromLastRound.Should().BeFalse();
    }

    [Fact]
    public void A_model_alone_keeps_the_last_rounds_agent()
    {
        var choice = IdeaWork.Choose(After("codex", "gpt-5"), null, "gpt-5-mini");

        choice.Agent.Should().Be("codex", "a model belongs to an agent");
        choice.Model.Should().Be("gpt-5-mini");
        choice.FromLastRound.Should().BeFalse();
    }

    [Fact]
    public void Another_agent_drops_the_last_rounds_model()
    {
        var choice = IdeaWork.Choose(After("codex", "gpt-5"), "claude", null);

        choice.Agent.Should().Be("claude");
        choice.Model.Should().BeNull("claude may not know a model codex was asked for");
    }

    [Fact]
    public void The_same_agent_named_again_keeps_its_model()
    {
        IdeaWork.Choose(After("codex", "gpt-5"), "codex", null).Model.Should().Be("gpt-5");
    }

    [Fact]
    public void A_failure_with_a_different_choice_is_handed_back_to_be_said()
    {
        var failure = new IdeaAttempt { Agent = "claude", Model = "opsu", Error = "no such model" };

        var choice = IdeaWork.Choose(After("claude", "opus", failure), null, null);

        choice.Model.Should().Be("opus", "the last round that came back, not the last attempt");
        choice.Failure.Should().BeSameAs(failure);
        choice.FailedDifferently.Should().BeTrue();
    }

    [Fact]
    public void A_failure_with_the_same_choice_is_not_a_different_one()
    {
        var failure = new IdeaAttempt { Agent = "claude", Model = "opus", Error = "timed out" };

        IdeaWork.Choose(After("claude", "opus", failure), null, null).FailedDifferently.Should().BeFalse();
    }

    [Fact]
    public void A_record_from_before_attempts_were_kept_reads_its_error_as_a_failure()
    {
        var record = new IdeaRecord { Id = "status-page", LastError = "the agent stopped" };

        var failure = IdeaWork.FailureOf(record);

        failure.Should().NotBeNull();
        failure!.Error.Should().Be("the agent stopped");
        failure.Agent.Should().BeEmpty("it was never recorded");
    }

    [Fact]
    public void Clearing_a_failure_clears_the_old_field_as_well()
    {
        var record = new IdeaRecord { Id = "status-page", LastError = "the agent stopped" };

        IdeaWork.ClearFailure(record);

        IdeaWork.FailureOf(record).Should().BeNull();
    }
}
