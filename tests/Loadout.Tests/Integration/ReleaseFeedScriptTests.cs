using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Loadout.Models.Updates;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Skips when PowerShell is not on PATH, which is the only reason this cannot
/// run. It is present on every GitHub runner and on the machines this is
/// developed on.
/// </summary>
public sealed class PwshFactAttribute : FactAttribute
{
    public PwshFactAttribute()
    {
        if (!ReleaseFeedScriptTests.HasPowerShell)
        {
            Skip = "PowerShell is not installed, so build/feed.ps1 cannot be run.";
        }
    }
}

/// <summary>
/// What the release publishes, read by the thing that consumes it.
/// </summary>
/// <remarks>
/// <para>
/// The two halves of updating were written years apart and never met.
/// <c>updates-source</c> wanted a JSON feed; the release published archives, a
/// manifest and no feed at all. So <c>loadout update</c> could not work on any
/// machine, however it was configured, and nothing said so — the setting was
/// there, the documentation described the format, and the document did not
/// exist.
/// </para>
/// <para>
/// This runs the script the workflow runs and parses what it wrote with the
/// model the updater parses it with. A test that checked the JSON by hand would
/// have passed just as happily while the two drifted apart again.
/// </para>
/// </remarks>
public sealed class ReleaseFeedScriptTests : IDisposable
{
    internal static bool HasPowerShell { get; } = Resolve("pwsh") is not null;

    private const string Version = "9.9.9";

    private readonly string _root;

    public ReleaseFeedScriptTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-feed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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
        catch (IOException)
        {
            // A leftover temp tree is not worth failing the run over.
        }
    }

    [PwshFact]
    public async Task The_feed_it_writes_is_the_feed_the_updater_reads()
    {
        Release("win-x64", "zip");
        Release("linux-x64", "tar.gz");
        Release("osx-arm64", "tar.gz");
        WriteManifest();

        var run = await RunAsync();

        run.ExitCode.Should().Be(0, run.Output);

        var feed = Read();

        feed.Should().NotBeNull("the updater deserialises this exact document");
        feed!.SchemaVersion.Should().Be(1);
        feed.Version.Should().Be(Version);

        // Keyed by runtime identifier, because that is what the updater looks
        // itself up by.
        feed.Artifacts.Keys.Should().BeEquivalentTo("win-x64", "linux-x64", "osx-arm64");

        var windows = feed.Artifacts["win-x64"];

        windows.Url.Should().Be(
            $"https://example.invalid/download/v{Version}/loadout-{Version}-win-x64.zip");

        // The hash the manifest states, which is the hash of the file that will
        // be published. The updater refuses an artifact without one, so a feed
        // that got this wrong would fail at the last step of an update rather
        // than at the first.
        windows.Sha256.Should().Be(HashOf("win-x64", "zip"));
        windows.Size.Should().Be(Bytes("win-x64", "zip").Length);
    }

    [PwshFact]
    public async Task Archives_are_offered_and_installers_are_not()
    {
        Release("win-x64", "zip");
        Release("win-x64", "msi");
        WriteManifest();

        (await RunAsync()).ExitCode.Should().Be(0);

        // The updater unpacks the download over the running binary. An .msi,
        // .deb or .rpm installs through the platform's own machinery and is not
        // something to extract, so offering one would be offering a download
        // that cannot be applied.
        Read()!.Artifacts["win-x64"].Url.Should().EndWith(".zip");
    }

    [PwshFact]
    public async Task A_platform_that_did_not_build_is_left_out_rather_than_guessed_at()
    {
        Release("linux-x64", "tar.gz");
        WriteManifest();

        (await RunAsync()).ExitCode.Should().Be(0);

        var feed = Read()!;

        // Not an error, at either end: the updater treats a feed with no build
        // for the machine it is on as an ordinary state, because a release may
        // simply not cover every architecture yet.
        feed.Artifacts.Should().ContainKey("linux-x64");
        feed.Artifacts.Should().NotContainKey("win-x64");
    }

    [PwshFact]
    public async Task An_archive_the_manifest_does_not_cover_stops_the_release()
    {
        Release("win-x64", "zip");
        Release("linux-x64", "tar.gz");

        // The manifest covers one of the two.
        WriteManifest("win-x64");

        var run = await RunAsync();

        // Refused rather than published with the platform quietly missing, or
        // worse, present without a hash. An artifact with no hash is one the
        // updater will download and then refuse to install, which is a failure
        // at the far end of somebody's slow connection.
        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("SHA256SUMS");

        File.Exists(Path.Combine(_root, "feed.json")).Should().BeFalse(
            "a feed that cannot be stated in full is not written at all");
    }

    [PwshFact]
    public async Task A_directory_with_no_manifest_is_refused()
    {
        Release("win-x64", "zip");

        var run = await RunAsync();

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("SHA256SUMS");

        // And no feed. Hashing the files here instead would be the obvious
        // convenience and the wrong one: the manifest is what the release
        // publishes and what somebody verifying a download checks against, so
        // a feed built from anything else can disagree with it.
        File.Exists(Path.Combine(_root, "feed.json")).Should().BeFalse(
            "the feed states the manifest's hashes, so without a manifest there is nothing to state");
    }

    /// <summary>Writes an artifact whose bytes are its own name, so hashes differ.</summary>
    private void Release(string rid, string extension) =>
        File.WriteAllBytes(Path.Combine(_root, Name(rid, extension)), Bytes(rid, extension));

    private static byte[] Bytes(string rid, string extension) =>
        System.Text.Encoding.UTF8.GetBytes($"pretend {rid} {extension} archive");

    private static string Name(string rid, string extension) =>
        $"loadout-{Version}-{rid}.{extension}";

    private static string HashOf(string rid, string extension) =>
        Convert.ToHexStringLower(SHA256.HashData(Bytes(rid, extension)));

    /// <summary>
    /// In sha256sum's own format, including the binary-mode star, because that
    /// is what the packaging scripts write and what the release publishes.
    /// </summary>
    private void WriteManifest(params string[] onlyThese)
    {
        var lines = Directory
            .EnumerateFiles(_root)
            .Select(Path.GetFileName)
            .Where(name => name is not null && name.StartsWith($"loadout-{Version}-", StringComparison.Ordinal))
            .Select(name => name!)
            .Where(name => onlyThese.Length == 0 || onlyThese.Any(rid => name.Contains(rid, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .Select(name =>
            {
                var hash = Convert.ToHexStringLower(
                    SHA256.HashData(File.ReadAllBytes(Path.Combine(_root, name))));

                return $"{hash} *{name}";
            });

        File.WriteAllLines(Path.Combine(_root, "SHA256SUMS"), lines);
    }

    private ReleaseFeed? Read() =>
        JsonSerializer.Deserialize<ReleaseFeed>(
            File.ReadAllText(Path.Combine(_root, "feed.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    private async Task<(int ExitCode, string Output)> RunAsync()
    {
        var script = Path.Combine(RepositoryRoot(), "build", "feed.ps1");

        File.Exists(script).Should().BeTrue($"the workflow runs {script}");

        var start = new ProcessStartInfo(Resolve("pwsh")!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in new[]
        {
            "-NoProfile", "-NonInteractive", "-File", script,
            "-Version", Version,
            "-Directory", _root,
            "-BaseUrl", $"https://example.invalid/download/v{Version}",
        })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return (process.ExitCode, output + error);
    }

    /// <summary>Walks up from the test binary until the repository is found.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "build")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }

    private static string? Resolve(string executable)
    {
        var extensions = OperatingSystem.IsWindows() ? [".exe", ".cmd", ""] : new[] { string.Empty };

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory.Trim(), executable + extension);

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
