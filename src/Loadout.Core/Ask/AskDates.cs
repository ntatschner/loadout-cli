using System.Globalization;
using System.Text.RegularExpressions;

namespace Loadout.Core.Ask;

/// <summary>A run of days, first to last inclusive, and how to say it.</summary>
/// <param name="From">The first day.</param>
/// <param name="Until">The last day.</param>
/// <param name="Label">The days in words, as an answer repeats them back: "Tue 20 Oct", "the last 7 days".</param>
public sealed record DateSpan(DateOnly From, DateOnly Until, string Label);

/// <summary>
/// Reads a period out of a request: "last tuesday", "this week", "the last 10 days".
/// </summary>
/// <remarks>
/// Only the phrasings people actually use for a spend question, each one
/// tested. Anything else leaves the period unfilled, which is an answer about
/// a stated default rather than a guess about what was meant.
/// </remarks>
public static partial class AskDates
{
    private static readonly string[] Weekdays =
        ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];

    private static readonly string[] Months =
        ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    /// <summary>The period a request names, or null when it names none.</summary>
    /// <param name="text">The request, in lower case.</param>
    /// <param name="today">Today, in the calendar the answer is given in.</param>
    public static DateSpan? Find(string text, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (Iso().Match(text) is { Success: true } iso
            && DateOnly.TryParseExact(iso.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return Day(exact);
        }

        if (DayMonth().Match(text) is { Success: true } dm && On(today, dm.Groups["day"].Value, dm.Groups["month"].Value) is { } named)
        {
            return Day(named);
        }

        if (MonthDay().Match(text) is { Success: true } md && On(today, md.Groups["day"].Value, md.Groups["month"].Value) is { } named2)
        {
            return Day(named2);
        }

        if (Recent().Match(text) is { Success: true } recent
            && AskWords.Number(recent.Groups["count"].Value) is { } count and > 0)
        {
            var span = recent.Groups["unit"].Value.StartsWith("week", StringComparison.Ordinal) ? count * 7 : count;

            return new DateSpan(today.AddDays(-(span - 1)), today, $"the last {span} days");
        }

        if (Has(text, "today"))
        {
            return new DateSpan(today, today, "today");
        }

        if (Has(text, "yesterday"))
        {
            var yesterday = today.AddDays(-1);

            return new DateSpan(yesterday, yesterday, $"yesterday, {Short(yesterday)}");
        }

        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        if (text.Contains("last week", StringComparison.Ordinal))
        {
            return new DateSpan(monday.AddDays(-7), monday.AddDays(-1), $"last week, {Short(monday.AddDays(-7))} to {Short(monday.AddDays(-1))}");
        }

        if (text.Contains("this week", StringComparison.Ordinal))
        {
            return new DateSpan(monday, today, $"this week, since {Short(monday)}");
        }

        var first = new DateOnly(today.Year, today.Month, 1);

        if (text.Contains("last month", StringComparison.Ordinal))
        {
            var start = first.AddMonths(-1);

            return new DateSpan(start, first.AddDays(-1), start.ToString("MMMM yyyy", CultureInfo.InvariantCulture));
        }

        if (text.Contains("this month", StringComparison.Ordinal))
        {
            return new DateSpan(first, today, $"this month, since {Short(first)}");
        }

        for (var index = 0; index < Weekdays.Length; index++)
        {
            if (!Has(text, Weekdays[index]))
            {
                continue;
            }

            // "Last tuesday" and "on tuesday" both mean the most recent one
            // before today. Today's own weekday is a week ago, because nobody
            // asks "how much did I spend last monday" meaning this morning.
            var back = ((int)today.DayOfWeek - index + 7) % 7;
            var day = today.AddDays(-(back == 0 ? 7 : back));

            return Day(day);
        }

        return null;
    }

    /// <summary>The usual default when no period is named.</summary>
    public static DateSpan LastDays(DateOnly today, int days) =>
        new(today.AddDays(-(days - 1)), today, $"the last {days} days");

    private static DateSpan Day(DateOnly day) => new(day, day, Short(day));

    private static string Short(DateOnly day) => day.ToString("ddd d MMM", CultureInfo.InvariantCulture);

    private static bool Has(string text, string word) =>
        Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.CultureInvariant);

    /// <summary>A day of a named month, this year, or last year when that would be in the future.</summary>
    private static DateOnly? On(DateOnly today, string day, string month)
    {
        var index = Array.FindIndex(Months, name => month.StartsWith(name, StringComparison.Ordinal));

        if (index < 0 || !int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number < 1 || number > DateTime.DaysInMonth(today.Year, index + 1))
        {
            return null;
        }

        var date = new DateOnly(today.Year, index + 1, number);

        return date > today ? date.AddYears(-1) : date;
    }

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Iso();

    [GeneratedRegex(@"\b(?<day>\d{1,2})(?:st|nd|rd|th)?\s+(?:of\s+)?(?<month>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\b", RegexOptions.CultureInvariant)]
    private static partial Regex DayMonth();

    [GeneratedRegex(@"\b(?<month>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\s+(?<day>\d{1,2})(?:st|nd|rd|th)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex MonthDay();

    [GeneratedRegex(@"\b(?:last|past)\s+(?<count>\w+)\s+(?<unit>days?|weeks?)\b", RegexOptions.CultureInvariant)]
    private static partial Regex Recent();
}
