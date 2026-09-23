using System.Globalization;

namespace Loadout.Models.Teams;

/// <summary>
/// A length of time written the way a person writes one: 30m, 2h, 1d.
/// </summary>
/// <remarks>
/// Here rather than in the command line because a team file says it too, and
/// the runner, the catalogue and the command that schedules a run all have to
/// read it the same way. Two readers of one small format drift.
/// </remarks>
public static class TeamDuration
{
    /// <summary>The duration, or null for anything that is not one.</summary>
    public static TimeSpan? Parse(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var trimmed = text.Trim().ToLowerInvariant();

        if (trimmed.Length < 2)
        {
            return null;
        }

        if (!double.TryParse(trimmed[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var count)
            || count <= 0)
        {
            return null;
        }

        return trimmed[^1] switch
        {
            'm' => TimeSpan.FromMinutes(count),
            'h' => TimeSpan.FromHours(count),
            'd' => TimeSpan.FromDays(count),
            _ => null,
        };
    }

    /// <summary>A duration the way <see cref="Parse"/> reads one back.</summary>
    public static string Spell(TimeSpan span) =>
        span.TotalDays >= 1 && span.TotalDays == Math.Floor(span.TotalDays) ? $"{span.TotalDays:0}d"
        : span.TotalHours >= 1 && span.TotalHours == Math.Floor(span.TotalHours) ? $"{span.TotalHours:0}h"
        : $"{span.TotalMinutes:0.##}m";
}
