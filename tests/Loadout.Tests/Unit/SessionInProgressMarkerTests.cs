using FluentAssertions;
using Loadout.Core.Sessions;
using Loadout.Models.Platform;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The one fact about a running session that something unable to run code can
/// still read.
/// </summary>
/// <remarks>
/// <para>
/// A session runs inside the launcher process — the agent inherits its terminal,
/// so the launcher is alive and is its parent for the whole session. The Windows
/// installer closes every launcher in order to replace the binary, which ends
/// those sessions. It cannot ask whether that is about to happen: the records
/// beside this are one file per session, and knowing whether any of them is
/// still alive means checking a process.
/// </para>
/// <para>
/// So the answer is kept as a file whose existence is the whole message, and
/// these are the transitions that have to be right. A marker left behind after
/// the last session ends stops somebody installing; a marker missing while a
/// session runs loses their work.
/// </para>
/// </remarks>
public sealed class SessionInProgressMarkerTests : IDisposable
{
    private readonly string _root;
    private readonly FakeProcessInspector _processes = new();
    private readonly SessionRegistry _registry;

    public SessionInProgressMarkerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-marker-" + Guid.NewGuid().ToString("N"));

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

        IPlatformPaths paths = new LinuxPaths(
            environment,
            permissions,
            new HostPlatform(
                HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST"));

        paths.EnsureDirectoriesExist();

        _registry = new SessionRegistry(paths, permissions, _processes, TimeProvider.System);
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

    private bool Marked => File.Exists(_registry.InProgressPath);

    [Fact]
    public async Task Nothing_running_says_nothing()
    {
        Marked.Should().BeFalse("an installer must not be blocked by a machine with no sessions");
    }

    [Fact]
    public async Task A_session_that_is_running_is_visible_without_reading_a_process_table()
    {
        _processes.MarkSelfLive();

        await _registry.RegisterAsync(Session("first"));

        Marked.Should().BeTrue();
    }

    [Fact]
    public async Task The_marker_goes_when_the_last_session_ends()
    {
        _processes.MarkSelfLive();

        await _registry.RegisterAsync(Session("first"));
        await _registry.ReleaseAsync("first");

        // The moment the installer is allowed to close the launcher again. A
        // marker that outlived its session would refuse every install from then
        // on, which is a worse failure than the one it prevents.
        Marked.Should().BeFalse();
    }

    [Fact]
    public async Task One_session_ending_does_not_speak_for_another_still_running()
    {
        _processes.CurrentProcessId = 100;
        _processes.CurrentProcessStartedAt = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        _processes.MarkSelfLive();

        await _registry.RegisterAsync(Session("first"));

        // A second launcher, in its own process, as a second terminal would be.
        _processes.CurrentProcessId = 200;
        _processes.CurrentProcessStartedAt = new DateTimeOffset(2026, 1, 1, 9, 5, 0, TimeSpan.Zero);
        _processes.MarkSelfLive();

        await _registry.RegisterAsync(Session("second"));
        await _registry.ReleaseAsync("first");

        Marked.Should().BeTrue("the second session is still running inside its launcher");
    }

    [Fact]
    public async Task A_launcher_that_died_without_releasing_stops_holding_the_marker()
    {
        _processes.MarkSelfLive();

        await _registry.RegisterAsync(Session("first"));

        // The crash. The entry is still on disk and the process behind it is
        // gone, which is the state a killed terminal leaves behind.
        _processes.KillEverything();

        _processes.CurrentProcessId = 300;
        _processes.CurrentProcessStartedAt = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        _processes.MarkSelfLive();

        await _registry.RegisterAsync(Session("second"));
        await _registry.ReleaseAsync("second");

        // Both entries are dead now — one released, one abandoned by a process
        // that no longer exists — so nothing is running and the marker goes.
        // Derived from the entries every time rather than counted, because a
        // count kept in a variable is wrong after exactly this.
        Marked.Should().BeFalse();
    }

    [Fact]
    public async Task A_dead_entry_still_on_disk_does_not_count_as_a_session()
    {
        _processes.CurrentProcessId = 100;
        _processes.CurrentProcessStartedAt = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        _processes.MarkSelfLive();

        await _registry.RegisterAsync(Session("first"));

        _processes.CurrentProcessId = 200;
        _processes.CurrentProcessStartedAt = new DateTimeOffset(2026, 1, 1, 9, 5, 0, TimeSpan.Zero);
        _processes.MarkSelfLive();

        await _registry.RegisterAsync(Session("second"));

        // The first launcher is killed and never releases, so its entry stays
        // on disk. Nothing sweeps it here: the sweep runs when a session is
        // registered, and none is.
        _processes.KillEverything();

        await _registry.ReleaseAsync("second");

        // So the answer has to come from asking whether the process behind the
        // remaining entry is alive. Counting entries would say a session is
        // running and refuse every install until somebody deleted a file they
        // had no reason to know about.
        Marked.Should().BeFalse();

        _processes.Asked.Should().Contain((100, new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero)),
            "the entry left behind is the one whose process has to be checked");
    }

    [Fact]
    public async Task The_marker_is_not_mistaken_for_a_session()
    {
        _processes.MarkSelfLive();

        await _registry.RegisterAsync(Session("first"));

        // It sits in the directory the entries are read from. They are
        // enumerated as *.json and this is not one, which is the only reason
        // it can live there at all.
        var running = await _registry.ListAsync();

        running.Should().HaveCount(1);
        running[0].LaunchId.Should().Be("first");
    }

    [Fact]
    public void The_installer_looks_for_the_file_the_launcher_actually_writes()
    {
        // Two lists that have to agree: the name here and the name the Windows
        // package searches for. Nothing else connects them — the package is
        // built by a different toolchain, from a file no compiler reads — so
        // renaming this would leave an installer looking for something that is
        // never written, which reads exactly like a package that protects
        // sessions while protecting nothing.
        var wxs = Path.Combine(RepositoryRoot(), "build", "windows", "loadout.wxs");

        File.Exists(wxs).Should().BeTrue("the Windows package is built from this file");

        var searched = System.Text.RegularExpressions.Regex.Match(
            File.ReadAllText(wxs),
            "<FileSearch[^>]*Name=\"(?<name>[^\"]+)\"");

        searched.Success.Should().BeTrue("the package has to look for the marker to honour it");

        searched.Groups["name"].Value
            .Should().Be(Path.GetFileName(_registry.InProgressPath));
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

    private static NewSession Session(string launchId) =>
        new(launchId, "starstats", "StarStats", "claude", null, "/tmp/starstats");
}
