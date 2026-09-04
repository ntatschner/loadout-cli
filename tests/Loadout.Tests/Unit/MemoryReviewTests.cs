using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Models.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which topics are put in front of somebody, and in what order.
/// </summary>
/// <remarks>
/// The audit has reported stale topics for as long as it has existed, and a
/// finding nobody acts on trains people to skim the report. Age is not falsity —
/// a two-year-old fact about the build can be perfectly true — so nothing here
/// decides anything. It only chooses what to ask about.
/// </remarks>
public sealed class MemoryReviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_topic_nobody_has_written_to_for_longer_than_the_window_is_asked_about()
    {
        var stale = MemoryReviewCommand.Unrevisited(
            [Topic("old", Now.AddMonths(-9))], months: 6, Now);

        stale.Should().ContainSingle().Which.Name.Should().Be("old");
    }

    [Fact]
    public void A_topic_touched_inside_the_window_is_left_alone()
    {
        MemoryReviewCommand.Unrevisited(
            [Topic("recent", Now.AddMonths(-2))], months: 6, Now)
            .Should().BeEmpty();
    }

    [Fact]
    public void The_boundary_falls_on_the_side_of_not_asking()
    {
        // Exactly at the window is not past it. Asking about a topic the day it
        // becomes eligible, every day, is how a review becomes a nag.
        MemoryReviewCommand.Unrevisited(
            [Topic("exactly", Now.AddMonths(-6))], months: 6, Now)
            .Should().BeEmpty();

        MemoryReviewCommand.Unrevisited(
            [Topic("just-past", Now.AddMonths(-6).AddDays(-1))], months: 6, Now)
            .Should().ContainSingle();
    }

    [Fact]
    public void The_oldest_is_asked_about_first()
    {
        // Somebody who stops halfway has dealt with the ones most likely to
        // have rotted, which is the whole value of going in an order at all.
        var stale = MemoryReviewCommand.Unrevisited(
            [
                Topic("middling", Now.AddMonths(-9)),
                Topic("ancient", Now.AddMonths(-30)),
                Topic("newly-stale", Now.AddMonths(-7)),
            ],
            months: 6,
            Now);

        stale.Select(topic => topic.Name)
            .Should().Equal("ancient", "middling", "newly-stale");
    }

    [Fact]
    public void Every_scope_is_reviewed_and_not_only_the_project_s()
    {
        // A fact about this machine goes stale the same way one about the
        // project does, and reviewing only one scope would leave the others to
        // rot unasked.
        var stale = MemoryReviewCommand.Unrevisited(
            [
                Topic("project-fact", Now.AddMonths(-9), MemoryScope.Project),
                Topic("machine-fact", Now.AddMonths(-9), MemoryScope.Machine),
            ],
            months: 6,
            Now);

        stale.Select(topic => topic.Scope)
            .Should().BeEquivalentTo([MemoryScope.Project, MemoryScope.Machine]);
    }

    [Fact]
    public void An_empty_store_asks_about_nothing()
    {
        MemoryReviewCommand.Unrevisited([], months: 6, Now).Should().BeEmpty();
    }

    private static MemoryTopic Topic(
        string name,
        DateTimeOffset written,
        MemoryScope scope = MemoryScope.Project) =>
        new(
            name,
            $"memory/{name}.md",
            "what this topic answers",
            MemoryKind.Lesson,
            ["Something durable."],
            [],
            Bytes: 200,
            written,
            scope);
}
