using FluentAssertions;
using Loadout.Core.Usage;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What the readers do with the shapes the agents actually write.
/// </summary>
/// <remarks>
/// Written against transcripts built to match real ones, because the two
/// counting mistakes worth guarding against are invisible in a tidy sample:
/// Claude repeats a message's accounting once per content block, and Codex
/// reports running totals rather than amounts spent. Both look like ordinary
/// data. Only a fixture that repeats things the way the real files do can tell
/// a reader that handles it from one that does not.
/// </remarks>
public sealed class UsageReaderTests : IDisposable
{
    private readonly string _home;

    public UsageReaderTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "loadout-usage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
            // A temporary directory that outlives the test is not a failure.
        }
    }

    private static readonly DateOnly Long = new(2000, 1, 1);

    // ---------------------------------------------------------------- Claude

    /// <summary>One Claude transcript line carrying a message's whole accounting.</summary>
    private static string ClaudeLine(
        string messageId,
        string day,
        long input = 0,
        long cacheRead = 0,
        long write5m = 0,
        long write1h = 0,
        long output = 0,
        long thinking = 0,
        string model = "claude-opus-5",
        string cwd = "/repos/alpha") =>
        // Braces that close an object are spaced away from the interpolations
        // so the raw string cannot read them as part of a hole. JSON does not
        // mind the whitespace.
        $$"""
        {"type":"assistant","timestamp":"{{day}}T12:00:00.000Z","cwd":"{{cwd}}",
         "message":{"id":"{{messageId}}","model":"{{model}}","usage":{
           "input_tokens":{{input}},"cache_read_input_tokens":{{cacheRead}},
           "cache_creation_input_tokens":{{write5m + write1h}},
           "cache_creation":{"ephemeral_5m_input_tokens":{{write5m}},
                             "ephemeral_1h_input_tokens":{{write1h}} },
           "output_tokens":{{output}},
           "output_tokens_details":{"thinking_tokens":{{thinking}} } } } }
        """.ReplaceLineEndings(string.Empty);

    private void WriteClaude(string sessionId, params string[] lines)
    {
        var directory = Path.Combine(_home, ".claude", "projects", "repos-alpha");

        Directory.CreateDirectory(directory);

        File.WriteAllLines(Path.Combine(directory, sessionId + ".jsonl"), lines);
    }

    private ClaudeUsageHistory Claude() => new(new FakeEnvironmentProvider(_home));

    [Fact]
    public async Task A_messages_accounting_is_counted_once_however_many_lines_repeat_it()
    {
        // Exactly what a real transcript does: one line per content block —
        // the thinking, the text, each tool call — every one carrying the
        // whole message's usage rather than its own share.
        WriteClaude(
            "session-a",
            ClaudeLine("msg_1", "2026-08-20", input: 10, cacheRead: 5_000, output: 400),
            ClaudeLine("msg_1", "2026-08-20", input: 10, cacheRead: 5_000, output: 400),
            ClaudeLine("msg_1", "2026-08-20", input: 10, cacheRead: 5_000, output: 400),
            ClaudeLine("msg_2", "2026-08-20", input: 20, cacheRead: 6_000, output: 500));

        var scan = await Claude().ScanAsync(Long);

        scan.Succeeded.Should().BeTrue();

        var totals = Total(scan.Value!);

        // Counting lines rather than messages gives 1,600 output and 21,000
        // read — figures that look perfectly ordinary and are wrong by three
        // quarters. This is that bug, held down.
        totals.Output.Should().Be(900);
        totals.CacheRead.Should().Be(11_000);
        totals.Input.Should().Be(30);

        scan.Value!.Integrity.RecordsCounted.Should().Be(2);
        scan.Value.Integrity.RecordsRepeated.Should().Be(2);
        scan.Value.Integrity.IsComplete.Should().BeTrue("repeats are expected, not a fault");
    }

    [Fact]
    public async Task A_message_copied_into_a_resumed_transcript_is_still_paid_for_once()
    {
        // Resuming or forking copies earlier messages into a new file. They
        // were billed when they happened, not again when they were copied.
        WriteClaude("session-a", ClaudeLine("msg_1", "2026-08-20", output: 400));
        WriteClaude("session-b", ClaudeLine("msg_1", "2026-08-20", output: 400));

        var scan = await Claude().ScanAsync(Long);

        Total(scan.Value!).Output.Should().Be(400);
    }

    [Fact]
    public async Task Cache_lifetimes_are_kept_apart_as_the_transcript_records_them()
    {
        WriteClaude("session-a", ClaudeLine("msg_1", "2026-08-20", write5m: 100, write1h: 900));

        var totals = Total((await Claude().ScanAsync(Long)).Value!);

        totals.CacheWrite5m.Should().Be(100);
        totals.CacheWrite1h.Should().Be(900);
        totals.CacheWrite.Should().Be(1_000);
    }

    [Fact]
    public async Task An_older_record_without_the_lifetime_split_still_counts_its_writes()
    {
        // Before the split existed there was only the flat figure. Dropping it
        // would quietly lose every cache write in older history.
        WriteClaude(
            "session-a",
            """
            {"type":"assistant","timestamp":"2026-08-20T12:00:00.000Z","cwd":"/repos/alpha",
             "message":{"id":"msg_old","model":"claude-opus-5","usage":{
               "input_tokens":5,"cache_creation_input_tokens":700,
               "cache_read_input_tokens":0,"output_tokens":50}}}
            """.ReplaceLineEndings(string.Empty));

        var scan = await Claude().ScanAsync(Long);

        Total(scan.Value!).CacheWrite.Should().Be(700);
        scan.Value!.Integrity.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task A_usage_object_with_no_field_it_knows_is_reported_rather_than_counted_as_zero()
    {
        // This is the rename, simulated. The reader must not shrug: a total
        // that quietly dropped this record would look entirely believable.
        WriteClaude(
            "session-a",
            ClaudeLine("msg_1", "2026-08-20", output: 400),
            """
            {"type":"assistant","timestamp":"2026-08-20T12:00:00.000Z","cwd":"/repos/alpha",
             "message":{"id":"msg_2","model":"claude-opus-5",
                        "usage":{"outputTokens":999,"inputTokens":11}}}
            """.ReplaceLineEndings(string.Empty));

        var scan = await Claude().ScanAsync(Long);

        Total(scan.Value!).Output.Should().Be(400);

        scan.Value!.Integrity.RecordsUnrecognised.Should().Be(1);
        scan.Value.Integrity.IsComplete.Should().BeFalse();
        scan.Value.Integrity.Caveat.Should().Contain("transcript format");
    }

    [Fact]
    public async Task A_record_with_no_identifier_cannot_be_deduplicated_so_it_is_reported()
    {
        // Without an id there is no way to tell a repeat from a new message,
        // and counting it would inflate the total by however many times the
        // transcript happened to write it.
        WriteClaude(
            "session-a",
            """
            {"type":"assistant","timestamp":"2026-08-20T12:00:00.000Z","cwd":"/repos/alpha",
             "message":{"model":"claude-opus-5","usage":{"output_tokens":400}}}
            """.ReplaceLineEndings(string.Empty));

        var scan = await Claude().ScanAsync(Long);

        Total(scan.Value!).Output.Should().Be(0);
        scan.Value!.Integrity.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task A_half_written_line_is_skipped_without_spoiling_the_rest()
    {
        // An agent that is still running has a partial final line. That is
        // ordinary, and unlike a rename it says nothing about the format.
        WriteClaude(
            "session-a",
            ClaudeLine("msg_1", "2026-08-20", output: 400),
            "{\"message\":{\"usage\":{\"output_tok");

        var scan = await Claude().ScanAsync(Long);

        Total(scan.Value!).Output.Should().Be(400);
        scan.Value!.Integrity.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task Only_days_inside_the_window_are_counted()
    {
        WriteClaude(
            "session-a",
            ClaudeLine("msg_old", "2026-08-01", output: 999),
            ClaudeLine("msg_new", "2026-08-20", output: 400));

        var scan = await Claude().ScanAsync(new DateOnly(2026, 8, 10));

        Total(scan.Value!).Output.Should().Be(400);
    }

    [Fact]
    public async Task Nothing_recorded_is_an_empty_answer_rather_than_a_failure()
    {
        var scan = await Claude().ScanAsync(Long);

        scan.Succeeded.Should().BeTrue("a machine that has never run Claude is not an error");
        scan.Value!.Buckets.Should().BeEmpty();
    }

    // ----------------------------------------------------------------- Codex

    private void WriteCodex(string name, params string[] lines)
    {
        var directory = Path.Combine(_home, ".codex", "sessions", "2026", "08", "20");

        Directory.CreateDirectory(directory);

        File.WriteAllLines(Path.Combine(directory, name + ".jsonl"), lines);
    }

    private static string CodexTokens(string day, long input, long cached, long output) =>
        $$"""
        {"timestamp":"{{day}}T12:00:00.000Z","type":"event_msg","payload":{
          "type":"token_count","info":{"total_token_usage":{
            "input_tokens":{{input}},"cached_input_tokens":{{cached}},
            "cache_write_input_tokens":0,"output_tokens":{{output}},
            "reasoning_output_tokens":0,"total_tokens":{{input + output}} } } } }
        """.ReplaceLineEndings(string.Empty);

    private CodexUsageHistory Codex() => new(new FakeEnvironmentProvider(_home));

    [Fact]
    public async Task Codex_running_totals_are_taken_not_added_up()
    {
        // These are cumulative and emitted more than once per turn. Adding
        // them gave eighty-four times the truth on a real session here.
        WriteCodex(
            "rollout-a",
            """
            {"timestamp":"2026-08-20T11:59:00.000Z","type":"session_meta",
             "payload":{"cwd":"/repos/alpha","model":"gpt-5.6-sol"}}
            """.ReplaceLineEndings(string.Empty),
            CodexTokens("2026-08-20", 4_000, 0, 100),
            CodexTokens("2026-08-20", 4_000, 0, 100),
            CodexTokens("2026-08-20", 9_000, 4_000, 250),
            CodexTokens("2026-08-20", 15_000, 9_000, 400));

        var scan = await Codex().ScanAsync(Long);

        var totals = Total(scan.Value!);

        // The last running total, and nothing else: 15,000 in of which 9,000
        // was cached, so 6,000 was not.
        totals.Output.Should().Be(400);
        totals.CacheRead.Should().Be(9_000);
        totals.Input.Should().Be(6_000);

        // Adding them would give 32,000 in and 850 out.
        totals.TotalInput.Should().Be(15_000);
    }

    [Fact]
    public async Task Codex_cached_tokens_are_taken_out_of_its_input_figure()
    {
        // Verified against real sessions: input_tokens + output_tokens comes to
        // exactly total_tokens, so cached tokens sit inside the input rather
        // than beside it. Leaving them there would count them twice.
        WriteCodex("rollout-a", CodexTokens("2026-08-20", 10_000, 8_000, 500));

        var totals = Total((await Codex().ScanAsync(Long)).Value!);

        totals.Input.Should().Be(2_000);
        totals.CacheRead.Should().Be(8_000);
        totals.TotalInput.Should().Be(10_000, "the parts must still come to the whole");
    }

    [Fact]
    public async Task A_codex_event_with_nothing_in_it_is_ordinary_rather_than_a_fault()
    {
        // Real sessions carry these: a token_count whose info is null.
        WriteCodex(
            "rollout-a",
            """
            {"timestamp":"2026-08-20T12:00:00.000Z","type":"event_msg",
             "payload":{"type":"token_count","info":null}}
            """.ReplaceLineEndings(string.Empty),
            CodexTokens("2026-08-20", 5_000, 0, 200));

        var scan = await Codex().ScanAsync(Long);

        Total(scan.Value!).Output.Should().Be(200);
        scan.Value!.Integrity.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task Two_codex_sessions_add_together_even_though_one_session_does_not()
    {
        WriteCodex("rollout-a", CodexTokens("2026-08-20", 5_000, 0, 200));
        WriteCodex("rollout-b", CodexTokens("2026-08-20", 7_000, 0, 300));

        var totals = Total((await Codex().ScanAsync(Long)).Value!);

        totals.Output.Should().Be(500);
        totals.TotalInput.Should().Be(12_000);
    }

    private static UsageTotals Total(UsageScan scan) =>
        scan.Buckets.Aggregate(UsageTotals.Zero, (running, bucket) => running + bucket.Totals);
}
