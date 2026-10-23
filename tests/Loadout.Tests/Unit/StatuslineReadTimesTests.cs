using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Statusline;
using Loadout.Models.Configuration;
using Loadout.Tests.Contract;
using Xunit;
using Fixtures = Loadout.Tests.Unit.TranscriptTimesTests.Fixtures;

namespace Loadout.Tests.Unit;

/// <summary>
/// The time readings the render command gathers, and the text the contract
/// tests expect the line to give them.
/// </summary>
public sealed class StatuslineReadTimesTests : IDisposable
{
    private static readonly DateTimeOffset T = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "loadout-readtimes-" + Guid.NewGuid().ToString("N")[..8]);

    public StatuslineReadTimesTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void With_every_time_segment_off_the_transcript_is_not_read()
    {
        var state = Path.Combine(_directory, "state");
        var options = new StatuslineSettings
        {
            ShowWaiting = false,
            ShowLastWrite = false,
            ShowClock = false,
            ShowSession = false,
            ShowTurn = false,
            ShowLastCommit = false,
        };

        var times = StatuslineRenderCommand.ReadTimes(options, Payload(Fixtures.Waiting), state, null, T.AddMinutes(3), null);

        times.TurnEndedAt.Should().BeNull("nothing on the line needs the transcript");
        Directory.Exists(TranscriptCache.Directory(state)).Should().BeFalse("a read leaves its reading in the cache");
    }

    [Fact]
    public void With_waiting_on_the_transcript_is_read()
    {
        var state = Path.Combine(_directory, "state");
        var options = new StatuslineSettings { ShowLastWrite = false };

        var times = StatuslineRenderCommand.ReadTimes(options, Payload(Fixtures.Waiting), state, null, T.AddMinutes(3), null);

        times.TurnEndedAt.Should().Be(T.AddMinutes(2).AddSeconds(5));
        Directory.Exists(TranscriptCache.Directory(state)).Should().BeTrue();
    }

    [Fact]
    public void The_contract_tests_expect_what_the_renderer_draws_just_after_midnight()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("plus-one", TimeSpan.FromHours(1), "plus-one", "plus-one");

        // 00:03 on the 22nd where the line is drawn, and a turn that ended
        // at 23:58 the evening before.
        var now = new DateTimeOffset(2026, 10, 22, 0, 3, 0, TimeSpan.FromHours(1));
        var ended = new DateTimeOffset(2026, 10, 21, 23, 58, 0, TimeSpan.FromHours(1));

        var line = StatuslineRenderer.Render(
            new StatuslineInputs(
                new StatuslinePayload(), null, null, null,
                Now: now,
                Zone: zone,
                Times: new StatuslineTimes(TurnStartedAt: ended.AddMinutes(-1), TurnEndedAt: ended)),
            new StatuslineSettings { Colour = false, ShowLastWrite = false });

        line.Should().Contain("waiting for you since " + StatuslineTimesContractTests.Expected(ended, now, zone));
        line.Should().NotContain(
            "waiting for you since " + TimeFormat.Clock(ended, zone, hour24: true),
            "a time from yesterday carries its day");
    }

    private StatuslinePayload Payload(IEnumerable<string> lines)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N")[..8] + ".jsonl");

        File.WriteAllLines(path, lines);

        return new StatuslinePayload { TranscriptPath = path };
    }
}
