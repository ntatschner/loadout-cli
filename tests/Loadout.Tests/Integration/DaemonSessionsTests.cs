using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Sessions.Hosting;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Platform;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// 'loadout session' reaching the daemon's hosted sessions from another
/// process: through the address and token the daemon's note gives, and only
/// while the daemon that wrote it is running.
/// </summary>
public sealed class DaemonSessionsTests : IAsyncLifetime
{
    private const int DaemonPid = 5150;
    private static readonly DateTimeOffset DaemonStarted = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-daemon-sessions-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _stopping = new();
    private readonly FakeProcessInspector _processes = new();
    private readonly HttpClient _client = new();
    private IPlatformPaths _paths = null!;
    private HostedSessions _hosted = null!;
    private DashboardServer _server = null!;
    private DaemonSessions _sessions = null!;

    public Task InitializeAsync()
    {
        var environment = new FakeEnvironmentProvider(
            Path.Combine(_root, "home"),
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
            new HostPlatform(HostOperatingSystem.Linux, System.Runtime.InteropServices.Architecture.X64, "test", "TEST"));
        _paths.EnsureDirectoriesExist();

        _hosted = new HostedSessions(() => new ScriptedTerminal(), () => ("loadout-host", []));
        _server = new DashboardServer(new EmptyJournal()) { Sessions = _hosted };
        _server.Start(0).Succeeded.Should().BeTrue();
        _ = _server.ListenAsync(_stopping.Token);

        _sessions = new DaemonSessions(_client, _paths, _processes);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _stopping.CancelAsync();
        await _hosted.DisposeAsync();
        _server.Dispose();
        _client.Dispose();
        _stopping.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task With_no_daemon_running_every_call_says_so()
    {
        WriteNote();

        var listed = await _sessions.ListAsync();

        listed.Failed.Should().BeTrue("the note's daemon is not running");
        listed.Error.Should().Be(DaemonSessions.NoDaemon);
    }

    [Fact]
    public async Task Renaming_and_ending_go_through_the_daemons_routes_and_every_page_sees_them()
    {
        WriteNote();
        _processes.MarkLive(DaemonPid, DaemonStarted);
        var id = (await _hosted.StartAsync(new HostedLaunch("starstats", Agent: "claude"))).Value!.Id;

        var renamed = await _sessions.RenameAsync(id, "release notes");
        var ended = await _sessions.EndAsync(id);
        var listed = await _sessions.ListAsync();

        renamed.Succeeded.Should().BeTrue(renamed.Error);
        renamed.Value!.Title.Should().Be("release notes");
        renamed.Value.LaunchTitle.Should().Be("starstats (claude)");
        ended.Value!.Ended.Should().BeTrue();
        listed.Value!.Should().ContainSingle().Which.Should().Match<HostedSessionRow>(one => one.Id == id && one.Ended && one.Title == "release notes");

        (await _sessions.RenameAsync(id, null)).Value!.Title.Should().Be("starstats (claude)");
    }

    [Fact]
    public async Task What_the_daemon_refuses_is_said_in_its_words()
    {
        WriteNote();
        _processes.MarkLive(DaemonPid, DaemonStarted);
        var id = (await _hosted.StartAsync(new HostedLaunch("starstats"))).Value!.Id;

        var refused = await _sessions.RenameAsync(id, new string('x', HostedSessions.LongestName + 1));
        _server.WatchOnly = true;
        var watching = await _sessions.EndAsync(id);

        refused.Error.Should().Contain($"{HostedSessions.LongestName} characters");
        watching.Error.Should().Contain("only watching");
        _hosted.Find(id)!.HasEnded.Should().BeFalse();
    }

    private void WriteNote()
    {
        var path = DaemonNote.PathFor(_paths);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new DaemonState(
            DaemonPid, DaemonStarted, $"http://127.0.0.1:{_server.Port}/?token={_server.Token}", DaemonStarted)));
    }

    private sealed class EmptyJournal : IRunJournal
    {
        public IReadOnlyList<string> List(int limit = 20) => [];

        public OperationResult<IReadOnlyList<RunEvent>> Read(string runId) =>
            OperationResult<IReadOnlyList<RunEvent>>.Fail("no such run");

        public OperationResult<RunSummary> Summarise(string runId) => OperationResult<RunSummary>.Fail("no such run");

        public string DirectoryOf(string runId) => Path.Combine(Path.GetTempPath(), runId);

        public OperationResult<RunForgotten> Forget(string runId, bool force = false) =>
            OperationResult<RunForgotten>.Fail("no such run");
    }
}
