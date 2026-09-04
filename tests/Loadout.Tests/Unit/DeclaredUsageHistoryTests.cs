using FluentAssertions;
using Loadout.Core.Usage;
using Loadout.Models.Agents;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Counting an agent's tokens from a description of its transcripts.
/// </summary>
/// <remarks>
/// <para>
/// As with the session reader, the first test is the one that carries the
/// weight: Claude's own layout expressed as configuration, checked against the
/// compiled Claude reader over the same files. A reader driven by configuration
/// will otherwise agree with whatever test was written beside it.
/// </para>
/// <para>
/// The rest are about the thing counting must get right and listing need not:
/// saying when it could not read something. A total that quietly counts zero for
/// a renamed field looks entirely reasonable, and that is the failure this layer
/// exists to make visible.
/// </para>
/// </remarks>
public sealed class DeclaredUsageHistoryTests : IDisposable
{
    private static readonly DateOnly Everything = new(2000, 1, 1);

    private readonly string _root;
    private readonly FakeEnvironmentProvider _environment;

    public DeclaredUsageHistoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-usage-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_root);

        _environment = new FakeEnvironmentProvider(Path.Combine(_root, "home"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing the run over.
        }
    }

    [Fact]
    public async Task A_described_format_counts_what_the_compiled_reader_for_it_counts()
    {
        WriteClaudeTranscript(
            "starstats",
            Record("msg-1", "/repos/starstats", "claude-opus-5", input: 120, output: 40),
            Record("msg-2", "/repos/starstats", "claude-opus-5", input: 200, output: 60));

        var compiled = await new ClaudeUsageHistory(_environment).ScanAsync(Everything);
        var declared = await Reader(ClaudeFormat()).ScanAsync(Everything);

        compiled.Succeeded.Should().BeTrue(compiled.Error);
        declared.Succeeded.Should().BeTrue(declared.Error);

        compiled.Value!.Buckets.Should().NotBeEmpty("the fixture has to exercise the compiled reader too");

        declared.Value!.Buckets
            .Select(bucket => (bucket.Directory, bucket.Day, bucket.Model, bucket.Totals))
            .Should().BeEquivalentTo(compiled.Value!.Buckets
                .Select(bucket => (bucket.Directory, bucket.Day, bucket.Model, bucket.Totals)));

        declared.Value.Integrity.RecordsCounted
            .Should().Be(compiled.Value.Integrity.RecordsCounted);
    }

    [Fact]
    public async Task A_record_counted_once_is_not_counted_again_when_a_session_is_resumed()
    {
        // Agents copy earlier accounting into the transcript of a resumed
        // conversation. Counting it twice is the easiest way to produce a number
        // that is wrong and looks right.
        WriteClaudeTranscript("first", Record("msg-1", "/repos/a", "opus", input: 100, output: 10));
        WriteClaudeTranscript("second", Record("msg-1", "/repos/a", "opus", input: 100, output: 10));

        var scan = (await Reader(ClaudeFormat()).ScanAsync(Everything)).Value!;

        scan.Buckets.Single().Totals.Input.Should().Be(100);
        scan.Integrity.RecordsRepeated.Should().Be(1);
        scan.Integrity.RecordsCounted.Should().Be(1);
    }

    [Fact]
    public async Task Everything_is_counted_when_the_format_names_no_identifier()
    {
        // Without something to tell one record from another there is no way to
        // see a repeat, so they are all counted. Reported here so the cost of
        // leaving the path out is a known cost rather than a surprise.
        WriteClaudeTranscript("first", Record("msg-1", "/repos/a", "opus", input: 100, output: 10));
        WriteClaudeTranscript("second", Record("msg-1", "/repos/a", "opus", input: 100, output: 10));

        var format = ClaudeFormat();
        format.Usage!.Id = null;

        var scan = (await Reader(format).ScanAsync(Everything)).Value!;

        scan.Buckets.Single().Totals.Input.Should().Be(200);
        scan.Integrity.RecordsRepeated.Should().Be(0);
    }

    [Fact]
    public async Task A_record_carrying_no_number_this_description_can_find_is_reported()
    {
        // The signal that the agent has changed its format underneath us. A
        // reader that met this and stayed quiet would return a smaller total
        // with nothing on screen to say so.
        WriteLines("logs", "session.jsonl",
            "{\"message\":{\"id\":\"msg-1\",\"model\":\"opus\",\"usage\":{\"renamed_input\":100}}}");

        var scan = (await Reader(ClaudeFormat(Path.Combine(_root, "logs"))).ScanAsync(Everything)).Value!;

        scan.Buckets.Should().BeEmpty();
        scan.Integrity.RecordsUnrecognised.Should().Be(1);
        scan.Integrity.IsComplete.Should().BeFalse();
        scan.Integrity.Caveat.Should().Contain("could not read");
    }

    [Fact]
    public async Task An_ordinary_line_that_is_not_accounting_is_passed_over_in_silence()
    {
        // Most lines of a transcript are not accounting records. Reporting them
        // as unreadable would put a caveat on every total ever printed, and a
        // caveat that is always there is one nobody reads.
        WriteLines("logs", "session.jsonl",
            "{\"type\":\"user\",\"text\":\"fix the upload path\"}",
            "not json at all",
            "{\"message\":{\"id\":\"msg-1\",\"model\":\"opus\",\"usage\":{\"input_tokens\":10}}}");

        var scan = (await Reader(ClaudeFormat(Path.Combine(_root, "logs"))).ScanAsync(Everything)).Value!;

        scan.Integrity.RecordsUnrecognised.Should().Be(0);
        scan.Integrity.IsComplete.Should().BeTrue();
        scan.Integrity.RecordsCounted.Should().Be(1);
    }

    [Fact]
    public async Task Records_before_the_window_are_read_but_not_counted_into_the_totals()
    {
        WriteLines("logs", "session.jsonl",
            Line("old", "/repos/a", "opus", 100, 10, "2026-01-01T09:00:00Z"),
            Line("new", "/repos/a", "opus", 5, 1, "2026-03-01T09:00:00Z"));

        var scan = (await Reader(ClaudeFormat(Path.Combine(_root, "logs")))
            .ScanAsync(new DateOnly(2026, 2, 1))).Value!;

        scan.Buckets.Single().Totals.Input.Should().Be(5);

        // Still read, so a repeat of it later in the scan is still recognised as
        // a repeat rather than counted as new.
        scan.Integrity.RecordsCounted.Should().Be(2);
    }

    [Fact]
    public async Task The_working_directory_carries_forward_to_the_lines_after_it()
    {
        // Formats that write it once expect it to hold. Filing later records
        // under "unknown" would split one project's spend in two.
        WriteLines("logs", "session.jsonl",
            "{\"cwd\":\"/repos/a\"}",
            "{\"message\":{\"id\":\"m1\",\"model\":\"opus\",\"usage\":{\"input_tokens\":10}}}");

        var scan = (await Reader(ClaudeFormat(Path.Combine(_root, "logs"))).ScanAsync(Everything)).Value!;

        scan.Buckets.Single().Directory.Should().Be("/repos/a");
    }

    [Fact]
    public void A_format_describing_no_numbers_counts_nothing_rather_than_guessing()
    {
        Directory.CreateDirectory(Path.Combine(_root, "logs"));

        var format = ClaudeFormat(Path.Combine(_root, "logs"));
        format.Usage = null;

        format.CanCount.Should().BeFalse();
        Reader(format).IsAvailable.Should().BeFalse();
    }

    private DeclaredUsageHistory Reader(TranscriptFormat format) =>
        new("scribe", format, _environment);

    /// <summary>Claude's layout, written as configuration rather than code.</summary>
    private static TranscriptFormat ClaudeFormat(string? root = null) => new()
    {
        Root = root ?? "~/.claude/projects",
        Files = "*.jsonl",
        Recursive = true,
        Session = new TranscriptSessionFormat { Id = "sessionId", Directory = "cwd" },
        Usage = new TranscriptUsageFormat
        {
            Timestamp = "timestamp",
            Directory = "cwd",
            Model = "message.model",
            Id = "message.id",
            Input = "message.usage.input_tokens",
            Output = "message.usage.output_tokens",
            CacheRead = "message.usage.cache_read_input_tokens",
            CacheWrite5m = "message.usage.cache_creation.ephemeral_5m_input_tokens",
            CacheWrite1h = "message.usage.cache_creation.ephemeral_1h_input_tokens",
        },
    };

    private static string Record(string id, string cwd, string model, long input, long output) =>
        Line(id, cwd, model, input, output, "2026-02-01T09:00:00Z");

    private static string Line(
        string id,
        string cwd,
        string model,
        long input,
        long output,
        string timestamp) =>
        $"{{\"timestamp\":\"{timestamp}\",\"cwd\":\"{cwd}\",\"message\":{{\"id\":\"{id}\","
        + $"\"model\":\"{model}\",\"usage\":{{\"input_tokens\":{input},\"output_tokens\":{output},"
        + "\"cache_read_input_tokens\":0,"
        + "\"cache_creation\":{\"ephemeral_5m_input_tokens\":0,\"ephemeral_1h_input_tokens\":0}}}}";

    private void WriteClaudeTranscript(string project, params string[] lines)
    {
        var directory = Path.Combine(_environment.HomeDirectory, ".claude", "projects", project);

        Directory.CreateDirectory(directory);

        File.WriteAllLines(Path.Combine(directory, project + ".jsonl"), lines);
    }

    private void WriteLines(string relative, string file, params string[] lines)
    {
        var directory = Path.Combine(_root, relative);

        Directory.CreateDirectory(directory);

        File.WriteAllLines(Path.Combine(directory, file), lines);
    }
}
