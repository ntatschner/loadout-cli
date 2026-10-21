using FluentAssertions;
using Loadout.Core.Events;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>Reading the history back: cleared folded in, read marks kept, oldest pruned.</summary>
public sealed class EventFeedTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly string _state = Path.Combine(Path.GetTempPath(), "loadout-feed-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_state))
        {
            Directory.Delete(_state, recursive: true);
        }
    }

    private static UserEvent Event(string id, DateTimeOffset at, string level = EventLevels.Needs, string direction = EventDirections.ToMe, string? clears = null) =>
        new(id, at, level, EventSources.Run, direction, clears is null ? EventKinds.Asking : EventKinds.Cleared, "docs asks: " + id, Run: "r1", Clears: clears);

    [Fact]
    public void A_clearing_is_folded_into_the_event_it_clears_and_not_listed()
    {
        IReadOnlyList<UserEvent> events = [Event("a", Noon), Event("c", Noon.AddMinutes(4), EventLevels.Info, clears: "a"), Event("b", Noon.AddMinutes(1))];

        var page = EventFeed.Query(events, EventMarks.None, EventFilter.Everything, Noon.AddHours(1));

        page.Items.Select(item => item.Event.Id).Should().Equal("b", "a");
        page.Items.Single(item => item.Event.Id == "a").Cleared.Should().Be(Noon.AddMinutes(4));
        page.Items.Single(item => item.Event.Id == "b").Cleared.Should().BeNull();
    }

    [Fact]
    public void The_unread_count_is_what_needs_you_or_went_wrong_said_to_you()
    {
        IReadOnlyList<UserEvent> events =
        [
            Event("needs", Noon),
            Event("problem", Noon, EventLevels.Problem),
            Event("info", Noon, EventLevels.Info),
            Event("mine", Noon, EventLevels.Needs, EventDirections.ByMe),
            Event("read", Noon),
        ];

        EventFeed.Query(events, new EventMarks(null, ["read"]), EventFilter.Everything, Noon).Unread.Should().Be(2);
    }

    [Fact]
    public void Marking_all_read_moves_the_watermark_and_drops_the_marks_it_covers()
    {
        var store = new EventStore(_state);

        store.Append([Event("a", Noon), Event("b", Noon.AddMinutes(10))]);
        store.MarkRead(["a", "b"]);

        var marks = store.MarkAllRead(Noon.AddMinutes(5));

        marks.Before.Should().Be(Noon.AddMinutes(5));
        marks.Read.Should().Equal("b");
        EventFeed.Query(store, EventFilter.Parse("unread=true").Value!, Noon.AddHours(1)).Items.Should().BeEmpty();
    }

    [Fact]
    public void Pruning_drops_what_is_older_than_the_days_kept_and_a_clearing_with_it()
    {
        var store = new EventStore(_state);

        store.Append(
        [
            Event("old", Noon.AddDays(-40)),
            Event("old-cleared", Noon.AddDays(-20), EventLevels.Info, clears: "old"),
            Event("new", Noon.AddDays(-1)),
        ]);

        store.Prune(Noon, 30).Should().Be(2);
        store.Read().Select(one => one.Id).Should().Equal("new");
    }

    [Fact]
    public void Pruning_cuts_an_oversized_feed_back_from_its_oldest()
    {
        var store = new EventStore(_state);
        var padding = new string('x', 4096);
        var many = Enumerable.Range(0, (int)(EventStore.MaximumBytes / 4096) + 50)
            .Select(index => Event($"e{index:D5}", Noon.AddSeconds(index)) with { Detail = padding })
            .ToList();

        store.Append(many);
        store.Prune(Noon.AddDays(1), 30).Should().BePositive();

        new FileInfo(store.FeedPath).Length.Should().BeLessThanOrEqualTo(EventStore.PrunedBytes);
        store.Read().Last().Id.Should().Be(many[^1].Id, "the newest are the ones kept");
    }

    [Fact]
    public void A_half_written_line_hides_nothing_else()
    {
        var store = new EventStore(_state);

        store.Append([Event("a", Noon)]);
        File.AppendAllText(store.FeedPath, "{\"id\":\"b\",\"at\":");
        store.Append([Event("c", Noon)]);

        store.Read().Select(one => one.Id).Should().Equal("a", "c");
    }
}
