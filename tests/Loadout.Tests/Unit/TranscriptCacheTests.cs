using FluentAssertions;
using Loadout.Core.Statusline;
using Xunit;
using Fixtures = Loadout.Tests.Unit.TranscriptTimesTests.Fixtures;

namespace Loadout.Tests.Unit;

/// <summary>
/// The status line reads an unchanged transcript once, and notices every way
/// one can change.
/// </summary>
public sealed class TranscriptCacheTests : IDisposable
{
    private static readonly DateTimeOffset T = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);

    // The turn in the Waiting fixture ends at 14:02:05; moved by a second, the
    // file keeps its length and reads differently.
    private const string Ended = "14:02:05";
    private const string EndedLater = "14:02:06";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-transcript-cache-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _state;
    private readonly string _transcript;
    private readonly DateTime _written = new(2026, 10, 22, 14, 3, 0, DateTimeKind.Utc);

    public TranscriptCacheTests()
    {
        _state = Path.Combine(_root, "state");
        _transcript = Path.Combine(_root, "session.jsonl");
        Directory.CreateDirectory(_root);
        File.WriteAllLines(_transcript, Fixtures.Waiting);
        File.SetLastWriteTimeUtc(_transcript, _written);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void An_unchanged_file_is_not_read_again()
    {
        Read()!.TurnEndedAt.Should().Be(T.AddMinutes(2).AddSeconds(5));

        Rewrite(_written);

        Read()!.TurnEndedAt.Should().Be(T.AddMinutes(2).AddSeconds(5), "the length and the time say nothing changed");
    }

    [Fact]
    public void A_longer_file_is_read_again()
    {
        Read()!.TurnEndedAt.Should().NotBeNull();

        File.AppendAllLines(_transcript, [Fixtures.Prompt(T.AddMinutes(10))]);
        File.SetLastWriteTimeUtc(_transcript, _written);

        var reading = Read()!;

        reading.TurnStartedAt.Should().Be(T.AddMinutes(10));
        reading.TurnEndedAt.Should().BeNull();
    }

    [Fact]
    public void A_touched_file_of_the_same_length_is_read_again()
    {
        Read();

        Rewrite(_written.AddSeconds(1));

        Read()!.TurnEndedAt.Should().Be(T.AddMinutes(2).AddSeconds(6));
    }

    [Fact]
    public void A_corrupt_cache_file_is_a_miss()
    {
        Read();

        var kept = Directory.GetFiles(TranscriptCache.Directory(_state), "*.json");

        kept.Should().ContainSingle();
        File.WriteAllText(kept[0], "{ not json");
        Rewrite(_written);

        var reading = () => Read();

        reading.Should().NotThrow();
        Read()!.TurnEndedAt.Should().Be(T.AddMinutes(2).AddSeconds(6));
    }

    [Fact]
    public void Outstanding_is_worked_out_at_read_time_not_cached()
    {
        File.WriteAllLines(_transcript, Fixtures.Monitor);
        File.SetLastWriteTimeUtc(_transcript, _written);

        Read()!.Outstanding(T.AddMinutes(5)).Should().BeTrue();
        Directory.GetFiles(TranscriptCache.Directory(_state), "*.json").Should().ContainSingle("the second read comes from the cache");

        var cached = Read()!;

        cached.Outstanding(T.AddMinutes(5)).Should().BeTrue();
        cached.Outstanding(T.AddMinutes(12)).Should().BeFalse("the Monitor's ten minutes are up");
    }

    private TranscriptReading? Read() => TranscriptCache.Read(_state, _transcript);

    /// <summary>Moves the turn's end by a second, which keeps the file's length, and sets its time.</summary>
    private void Rewrite(DateTime written)
    {
        var before = File.ReadAllText(_transcript);
        var after = before.Replace(Ended, EndedLater, StringComparison.Ordinal);

        after.Should().NotBe(before);
        after.Length.Should().Be(before.Length);
        File.WriteAllText(_transcript, after);
        File.SetLastWriteTimeUtc(_transcript, written);
    }
}
