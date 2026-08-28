using System.Globalization;
using System.Text.Json;

namespace Loadout.Core.Usage;

/// <summary>One number an agent reported about itself.</summary>
/// <param name="When">When the reporting window ended.</param>
/// <param name="Session">The agent session it belongs to.</param>
/// <param name="Metric">The metric name, as the agent spells it.</param>
/// <param name="Kind">
/// What sort of number this is within its metric — <c>input</c>,
/// <c>cacheRead</c> and so on — or empty where the metric has only one.
/// </param>
/// <param name="Model">The model it concerns, where the metric names one.</param>
/// <param name="Value">The number itself.</param>
/// <param name="IsCumulative">
/// Whether the value is a running total rather than an amount since the last
/// report. Carried because it decides whether these may be added up.
/// </param>
public sealed record TelemetrySample(
    DateTimeOffset When,
    string Session,
    string Metric,
    string Kind,
    string Model,
    double Value,
    bool IsCumulative);

/// <summary>
/// Reads the OpenTelemetry metrics an agent posts.
/// </summary>
/// <remarks>
/// <para>
/// The payload is OTLP over HTTP in JSON, which the agents produce when asked
/// for <c>http/json</c>. That choice is what keeps this to ordinary JSON
/// parsing rather than protobuf and gRPC.
/// </para>
/// <para>
/// Only the identifiers needed to attribute a number are kept: the session, the
/// model, and what sort of token it was. The payload also carries an account
/// identifier and an email address in plain text, and neither is read, stored
/// or reported.
/// </para>
/// </remarks>
internal static class OtlpMetricReader
{
    /// <summary>OTLP's spelling of "this is a running total".</summary>
    private const int Cumulative = 2;

    /// <summary>Identifiers this reader deliberately does not take.</summary>
    /// <remarks>
    /// Written down so a test can hold it down. The agents put an email address
    /// among the metric attributes by default; a usage store is not the place
    /// for it, and nothing here needs it to attribute a count.
    /// </remarks>
    public static IReadOnlyList<string> IgnoredAttributes =>
    [
        "user.email",
        "user.id",
        "user.account_uuid",
        "user.account_id",
        "organization.id",
    ];

    /// <summary>
    /// Every sample in one posted payload, or nothing when it cannot be read.
    /// </summary>
    public static IReadOnlyList<TelemetrySample> Read(string json, DateTimeOffset received)
    {
        var samples = new List<TelemetrySample>();

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return samples;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("resourceMetrics", out var resources)
                || resources.ValueKind != JsonValueKind.Array)
            {
                return samples;
            }

            foreach (var resource in resources.EnumerateArray())
            {
                foreach (var scope in Array(resource, "scopeMetrics"))
                {
                    foreach (var metric in Array(scope, "metrics"))
                    {
                        ReadMetric(metric, received, samples);
                    }
                }
            }
        }

        return samples;
    }

    private static void ReadMetric(
        JsonElement metric,
        DateTimeOffset received,
        List<TelemetrySample> samples)
    {
        if (Text(metric, "name") is not { Length: > 0 } name)
        {
            return;
        }

        // Counters arrive under "sum"; anything else the agents add later
        // arrives under "gauge", and both hold their points the same way.
        if (!metric.TryGetProperty("sum", out var series)
            && !metric.TryGetProperty("gauge", out series))
        {
            return;
        }

        var cumulative = series.TryGetProperty("aggregationTemporality", out var temporality)
            && temporality.ValueKind == JsonValueKind.Number
            && temporality.GetInt32() == Cumulative;

        foreach (var point in Array(series, "dataPoints"))
        {
            var value = Value(point);

            if (value is null)
            {
                continue;
            }

            var attributes = Attributes(point);

            samples.Add(new TelemetrySample(
                Moment(point) ?? received,
                attributes.GetValueOrDefault("session.id", string.Empty),
                name,
                attributes.GetValueOrDefault("type", string.Empty),
                attributes.GetValueOrDefault("model", string.Empty),
                value.Value,
                cumulative));
        }
    }

    /// <summary>
    /// The attributes worth keeping, flattened out of OTLP's key/value shape.
    /// </summary>
    private static Dictionary<string, string> Attributes(JsonElement point)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in Array(point, "attributes"))
        {
            if (Text(entry, "key") is not { Length: > 0 } key
                || IgnoredAttributes.Contains(key, StringComparer.Ordinal))
            {
                continue;
            }

            if (entry.TryGetProperty("value", out var wrapper)
                && wrapper.ValueKind == JsonValueKind.Object
                && Text(wrapper, "stringValue") is { } text)
            {
                attributes[key] = text;
            }
        }

        return attributes;
    }

    /// <summary>
    /// A point's value, which OTLP writes as an integer or a double depending
    /// on the metric.
    /// </summary>
    private static double? Value(JsonElement point)
    {
        if (point.TryGetProperty("asInt", out var integer))
        {
            // Written as a quoted string in JSON OTLP, per the specification.
            return integer.ValueKind switch
            {
                JsonValueKind.Number when integer.TryGetInt64(out var number) => number,
                JsonValueKind.String when long.TryParse(
                    integer.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsed) => parsed,
                _ => null,
            };
        }

        return point.TryGetProperty("asDouble", out var real)
            && real.ValueKind == JsonValueKind.Number
            && real.TryGetDouble(out var value)
            ? value
            : null;
    }

    /// <summary>When the reporting window closed.</summary>
    private static DateTimeOffset? Moment(JsonElement point)
    {
        if (!point.TryGetProperty("timeUnixNano", out var stamp))
        {
            return null;
        }

        var nanoseconds = stamp.ValueKind switch
        {
            JsonValueKind.String when long.TryParse(
                stamp.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            JsonValueKind.Number when stamp.TryGetInt64(out var number) => number,
            _ => 0L,
        };

        return nanoseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(nanoseconds / 1_000_000)
            : null;
    }

    private static IEnumerable<JsonElement> Array(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray()
            : [];

    private static string? Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
