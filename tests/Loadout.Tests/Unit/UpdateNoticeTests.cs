using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Core.Updates;
using Loadout.Models;
using Loadout.Models.Configuration;
using Loadout.Models.Platform;
using Loadout.Models.Results;
using Loadout.Models.Updates;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The launcher's corner-of-the-screen update notice: what it asks, how often,
/// and that it never becomes an error.
/// </summary>
/// <remarks>
/// The check behind it goes to the network, and the launcher opens many times a
/// day. Every test here is really about one of two things: that the network is
/// asked once a day and not once a start, and that nothing the network does can
/// reach the screen as anything but a version number or silence.
/// </remarks>
public sealed class UpdateNoticeTests : IDisposable
{
    private readonly string _root;
    private readonly LinuxPaths _paths;
    private readonly FixedTime _time = new();
    private readonly CountingUpdates _updates = new();
    private bool _checkAutomatically = true;

    public UpdateNoticeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-notice-" + Guid.NewGuid().ToString("N"));

        var environment = new FakeEnvironmentProvider(
            _root,
            new Dictionary<string, string>
            {
                ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
            });

        _paths = new LinuxPaths(
            environment,
            new NoOpFilePermissions(),
            new HostPlatform(
                HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST-MACHINE"));

        _paths.EnsureDirectoriesExist();
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

    private UpdateNotice Notice() =>
        new(new StubConfiguration(() => _checkAutomatically), _updates, _paths, _time, "0.32.0");

    [Fact]
    public async Task A_newer_version_is_named()
    {
        _updates.Offer("0.33.0");

        (await Notice().AvailableAsync()).Should().Be("0.33.0");
    }

    [Fact]
    public async Task The_same_version_is_nothing_to_mention()
    {
        _updates.Offer("0.32.0");

        (await Notice().AvailableAsync()).Should().BeNull();
    }

    [Fact]
    public async Task The_network_is_asked_once_a_day_not_once_a_start()
    {
        _updates.Offer("0.33.0");

        var notice = Notice();

        await notice.AvailableAsync();
        await notice.AvailableAsync();
        _time.Now += TimeSpan.FromHours(23);
        (await Notice().AvailableAsync()).Should().Be("0.33.0", "the cached answer is still fresh");

        _updates.Calls.Should().Be(1, "three starts inside a day are one question");

        _time.Now += TimeSpan.FromHours(2);
        await Notice().AvailableAsync();

        _updates.Calls.Should().Be(2, "a day later it is worth asking again");
    }

    [Fact]
    public async Task A_cache_written_by_another_build_is_not_believed()
    {
        // Upgrade day: the old launcher cached "0.33.0 is available", and the
        // new one is 0.33.0. Trusting that cache would have the new build
        // announce itself as an update for a day.
        _updates.Offer("0.33.0");
        await Notice().AvailableAsync();

        _updates.Current = "0.33.0";
        _updates.Offer("0.33.0");

        var upgraded = new UpdateNotice(
            new StubConfiguration(() => true), _updates, _paths, _time, "0.33.0");

        (await upgraded.AvailableAsync()).Should().BeNull();
        _updates.Calls.Should().Be(2, "a different running version asks afresh");
    }

    [Fact]
    public async Task Switching_checking_off_asks_nothing()
    {
        _checkAutomatically = false;
        _updates.Offer("0.33.0");

        (await Notice().AvailableAsync()).Should().BeNull();
        _updates.Calls.Should().Be(0, "off means the source is not contacted, not merely not mentioned");
    }

    [Fact]
    public async Task A_failed_check_is_silence_and_is_not_retried_until_tomorrow()
    {
        _updates.Fail("could not reach the release source");

        var notice = Notice();

        (await notice.AvailableAsync()).Should().BeNull();
        (await notice.AvailableAsync()).Should().BeNull();

        // A machine that cannot reach the source must not try on every start.
        // The price is an update mentioned a day late, which the cache costs
        // anyway.
        _updates.Calls.Should().Be(1);
    }

    [Fact]
    public async Task An_unreadable_cache_is_a_missing_cache()
    {
        _updates.Offer("0.33.0");

        var notice = Notice();

        Directory.CreateDirectory(Path.GetDirectoryName(notice.Path)!);
        File.WriteAllText(notice.Path, "{ this is not json");

        (await notice.AvailableAsync()).Should().Be("0.33.0");
        _updates.Calls.Should().Be(1);
    }

    /// <summary>A release source that answers however the test says, and counts.</summary>
    private sealed class CountingUpdates : IUpdateService
    {
        private OperationResult<UpdateCheck> _next =
            OperationResult<UpdateCheck>.Fail("not configured", ExitCode.GeneralFailure);

        public int Calls { get; private set; }

        /// <summary>
        /// What the service believes is running. The real one reads its own
        /// assembly, so it always agrees with the notice in the same build;
        /// a test standing in for an upgraded build has to move both.
        /// </summary>
        public string Current { get; set; } = "0.32.0";

        public void Offer(string version) =>
            _next = OperationResult<UpdateCheck>.Ok(new UpdateCheck(
                Current,
                version,
                IsNewer: UpdateService.IsNewer(version, Current),
                new ReleaseArtifact { Url = "https://example/x", Sha256 = "00" },
                null));

        public void Fail(string why) =>
            _next = OperationResult<UpdateCheck>.Fail(why, ExitCode.GeneralFailure);

        public Task<OperationResult<UpdateCheck>> CheckAsync(CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_next);
        }

        public Task<OperationResult<string>> ApplyAsync(UpdateCheck check, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Only the one setting the notice reads.</summary>
    private sealed class StubConfiguration(Func<bool> checkAutomatically) : IConfigurationService
    {
        public Task<OperationResult<LauncherConfig>> LoadConfigAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<LauncherConfig>.Ok(new LauncherConfig
            {
                Updates = new UpdateSettings { CheckAutomatically = checkAutomatically() },
            }));

        public Task<OperationResult> SaveConfigAsync(LauncherConfig config, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<MachineConfig>> LoadMachineAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult> SaveMachineAsync(MachineConfig machine, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<LauncherConfig>> UpdateConfigAsync(Action<LauncherConfig> change, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<MachineConfig>> UpdateMachineAsync(Action<MachineConfig> change, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>A clock the test moves, so a day can pass without waiting for one.</summary>
    private sealed class FixedTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
