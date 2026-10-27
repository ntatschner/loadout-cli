using FluentAssertions;
using Loadout.Core.Delivery;
using Loadout.Core.Git;
using Loadout.Models.Projects;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which pull request mode a queued run follows, and what gh is asked to do about it.
/// </summary>
/// <remarks>
/// The narrowest level that says anything wins, and nothing said means no pull
/// request: a run that opens one nobody asked for does it unattended, where it
/// cannot be taken back quietly.
/// </remarks>
public sealed class PullRequestModesTests
{
    private static ProjectDelivery Project(string mode) => new() { PrMode = mode };

    [Fact]
    public void Nothing_set_anywhere_opens_none()
    {
        PullRequestModes.Resolve(string.Empty, new QueueSettings(), Project(string.Empty))
            .Should().Be(new PullRequestModeChoice(PullRequestMode.None, PullRequestModeLevel.Default));
    }

    [Fact]
    public void A_project_mode_applies_when_the_queue_and_the_task_say_nothing()
    {
        PullRequestModes.Resolve(string.Empty, new QueueSettings(), Project("ready"))
            .Should().Be(new PullRequestModeChoice(PullRequestMode.Ready, PullRequestModeLevel.Project));
    }

    [Fact]
    public void The_queue_overrides_the_project()
    {
        PullRequestModes.Resolve(string.Empty, new QueueSettings { PrMode = "none" }, Project("ready"))
            .Should().Be(new PullRequestModeChoice(PullRequestMode.None, PullRequestModeLevel.Queue));
    }

    [Fact]
    public void The_queued_task_overrides_the_queue()
    {
        PullRequestModes.Resolve("ready", new QueueSettings { PrMode = "draft" }, Project("none"))
            .Should().Be(new PullRequestModeChoice(PullRequestMode.Ready, PullRequestModeLevel.Item));
    }

    [Fact]
    public void The_old_draft_grant_still_reads_as_draft_at_the_queue()
    {
        PullRequestModes.Resolve(string.Empty, new QueueSettings { DraftPullRequests = true }, Project("ready"))
            .Should().Be(new PullRequestModeChoice(PullRequestMode.Draft, PullRequestModeLevel.Queue));
    }

    [Fact]
    public void A_mode_set_on_the_queue_wins_over_its_old_draft_grant()
    {
        PullRequestModes.OfQueue(new QueueSettings { DraftPullRequests = true, PrMode = "ready" })
            .Should().Be(PullRequestMode.Ready);
    }

    [Fact]
    public void A_misspelt_mode_is_passed_over_rather_than_read_as_anything()
    {
        PullRequestModes.Resolve("redy", new QueueSettings(), Project("draft"))
            .Should().Be(new PullRequestModeChoice(PullRequestMode.Draft, PullRequestModeLevel.Project));
    }

    [Fact]
    public void An_unreadable_manifest_counts_as_saying_nothing()
    {
        PullRequestModes.Resolve(string.Empty, new QueueSettings(), project: null).Mode.Should().Be(PullRequestMode.None);
    }

    [Theory]
    [InlineData("Draft", PullRequestMode.Draft)]
    [InlineData(" ready ", PullRequestMode.Ready)]
    [InlineData("NONE", PullRequestMode.None)]
    public void Mode_words_are_read_ignoring_case_and_spaces(string word, PullRequestMode expected)
    {
        PullRequestModes.TryParse(word, out var mode).Should().BeTrue();
        mode.Should().Be(expected);
    }

    [Fact]
    public void A_word_that_is_not_a_mode_is_refused()
    {
        PullRequestModes.TryParse("yes", out _).Should().BeFalse();
    }

    [Fact]
    public void Gh_is_asked_for_a_draft_with_exactly_these_arguments()
    {
        GhRepositoryHost.CreateArguments(new PullRequestOpening("queue/login-1", "Fix the login", "body", Draft: true))
            .Should().Equal("pr", "create", "--draft", "--head", "queue/login-1", "--title", "Fix the login", "--body", "body");
    }

    [Fact]
    public void Gh_is_asked_for_a_ready_pull_request_with_exactly_these_arguments()
    {
        GhRepositoryHost.CreateArguments(new PullRequestOpening("queue/login-1", "Fix the login", "body", Draft: false))
            .Should().Equal("pr", "create", "--head", "queue/login-1", "--title", "Fix the login", "--body", "body");
    }
}
