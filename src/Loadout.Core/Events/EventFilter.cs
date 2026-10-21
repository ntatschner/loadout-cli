using System.Globalization;
using System.Text.RegularExpressions;
using Loadout.Models.Results;

namespace Loadout.Core.Events;

/// <summary>
/// Which events to show, as one value the page, the command and the daemon
/// all read the same way.
/// </summary>
/// <remarks>
/// <para>
/// It has a canonical string, <c>level=needs&amp;project=x&amp;since=1d</c>,
/// written by <see cref="Format"/> and read by <see cref="Parse"/>. Studio
/// keeps it after the <c>?</c> in its address, so a filtered view can be
/// bookmarked; <c>loadout events --filter</c> takes it whole; and the daemon's
/// <c>/api/v1/eventlog</c> takes it as its query. One spelling, so a view
/// copied from one place opens the same in the others, and so whatever later
/// decides which events go out as notices can be handed one of these.
/// </para>
/// <para>
/// Levels and sources are held as their canonical comma-separated words
/// rather than as lists, so two filters that mean the same thing are equal,
/// which is what lets a test say <c>Parse(Format(f)) == f</c>. Times are held
/// as they were written, <c>1d</c> or <c>2026-10-01</c>, and only turned into
/// moments against a clock, so a bookmarked "last day" stays the last day.
/// </para>
/// <para>
/// Once bookmarks exist the string is a format other people hold, so a key
/// may be added but none renamed: a test pins the spelling.
/// </para>
/// </remarks>
public sealed partial record EventFilter
{
    /// <summary>Levels to show, canonical and comma separated, or null for all of them.</summary>
    public string? Level { get; init; }

    /// <summary>The one project to show, or null for every project.</summary>
    public string? Project { get; init; }

    /// <summary>Sources to show, canonical and comma separated, or null for all of them.</summary>
    public string? Source { get; init; }

    /// <summary>Which way: said to you, done by you, or both. Said to you unless asked.</summary>
    public string Direction { get; init; } = EventDirections.ToMe;

    /// <summary>The earliest, as written: <c>30m</c>, <c>6h</c>, <c>1d</c>, <c>2w</c>, or a date or moment.</summary>
    public string? Since { get; init; }

    /// <summary>The latest, written the same way as <see cref="Since"/>.</summary>
    public string? Until { get; init; }

    /// <summary>Only what has not been read.</summary>
    public bool Unread { get; init; }

    /// <summary>Words the title must contain, ignoring case. Only the title: what is searched is what is shown.</summary>
    public string? Text { get; init; }

    /// <summary>Nothing narrowed: everything said to you.</summary>
    public static EventFilter Everything { get; } = new();

    /// <summary>The keys, in the order <see cref="Format"/> writes them.</summary>
    public static readonly IReadOnlyList<string> Keys = ["level", "project", "source", "direction", "since", "until", "unread", "text"];

    /// <summary>The levels this filter lets through.</summary>
    public IReadOnlyList<string> Levels => Words(Level, EventLevels.All);

    /// <summary>The sources this filter lets through.</summary>
    public IReadOnlyList<string> Sources => Words(Source, EventSources.All);

    /// <summary>Reads the canonical string, or anything close enough to it: keys in any order, a leading <c>?</c>.</summary>
    /// <returns>The filter, or a failure naming the part it could not read.</returns>
    public static OperationResult<EventFilter> Parse(string? text)
    {
        var filter = new EventFilter();

        if (string.IsNullOrWhiteSpace(text))
        {
            return OperationResult<EventFilter>.Ok(filter);
        }

        foreach (var part in text.Trim().TrimStart('?', '#').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=', StringComparison.Ordinal);
            var key = Uri.UnescapeDataString(equals < 0 ? part : part[..equals]).Trim().ToLowerInvariant();
            var value = equals < 0 ? string.Empty : Uri.UnescapeDataString(part[(equals + 1)..].Replace('+', ' ')).Trim();

            switch (key)
            {
                case "level":
                    if (Canonical(value, EventLevels.All, "level") is { } badLevel)
                    {
                        return OperationResult<EventFilter>.Fail(badLevel);
                    }

                    filter = filter with { Level = Joined(value, EventLevels.All) };
                    break;

                case "source":
                    if (Canonical(value, EventSources.All, "source") is { } badSource)
                    {
                        return OperationResult<EventFilter>.Fail(badSource);
                    }

                    filter = filter with { Source = Joined(value, EventSources.All) };
                    break;

                case "project":
                    filter = filter with { Project = value.Length > 0 ? value : null };
                    break;

                case "direction":
                    var direction = value.ToLowerInvariant();

                    if (direction is not (EventDirections.ToMe or EventDirections.ByMe or EventDirections.Any))
                    {
                        return OperationResult<EventFilter>.Fail(
                            $"'{value}' is not a direction. It is to-me, by-me or any.");
                    }

                    filter = filter with { Direction = direction };
                    break;

                case "since":
                case "until":
                    if (value.Length > 0 && !IsWhen(value))
                    {
                        return OperationResult<EventFilter>.Fail(
                            $"'{value}' is not a time {key} can take. Give a span such as 30m, 6h, 1d or 2w, or a date such as 2026-10-01.");
                    }

                    filter = key == "since"
                        ? filter with { Since = value.Length > 0 ? value.ToLowerInvariant() : null }
                        : filter with { Until = value.Length > 0 ? value.ToLowerInvariant() : null };
                    break;

                case "unread":
                    if (value.Length > 0 && !bool.TryParse(value, out _) && value is not ("1" or "0"))
                    {
                        return OperationResult<EventFilter>.Fail($"'{value}' is not true or false, which is all unread takes.");
                    }

                    filter = filter with { Unread = value.Length == 0 || value is "1" || (bool.TryParse(value, out var yes) && yes) };
                    break;

                case "text":
                    filter = filter with { Text = value.Length > 0 ? value : null };
                    break;

                default:
                    return OperationResult<EventFilter>.Fail(
                        $"'{key}' is not something events can be filtered by. It takes {string.Join(", ", Keys)}.");
            }
        }

        return OperationResult<EventFilter>.Ok(filter);
    }

    /// <summary>The canonical string: keys in <see cref="Keys"/> order, defaults left out, empty for everything.</summary>
    public string Format()
    {
        var parts = new List<string>();

        void Add(string key, string? value)
        {
            if (value is { Length: > 0 })
            {
                parts.Add(key + "=" + Uri.EscapeDataString(value));
            }
        }

        Add("level", Level);
        Add("project", Project);
        Add("source", Source);
        Add("direction", Direction == EventDirections.ToMe ? null : Direction);
        Add("since", Since);
        Add("until", Until);
        Add("unread", Unread ? "true" : null);
        Add("text", Text);

        return string.Join('&', parts);
    }

    /// <inheritdoc />
    public override string ToString() => Format();

    /// <summary>Whether one event gets through, against a clock for the times and the read marks for unread.</summary>
    public bool Admits(UserEvent one, DateTimeOffset now, bool read)
    {
        ArgumentNullException.ThrowIfNull(one);

        if (Level is not null && !Levels.Contains(one.Level, StringComparer.Ordinal))
        {
            return false;
        }

        if (Source is not null && !Sources.Contains(one.Source, StringComparer.Ordinal))
        {
            return false;
        }

        if (Project is not null && !string.Equals(one.Project, Project, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Direction != EventDirections.Any && !string.Equals(one.Direction, Direction, StringComparison.Ordinal))
        {
            return false;
        }

        if (Moment(Since, now) is { } since && one.At < since)
        {
            return false;
        }

        if (Moment(Until, now, end: true) is { } until && one.At > until)
        {
            return false;
        }

        if (Unread && read)
        {
            return false;
        }

        return Text is null || one.Title.Contains(Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A written time as a moment: a span counted back from now, a date at
    /// its start (or its end, for <paramref name="end"/>), or a moment as given.
    /// </summary>
    /// <remarks>
    /// A date is read in the machine's own time zone, because "2026-10-01"
    /// typed by a person means their first of October rather than UTC's.
    /// </remarks>
    public static DateTimeOffset? Moment(string? written, DateTimeOffset now, bool end = false)
    {
        if (written is not { Length: > 0 })
        {
            return null;
        }

        if (Span().Match(written) is { Success: true } span)
        {
            var count = int.Parse(span.Groups[1].Value, CultureInfo.InvariantCulture);

            return now - (span.Groups[2].Value switch
            {
                "m" => TimeSpan.FromMinutes(count),
                "h" => TimeSpan.FromHours(count),
                "d" => TimeSpan.FromDays(count),
                _ => TimeSpan.FromDays(count * 7),
            });
        }

        if (DateOnly.TryParseExact(written, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            var local = date.ToDateTime(end ? TimeOnly.MaxValue : TimeOnly.MinValue, DateTimeKind.Local);

            return new DateTimeOffset(local);
        }

        return DateTimeOffset.TryParse(written, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moment)
            ? moment
            : null;
    }

    private static bool IsWhen(string value) => Moment(value, DateTimeOffset.UtcNow) is not null;

    private static string? Canonical(string value, IReadOnlyList<string> known, string what)
    {
        var unknown = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(one => !known.Contains(one.ToLowerInvariant(), StringComparer.Ordinal))
            .ToList();

        return unknown.Count == 0
            ? null
            : $"'{unknown[0]}' is not a {what}. It is one or more of {string.Join(", ", known)}, comma separated.";
    }

    /// <summary>The words given, in the known order, once each; null for none, so nothing given means everything.</summary>
    private static string? Joined(string value, IReadOnlyList<string> known)
    {
        var given = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(one => one.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var ordered = known.Where(given.Contains).ToList();

        return ordered.Count == 0 ? null : string.Join(',', ordered);
    }

    private static IReadOnlyList<string> Words(string? joined, IReadOnlyList<string> all) =>
        joined is null ? all : joined.Split(',');

    [GeneratedRegex("^([0-9]{1,5})([mhdw])$")]
    private static partial Regex Span();
}
