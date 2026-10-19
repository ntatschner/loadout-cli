using System.Text.RegularExpressions;
using Loadout.Core.Instructions;
using Loadout.Models.Voice;

namespace Loadout.Core.Voice;

/// <summary>A date format a surface can be set to.</summary>
/// <param name="Name">What it is called in a setting, such as <c>iso</c>.</param>
/// <param name="Manner">How dates are written in it, to follow "write dates", as in "in ISO 8601".</param>
/// <param name="Example">How it reads, for the rendered rule and the findings.</param>
/// <param name="Accepts">The shapes of date the check lets through on a surface set to it.</param>
public sealed record TimePreset(string Name, string Manner, string Example, IReadOnlyList<DateShape> Accepts);

/// <summary>A way a date can be written, as far as the check can tell them apart.</summary>
public enum DateShape
{
    /// <summary>2026-10-19, with or without a time.</summary>
    Iso,

    /// <summary>19 October 2026.</summary>
    DayMonthYear,

    /// <summary>October 19, 2026.</summary>
    MonthDayYear,

    /// <summary>19/10/2026.</summary>
    Numeric,
}

/// <summary>One time setting in force on one surface, and the layer that set it.</summary>
/// <param name="Surface">The surface.</param>
/// <param name="Estimates">The estimates policy in force.</param>
/// <param name="EstimatesFrom">The layer it came from, or null for the built-in default.</param>
/// <param name="Format">The format in force, or null when none is set.</param>
/// <param name="FormatFrom">The layer it came from, or null when none is set.</param>
public sealed record TimeSurface(
    string Surface,
    string Estimates,
    StyleLayer? EstimatesFrom,
    string? Format,
    StyleLayer? FormatFrom);

/// <summary>
/// The time guide's settings: how estimates are given and how dates are
/// written, on each surface.
/// </summary>
/// <remarks>
/// <para>
/// Settings rather than learned rules. Nobody's history says reliably which
/// date format they want, and an estimate policy is a decision, not a habit,
/// so the rules here are typed in with <c>loadout voice time set</c> and kept
/// in the time guide's profile at a layer, beside what was learned.
/// </para>
/// <para>
/// Which layer wins depends on the surface. A codebase decides how its own
/// docs, commits and pull requests are written, as it does for every guide;
/// how an agent talks to you in a chat, and what an interface shows you, is
/// yours. Each falls back to the other layer when its own has nothing set.
/// </para>
/// <para>
/// Both checks report and never gate. Spotting an estimate in prose is a
/// heuristic: it looks for a duration in a sentence that also says something
/// will or should take time, which misses estimates phrased some other way and
/// can catch a sentence that only describes one.
/// </para>
/// </remarks>
public static partial class VoiceTime
{
    /// <summary>The rule id holding the estimates policy.</summary>
    public const string EstimatesRule = "estimates";

    /// <summary>The prefix of the rule ids holding a surface's format, as in <c>format.commit</c>.</summary>
    public const string FormatPrefix = "format.";

    /// <summary>Give a range and say whose time it is. The default.</summary>
    public const string RangeWithOwner = "range-with-owner";

    /// <summary>Give no estimate of how long work will take.</summary>
    public const string Never = "never";

    /// <summary>The prefix of a format that is a pattern rather than a preset.</summary>
    public const string PatternPrefix = "pattern:";

    /// <summary>The citation source a setting is recorded under.</summary>
    public const string SettingSource = "setting";

    /// <summary>The estimates policies there are.</summary>
    public static readonly IReadOnlyList<string> Policies = [RangeWithOwner, Never];

    /// <summary>The format presets there are.</summary>
    public static readonly IReadOnlyList<TimePreset> Presets =
    [
        new("iso", "in ISO 8601", "2026-10-19, and 14:05 UTC for a time", [DateShape.Iso]),
        new("long-british", "as the day, the month in words, then the year",
            "19 October 2026, with a 24-hour time such as 14:05", [DateShape.DayMonthYear]),
        new("long-american", "as the month in words, the day, then the year",
            "October 19, 2026, with a 12-hour time such as 2:05 pm", [DateShape.MonthDayYear]),
        new("short-numeric", "as numbers, day first", "19/10/2026", [DateShape.Numeric]),
        new("relative", "relative to now", "3 hours ago, or in 2 days", []),
        new("relative-with-absolute", "relative to now, with the date beside it", "3 hours ago (19 October 2026)",
            [DateShape.Iso, DateShape.DayMonthYear, DateShape.MonthDayYear]),
    ];

    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(250);

    private const string Months =
        "January|February|March|April|May|June|July|August|September|October|November|December"
        + "|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec";

    private const string Count =
        @"\d+(?:\.\d+)?|an?|one|two|three|four|five|six|seven|eight|nine|ten|twelve|fifteen|twenty|thirty|forty|fifty|sixty|ninety"
        + @"|a\s+few|a\s+couple\s+of|several";

    [GeneratedRegex(@"(?<![\w-])\d{4}-\d{2}-\d{2}(?:[T ]\d{2}:\d{2}(?::\d{2})?(?:Z|[+-]\d{2}:?\d{2})?)?(?![\w-])")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"(?<![\w/])\d{1,2}/\d{1,2}/\d{2,4}(?![\w/])")]
    private static partial Regex NumericDate();

    [GeneratedRegex(@"(?<!\w)\d{1,2}(?:st|nd|rd|th)?\s+(?:" + Months + @")\.?,?\s+\d{4}(?!\w)", RegexOptions.IgnoreCase)]
    private static partial Regex DayMonthYear();

    [GeneratedRegex(@"(?<!\w)(?:" + Months + @")\.?\s+\d{1,2}(?:st|nd|rd|th)?,?\s+\d{4}(?!\w)", RegexOptions.IgnoreCase)]
    private static partial Regex MonthDayYear();

    [GeneratedRegex(
        @"(?<!\w)(?:between\s+)?(?<low>" + Count + @")(?:\s*(?:-|–|to|or|and)\s*(?<high>" + Count + @"))?\s*(?:more\s+)?"
        + @"(?:seconds?|secs?|minutes?|mins?|hours?|hrs?|days?|weeks?|months?)(?!\w)"
        + @"(?<tail>\s+or\s+(?:two|three|four|so|more|\d+)(?!\w))?",
        RegexOptions.IgnoreCase)]
    private static partial Regex Duration();

    [GeneratedRegex(
        @"(?<!\w)(?:(?:will|would|should|could|might|may|going\s+to)\s+(?:only\s+)?take|takes?\s+(?:about|around|roughly|approximately|up\s+to)"
        + @"|estimated?|estimates|eta|expect(?:s|ed)?|(?:about|around|roughly|approximately)\s+(?=" + Count + @"))(?!\w)",
        RegexOptions.IgnoreCase)]
    private static partial Regex EstimateCue();

    [GeneratedRegex(
        @"(?<!\w)(?:agent|agents|agent's|you|your|yours|human|person|person's|people|reviewer|review|reviewing|ci|waiting|wall[- ]clock|machine)(?!\w)",
        RegexOptions.IgnoreCase)]
    private static partial Regex Owner();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBreak();

    /// <summary>A preset by name, or null.</summary>
    public static TimePreset? Preset(string name) =>
        Presets.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The rule id holding a surface's format.</summary>
    public static string FormatRule(string surface) => FormatPrefix + surface;

    /// <summary>
    /// Why a format value is not one, or null when it is: a preset's name, or
    /// <c>pattern:</c> followed by a format.
    /// </summary>
    public static string? FormatProblem(string value)
    {
        if (value.StartsWith(PatternPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return value.Length > PatternPrefix.Length && !string.IsNullOrWhiteSpace(value[PatternPrefix.Length..])
                ? null
                : "A pattern format needs the pattern after 'pattern:', such as pattern:dd MMM yyyy.";
        }

        return Preset(value) is null
            ? $"'{value}' is not a format. Choose one of {string.Join(", ", Presets.Select(p => p.Name))}, or pattern:<format>."
            : null;
    }

    /// <summary>The rule a policy is kept as.</summary>
    public static VoiceRule EstimatesSetting(string policy, DateTimeOffset now) => new()
    {
        Id = EstimatesRule,
        Statement = string.Equals(policy, Never, StringComparison.Ordinal)
            ? "Do not say how long work will take. Describe its size instead: what it touches, how many steps it has, "
              + "and what could go wrong."
            : "When saying how long work will take, give a range and say whose time it is: agent time, a person's "
              + "time, or waiting on something such as CI or a review.",
        Check = new VoiceCheck { Type = VoiceChecker.UnownedEstimate, Value = policy },
        Citations = [Setting()],
        Accepted = now,
    };

    /// <summary>The rule a surface's format is kept as.</summary>
    public static VoiceRule FormatSetting(string surface, string format, DateTimeOffset now)
    {
        var statement = Preset(format) is { } preset
            ? $"Write dates {preset.Manner}, such as {preset.Example}."
            : $"Write dates in the pattern {format[PatternPrefix.Length..].Trim()}.";

        return new VoiceRule
        {
            Id = FormatRule(surface),
            Statement = statement,
            Check = new VoiceCheck { Type = VoiceChecker.DateFormat, Value = format },
            Surfaces = [surface],
            Citations = [Setting()],
            Accepted = now,
        };
    }

    /// <summary>
    /// The settings in force on each of a guide's surfaces, with the layer
    /// that set each one.
    /// </summary>
    /// <param name="guide">The time guide.</param>
    /// <param name="layers">Its profiles, least specific first, as <see cref="VoiceStore.LayersAsync"/> gives them.</param>
    public static IReadOnlyList<TimeSurface> Effective(VoiceGuide guide, IReadOnlyList<VoiceLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(guide);
        ArgumentNullException.ThrowIfNull(layers);

        var result = new List<TimeSurface>();

        foreach (var surface in guide.Surfaces)
        {
            var estimates = Winner(guide, layers, surface, EstimatesRule);
            var format = Winner(guide, layers, surface, FormatRule(surface));

            result.Add(new TimeSurface(
                surface,
                estimates?.Rule.Check?.Value ?? RangeWithOwner,
                estimates?.Layer,
                format?.Rule.Check?.Value,
                format?.Layer));
        }

        return result;
    }

    /// <summary>
    /// The rule of an id in force on one surface: the most specific layer's,
    /// except on a surface the person owns, where the least specific layer
    /// that sets it wins.
    /// </summary>
    public static ResolvedVoiceRule? Winner(VoiceGuide guide, IReadOnlyList<VoiceLayer> layers, string surface, string id)
    {
        ArgumentNullException.ThrowIfNull(guide);
        ArgumentNullException.ThrowIfNull(layers);

        var setting = layers
            .SelectMany(l => (l.Profile?.Rules ?? [])
                .Where(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)
                    && (r.Surfaces.Count == 0 || r.Surfaces.Contains(surface, StringComparer.OrdinalIgnoreCase)))
                .Select(r => new ResolvedVoiceRule(r, l.Layer, l.Name)))
            .ToList();

        if (setting.Count == 0)
        {
            return null;
        }

        return guide.PersonalSurfaces.Contains(surface, StringComparer.OrdinalIgnoreCase)
            ? setting[0]
            : setting[^1];
    }

    /// <summary>
    /// Flags dates written in a shape the surface's format does not allow.
    /// A pattern format is reported as unchecked rather than guessed at.
    /// </summary>
    internal static void Dates(
        VoiceRule rule,
        VoiceCheck check,
        string[] lines,
        List<VoiceFinding> findings,
        List<(string, string)> skipped)
    {
        if (string.IsNullOrWhiteSpace(check.Value))
        {
            skipped.Add((rule.Id, "names no format"));

            return;
        }

        if (Preset(check.Value) is not { } preset)
        {
            skipped.Add((rule.Id, check.Value.StartsWith(PatternPrefix, StringComparison.OrdinalIgnoreCase)
                ? "a pattern format is not checked, only presets are"
                : $"'{check.Value}' is not a format this version knows"));

            return;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            foreach (var (shape, value) in DatesIn(lines[i]))
            {
                if (!preset.Accepts.Contains(shape))
                {
                    findings.Add(new VoiceFinding(
                        rule.Id, VoiceChecker.DateFormat, i + 1,
                        $"'{value}' is not written {preset.Manner}, such as {preset.Example}.", Gates: false));
                }
            }
        }
    }

    /// <summary>
    /// Flags estimates of how long something will take: under
    /// <see cref="Never"/> every one, otherwise those with no range or with
    /// nobody's time named.
    /// </summary>
    internal static void Estimates(VoiceRule rule, VoiceCheck check, string[] lines, List<VoiceFinding> findings)
    {
        var never = string.Equals(check.Value?.Trim(), Never, StringComparison.OrdinalIgnoreCase);

        for (var i = 0; i < lines.Length; i++)
        {
            foreach (var sentence in SentenceBreak().Split(lines[i]))
            {
                if (Estimate(sentence) is not { } estimate)
                {
                    continue;
                }

                if (never)
                {
                    findings.Add(new VoiceFinding(
                        rule.Id, VoiceChecker.UnownedEstimate, i + 1,
                        $"'{estimate.Duration}' estimates how long work will take, which this voice says not to do.",
                        Gates: false));

                    continue;
                }

                var missing = new List<string>();

                if (!estimate.Ranged)
                {
                    missing.Add("a range");
                }

                if (!estimate.Owned)
                {
                    missing.Add("whose time it is");
                }

                if (missing.Count > 0)
                {
                    findings.Add(new VoiceFinding(
                        rule.Id, VoiceChecker.UnownedEstimate, i + 1,
                        $"'{estimate.Duration}' is an estimate without {string.Join(" or ", missing)}.", Gates: false));
                }
            }
        }
    }

    /// <summary>The estimate in a sentence, or null: a duration beside something saying it will or should take that long.</summary>
    internal static (string Duration, bool Ranged, bool Owned)? Estimate(string sentence)
    {
        try
        {
            if (!EstimateCue().IsMatch(sentence) || Duration().Match(sentence) is not { Success: true } duration)
            {
                return null;
            }

            var ranged = duration.Groups["high"].Success || duration.Groups["tail"].Success;

            return (duration.Value.Trim(), ranged, Owner().IsMatch(sentence));
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>Every date in a line, with the shape it is written in.</summary>
    internal static IEnumerable<(DateShape Shape, string Value)> DatesIn(string line)
    {
        foreach (Match match in IsoDate().Matches(line))
        {
            yield return (DateShape.Iso, match.Value);
        }

        foreach (Match match in NumericDate().Matches(line))
        {
            yield return (DateShape.Numeric, match.Value);
        }

        foreach (Match match in DayMonthYear().Matches(line))
        {
            yield return (DateShape.DayMonthYear, match.Value);
        }

        foreach (Match match in MonthDayYear().Matches(line))
        {
            yield return (DateShape.MonthDayYear, match.Value);
        }
    }

    private static VoiceCitation Setting() => new()
    {
        Source = SettingSource,
        Reference = "loadout voice time set",
        Excerpt = string.Empty,
    };
}
