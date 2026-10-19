using System.Globalization;
using System.Text.RegularExpressions;

namespace Loadout.Core.Lists;

/// <summary>Reads the values a date or number term compares against.</summary>
internal static partial class ListValues
{
    /// <summary>A comparison against a moment: an age, or a day.</summary>
    internal readonly record struct DateTest(string Op, TimeSpan? Age, DateTimeOffset? Day)
    {
        public bool Holds(DateTimeOffset at, DateTimeOffset now)
        {
            if (Age is { } age)
            {
                // An age reads as how old the thing is: <7d is newer than a
                // week, >7d older, and a bare 7d means within it.
                var old = now - at;

                return Op switch
                {
                    ">" => old > age,
                    ">=" => old >= age,
                    "<" => old < age,
                    _ => old <= age,
                };
            }

            // A day reads as the calendar: >D is after it, <D before it, and a
            // bare D is that day, all in UTC.
            var start = Day!.Value;
            var end = start.AddDays(1);

            return Op switch
            {
                ">" => at >= end,
                ">=" => at >= start,
                "<" => at < start,
                "<=" => at < end,
                _ => at >= start && at < end,
            };
        }
    }

    internal readonly record struct NumberTest(string Op, double Value)
    {
        public bool Holds(double count) => Op switch
        {
            ">" => count > Value,
            ">=" => count >= Value,
            "<" => count < Value,
            "<=" => count <= Value,
            _ => count == Value,
        };
    }

    private static (string Op, string Operand) SplitOp(string value)
    {
        foreach (var op in new[] { ">=", "<=", ">", "<" })
        {
            if (value.StartsWith(op, StringComparison.Ordinal))
            {
                return (op, value[op.Length..]);
            }
        }

        return ("=", value);
    }

    internal static bool TryDate(string value, out DateTest test)
    {
        var (op, rest) = SplitOp(value);
        var age = AgePattern().Match(rest);

        if (age.Success)
        {
            var count = int.Parse(age.Groups[1].Value, CultureInfo.InvariantCulture);
            var span = age.Groups[2].Value.ToLowerInvariant() switch
            {
                "m" => TimeSpan.FromMinutes(count),
                "h" => TimeSpan.FromHours(count),
                "d" => TimeSpan.FromDays(count),
                _ => TimeSpan.FromDays(count * 7),
            };

            test = new DateTest(op, span, null);

            return true;
        }

        if (DateTimeOffset.TryParseExact(rest, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var day))
        {
            test = new DateTest(op, null, day.ToUniversalTime());

            return true;
        }

        test = default;

        return false;
    }

    internal static bool TryNumber(string value, out NumberTest test)
    {
        var (op, rest) = SplitOp(value);

        if (double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            test = new NumberTest(op, number);

            return true;
        }

        test = default;

        return false;
    }

    [GeneratedRegex("^([0-9]{1,6})([mhdwMHDW])$")]
    private static partial Regex AgePattern();
}
