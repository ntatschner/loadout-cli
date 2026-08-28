using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Loadout.Core.Configuration;

/// <summary>
/// Reads and writes <see cref="DateTimeOffset"/> as a single ISO-8601 string.
/// <para>
/// Without this, YamlDotNet serialises the struct as a graph of its own
/// properties — <c>date_time</c>, <c>utc_date_time</c>, <c>day_of_year</c> and
/// twenty more — which is unreadable in a file meant to be reviewed in a pull
/// request and, worse, cannot be read back. Deserialisation silently produced
/// <c>DateTimeOffset.MinValue</c>, so every timestamp the launcher wrote came
/// back as the year 1: backup sets sorted arbitrarily and recent-project
/// ordering had nothing to sort on.
/// </para>
/// </summary>
internal sealed class DateTimeOffsetConverter : IYamlTypeConverter
{
    /// <summary>
    /// Round-trip format. Keeps the offset, so a timestamp written in one time
    /// zone still means the same instant when read in another.
    /// </summary>
    private const string Format = "O";

    /// <inheritdoc />
    public bool Accepts(Type type) =>
        type == typeof(DateTimeOffset) || type == typeof(DateTimeOffset?);

    /// <summary>
    /// The key to trust in a legacy mapping. The struct exposed several
    /// timestamps and only this one is unambiguous about its zone.
    /// </summary>
    private const string LegacyKey = "utc_date_time";

    /// <inheritdoc />
    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        // Files written before this converter existed hold the struct expanded
        // into a mapping of its own properties. Reading those is not politeness
        // about an old format: every one of them is a backup set somebody could
        // still need to restore, and refusing the file would orphan it.
        if (parser.Current is MappingStart)
        {
            return ReadLegacyMapping(parser, type);
        }

        var scalar = parser.Consume<Scalar>();

        if (string.IsNullOrWhiteSpace(scalar.Value))
        {
            return type == typeof(DateTimeOffset?)
                ? null
                : throw new YamlException(
                    scalar.Start,
                    scalar.End,
                    "A timestamp was expected but the value was empty.");
        }

        if (DateTimeOffset.TryParse(
            scalar.Value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed))
        {
            return parsed;
        }

        // Reported with its position rather than defaulted. A timestamp that
        // silently became the year 1 is exactly the failure this converter
        // exists to fix, and swallowing a bad value here would reintroduce it.
        throw new YamlException(
            scalar.Start, scalar.End, $"'{scalar.Value}' is not a valid timestamp.");
    }

    private static object? ReadLegacyMapping(IParser parser, Type type)
    {
        parser.Consume<MappingStart>();

        DateTimeOffset? found = null;
        var depth = 0;

        while (true)
        {
            if (parser.Current is MappingStart or SequenceStart)
            {
                depth++;
                parser.MoveNext();

                continue;
            }

            if (parser.Current is MappingEnd or SequenceEnd)
            {
                parser.MoveNext();

                if (depth == 0)
                {
                    break;
                }

                depth--;

                continue;
            }

            if (depth == 0
                && parser.Current is Scalar { Value: LegacyKey }
                && parser.MoveNext()
                && parser.Current is Scalar value
                && DateTimeOffset.TryParse(
                    value.Value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsed))
            {
                found = parsed;
            }

            parser.MoveNext();
        }

        return found ?? (type == typeof(DateTimeOffset?) ? null : DateTimeOffset.MinValue);
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        if (value is null)
        {
            emitter.Emit(new Scalar(string.Empty));
            return;
        }

        emitter.Emit(new Scalar(
            ((DateTimeOffset)value).ToString(Format, CultureInfo.InvariantCulture)));
    }
}
