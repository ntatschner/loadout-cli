using Loadout.Models.Configuration;

namespace Loadout.Core.Usage;

/// <summary>
/// What a launched agent is told about reporting its own usage.
/// </summary>
/// <remarks>
/// <para>
/// Both agents can emit OpenTelemetry, and both read the same standard
/// variables to decide where. The launcher is the only component placed to set
/// them, because launching is the thing it does — nobody has to edit an agent's
/// own configuration, and nothing has to be remembered per project.
/// </para>
/// <para>
/// A pure function on purpose. What this returns is the whole of the policy,
/// including everything it deliberately does not return, and that is far easier
/// to hold down in a test than behaviour spread through a launch path.
/// </para>
/// </remarks>
public static class TelemetryEnvironment
{
    /// <summary>
    /// The settings that would carry conversation text if they were ever set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All five default to off in the agents, and content is redacted until
    /// one of them is turned on. So the rule here is not to redact anything —
    /// it is never to ask for it in the first place.
    /// </para>
    /// <para>
    /// Named rather than merely omitted so that a test can assert their
    /// absence. A list of things that must not happen is only enforceable if
    /// something writes down what they are.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> ContentVariables =>
    [
        "OTEL_LOG_USER_PROMPTS",
        "OTEL_LOG_ASSISTANT_RESPONSES",
        "OTEL_LOG_TOOL_DETAILS",
        "OTEL_LOG_TOOL_CONTENT",
        "OTEL_LOG_RAW_API_BODIES",
    ];

    /// <summary>
    /// The variables to add to a launched agent's environment, or nothing at
    /// all when reporting is off or the endpoint cannot be trusted.
    /// </summary>
    /// <remarks>
    /// Returning nothing rather than throwing when the endpoint is wrong: a
    /// misconfigured report must not stop somebody starting work. The caller
    /// warns instead, which is <see cref="Describe"/>.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> For(TelemetrySettings? settings)
    {
        if (settings is not { Enabled: true } || !IsLoopback(settings.Endpoint))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CLAUDE_CODE_ENABLE_TELEMETRY"] = "1",

            // Metrics are counts and nothing else. Log events are the signal
            // that would carry text if the content settings were ever turned
            // on, and traces carry tool arguments, so neither is asked for.
            ["OTEL_METRICS_EXPORTER"] = "otlp",
            ["OTEL_LOGS_EXPORTER"] = "none",
            ["OTEL_TRACES_EXPORTER"] = "none",

            // JSON over HTTP rather than gRPC and protobuf. It is the same
            // data, and it means the thing that receives it can be an ordinary
            // HTTP listener rather than a dependency.
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/json",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = settings.Endpoint,

            // Often enough to be useful in a session somebody is still in.
            ["OTEL_METRIC_EXPORT_INTERVAL"] = "10000",

            // The session identifier is what makes a count attributable to a
            // piece of work. The account identifiers are not: they say who,
            // which is already known — there is one person on this machine —
            // and Claude Code puts an email address in plain text among them.
            ["OTEL_METRICS_INCLUDE_SESSION_ID"] = "true",
            ["OTEL_METRICS_INCLUDE_ACCOUNT_UUID"] = "false",
        };
    }

    /// <summary>
    /// What to tell somebody about the state of this, or null when there is
    /// nothing worth saying.
    /// </summary>
    /// <remarks>
    /// Only speaks up when reporting was asked for and cannot be delivered.
    /// Silence when it is switched off is correct: that is the default, and a
    /// launcher that mentioned every disabled feature at every launch would
    /// teach people to read past it.
    /// </remarks>
    public static string? Describe(TelemetrySettings? settings)
    {
        if (settings is not { Enabled: true })
        {
            return null;
        }

        return IsLoopback(settings.Endpoint)
            ? null
            : $"Usage reporting is on but '{settings.Endpoint}' is not a loopback address, "
                + "so it has been left off for this launch. Set telemetry-endpoint to an "
                + "address on this machine, or turn telemetry off.";
    }

    /// <summary>
    /// Whether an endpoint stays on this machine.
    /// </summary>
    /// <remarks>
    /// Checked rather than assumed, and checked here rather than when the value
    /// is set: a configuration file can be edited by hand, copied between
    /// machines, or written by an older build that did not check. The moment
    /// that matters is the moment before an agent is told where to send
    /// something.
    /// </remarks>
    public static bool IsLoopback(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        // IsLoopback covers 127.0.0.0/8 and ::1; the name is checked too
        // because a host file can point "localhost" somewhere else, and the
        // literal spelling is what somebody reading the config will recognise.
        return uri.IsLoopback
            || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
    }
}
