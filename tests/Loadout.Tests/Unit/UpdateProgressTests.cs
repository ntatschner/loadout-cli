using System.IO.Compression;
using System.Security.Cryptography;
using FluentAssertions;
using Loadout.Core.Updates;
using Loadout.Models.Updates;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>What the updater says while it works, which the About window shows.</summary>
public sealed class UpdateProgressTests : IDisposable
{
    private readonly AboutHome _home = new();

    public void Dispose() => _home.Dispose();

    [Fact]
    public async Task Apply_from_a_local_feed_reports_each_stage_in_order()
    {
        var install = Directory.CreateDirectory(Path.Combine(_home.Root, "bin")).FullName;
        var executable = Path.Combine(install, "loadout-test.exe");
        await File.WriteAllTextAsync(executable, "OLD");

        var source = Directory.CreateDirectory(Path.Combine(_home.Root, "feed")).FullName;
        var archive = Path.Combine(source, "loadout.zip");

        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            await using var entry = zip.CreateEntry("loadout-test.exe").Open();
            await entry.WriteAsync("NEW"u8.ToArray());
        }

        var bytes = await File.ReadAllBytesAsync(archive);
        var check = new UpdateCheck(
            "0.1.0",
            "0.2.0",
            true,
            new ReleaseArtifact { Url = archive, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), Size = bytes.Length },
            null);

        var service = new UpdateService(
            new SourceConfiguration(null), _home.Paths, new NoOpFilePermissions(), new HttpClient(),
            currentExecutable: () => executable, currentVersion: "0.1.0");

        var seen = new Recorder();

        var applied = await service.ApplyAsync(check, seen);

        applied.Succeeded.Should().BeTrue(applied.Error);
        (await File.ReadAllTextAsync(executable)).Should().Be("NEW");
        seen.Stages.Distinct().Should().Equal(
            UpdateStage.Downloading,
            UpdateStage.Verifying,
            UpdateStage.Extracting,
            UpdateStage.Installing,
            UpdateStage.Done);
    }

    /// <summary>Keeps every report, on the reporting thread, in order.</summary>
    private sealed class Recorder : IProgress<UpdateProgress>
    {
        public List<UpdateStage> Stages { get; } = [];

        public void Report(UpdateProgress value) => Stages.Add(value.Stage);
    }
}
