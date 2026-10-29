using FluentAssertions;
using Loadout.Core.Transcripts;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Two scans asking for the same transcript at once: Studio asks for the
/// history and for the lost sessions together, and both used to read every
/// changed transcript in full.
/// </summary>
public sealed class TranscriptReadingsSharingTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "loadout-sharing-" + Guid.NewGuid().ToString("N") + ".jsonl");

    public TranscriptReadingsSharingTests() => File.WriteAllText(_file, "{}\n");

    public void Dispose()
    {
        try
        {
            File.Delete(_file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp file is not worth failing the run over.
        }
    }

    [Fact]
    public async Task Two_scans_asking_at_once_read_the_transcript_once()
    {
        var readings = new TranscriptReadings<string>("sharing", version: 1);
        var gate = new TaskCompletionSource();
        var reads = 0;

        async Task<string?> ReadAsync(FileInfo file, CancellationToken ct)
        {
            Interlocked.Increment(ref reads);
            await gate.Task.WaitAsync(ct);

            return "read";
        }

        var first = readings.GetAsync(new FileInfo(_file), kept: null, ReadAsync, CancellationToken.None);
        var second = readings.GetAsync(new FileInfo(_file), kept: null, ReadAsync, CancellationToken.None);

        gate.SetResult();

        (await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10))).Should().Equal("read", "read");
        reads.Should().Be(1);
    }

    [Fact]
    public async Task One_scan_giving_up_leaves_the_other_its_reading()
    {
        var readings = new TranscriptReadings<string>("sharing", version: 1);
        var gate = new TaskCompletionSource();
        using var stop = new CancellationTokenSource();

        async Task<string?> ReadAsync(FileInfo file, CancellationToken ct)
        {
            await gate.Task.WaitAsync(ct);

            return "read";
        }

        var abandoned = readings.GetAsync(new FileInfo(_file), kept: null, ReadAsync, stop.Token);
        var waiting = readings.GetAsync(new FileInfo(_file), kept: null, ReadAsync, CancellationToken.None);

        await stop.CancelAsync();

        await abandoned.Invoking(task => task.WaitAsync(TimeSpan.FromSeconds(10)))
            .Should().ThrowAsync<OperationCanceledException>();

        gate.SetResult();

        (await waiting.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be("read");
    }

    [Fact]
    public async Task A_transcript_that_changed_since_is_read_again()
    {
        var readings = new TranscriptReadings<string>("sharing", version: 1);
        var reads = 0;

        Task<string?> ReadAsync(FileInfo file, CancellationToken ct) =>
            Task.FromResult<string?>("read " + Interlocked.Increment(ref reads));

        (await readings.GetAsync(new FileInfo(_file), kept: null, ReadAsync, CancellationToken.None)).Should().Be("read 1");

        File.AppendAllText(_file, "{}\n");

        (await readings.GetAsync(new FileInfo(_file), kept: null, ReadAsync, CancellationToken.None)).Should().Be("read 2");
    }
}
