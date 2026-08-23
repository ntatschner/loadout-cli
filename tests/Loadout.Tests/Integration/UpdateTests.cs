using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Loadout.Core.Configuration;
using Loadout.Core.Updates;
using Loadout.Models.Configuration;
using Loadout.Models.Platform;
using Loadout.Models.Updates;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Linux;
using Loadout.Platform.Windows;
using Loadout.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The update path, driven against a real feed and real archives on disk
/// (spec section 79).
/// <para>
/// This is the most dangerous thing the launcher does: it replaces the binary
/// the user runs next time. The tests therefore care less about the happy path
/// than about what happens when the download is wrong — a mismatched hash, an
/// unexpected size, a feed with no hash at all. Each of those must stop the
/// update with the working binary untouched.
/// </para>
/// </summary>
public sealed class UpdateTests : IDisposable
{
    private const string CurrentVersion = "0.1.0";

    private readonly string _root;
    private readonly IPlatformPaths _paths;
    private readonly ConfigurationService _configuration;
    private readonly string _installedExecutable;

    public UpdateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var environment = new FakeEnvironmentProvider(
            Path.Combine(_root, "home"),
            new Dictionary<string, string>
            {
                ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
            });

        var permissions = new NoOpFilePermissions();

        _paths = new LinuxPaths(
            environment,
            permissions,
            new HostPlatform(
                HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST"));

        _paths.EnsureDirectoriesExist();

        _configuration = new ConfigurationService(_paths, environment, new YamlStore(permissions));

        // Stands in for the installed binary. Its contents are what the tests
        // check to see whether a replacement actually happened.
        var installDirectory = Path.Combine(_root, "install");
        Directory.CreateDirectory(installDirectory);

        _installedExecutable = Path.Combine(installDirectory, "loadout");
        File.WriteAllText(_installedExecutable, "ORIGINAL BINARY");
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

    [Fact]
    public async Task An_unconfigured_source_says_so_rather_than_failing_obscurely()
    {
        await _configuration.SaveConfigAsync(new LauncherConfig());

        var result = await Service().CheckAsync();

        result.Failed.Should().BeTrue();
        result.Error.Should().Contain("loadout config set updates-source");
    }

    [Fact]
    public async Task A_newer_release_is_reported_as_available()
    {
        await PublishAsync("0.2.0", "NEW BINARY");

        var result = await Service().CheckAsync();

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.AvailableVersion.Should().Be("0.2.0");
        result.Value.IsNewer.Should().BeTrue();
    }

    [Fact]
    public async Task The_same_version_is_not_an_update()
    {
        await PublishAsync(CurrentVersion, "SAME BINARY");

        var result = await Service().CheckAsync();

        result.Value!.IsNewer.Should().BeFalse();
    }

    [Fact]
    public async Task An_older_release_is_never_offered()
    {
        await PublishAsync("0.0.9", "OLD BINARY");

        // A source that has rolled back must not walk the user backwards
        // without them asking for it.
        (await Service().CheckAsync()).Value!.IsNewer.Should().BeFalse();
    }

    [Fact]
    public async Task A_feed_with_no_build_for_this_platform_is_not_an_error()
    {
        await PublishAsync("0.2.0", "NEW BINARY", runtimeIdentifier: "some-other-rid");

        var result = await Service().CheckAsync();

        result.Succeeded.Should().BeTrue();
        result.Value!.AvailableVersion.Should().BeNull();
        result.Value.IsNewer.Should().BeFalse();
    }

    [Fact]
    public async Task Applying_an_update_replaces_the_binary_and_keeps_the_old_one()
    {
        await PublishAsync("0.2.0", "NEW BINARY");

        var check = (await Service().CheckAsync()).Value!;
        var result = await Service().ApplyAsync(check);

        result.Succeeded.Should().BeTrue(result.Error);

        (await File.ReadAllTextAsync(_installedExecutable)).Should().Be("NEW BINARY");

        // Keeping the previous binary is what makes a bad update recoverable by
        // hand rather than a reinstall.
        File.Exists(result.Value!).Should().BeTrue();
        (await File.ReadAllTextAsync(result.Value!)).Should().Be("ORIGINAL BINARY");
    }

    [Fact]
    public async Task A_download_whose_hash_does_not_match_is_refused()
    {
        await PublishAsync("0.2.0", "NEW BINARY", corruptAfterHashing: true);

        var check = (await Service().CheckAsync()).Value!;
        var result = await Service().ApplyAsync(check);

        result.Failed.Should().BeTrue();
        result.Error.Should().Contain("does not match the published SHA-256");

        // The whole point: the working binary is still there and still works.
        (await File.ReadAllTextAsync(_installedExecutable)).Should().Be("ORIGINAL BINARY");
    }

    [Fact]
    public async Task A_feed_that_publishes_no_hash_is_refused()
    {
        await PublishAsync("0.2.0", "NEW BINARY", omitHash: true);

        var check = (await Service().CheckAsync()).Value!;
        var result = await Service().ApplyAsync(check);

        // Without a hash the source can hand over anything at all, and this
        // download becomes the binary the user runs next.
        result.Failed.Should().BeTrue();
        result.ExitCode.Should().Be(Models.ExitCode.PolicyViolation);
        (await File.ReadAllTextAsync(_installedExecutable)).Should().Be("ORIGINAL BINARY");
    }

    [Fact]
    public async Task A_download_of_unexpected_size_is_refused()
    {
        await PublishAsync("0.2.0", "NEW BINARY", declaredSize: 999999);

        var result = await Service().ApplyAsync((await Service().CheckAsync()).Value!);

        result.Failed.Should().BeTrue();
        result.Error.Should().Contain("bytes but the feed said");
        (await File.ReadAllTextAsync(_installedExecutable)).Should().Be("ORIGINAL BINARY");
    }

    [Fact]
    public async Task An_archive_without_the_executable_is_refused()
    {
        await PublishAsync("0.2.0", "NEW BINARY", executableName: "something-else");

        var result = await Service().ApplyAsync((await Service().CheckAsync()).Value!);

        result.Failed.Should().BeTrue();
        result.Error.Should().Contain("does not contain");
        (await File.ReadAllTextAsync(_installedExecutable)).Should().Be("ORIGINAL BINARY");
    }

    [Fact]
    public async Task A_malformed_feed_is_reported_clearly()
    {
        var feedPath = Path.Combine(_root, "feed.json");
        await File.WriteAllTextAsync(feedPath, "{ this is not json");

        await _configuration.SaveConfigAsync(new LauncherConfig
        {
            Updates = new UpdateSettings { Source = feedPath },
        });

        var result = await Service().CheckAsync();

        result.Failed.Should().BeTrue();
        result.Error.Should().Contain("not valid JSON");
    }

    [Theory]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("1.0.0", "0.9.9", true)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.0.9", "0.1.0", false)]
    [InlineData("not-a-version", "0.1.0", false)]
    [InlineData("", "0.1.0", false)]
    public void Version_comparison_never_treats_nonsense_as_newer(
        string candidate, string current, bool expected)
    {
        // A malformed version must not talk the launcher into replacing itself.
        UpdateService.IsNewer(candidate, current).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://example.invalid/feed.json", false)]
    [InlineData("http://example.invalid/feed.json", false)]
    [InlineData("/home/test/feed.json", true)]
    [InlineData("relative/feed.json", true)]
    public void Local_and_remote_sources_are_told_apart(string source, bool expectedLocal) =>
        UpdateService.IsLocal(source, out _).Should().Be(expectedLocal);

    [Fact]
    public void A_windows_path_is_treated_as_local_despite_parsing_as_a_uri()
    {
        // D:\feed.json parses as an absolute URI whose scheme is the drive
        // letter, which would otherwise send the launcher looking for a server.
        UpdateService.IsLocal(@"D:\releases\feed.json", out var path).Should().BeTrue();
        path.Should().Contain("feed.json");
    }

    private UpdateService Service() => new(
        _configuration,
        _paths,
        OperatingSystem.IsWindows() ? new WindowsFilePermissions() : new NoOpFilePermissions(),
        new HttpClient(),
        () => _installedExecutable,
        CurrentVersion);

    /// <summary>
    /// Writes a release archive and a feed pointing at it, mimicking a
    /// self-hosted source that is just files on disk (spec section 79).
    /// </summary>
    private async Task PublishAsync(
        string version,
        string binaryContent,
        string? runtimeIdentifier = null,
        string executableName = "loadout",
        bool corruptAfterHashing = false,
        bool omitHash = false,
        long? declaredSize = null)
    {
        var releases = Path.Combine(_root, "releases");
        Directory.CreateDirectory(releases);

        var archivePath = Path.Combine(releases, $"loadout-{version}.zip");

        // A zip so the test does not depend on a tar binary being present, and
        // because the service picks its extractor from the file signature.
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(executableName);

            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync(binaryContent);
        }

        string hash;
        await using (var stream = File.OpenRead(archivePath))
        {
            hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
        }

        var size = new FileInfo(archivePath).Length;

        if (corruptAfterHashing)
        {
            // The feed's hash is now stale, which is exactly what a tampered or
            // truncated download looks like.
            await File.AppendAllTextAsync(archivePath, "tampered");
        }

        var feed = new ReleaseFeed
        {
            Version = version,
            Released = DateTimeOffset.UtcNow,
            Artifacts =
            {
                [runtimeIdentifier ?? _paths.Host.RuntimeIdentifier] = new ReleaseArtifact
                {
                    Url = archivePath,
                    Sha256 = omitHash ? string.Empty : hash,
                    Size = declaredSize ?? (corruptAfterHashing ? null : size),
                },
            },
        };

        var feedPath = Path.Combine(_root, "feed.json");
        await File.WriteAllTextAsync(feedPath, JsonSerializer.Serialize(feed));

        await _configuration.SaveConfigAsync(new LauncherConfig
        {
            Updates = new UpdateSettings { Source = feedPath },
        });
    }
}
