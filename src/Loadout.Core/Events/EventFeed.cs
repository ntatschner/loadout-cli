namespace Loadout.Core.Events;

/// <summary>One event as it is shown: with when it stopped being true, and whether it has been read.</summary>
/// <param name="Event">The event.</param>
/// <param name="Cleared">When the reason stopped being true, or null while it still is, or for an event that never could.</param>
/// <param name="Read">Whether it has been read.</param>
public sealed record FeedItem(UserEvent Event, DateTimeOffset? Cleared, bool Read);

/// <summary>What a filter found.</summary>
/// <param name="Items">Newest first, at most the limit asked for.</param>
/// <param name="Matched">How many matched before the limit, so a surface can say there are more.</param>
/// <param name="Unread">What the nav counts: unread events said to you that need you or went wrong.</param>
public sealed record FeedPage(IReadOnlyList<FeedItem> Items, int Matched, int Unread);

/// <summary>
/// Reads the history back: every <c>cleared</c> folded into the event it
/// clears, put through a filter, newest first.
/// </summary>
/// <remarks>
/// The page, the command and the daemon's route all come here, which is why
/// they agree. The whole feed is read each time; it is capped at a size where
/// that costs a few milliseconds.
/// </remarks>
public static class EventFeed
{
    /// <summary>The most one answer holds.</summary>
    public const int MaximumLimit = 500;

    /// <summary>What one answer holds where nothing is asked.</summary>
    public const int DefaultLimit = 100;

    /// <summary>Folds, filters and counts.</summary>
    public static FeedPage Query(
        IReadOnlyList<UserEvent> events,
        EventMarks marks,
        EventFilter filter,
        DateTimeOffset now,
        int limit = DefaultLimit)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentNullException.ThrowIfNull(filter);

        var cleared = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        foreach (var one in events.Where(one => one.Clears is not null))
        {
            // The first clearing is the one that happened; a second could only
            // come from a reason that came back under the same id, which ids
            // made from the moment never do.
            cleared.TryAdd(one.Clears!, one.At);
        }

        var shown = events
            .Where(one => one.Clears is null)
            .DistinctBy(one => one.Id, StringComparer.Ordinal)
            .Select(one => new FeedItem(one, cleared.TryGetValue(one.Id, out var at) ? at : null, marks.Has(one)))
            .ToList();

        var matched = shown
            .Where(item => filter.Admits(item.Event, now, item.Read))
            .OrderByDescending(item => item.Event.At)
            .ThenBy(item => item.Event.Id, StringComparer.Ordinal)
            .ToList();

        var unread = shown.Count(item => !item.Read
            && item.Event.Direction == EventDirections.ToMe
            && item.Event.Level is EventLevels.Needs or EventLevels.Problem);

        return new FeedPage([.. matched.Take(Math.Clamp(limit, 1, MaximumLimit))], matched.Count, unread);
    }

    /// <summary>Reads the store and queries it.</summary>
    public static FeedPage Query(EventStore store, EventFilter filter, DateTimeOffset now, int limit = DefaultLimit)
    {
        ArgumentNullException.ThrowIfNull(store);

        return Query(store.Read(), store.Marks(), filter, now, limit);
    }
}
