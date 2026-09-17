using Loadout.Core.Sessions;
using Loadout.Models.Platform;
using Loadout.Platform.Common;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Collecting the runtime directories of sessions that never ended.
/// <para>
/// The launcher deletes a launch's runtime directory when the agent exits,
/// which covers every session that ends and none of the ones that do not.
/// Twenty-seven had accumulated over three weeks on one machine, each still
/// holding the instructions and memory index that session was given.
/// </para>
/// <para>
/// Everything here is about the two guards, because the failure this must not
/// have is deleting the context a session is reading. A stale directory left
/// alone is the situation as it already was; a live session losing its
/// instructions mid-work is a new and much worse one.
/// </para>
/// </summary>
public sealed class RuntimeReaperTests : IDisposable
{
    private readonly string _root;
    private readonly string _runtime;
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));

    public RuntimeReaperTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-reaper-" + Guid.NewGuid().ToString("N"));

        var paths = Paths();
        paths.EnsureDirectoriesExist();

        _runtime = paths.Paths.Runtime;

        Directory.CreateDirectory(_runtime);
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

    private LinuxPaths Paths() =>
        new(
            new FakeEnvironmentProvider(
                Path.Combine(_root, "home"),
                new Dictionary<string, string>
                {
                    ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                    ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                    ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                    ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
                }),
            new NoOpFilePermissions(),
            new HostPlatform(
                HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST-MACHINE"));

    /// <summary>A runtime directory last written at a given moment.</summary>
    private string Leftover(string name, DateTimeOffset written)
    {
        var path = Path.Combine(_runtime, name);

        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "compiled-context.md"), "instructions");
        Directory.SetLastWriteTimeUtc(path, written.UtcDateTime);

        return path;
    }

    private RuntimeReaper Reaper(params RunningSession[] running)
    {
        var registry = new QuietSessionRegistry();

        registry.Running.AddRange(running);

        return new RuntimeReaper(Paths(), registry, _clock);
    }

    private static RunningSession Session(DateTimeOffset startedAt) =>
        new("launch", "alpha", "Alpha", "claude", null, "/repos/alpha", 4242, startedAt, startedAt);

    [Fact]
    public async Task A_directory_from_a_session_that_never_ended_is_collected()
    {
        var stale = Leftover("20260823-210843-7852b1ea", _clock.GetUtcNow().AddDays(-16));

        var reaped = await Reaper().ReapAsync();

        reaped.Value.Should().Be(1);
        Directory.Exists(stale).Should().BeFalse();
    }

    [Fact]
    public async Task Todays_directory_is_left_alone()
    {
        // The launch that is running now created one of these seconds ago, and
        // registers the session holding it a moment after. A reaper running in
        // that gap must not take it.
        var fresh = Leftover("20260908-115900-aaaaaaaa", _clock.GetUtcNow().AddMinutes(-1));

        var reaped = await Reaper().ReapAsync();

        reaped.Value.Should().Be(0);
        Directory.Exists(fresh).Should().BeTrue();
    }

    [Fact]
    public async Task Nothing_newer_than_the_oldest_running_session_is_touched()
    {
        // The guard that matters. A session started five days ago is still
        // reading its context, and so is everything started since — including
        // sessions whose launcher never recorded which directory was theirs,
        // which is every session running when this shipped.
        var duringThatSession = Leftover("20260905-090000-bbbbbbbb", _clock.GetUtcNow().AddDays(-3));
        var beforeIt = Leftover("20260820-090000-cccccccc", _clock.GetUtcNow().AddDays(-19));

        var reaped = await Reaper(Session(_clock.GetUtcNow().AddDays(-5))).ReapAsync();

        reaped.Value.Should().Be(1);
        Directory.Exists(duringThatSession).Should().BeTrue(
            "a session older than this directory is still running, so it may be reading it");
        Directory.Exists(beforeIt).Should().BeFalse();
    }

    [Fact]
    public async Task A_running_session_does_not_protect_what_predates_every_session()
    {
        // The other half of the same rule: the floor is the oldest session, so
        // one recent session does not make the whole history untouchable.
        var ancient = Leftover("20260801-090000-dddddddd", _clock.GetUtcNow().AddDays(-38));

        var reaped = await Reaper(Session(_clock.GetUtcNow().AddMinutes(-5))).ReapAsync();

        reaped.Value.Should().Be(1);
        Directory.Exists(ancient).Should().BeFalse();
    }

    [Fact]
    public async Task A_missing_runtime_root_is_not_a_failure() =>
        // Nothing has launched on this machine yet, which is a state and not a
        // fault.
        (await Reaper().ReapAsync()).Succeeded.Should().BeTrue();

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
