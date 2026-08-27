using FluentAssertions;
using Loadout.Core.Usage;
using Loadout.Models.Configuration;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a launched agent is told about reporting itself, and what it is
/// deliberately never told.
/// </summary>
/// <remarks>
/// The absences matter more than the presences here. Both agents redact
/// conversation content until something asks for it, so the whole of the
/// privacy position is a list of settings this must never produce — and a list
/// of things that must not happen is only enforceable if something checks.
/// </remarks>
public sealed class TelemetryEnvironmentTests
{
    private static TelemetrySettings On(string endpoint = "http://127.0.0.1:4318") =>
        new() { Enabled = true, Endpoint = endpoint };

    [Fact]
    public void Nothing_is_set_when_reporting_is_off()
    {
        TelemetryEnvironment.For(new TelemetrySettings()).Should().BeEmpty();
        TelemetryEnvironment.For(null).Should().BeEmpty();
    }

    [Fact]
    public void Reporting_is_off_by_default()
    {
        // A launcher that started shipping usage somewhere the moment it was
        // installed would be doing something nobody asked it to do.
        new TelemetrySettings().Enabled.Should().BeFalse();
        TelemetryEnvironment.IsLoopback(new TelemetrySettings().Endpoint).Should().BeTrue();
    }

    [Theory]
    [InlineData("OTEL_LOG_USER_PROMPTS")]
    [InlineData("OTEL_LOG_ASSISTANT_RESPONSES")]
    [InlineData("OTEL_LOG_TOOL_DETAILS")]
    [InlineData("OTEL_LOG_TOOL_CONTENT")]
    [InlineData("OTEL_LOG_RAW_API_BODIES")]
    public void No_setting_that_would_carry_conversation_text_is_ever_produced(string variable)
    {
        var environment = TelemetryEnvironment.For(On());

        // Asserted first, because "does not contain" is true of an empty
        // dictionary: without this the test would pass just as happily if
        // reporting had been switched off entirely, and would be guarding
        // nothing at all.
        environment.Should().NotBeEmpty("reporting has to actually be configured here");

        // Each of these turns redaction off for one kind of content. They are
        // off in the agents unless asked for, so the rule is not to redact —
        // it is never to ask.
        environment.Should().NotContainKey(variable);

        TelemetryEnvironment.ContentVariables.Should().Contain(
            variable,
            "the list has to name it for this test to be able to guard it");
    }

    [Fact]
    public void Only_counts_are_asked_for_and_never_events_or_traces()
    {
        var environment = TelemetryEnvironment.For(On());

        environment["OTEL_METRICS_EXPORTER"].Should().Be("otlp");

        // Log events are where text would travel if a content setting were ever
        // turned on, and traces carry tool arguments. Neither is wanted.
        environment["OTEL_LOGS_EXPORTER"].Should().Be("none");
        environment["OTEL_TRACES_EXPORTER"].Should().Be("none");
    }

    [Fact]
    public void The_account_identifiers_are_switched_off()
    {
        // Claude Code puts an email address in plain text among the metric
        // attributes by default, and nothing here needs it: there is one person
        // on this machine and the session is what makes a count attributable.
        TelemetryEnvironment.For(On())["OTEL_METRICS_INCLUDE_ACCOUNT_UUID"].Should().Be("false");
        TelemetryEnvironment.For(On())["OTEL_METRICS_INCLUDE_SESSION_ID"].Should().Be("true");
    }

    [Theory]
    [InlineData("http://127.0.0.1:4318")]
    [InlineData("http://localhost:4318")]
    [InlineData("https://127.0.0.1:4318")]
    [InlineData("http://[::1]:4318")]
    public void An_address_on_this_machine_is_allowed(string endpoint) =>
        TelemetryEnvironment.IsLoopback(endpoint).Should().BeTrue();

    [Theory]
    [InlineData("http://192.168.1.10:4318")]
    [InlineData("http://collector.example.com:4318")]
    [InlineData("http://0.0.0.0:4318")]
    [InlineData("ftp://127.0.0.1:4318")]
    [InlineData("not a url")]
    [InlineData("")]
    public void Anything_that_leaves_this_machine_is_refused(string endpoint)
    {
        TelemetryEnvironment.IsLoopback(endpoint).Should().BeFalse();

        // Refused rather than obeyed: usage counts say when somebody works and
        // on what, and a config file can be edited by hand or copied between
        // machines. The moment that matters is the one before an agent is told
        // where to send something.
        TelemetryEnvironment.For(On(endpoint)).Should().BeEmpty();
    }

    [Fact]
    public void Being_refused_is_explained_rather_than_silent()
    {
        var said = TelemetryEnvironment.Describe(On("http://192.168.1.10:4318"));

        said.Should().NotBeNull();
        said.Should().Contain("loopback");
        said.Should().Contain("192.168.1.10");
    }

    [Fact]
    public void Nothing_is_said_when_it_is_simply_switched_off()
    {
        // The default. A launcher that mentioned every disabled feature at
        // every launch would teach people to read past it.
        TelemetryEnvironment.Describe(new TelemetrySettings()).Should().BeNull();
        TelemetryEnvironment.Describe(On()).Should().BeNull();
    }
}

/// <summary>
/// Reading what the agents post, and what is dropped on the way in.
/// </summary>
public sealed class OtlpMetricReaderTests
{
    /// <summary>A payload shaped like the ones a real agent sends.</summary>
    private const string Payload = """
        {"resourceMetrics":[{"resource":{"attributes":[]},"scopeMetrics":[{"metrics":[
          {"name":"claude_code.token.usage","unit":"tokens","sum":{
            "aggregationTemporality":1,"isMonotonic":true,"dataPoints":[
              {"attributes":[
                 {"key":"session.id","value":{"stringValue":"abc"}},
                 {"key":"user.email","value":{"stringValue":"someone@example.com"}},
                 {"key":"user.account_uuid","value":{"stringValue":"uuid-here"}},
                 {"key":"organization.id","value":{"stringValue":"org-here"}},
                 {"key":"model","value":{"stringValue":"claude-opus-5"}},
                 {"key":"type","value":{"stringValue":"cacheRead"}}],
               "timeUnixNano":"1787832196812000000","asInt":"28842"}]}},
          {"name":"claude_code.cost.usage","unit":"USD","sum":{
            "aggregationTemporality":1,"isMonotonic":true,"dataPoints":[
              {"attributes":[{"key":"session.id","value":{"stringValue":"abc"}}],
               "timeUnixNano":"1787832196812000000","asDouble":0.0247}]}}]}]}]}
        """;

    [Fact]
    public void The_counts_and_what_they_belong_to_are_read()
    {
        var samples = OtlpMetricReader.Read(Payload, DateTimeOffset.UnixEpoch);

        var tokens = samples.Single(s => s.Metric == "claude_code.token.usage");

        tokens.Value.Should().Be(28_842);
        tokens.Kind.Should().Be("cacheRead");
        tokens.Model.Should().Be("claude-opus-5");
        tokens.Session.Should().Be("abc");

        // The specification writes integers as quoted strings in JSON, which is
        // easy to read as text and get zero from.
        tokens.Value.Should().NotBe(0);
    }

    [Fact]
    public void Cost_arrives_as_a_real_number_rather_than_an_integer()
    {
        var cost = OtlpMetricReader
            .Read(Payload, DateTimeOffset.UnixEpoch)
            .Single(s => s.Metric == "claude_code.cost.usage");

        cost.Value.Should().BeApproximately(0.0247, 0.00001);
    }

    [Theory]
    [InlineData("someone@example.com")]
    [InlineData("uuid-here")]
    [InlineData("org-here")]
    public void Identity_in_the_payload_is_not_taken(string secret)
    {
        var samples = OtlpMetricReader.Read(Payload, DateTimeOffset.UnixEpoch);

        // The agents send these whether or not anything wants them. Not reading
        // them is what keeps them out of a file on disk.
        samples.Should().NotBeEmpty();

        foreach (var sample in samples)
        {
            sample.Session.Should().NotContain(secret);
            sample.Kind.Should().NotContain(secret);
            sample.Model.Should().NotContain(secret);
        }
    }

    [Fact]
    public void A_running_total_is_marked_as_one()
    {
        // Adding these up would multiply the answer by however many times the
        // agent reported. Both mistakes of exactly this shape were made while
        // building this feature, so the distinction is carried rather than
        // assumed.
        var cumulative = Payload.Replace("\"aggregationTemporality\":1", "\"aggregationTemporality\":2",
            StringComparison.Ordinal);

        OtlpMetricReader.Read(cumulative, DateTimeOffset.UnixEpoch)
            .Should().OnlyContain(s => s.IsCumulative);

        OtlpMetricReader.Read(Payload, DateTimeOffset.UnixEpoch)
            .Should().OnlyContain(s => !s.IsCumulative);
    }

    [Fact]
    public void Something_that_is_not_a_payload_yields_nothing_rather_than_throwing()
    {
        // This is fed by a socket. It must not be possible to stop the receiver
        // by posting rubbish at it.
        OtlpMetricReader.Read("not json", DateTimeOffset.UnixEpoch).Should().BeEmpty();
        OtlpMetricReader.Read("[]", DateTimeOffset.UnixEpoch).Should().BeEmpty();
        OtlpMetricReader.Read("{}", DateTimeOffset.UnixEpoch).Should().BeEmpty();
        OtlpMetricReader.Read("""{"resourceMetrics":"nonsense"}""", DateTimeOffset.UnixEpoch)
            .Should().BeEmpty();
    }

    [Fact]
    public void The_time_comes_from_the_payload_rather_than_when_it_arrived()
    {
        var sample = OtlpMetricReader.Read(Payload, DateTimeOffset.UnixEpoch).First();

        sample.When.Should().BeAfter(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }
}
