using FluentAssertions;
using Loadout.Core.Events;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The one description of which events to show, shared by Studio's address,
/// the command and the daemon's route.
/// </summary>
/// <remarks>
/// Once somebody has bookmarked a filtered view the string is theirs, so its
/// spelling is pinned here: a key may be added, none renamed.
/// </remarks>
public sealed class EventFilterTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_canonical_string_is_pinned()
    {
        var filter = new EventFilter
        {
            Level = "needs,problem",
            Project = "loadout cli",
            Source = "run",
            Direction = EventDirections.Any,
            Since = "1d",
            Until = "2026-10-21",
            Unread = true,
            Text = "merge",
        };

        filter.Format().Should().Be(
            "level=needs%2Cproblem&project=loadout%20cli&source=run&direction=any&since=1d&until=2026-10-21&unread=true&text=merge");
    }

    [Fact]
    public void Nothing_narrowed_is_the_empty_string_and_means_said_to_you()
    {
        EventFilter.Everything.Format().Should().BeEmpty();
        EventFilter.Parse("").Value!.Direction.Should().Be(EventDirections.ToMe);
    }

    [Theory]
    [InlineData("")]
    [InlineData("level=needs")]
    [InlineData("level=needs%2Cinfo&project=x")]
    [InlineData("source=session%2Cnotice&direction=by-me&since=30m")]
    [InlineData("since=2026-10-01&until=2026-10-20&unread=true")]
    [InlineData("direction=any&text=docs%20asks")]
    public void Reading_what_was_written_gives_the_same_filter(string canonical)
    {
        var filter = EventFilter.Parse(canonical).Value!;

        filter.Format().Should().Be(canonical);
        EventFilter.Parse(filter.Format()).Value.Should().Be(filter);
    }

    [Fact]
    public void Levels_are_put_in_order_once_each_so_the_same_meaning_is_the_same_filter()
    {
        EventFilter.Parse("level=info,NEEDS,needs").Value.Should().Be(EventFilter.Parse("level=needs,info").Value);
        EventFilter.Parse("level=info,NEEDS,needs").Value!.Format().Should().Be("level=needs%2Cinfo");
    }

    [Theory]
    [InlineData("level=urgent", "'urgent' is not a level")]
    [InlineData("source=slack", "'slack' is not a source")]
    [InlineData("direction=sideways", "is not a direction")]
    [InlineData("since=yesterday", "is not a time since can take")]
    [InlineData("colour=red", "'colour' is not something events can be filtered by")]
    public void What_it_cannot_read_it_names(string text, string said)
    {
        var read = EventFilter.Parse(text);

        read.Failed.Should().BeTrue();
        read.Error.Should().Contain(said);
    }

    [Fact]
    public void A_span_counts_back_from_now()
    {
        EventFilter.Moment("6h", Noon).Should().Be(Noon.AddHours(-6));
        EventFilter.Moment("2w", Noon).Should().Be(Noon.AddDays(-14));
    }

    [Fact]
    public void It_lets_through_only_what_matches_every_part()
    {
        var one = new UserEvent("e1", Noon.AddHours(-2), EventLevels.Needs, EventSources.Run, EventDirections.ToMe,
            EventKinds.Asking, "docs asks: may I merge?", Project: "p");
        var filter = EventFilter.Parse("level=needs&project=P&since=1d&text=MERGE").Value!;

        filter.Admits(one, Noon, read: false).Should().BeTrue();
        filter.Admits(one with { Level = EventLevels.Info }, Noon, false).Should().BeFalse();
        filter.Admits(one with { Project = "q" }, Noon, false).Should().BeFalse();
        filter.Admits(one with { At = Noon.AddDays(-2) }, Noon, false).Should().BeFalse();
        filter.Admits(one with { Title = "docs asks: may I push?" }, Noon, false).Should().BeFalse();
        filter.Admits(one with { Direction = EventDirections.ByMe }, Noon, false).Should().BeFalse("said to you unless asked");
        (filter with { Unread = true }).Admits(one, Noon, read: true).Should().BeFalse();
    }
}
