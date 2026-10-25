using FluentAssertions;
using Loadout.Core.Transcripts;
using Loadout.Core.Usage;
using Loadout.Models.Platform;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Reading usage from what was kept of each transcript: the totals and the
/// integrity figures must be what a full scan says, however the transcripts
/// changed in between.
/// </summary>
/// <remarks>
/// Each test compares a scan made with readings kept against one made after
/// every reading was dropped, which reads every transcript again. The kept
/// readings are shared by the whole process, so these tests run one at a time.
/// </remarks>
[Collection(Name)]
public sealed class TranscriptReadingsTests : IDisposable
{
    internal const string Name = "Transcript readings";

    private static readonly DateOnly Everything = new(2000, 1, 1);

    private readonly string _root;
    private readonly FakeEnvironmentProvider _environment;
    private readonly IPlatformPaths _paths;

    public TranscriptReadingsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-readings-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_root);

        _environment = new FakeEnvironmentProvider(
            Path.Combine(_root, "home"),
            new Dictionary<string, string>
            {
                ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
            });

        _paths = new LinuxPaths(
            _environment,
            new NoOpFilePermissions(),
            new HostPlatform(HostOperatingSystem.Linux, System.Runtime.InteropServices.Architecture.X64, "test", "TEST"));

        ClaudeUsageHistory.Forget();
        CodexUsageHistory.Forget();
    }

    public void Dispose()
    {
        ClaudeUsageHistory.Forget();
        CodexUsageHistory.Forget();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing the run over.
        }
    }

    [Fact]
    public async Task A_transcript_appended_to_is_counted_as_a_full_scan_counts_it()
    {
        var first = Transcript("alpha", Line("m1", 100, 10), Line("m2", 200, 20));
        Transcript("beta", Line("m3", 300, 30), Line("m1", 100, 10));

        await ScanAsync();

        // A turn later: one new message, and one repeated from the other file,
        // which has to be counted as the repeat it is.
        File.AppendAllLines(first, [Line("m4", 400, 40), Line("m3", 300, 30)]);
        Touch(first, minutes: 1);

        await AgreesWithFullScanAsync();
    }

    [Fact]
    public async Task A_transcript_rewritten_shorter_is_counted_as_a_full_scan_counts_it()
    {
        var first = Transcript("alpha", Line("m1", 100, 10), Line("m2", 200, 20), Line("m3", 300, 30));
        Transcript("beta", Line("m4", 400, 40));

        await ScanAsync();

        File.WriteAllLines(first, [Line("m1", 150, 15), "{\"message\":{\"usage\":{\"input_tokens\":1}}}"]);
        Touch(first, minutes: 1);

        var scan = await AgreesWithFullScanAsync();

        scan.Integrity.RecordsUnrecognised.Should().Be(1, "the rewritten file has a line with usage and no message identifier");
    }

    [Fact]
    public async Task A_transcript_removed_is_no_longer_counted()
    {
        Transcript("alpha", Line("m1", 100, 10));
        var second = Transcript("beta", Line("m2", 200, 20));

        await ScanAsync();

        File.Delete(second);

        var scan = await AgreesWithFullScanAsync();

        scan.Integrity.FilesRead.Should().Be(1);
    }

    [Fact]
    public async Task Readings_are_kept_on_disk_for_the_next_process()
    {
        var first = Transcript("alpha", Line("m1", 100, 10));

        await ScanAsync();

        // The process ends, and what it kept in memory with it.
        ClaudeUsageHistory.Forget();

        // Changed without changing its length or when it was last written,
        // which no agent does: the only way a scan can still say 100 now is by
        // having taken the reading kept on disk rather than reading the file.
        var written = File.GetLastWriteTimeUtc(first);

        File.WriteAllLines(first, [Line("m1", 900, 10)]);
        File.SetLastWriteTimeUtc(first, written);

        var scan = await ScanAsync();

        scan.Buckets.Should().ContainSingle().Which.Totals.Input.Should().Be(100);
    }

    [Fact]
    public async Task A_window_scanned_after_a_wider_one_is_what_a_full_scan_of_it_says()
    {
        Transcript("alpha", Line("m1", 100, 10, "2026-01-01T09:00:00Z"), Line("m2", 200, 20, "2026-03-01T09:00:00Z"));

        await ScanAsync(Everything);

        var narrow = new DateOnly(2026, 2, 1);
        var kept = await ScanAsync(narrow);

        ClaudeUsageHistory.Forget();

        var full = await ScanAsync(narrow);

        Same(kept, full);
        kept.Buckets.Should().ContainSingle().Which.Totals.Input.Should().Be(200);
        kept.Integrity.RecordsCounted.Should().Be(2, "a message read before the window is still a message read");
    }

    private async Task<UsageScan> AgreesWithFullScanAsync()
    {
        var kept = await ScanAsync();

        // Nothing in memory and nothing on disk: every transcript read again.
        ClaudeUsageHistory.Forget();

        if (Directory.Exists(TranscriptReadingsDirectory.Under(_paths.Paths.State)))
        {
            Directory.Delete(TranscriptReadingsDirectory.Under(_paths.Paths.State), recursive: true);
        }

        var full = await ScanAsync();

        Same(kept, full);

        return kept;
    }

    private static void Same(UsageScan kept, UsageScan full)
    {
        kept.Buckets.Should().BeEquivalentTo(full.Buckets);
        kept.Integrity.Should().Be(full.Integrity);
    }

    private async Task<UsageScan> ScanAsync(DateOnly? since = null)
    {
        var scan = await new ClaudeUsageHistory(_environment, _paths).ScanAsync(since ?? Everything);

        scan.Succeeded.Should().BeTrue(scan.Error);

        return scan.Value!;
    }

    private string Transcript(string project, params string[] lines)
    {
        var directory = Path.Combine(_environment.HomeDirectory, ".claude", "projects", project);

        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, project + ".jsonl");

        File.WriteAllLines(path, lines);

        return path;
    }

    /// <summary>Moves a file's last write on, as the agent writing it would.</summary>
    private static void Touch(string path, int minutes) =>
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(minutes));

    private static string Line(string id, long input, long output, string timestamp = "2026-02-01T09:00:00Z") =>
        $"{{\"timestamp\":\"{timestamp}\",\"cwd\":\"/repos/widget\",\"message\":{{\"id\":\"{id}\","
        + $"\"model\":\"claude-opus-5\",\"usage\":{{\"input_tokens\":{input},\"output_tokens\":{output},"
        + "\"cache_read_input_tokens\":0,"
        + "\"cache_creation\":{\"ephemeral_5m_input_tokens\":0,\"ephemeral_1h_input_tokens\":0}}}}";
}

/// <summary>The tests that share the process's transcript readings, kept from running at once.</summary>
[CollectionDefinition(TranscriptReadingsTests.Name, DisableParallelization = true)]
public sealed class TranscriptReadingsCollection;
