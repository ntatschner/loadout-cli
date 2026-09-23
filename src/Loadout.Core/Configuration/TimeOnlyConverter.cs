using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Loadout.Core.Configuration;

/// <summary>
/// Reads and writes <see cref="TimeOnly"/> as a single clock time.
/// <para>
/// The same failure <see cref="DateTimeOffsetConverter"/> exists to fix, found
/// again in a type added after it. Without this, YamlDotNet serialises the
/// struct as a mapping of its own properties — <c>hour</c>, <c>minute</c>,
/// <c>microsecond</c>, <c>ticks</c> and the rest — and cannot read any of it
/// back, so every value returned as midnight.
/// </para>
/// <para>
/// What that cost: <c>team schedule add nightly docs-crew "..." --at 23:00</c>
/// was accepted, echoed back as 23:00, and written to disk as
/// <c>hour: 0, minute: 0, ticks: 0</c>. Every daily schedule on the machine
/// then came due at midnight instead of the hour somebody chose, and nothing
/// anywhere said so — the command had already printed the right answer.
/// </para>
/// </summary>
internal sealed class TimeOnlyConverter : IYamlTypeConverter
{
    /// <summary>
    /// Written as somebody would type it. <c>23:00</c> rather than a
    /// round-trip form full of zeroes, because these files are read by people
    /// and a schedule's time is the part they check.
    /// </summary>
    private const string Short = "HH\\:mm";

    /// <summary>For anything with a second or finer in it, which is unusual here.</summary>
    private const string Long = "HH\\:mm\\:ss.FFFFFFF";

    /// <inheritdoc />
    public bool Accepts(Type type) =>
        type == typeof(TimeOnly) || type == typeof(TimeOnly?);

    /// <inheritdoc />
    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        ArgumentNullException.ThrowIfNull(parser);

        // Files written before this converter existed hold the struct expanded
        // into a mapping. Every one of those is a schedule somebody made, and
        // refusing the file would take out the whole list rather than one
        // entry - so they are read, and their hour and minute honoured.
        if (parser.Current is MappingStart)
        {
            return ReadLegacyMapping(parser, type);
        }

        var scalar = parser.Consume<Scalar>();

        if (string.IsNullOrWhiteSpace(scalar.Value))
        {
            return type == typeof(TimeOnly?)
                ? null
                : throw new YamlException(
                    scalar.Start, scalar.End, "A time of day was expected but the value was empty.");
        }

        if (TimeOnly.TryParse(scalar.Value, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        // Reported with its position rather than defaulted. A time that
        // silently became midnight is the failure this exists to fix, and
        // swallowing a bad value here would put it straight back.
        throw new YamlException(
            scalar.Start, scalar.End, $"'{scalar.Value}' is not a valid time of day.");
    }

    /// <summary>Rebuilds the time from the properties an older file wrote.</summary>
    private static object? ReadLegacyMapping(IParser parser, Type type)
    {
        parser.Consume<MappingStart>();

        int hour = 0, minute = 0, second = 0;
        var found = false;
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
                && parser.Current is Scalar { Value: "hour" or "minute" or "second" } key
                && parser.MoveNext()
                && parser.Current is Scalar number
                && int.TryParse(number.Value, CultureInfo.InvariantCulture, out var value))
            {
                found = true;

                switch (key.Value)
                {
                    case "hour":
                        hour = value;
                        break;
                    case "minute":
                        minute = value;
                        break;
                    default:
                        second = value;
                        break;
                }
            }

            parser.MoveNext();
        }

        if (!found)
        {
            return type == typeof(TimeOnly?) ? null : TimeOnly.MinValue;
        }

        // Out of range means the mapping was not a time at all, and midnight is
        // a better answer there than an exception that takes the file with it.
        return hour is >= 0 and < 24 && minute is >= 0 and < 60 && second is >= 0 and < 60
            ? new TimeOnly(hour, minute, second)
            : TimeOnly.MinValue;
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(emitter);

        if (value is not TimeOnly time)
        {
            emitter.Emit(new Scalar(string.Empty));

            return;
        }

        emitter.Emit(new Scalar(time.ToString(
            time.Second == 0 && time.Millisecond == 0 ? Short : Long,
            CultureInfo.InvariantCulture)));
    }
}
