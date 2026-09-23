using System.Globalization;
using System.Text.RegularExpressions;

namespace Loadout.Models.Teams;

/// <summary>
/// A length of time written the way a person writes one: 30m, 2h, 1d.
/// </summary>
/// <remarks>
/// Here rather than in the command line because a team file says it too, and
/// the runner, the catalogue and the command that schedules a run all have to
/// read it the same way. Two readers of one small format drift.
/// </remarks>
public static partial class TeamDuration
{
    /// <summary>The duration, or null for anything that is not one.</summary>
    /// <remarks>
    /// 30m, 2h and 1d, and the same with the unit written out - "30 mins",
    /// "1 hour", "2 days" - because the box on the dashboard is free text and
    /// that is what people type into it. A bare number is refused rather than
    /// guessed at, since ten of one unit is not ten of another.
    /// </remarks>
    public static TimeSpan? Parse(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var match = Written().Match(text.Trim().ToLowerInvariant());

        if (!match.Success
            || !double.TryParse(match.Groups["count"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var count)
            || count <= 0)
        {
            return null;
        }

        return match.Groups["unit"].Value switch
        {
            "m" or "min" or "mins" or "minute" or "minutes" => TimeSpan.FromMinutes(count),
            "h" or "hr" or "hrs" or "hour" or "hours" => TimeSpan.FromHours(count),
            "d" or "day" or "days" => TimeSpan.FromDays(count),
            _ => null,
        };
    }

    /// <summary>Whether the text says there is to be no such time at all.</summary>
    /// <remarks>
    /// "never" is what the dashboard's box shows when it is empty, so it is what
    /// people type when they mean that. It was refused as not a duration, and
    /// the page reported the refusal as a template it had been asked to run.
    /// </remarks>
    public static bool IsNever(string? text) =>
        text?.Trim().ToLowerInvariant() is "never" or "off" or "none" or "no";

    /// <summary>What to say about text that is neither a duration nor "never".</summary>
    public static string Refusal(string text) =>
        $"'{text.Trim()}' is not a length of time. Write it as 30m, 2h or 1d - or never, or leave it empty.";

    /// <summary>A duration the way <see cref="Parse"/> reads one back.</summary>
    public static string Spell(TimeSpan span) =>
        span.TotalDays >= 1 && span.TotalDays == Math.Floor(span.TotalDays) ? $"{span.TotalDays:0}d"
        : span.TotalHours >= 1 && span.TotalHours == Math.Floor(span.TotalHours) ? $"{span.TotalHours:0}h"
        : $"{span.TotalMinutes:0.##}m";

    [GeneratedRegex(@"^(?<count>\d+(\.\d+)?)\s*(?<unit>[a-z]+)$")]
    private static partial Regex Written();
}
