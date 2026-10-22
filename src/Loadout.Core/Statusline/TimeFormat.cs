using System.Globalization;

namespace Loadout.Core.Statusline;

/// <summary>
/// How the status line writes a time: an age, a duration, or a clock time.
/// <para>
/// Every method takes <c>now</c> and the zone rather than reading either,
/// so a test can pin both and the renderer stays free of a clock. The wire
/// formats are invariant; nothing here follows the current culture, because
/// "14:02" should not become "14.02" on one machine and not another.
/// </para>
/// </summary>
public static class TimeFormat
{
    /// <summary>
    /// An age: <c>&lt;1m ago</c>, <c>3m ago</c>, <c>2h05m ago</c>, <c>3d ago</c>.
    /// A time in the future reads as <c>&lt;1m ago</c>: two machines'
    /// clocks disagreeing by a second is not worth a stranger phrase.
    /// </summary>
    public static string Relative(DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "<1m ago";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return Invariant($"{(int)elapsed.TotalMinutes}m ago");
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return Invariant($"{(int)elapsed.TotalHours}h{elapsed.Minutes:00}m ago");
        }

        return Invariant($"{(int)elapsed.TotalDays}d ago");
    }

    /// <summary>A length of time: <c>45s</c>, <c>2m14s</c>, <c>1h12m</c>.</summary>
    public static string Duration(TimeSpan length)
    {
        if (length < TimeSpan.Zero)
        {
            length = TimeSpan.Zero;
        }

        if (length < TimeSpan.FromMinutes(1))
        {
            return Invariant($"{(int)length.TotalSeconds}s");
        }

        if (length < TimeSpan.FromHours(1))
        {
            return Invariant($"{(int)length.TotalMinutes}m{length.Seconds:00}s");
        }

        return Invariant($"{(int)length.TotalHours}h{length.Minutes:00}m");
    }

    /// <summary>The time of day in the zone: <c>14:05</c>, or <c>2:05pm</c> on a 12-hour clock.</summary>
    public static string Clock(DateTimeOffset at, TimeZoneInfo zone, bool hour24)
    {
        var local = TimeZoneInfo.ConvertTime(at, zone);

        if (hour24)
        {
            return local.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        // Written out rather than "h:mmtt", whose AM/PM designator is
        // upper case in the invariant culture.
        var hour = local.Hour % 12 == 0 ? 12 : local.Hour % 12;
        var half = local.Hour < 12 ? "am" : "pm";

        return Invariant($"{hour}:{local.Minute:00}{half}");
    }

    /// <summary>
    /// A clock time, with the day in front when it is not today in the zone:
    /// <c>14:02</c> today, <c>21 Oct 14:02</c> otherwise.
    /// </summary>
    public static string Absolute(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone, bool hour24)
    {
        var clock = Clock(at, zone, hour24);
        var day = TimeZoneInfo.ConvertTime(at, zone).Date;

        return day == TimeZoneInfo.ConvertTime(now, zone).Date
            ? clock
            : day.ToString("d MMM", CultureInfo.InvariantCulture) + " " + clock;
    }

    /// <summary>
    /// Reads a threshold setting such as <c>10m,30m</c>: the age at which a
    /// segment turns amber, then the one at which it turns red. One value
    /// gives amber only. Anything that cannot be read gives neither, never
    /// an exception, because the line is not worth an error.
    /// </summary>
    public static (TimeSpan? Amber, TimeSpan? Red) Thresholds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length > 2)
        {
            return (null, null);
        }

        var amber = Span(parts[0]);
        var red = parts.Length == 2 ? Span(parts[1]) : null;

        if (amber is null || (parts.Length == 2 && red is null))
        {
            return (null, null);
        }

        return (amber, red);
    }

    /// <summary>A span written as a number and a unit, or several: <c>45s</c>, <c>10m</c>, <c>1h30m</c>, <c>2d</c>.</summary>
    private static TimeSpan? Span(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        var total = TimeSpan.Zero;
        var number = 0L;
        var digits = 0;

        foreach (var c in text)
        {
            if (c is >= '0' and <= '9')
            {
                if (digits >= 9)
                {
                    return null;
                }

                number = (number * 10) + (c - '0');
                digits++;
                continue;
            }

            if (digits == 0)
            {
                return null;
            }

            TimeSpan? unit = char.ToLowerInvariant(c) switch
            {
                's' => TimeSpan.FromSeconds(number),
                'm' => TimeSpan.FromMinutes(number),
                'h' => TimeSpan.FromHours(number),
                'd' => TimeSpan.FromDays(number),
                _ => null,
            };

            if (unit is null)
            {
                return null;
            }

            total += unit.Value;
            number = 0;
            digits = 0;
        }

        // A bare number names no unit, and guessing one is how a threshold
        // meant in minutes fires after seconds.
        return digits == 0 ? total : null;
    }

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);
}
