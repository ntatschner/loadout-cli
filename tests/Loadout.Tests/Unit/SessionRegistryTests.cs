using FluentAssertions;
using Loadout.Core.Sessions;
using Loadout.Models.Platform;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which sessions the registry says are running, and which it refuses to.
/// </summary>
/// <remarks>
/// <para>
/// An entry is a claim rather than a fact: a session that is killed never
/// deletes its own file. So the cases that matter are the ones where the file
/// says one thing and the machine says another, and every one of them needs a
/// process table the test controls rather than the real one.
/// </para>
/// <para>
/// The reuse case is the reason the start time is recorded at all. Process
/// identifiers come round again, and a registry that matched on the number alone
/// would report somebody else's process as a session of ours still running —
/// confidently, and only after something had already gone wrong.
/// </para>
/// </remarks>
public sealed class SessionRegistryTests : IDisposable
{
    private readonly string _root;
    private readonly FakeProcessInspector _processes = new();
    private readonly FixedTime _time = new();
    private readonly SessionRegistry _registry;

    public SessionRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-running-" + Guid.NewGuid().ToString("N"));

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

        var paths = new LinuxPaths(
            environment,
            permissions,
            new HostPlatform(
                HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST-MACHINE"));

        paths.EnsureDirectoriesExist();

        _registry = new SessionRegistry(paths, permissions, _processes, _time);
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

    [Fact]
    public async Task A_running_session_is_listed_with_what_it_is_working_on()
    {
        _processes.MarkSelfLive();

        await _registry.RegisterAsync(Session("launch-1"));

        var session = (await _registry.ListAsync()).Should().ContainSingle().Subject;

        session.LaunchId.Should().Be("launch-1");
        session.ProjectSlug.Should().Be("starstats");
        session.ProjectName.Should().Be("StarStats");
        session.Agent.Should().Be("claude");
        session.Worktree.Should().Be("release");
        session.WorkingDirectory.Should().Be("/repos/starstats");
        session.ProcessId.Should().Be(_processes.CurrentProcessId);
        session.StartedAt.Should().Be(_time.Now);
    }

    [Fact]
    public async Task A_session_that_was_released_is_gone()
    {
        _processes.MarkSelfLive();

        await _registry.RegisterAsync(Session("launch-1"));
        await _registry.ReleaseAsync("launch-1");

        (await _registry.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_session_whose_process_died_is_not_listed()
    {
        _processes.MarkSelfLive();

        await _registry.RegisterAsync(Session("launch-1"));

        // Killed, or the machine went down. The file is still there and says
        // nothing about it.
        _processes.KillEverything();

        (await _registry.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task The_identity_that_was_recorded_is_the_identity_that_gets_checked()
    {
        _processes.MarkSelfLive();

        await _registry.RegisterAsync(Session("launch-1"));

        var recordedAt = _processes.CurrentProcessStartedAt;

        // The launcher goes on running and its own start time stays what it was,
        // so an entry checked against "whatever this process is" rather than
        // against what the entry says would give the same answer here and a
        // wrong one later. Only the question asked tells the two apart.
        _processes.Asked.Clear();
        _processes.CurrentProcessStartedAt = recordedAt.AddHours(3);

        await _registry.ListAsync();

        _processes.Asked.Should().AllSatisfy(question =>
        {
            question.ProcessId.Should().Be(_processes.CurrentProcessId);
            question.StartedAt.Should().Be(recordedAt);
        });

        _processes.Asked.Should().NotBeEmpty("the entry has to be checked at all");
    }

    [Fact]
    public async Task Registering_clears_the_entries_whose_processes_are_gone()
    {
        _processes.MarkSelfLive();

        await _registry.RegisterAsync(Session("dead"));

        var abandoned = Path.Combine(_registry.Path, "dead.json");

        File.Exists(abandoned).Should().BeTrue();

        _processes.KillEverything();

        _processes.CurrentProcessId = 5150;
        _processes.MarkSelfLive();

        await _registry.RegisterAsync(Session("alive"));

        File.Exists(abandoned).Should().BeFalse("a new launch is the moment to tidy up");

        (await _registry.ListAsync()).Should().ContainSingle()
            .Which.LaunchId.Should().Be("alive");
    }

    [Fact]
    public async Task Listing_deletes_nothing()
    {
        _processes.MarkSelfLive();

        await _registry.RegisterAsync(Session("launch-1"));

        _processes.KillEverything();

        (await _registry.ListAsync()).Should().BeEmpty();

        // Reading is a question. A question that tidies up behaves differently
        // depending on who asked it first, which is not something a report
        // should do to the thing it is reporting on.
        File.Exists(Path.Combine(_registry.Path, "launch-1.json")).Should().BeTrue();
    }

    [Fact]
    public async Task An_entry_that_cannot_be_read_costs_that_entry_only()
    {
        _processes.MarkSelfLive();

        await _registry.RegisterAsync(Session("good"));

        await File.WriteAllTextAsync(Path.Combine(_registry.Path, "torn.json"), "{\"LaunchId\":");

        (await _registry.ListAsync()).Should().ContainSingle()
            .Which.LaunchId.Should().Be("good");
    }

    [Fact]
    public async Task Sessions_are_listed_oldest_first()
    {
        _processes.MarkSelfLive();

        // Named so that the order asked for is the opposite of the order the
        // filesystem hands them back in. With names that sorted the same way,
        // this would pass whether or not anything sorted them.
        await _registry.RegisterAsync(Session("zulu"));

        _time.Now = _time.Now.AddMinutes(20);

        await _registry.RegisterAsync(Session("alpha"));

        (await _registry.ListAsync()).Select(session => session.LaunchId)
            .Should().Equal("zulu", "alpha");
    }

    [Fact]
    public async Task A_registry_that_cannot_be_written_does_not_stop_a_launch()
    {
        _processes.MarkSelfLive();

        // A file where the directory of entries belongs. Nothing can be created
        // inside it, which is the failure the registry promises to swallow.
        Directory.CreateDirectory(Path.GetDirectoryName(_registry.Path)!);
        await File.WriteAllTextAsync(_registry.Path, "not a directory");

        await _registry.RegisterAsync(Session("launch-1"));
        await _registry.ReleaseAsync("launch-1");

        (await _registry.ListAsync()).Should().BeEmpty();
    }

    private static NewSession Session(string launchId) => new(
        launchId,
        "starstats",
        "StarStats",
        "claude",
        "release",
        "/repos/starstats");

    /// <summary>A clock the test moves, so ordering can be asserted without waiting.</summary>
    private sealed class FixedTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
