using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Sessions.Hosting;
using Loadout.Core.Statusline;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Reading the times the status line shows from the end of a transcript.
/// </summary>
/// <remarks>
/// The entries follow the shape of real Claude Code transcripts with the
/// conversation taken out: one entry per content block, each carrying its
/// message's <c>stop_reason</c>, and the <c>system</c> entries an interactive
/// session writes after a turn ends.
/// </remarks>
public sealed class TranscriptTimesTests : IDisposable
{
    private static readonly DateTimeOffset T = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "loadout-transcript-" + Guid.NewGuid().ToString("N")[..8]);

    public TranscriptTimesTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Waiting_for_a_prompt()
    {
        var reading = Read(Fixtures.Waiting);

        reading.Should().NotBeNull();
        reading!.TurnStartedAt.Should().Be(T);
        reading.TurnEndedAt.Should().Be(T.AddMinutes(2).AddSeconds(5));
        reading.PendingToolSince.Should().BeNull();
        reading.Outstanding(T.AddMinutes(3)).Should().BeFalse();
    }

    [Fact]
    public void A_pending_tool_call()
    {
        var reading = Read(Fixtures.Pending)!;

        reading.PendingToolSince.Should().Be(T.AddMinutes(1));
        reading.TurnEndedAt.Should().BeNull();
    }

    [Fact]
    public void A_tool_call_with_its_result_is_not_pending()
    {
        var reading = Read([.. Fixtures.Pending, Fixtures.Result("toolu_b1", T.AddMinutes(2))])!;

        reading.PendingToolSince.Should().BeNull();
        reading.TurnEndedAt.Should().BeNull();
    }

    [Fact]
    public void A_wakeup_still_to_come()
    {
        var reading = Read(Fixtures.Wakeup)!;

        reading.WakeupDue.Should().Be(T.AddMinutes(30));
        reading.TurnEndedAt.Should().Be(T.AddMinutes(1).AddSeconds(5));
    }

    [Fact]
    public void A_wakeup_that_has_fired_is_gone()
    {
        var reading = Read([.. Fixtures.Wakeup, Fixtures.Prompt(T.AddMinutes(31))])!;

        reading.WakeupDue.Should().BeNull();
    }

    [Fact]
    public void Background_with_no_wakeup()
    {
        var reading = Read(Fixtures.Background)!;

        reading.Outstanding(T.AddMinutes(3)).Should().BeTrue();
        reading.WakeupDue.Should().BeNull();
        reading.TurnEndedAt.Should().Be(T.AddMinutes(2));
    }

    [Fact]
    public void Background_silent_past_the_limit_is_not_outstanding()
    {
        var reading = Read(Fixtures.Background, SessionSignals.BackgroundLimit)!;

        reading.Outstanding(T.AddMinutes(1 + 29)).Should().BeTrue();
        reading.Outstanding(T.AddMinutes(1 + 31)).Should().BeFalse();
    }

    [Fact]
    public void The_last_write_is_the_last_successful_edit_call()
    {
        var reading = Read(Fixtures.Edits)!;

        reading.LastWriteAt.Should().Be(T);
    }

    [Fact]
    public void A_stale_signal_is_ignored()
    {
        TranscriptReading.SignalCounts(Signal(T.AddSeconds(-1)), T).Should().BeFalse();
    }

    [Fact]
    public void A_signal_newer_than_the_last_entry_counts()
    {
        TranscriptReading.SignalCounts(Signal(T), T).Should().BeTrue();
        TranscriptReading.SignalCounts(Signal(T.AddSeconds(1)), T).Should().BeTrue();
    }

    [Fact]
    public void One_tail_read_feeds_both_readings()
    {
        var nows = new[] { T, T.AddMinutes(3), T.AddMinutes(12), T.AddMinutes(33), T.AddHours(2) };

        foreach (var (name, lines) in Fixtures.All)
        {
            foreach (var silentAfter in new TimeSpan?[] { null, SessionSignals.BackgroundLimit })
            {
                var path = Write(lines);
                var reading = TranscriptTimes.Read(path, silentAfter)!;

                foreach (var now in nows)
                {
                    var background = BackgroundWork.Read(path, now, silentAfter)!;

                    reading.Outstanding(now).Should().Be(background.Outstanding, $"{name} at {now:t}, silent after {silentAfter}");
                    reading.WakeupDue.Should().Be(background.WakeupDue, name);
                }
            }
        }
    }

    private static SessionSignal Signal(DateTimeOffset at) => new("approval", null, at);

    private TranscriptReading? Read(IEnumerable<string> lines, TimeSpan? silentAfter = null) => TranscriptTimes.Read(Write(lines), silentAfter);

    private string Write(IEnumerable<string> lines)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N")[..8] + ".jsonl");

        File.WriteAllLines(path, lines);

        return path;
    }

    /// <summary>Transcripts in the shape Claude Code writes, with only ids, times and shapes kept.</summary>
    internal static class Fixtures
    {
        /// <summary>A Bash call and its result, then a reply that ends the turn, then what an interactive session writes after.</summary>
        public static List<string> Waiting =>
        [
            Prompt(T),
            Call("toolu_a1", "Bash", T.AddMinutes(1)),
            Result("toolu_a1", T.AddMinutes(1).AddSeconds(3)),
            Said("thinking", "end_turn", T.AddMinutes(2)),
            Said("text", "end_turn", T.AddMinutes(2).AddSeconds(5)),
            System("stop_hook_summary", T.AddMinutes(2).AddSeconds(6)),
            System("turn_duration", T.AddMinutes(2).AddSeconds(6)),
        ];

        public static List<string> Pending =>
        [
            Prompt(T),
            Said("thinking", "tool_use", T.AddSeconds(50)),
            Call("toolu_b1", "Bash", T.AddMinutes(1)),
        ];

        public static List<string> Wakeup =>
        [
            Prompt(T),
            Call("toolu_c1", "ScheduleWakeup", T.AddMinutes(1)),
            Result("toolu_c1", T.AddMinutes(1), new { scheduledFor = T.AddMinutes(30).ToUnixTimeMilliseconds() }),
            Said("text", "end_turn", T.AddMinutes(1).AddSeconds(5)),
        ];

        public static List<string> Background =>
        [
            Prompt(T),
            Call("toolu_d1", "Bash", T.AddMinutes(1)),
            Result("toolu_d1", T.AddMinutes(1), new { backgroundTaskId = "bd1shell", stdout = "" }),
            Said("text", "end_turn", T.AddMinutes(2)),
            System("turn_duration", T.AddMinutes(2).AddSeconds(1)),
        ];

        /// <summary>A Monitor that ends on its own ten minutes after it starts.</summary>
        public static List<string> Monitor =>
        [
            Prompt(T),
            Call("toolu_e1", "Monitor", T.AddMinutes(1)),
            Result("toolu_e1", T.AddMinutes(1), new { taskId = "me1watch", timeoutMs = 600_000 }),
            Said("text", "end_turn", T.AddMinutes(2)),
        ];

        public static List<string> Edits =>
        [
            Prompt(T.AddMinutes(-1)),
            Call("toolu_f1", "Write", T),
            Result("toolu_f1", T.AddSeconds(1)),
            Call("toolu_f2", "Edit", T.AddMinutes(5)),
            Result("toolu_f2", T.AddMinutes(5).AddSeconds(1), error: true),
            Call("toolu_f3", "Read", T.AddMinutes(6)),
            Result("toolu_f3", T.AddMinutes(6).AddSeconds(1)),
            Said("text", "end_turn", T.AddMinutes(7)),
        ];

        public static IEnumerable<(string Name, List<string> Lines)> All =>
        [
            (nameof(Waiting), Waiting),
            (nameof(Pending), Pending),
            (nameof(Wakeup), Wakeup),
            ("Fired", [.. Wakeup, Prompt(T.AddMinutes(31))]),
            (nameof(Background), Background),
            (nameof(Monitor), Monitor),
            (nameof(Edits), Edits),
        ];

        public static string Prompt(DateTimeOffset at) => Line(new
        {
            parentUuid = (string?)null,
            isSidechain = false,
            type = "user",
            message = new { role = "user", content = "" },
            uuid = "u-prompt",
            timestamp = at,
            origin = new { kind = "human" },
        });

        public static string Said(string block, string stop, DateTimeOffset at) => Line(new
        {
            isSidechain = false,
            type = "assistant",
            message = new
            {
                model = "claude",
                id = "msg_1",
                type = "message",
                role = "assistant",
                content = new object[] { block == "thinking" ? new { type = "thinking", thinking = "", signature = "" } : new { type = "text", text = "" } },
                stop_reason = stop,
                stop_sequence = (string?)null,
            },
            uuid = "u-said",
            timestamp = at,
        });

        public static string Call(string id, string tool, DateTimeOffset at) => Line(new
        {
            isSidechain = false,
            type = "assistant",
            message = new
            {
                model = "claude",
                id = "msg_2",
                type = "message",
                role = "assistant",
                content = new object[] { new { type = "tool_use", id, name = tool, input = new { } } },
                stop_reason = "tool_use",
                stop_sequence = (string?)null,
            },
            uuid = "u-call",
            timestamp = at,
        });

        public static string Result(string id, DateTimeOffset at, object? result = null, bool error = false) => Line(new
        {
            isSidechain = false,
            type = "user",
            message = new { role = "user", content = new object[] { new { tool_use_id = id, type = "tool_result", content = "", is_error = error } } },
            uuid = "u-result",
            timestamp = at,
            toolUseResult = result ?? new { stdout = "", stderr = "" },
        });

        public static string System(string subtype, DateTimeOffset at) => Line(new
        {
            isSidechain = false,
            type = "system",
            subtype,
            durationMs = 120_000,
            isMeta = false,
            uuid = "u-system",
            timestamp = at,
        });

        private static string Line(object entry) => JsonSerializer.Serialize(entry);
    }
}
