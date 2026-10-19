using FluentAssertions;
using Loadout.Agents;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Sessions;
using Loadout.Models;
using Loadout.Models.Agents;
using Loadout.Models.Platform;
using Loadout.Models.Results;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Loadout.Tui;
using Spectre.Console.Testing;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What <c>loadout resume</c> launches and says, end to end through the
/// command with the agent faked out.
/// </summary>
/// <remarks>
/// Written before choosing, carry-over and the running check moved into
/// Core, to pin what the command did so the move could not change it
/// unnoticed. The carry-over fixture has both a link and the launch the old
/// inference would have picked, so it holds on either side of the move.
/// </remarks>
public sealed class ResumeCommandTests : IDisposable
{
    private const string SessionId = "0f1e2d3c-0000-4000-8000-000000000001";
    private const string LaunchId = "1234567890abcdef1234567890abcdef";

    private readonly string _root;
    private readonly LaunchLedger _ledger;
    private readonly FakeGit _git;
    private readonly FakeProjects _projects;
    private readonly CapturingLauncher _launcher = new();
    private readonly TestConsole _console = new TestConsole().Width(200);

    public ResumeCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-resume-" + Guid.NewGuid().ToString("N"));
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

        var paths = new LinuxPaths(
            environment,
            new NoOpFilePermissions(),
            new HostPlatform(HostOperatingSystem.Linux, System.Runtime.InteropServices.Architecture.X64, "test", "TEST"));

        _ledger = new LaunchLedger(paths, new NoOpFilePermissions(), TimeProvider.System);
        _projects = new FakeProjects("alpha", Path.Combine(_root, "alpha"));
        _git = new FakeGit(Path.Combine(_root, "alpha"));
        _git.Linked.Add(new Core.Git.GitWorktree(Path.Combine(_root, "feature-x"), "feature-x", IsPrimary: false));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static AgentSession Session(string? slug = "alpha") =>
        new("claude", SessionId, "Fix the importer", "/repos/alpha", "main",
            new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), "/transcripts/x.jsonl", slug);

    /// <summary>The launch the conversation came from, written as the ledger writes it, and its link.</summary>
    private void Launched()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_ledger.Path)!);

        File.WriteAllLines(_ledger.Path,
        [
            $$"""{"Kind":"start","Id":"{{LaunchId}}","When":"2026-10-01T09:00:00.0000000+00:00","Slug":"alpha","Project":"alpha","Agent":"claude","Mode":"implement","Task":"fix the importer","Worktree":"feature-x"}""",
            $$"""{"Kind":"link","Id":"{{SessionId}}","When":"2026-10-01T09:00:05.0000000+00:00","Agent":"claude","Launch":"{{LaunchId}}"}""",
        ]);
    }

    private ResumeCommand Command(params AgentSession[] sessions)
    {
        var history = new ListedHistory(sessions);

        return new ResumeCommand(
            new SessionResumption(history, _ledger, new NoneRunning(), _projects, _git, TimeProvider.System),
            new SessionScope(_projects),
            _launcher,
            new NoTranscripts(),
            _console,
            new ReadingProfile(null));
    }

    [Fact]
    public async Task What_the_session_was_launched_for_is_carried_into_the_launch_and_said()
    {
        Launched();

        await Command(Session()).ExecuteAsync(null!, new ResumeSettings { Session = "0f1e", Project = "alpha" }, CancellationToken.None);

        var request = _launcher.Requests.Should().ContainSingle().Subject;

        request.ResumeSessionId.Should().Be(SessionId);
        request.AgentName.Should().Be("claude");
        request.Task.Should().Be("fix the importer");
        request.Mode.Should().Be("implement");
        request.Worktree.Should().Be("feature-x");

        _console.Output.Should().Contain("Resuming alpha with claude: Fix the importer");
        _console.Output.Should().Contain("Carrying over from its launch: task 'fix the importer', mode implement, worktree feature-x");
    }

    [Fact]
    public async Task A_task_named_by_id_replaces_the_carried_task_and_keeps_the_rest()
    {
        Launched();

        await Command(Session()).ExecuteAsync(
            null!, new ResumeSettings { Session = SessionId, Project = "alpha", TaskId = "ship-it" }, CancellationToken.None);

        var request = _launcher.Requests.Should().ContainSingle().Subject;

        request.TaskId.Should().Be("ship-it");
        request.Task.Should().BeNull("the carried words would choose specialists for a different task");
        request.Mode.Should().Be("implement");
        _console.Output.Should().NotContain("Carrying over");
    }

    [Fact]
    public async Task A_session_from_an_unregistered_folder_says_how_to_register_it()
    {
        var exit = await Command(Session(slug: null)).ExecuteAsync(
            null!, new ResumeSettings { Session = SessionId, All = true }, CancellationToken.None);

        exit.Should().Be((int)ExitCode.ProjectNotFound);
        // What it says goes to stderr, and is pinned on the Core service
        // that words it rather than by capturing the process's own stream.
        _launcher.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_session_that_does_not_exist_launches_nothing()
    {
        var exit = await Command(Session()).ExecuteAsync(
            null!, new ResumeSettings { Session = "zzz", Project = "alpha" }, CancellationToken.None);

        exit.Should().Be((int)ExitCode.ProjectNotFound);
        _launcher.Requests.Should().BeEmpty();
    }

    private sealed class ListedHistory(IReadOnlyList<AgentSession> sessions) : ISessionHistoryService
    {
        public Task<OperationResult<IReadOnlyList<AgentSession>>> ListAsync(SessionQuery query, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<AgentSession>>.Ok(sessions));
    }

    private sealed class NoneRunning : ISessionRegistry
    {
        public string Path => "running";

        public string InProgressPath => "running/in-progress";

        public Task RegisterAsync(NewSession session, CancellationToken ct = default) => Task.CompletedTask;

        public Task ReleaseAsync(string launchId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<RunningSession>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RunningSession>>([]);
    }

    private sealed class NoTranscripts : IHeadlessTranscripts
    {
        public Task<bool> PutAwayAsync(string agent, string sessionId, CancellationToken ct = default) => Task.FromResult(false);

        public bool Restore(string agent, string sessionId) => false;
    }

    /// <summary>Takes the request and stops there: what is under test is what the command asked for.</summary>
    private sealed class CapturingLauncher : IAgentLauncher
    {
        public List<LaunchRequest> Requests { get; } = [];

        public Task<OperationResult<LaunchOutcome>> LaunchAsync(LaunchRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);

            return Task.FromResult(OperationResult<LaunchOutcome>.Fail("stopped by the test", ExitCode.AgentUnavailable));
        }

        public Task<OperationResult<HeadlessLaunch>> StartHeadlessAsync(
            LaunchRequest request, HeadlessOptions options, CancellationToken ct = default) =>
            throw new NotSupportedException("resume is never headless");
    }
}
