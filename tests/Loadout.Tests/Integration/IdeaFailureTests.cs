using FluentAssertions;
using Loadout.Core.Ideas;
using Loadout.Models.Ideas;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// A failed round as the lists see it, dismissing one, and a round running.
/// </summary>
public sealed class IdeaFailureTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();

    public void Dispose() => _space.Dispose();

    private async Task<IdeaPlace> FailedAsync()
    {
        var idea = (await _space.Ideas.CaptureAsync(null, "A status page", "me")).Value!;
        var place = new IdeaPlace(null, idea.Id);

        await _space.Ideas.RecordFailureAsync(
            place, "no such model", default, new IdeaAttempt { Agent = "claude", Model = "opsu" });

        return place;
    }

    [Fact]
    public async Task The_list_carries_the_failure_with_what_was_asked()
    {
        var place = await FailedAsync();

        var summary = (await _space.Ideas.ListAsync()).Value!.Single(i => i.Place.Id == place.Id);

        summary.Failure!.Error.Should().Be("no such model");
        summary.Failure.Model.Should().Be("opsu");
        summary.Running.Should().BeFalse();
    }

    [Fact]
    public async Task Dismissing_clears_the_failure_and_keeps_the_answers()
    {
        var idea = (await _space.Ideas.CaptureAsync(null, "A status page", "me")).Value!;
        var place = new IdeaPlace(null, idea.Id);

        await _space.Ideas.RecordReplyAsync(place, new IdeaReply
        {
            Understanding = "A page.",
            Questions = [new IdeaReply.ReplyQuestion { Question = "Public or private?", Why = "Hosting." }],
        });
        await _space.Ideas.AnswerAsync(place, "Q1", "private");
        await _space.Ideas.RecordFailureAsync(place, "no such model");

        var dismissed = await _space.Ideas.DismissFailureAsync(place);

        dismissed.Succeeded.Should().BeTrue(dismissed.Error);
        IdeaWork.FailureOf(dismissed.Value!).Should().BeNull();
        dismissed.Value!.Rounds.Single().Questions.Single().Answer.Should().Be("private");
    }

    [Fact]
    public async Task Dismissing_an_idea_with_no_failure_says_so()
    {
        var idea = (await _space.Ideas.CaptureAsync(null, "A status page", "me")).Value!;

        var dismissed = await _space.Ideas.DismissFailureAsync(new IdeaPlace(null, idea.Id));

        dismissed.Failed.Should().BeTrue();
        dismissed.Error.Should().Contain("no failed round");
    }

    [Fact]
    public async Task A_failure_written_by_an_older_launcher_is_listed_and_can_be_dismissed()
    {
        var idea = (await _space.Ideas.CaptureAsync(null, "A status page", "me")).Value!;
        var place = new IdeaPlace(null, idea.Id);
        var file = Path.Combine(Core.Tasks.TaskPaths.Ideas(_space.Workspace.LocalPath, null), $"{idea.Id}.yaml");

        // As a record from before attempts were kept: an error and nothing else.
        var text = await File.ReadAllTextAsync(file);
        text.Should().Contain("last_error: ''", "the calibration this test rests on");
        await File.WriteAllTextAsync(file, text.Replace("last_error: ''", "last_error: the agent stopped", StringComparison.Ordinal));

        var summary = (await _space.Ideas.ListAsync()).Value!.Single(i => i.Place.Id == place.Id);
        summary.Failure!.Error.Should().Be("the agent stopped");

        var dismissed = await _space.Ideas.DismissFailureAsync(place);

        dismissed.Succeeded.Should().BeTrue(dismissed.Error);
        IdeaWork.FailureOf(dismissed.Value!).Should().BeNull();
    }

    [Fact]
    public async Task The_list_says_a_round_is_running_while_one_holds_the_idea()
    {
        var place = await FailedAsync();

        using (_space.Locks.Take(place).Value!)
        {
            (await _space.Ideas.ListAsync()).Value!.Single(i => i.Place.Id == place.Id)
                .Running.Should().BeTrue();
        }

        (await _space.Ideas.ListAsync()).Value!.Single(i => i.Place.Id == place.Id)
            .Running.Should().BeFalse();
    }
}
