using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Platform;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Loadout.Tui;
using Spectre.Console;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Holding, releasing, stopping and restarting the daemon from another shell.
/// </summary>
/// <remarks>
/// <para>
/// The daemon usually runs where nobody is looking - started at login in a
/// minimised window - and the only way to stop it was to find that window and
/// press Ctrl+C in it. These are the files it watches instead, and what it
/// does when it sees them.
/// </para>
/// <para>
/// The stop tests wait on real time, a glance or two, because the daemon looks
/// at its controls on its own clock. They are bounded so that a daemon that
/// never notices fails rather than holding the suite.
/// </para>
/// </remarks>
public sealed class DaemonControlTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-dctl-" + Guid.NewGuid().ToString("N"));
    private readonly IPlatformPaths _paths;
    private readonly StringWriter _said = new();

    public DaemonControlTests()
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
    }

    public void Dispose()
    {
        _said.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task A_hold_is_there_until_it_is_lifted_and_says_whether_it_was()
    {
        DaemonControl.Paused(_paths).Should().BeFalse("nothing has held it");

        await DaemonControl.PauseAsync(_paths);

        DaemonControl.Paused(_paths).Should().BeTrue();
        DaemonControl.Resume(_paths).Should().BeTrue("it was held");
        DaemonControl.Paused(_paths).Should().BeFalse();
        DaemonControl.Resume(_paths).Should().BeFalse("there was nothing left to lift");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task A_stop_is_read_back_as_it_was_asked_for(bool now, bool restart)
    {
        DaemonControl.Stopping(_paths).Should().BeNull();

        await DaemonControl.StopAsync(_paths, new DaemonStopRequest(now, restart));

        DaemonControl.Stopping(_paths).Should().Be(new DaemonStopRequest(now, restart));

        DaemonControl.ClearStop(_paths);

        DaemonControl.Stopping(_paths).Should().BeNull();
    }

    [Fact]
    public async Task A_held_daemon_fires_nothing_and_says_so_once_each_way()
    {
        var daemon = Daemon(new Held());
        var output = Output();

        daemon.Firing(output).Should().BeTrue("nothing is held yet");

        await DaemonControl.PauseAsync(_paths);

        daemon.Firing(output).Should().BeFalse();
        daemon.Firing(output).Should().BeFalse();

        DaemonControl.Resume(_paths);

        daemon.Firing(output).Should().BeTrue();

        // Once when it was held and once when it was let go: a daemon that
        // repeated "paused" every two seconds would bury everything else in
        // its window.
        var said = _said.ToString();

        Occurrences(said, "Paused").Should().Be(1);
        Occurrences(said, "Resumed").Should().Be(1);
    }

    [Fact]
    public async Task Nothing_new_fires_once_a_stop_has_been_asked_for()
    {
        var daemon = Daemon(new Held());

        await DaemonControl.StopAsync(_paths, new DaemonStopRequest(Now: false, Restart: false));

        daemon.Firing(Output()).Should().BeFalse("a daemon on its way out should not start a twenty-minute run");
    }

    /// <summary>
    /// The default stop waits for the runs the daemon started. A node killed
    /// mid-turn loses the turn and the money spent on it.
    /// </summary>
    [Fact]
    public async Task A_stop_waits_for_the_run_it_started_and_then_ends_it()
    {
        var held = new Held();
        var daemon = Daemon(held);
        using var stopping = new CancellationTokenSource();

        (await daemon.TriggeredAsync(
            new TriggerRequest("docs-crew", "check the docs", "loadout-cli"), Output(), CancellationToken.None))
            .Succeeded.Should().BeTrue();

        await held.Asked();

        await DaemonControl.StopAsync(_paths, new DaemonStopRequest(Now: false, Restart: false));

        var watching = daemon.WatchAsync(stopping, Output());

        // Two glances and then some: long enough that a daemon ignoring its
        // run would have stopped by now.
        await Task.Delay(TeamDaemonCommand.Glance * 2 + TimeSpan.FromMilliseconds(500));

        stopping.IsCancellationRequested.Should().BeFalse("its run is still going");

        held.Release();

        (await Task.WhenAny(watching, Task.Delay(TimeSpan.FromSeconds(10))))
            .Should().BeSameAs(watching, "the run has finished, so the stop should land at the next glance");

        stopping.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task A_stop_now_does_not_wait_for_anything()
    {
        var held = new Held();
        var daemon = Daemon(held);
        using var stopping = new CancellationTokenSource();

        (await daemon.TriggeredAsync(
            new TriggerRequest("docs-crew", "check the docs", "loadout-cli"), Output(), CancellationToken.None))
            .Succeeded.Should().BeTrue();

        await held.Asked();

        await DaemonControl.StopAsync(_paths, new DaemonStopRequest(Now: true, Restart: false));

        var watching = daemon.WatchAsync(stopping, Output());

        (await Task.WhenAny(watching, Task.Delay(TimeSpan.FromSeconds(10))))
            .Should().BeSameAs(watching);

        stopping.IsCancellationRequested.Should().BeTrue("'now' means now, whatever is running");

        held.Release();
    }

    /// <summary>
    /// The successor gets the settings its predecessor was started with, and
    /// the port it was actually serving when that was left to the machine: a
    /// bookmarked page should still be there after a restart.
    /// </summary>
    [Fact]
    public void A_restart_starts_its_successor_with_the_same_settings_and_port()
    {
        var launcher = new StubProcessLauncher(string.Empty);
        var daemon = Daemon(new Held(), launcher);

        daemon.Succeed(
            Output(),
            "http://127.0.0.1:51999/?token=abc",
            new TeamDaemonCommand.Settings { Listen = "0.0.0.0" });

        var started = launcher.Background;

        started.Should().NotBeNull("a restart has to start something, and in the background");
        launcher.Detached.Should().BeNull("a window of its own is one more window that ends it when closed");

        var arguments = string.Join(' ', started!.Arguments);

        arguments.Should().Contain("team daemon")
            .And.Contain("--after 4242", "the successor has to know which daemon to wait for")
            .And.Contain("--port 51999")
            .And.Contain("--listen 0.0.0.0")
            .And.Contain($"--log {DaemonLog.PathFor(_paths)}", "a terminal showing the log goes on to show the successor");
    }

    [Fact]
    public void A_restart_of_one_serving_nothing_serves_nothing()
    {
        var launcher = new StubProcessLauncher(string.Empty);
        var daemon = Daemon(new Held(), launcher);

        daemon.Succeed(Output(), null, new TeamDaemonCommand.Settings { NoDashboard = true });

        string.Join(' ', launcher.Background!.Arguments)
            .Should().Contain("--no-dashboard").And.NotContain("--port");
    }

    /// <summary>The daemon the stub launcher says it started.</summary>
    private static readonly BackgroundProcess Child = new(5151, new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));

    private void Note(int pid, DateTimeOffset startedAt, string? address)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DaemonNote.PathFor(_paths))!);
        File.WriteAllText(
            DaemonNote.PathFor(_paths),
            System.Text.Json.JsonSerializer.Serialize(new DaemonState(pid, startedAt, address, DateTimeOffset.UtcNow)));
    }

    private void Said(string line)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DaemonLog.PathFor(_paths))!);
        File.AppendAllText(DaemonLog.PathFor(_paths), line + "\n");
    }

    /// <summary>
    /// The whole of the fix: the daemon is started as a process of its own
    /// with no window, rather than being this command, in this terminal, where
    /// closing the terminal ended it.
    /// </summary>
    [Fact]
    public async Task Starting_it_starts_a_daemon_in_the_background_writing_to_its_log()
    {
        var launcher = new StubProcessLauncher(string.Empty) { Started = Child };
        var processes = new FakeProcessInspector().MarkLive(Child.Pid, Child.StartedAt);
        var daemon = Daemon(new Held(), launcher, processes: processes);

        var starting = daemon.BackgroundAsync(
            new TeamDaemonCommand.Settings { Port = 8080 }, Output(), watched: false, CancellationToken.None);

        // What the daemon writes once it is listening.
        Note(Child.Pid, Child.StartedAt, "http://127.0.0.1:8080/?token=abc");

        (await starting).Should().Be(0);

        launcher.Detached.Should().BeNull();
        string.Join(' ', launcher.Background!.Arguments)
            .Should().Contain("team daemon")
            .And.Contain("--port 8080")
            .And.Contain($"--log {DaemonLog.PathFor(_paths)}")
            .And.NotContain("--foreground");

        _said.ToString().Should().Contain("http://127.0.0.1:8080/?token=abc", "where the page is, said once it is there");
    }

    [Fact]
    public async Task One_that_dies_before_it_has_started_is_a_failure_where_nobody_is_watching()
    {
        var launcher = new SaysOnStart(DaemonLog.PathFor(_paths), "The port 8080 is in use.");
        var daemon = Daemon(new Held(), launcher, processes: new FakeProcessInspector());

        var code = await daemon.BackgroundAsync(
            new TeamDaemonCommand.Settings { Port = 8080 }, Output(), watched: false, CancellationToken.None);

        code.Should().NotBe(0, "nothing is running");
    }

    [Fact]
    public async Task A_terminal_shows_what_it_says_until_it_stops()
    {
        var processes = new FakeProcessInspector().MarkLive(Child.Pid, Child.StartedAt);
        var daemon = Daemon(new Held(), processes: processes, console: Shown());

        Said("before it started, and not its business");

        var from = DaemonLog.Length(DaemonLog.PathFor(_paths));

        Note(Child.Pid, Child.StartedAt, null);
        Said("09:00 starting nightly");

        var following = daemon.FollowAsync(DaemonLog.PathFor(_paths), from, Child, Output(), CancellationToken.None);

        await Task.Delay(TeamDaemonCommand.Follow * 3);

        Said("09:20 nightly finished");
        processes.KillEverything();

        (await following.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(0, "it had started, and then stopped");

        var shown = _said.ToString();

        shown.Should().Contain("09:00 starting nightly").And.Contain("09:20 nightly finished");
        shown.Should().NotContain("not its business", "only this daemon's lines are shown");
        shown.Should().Contain("has stopped");
    }

    /// <summary>
    /// Ctrl+C, or the terminal closing, ends the showing. What it must not do
    /// is end the daemon, which is the thing that went wrong.
    /// </summary>
    [Fact]
    public async Task Stopping_the_showing_leaves_the_daemon_running_and_says_so()
    {
        var processes = new FakeProcessInspector().MarkLive(Child.Pid, Child.StartedAt);
        var daemon = Daemon(new Held(), processes: processes);
        using var watching = new CancellationTokenSource();

        Note(Child.Pid, Child.StartedAt, null);

        var following = daemon.FollowAsync(DaemonLog.PathFor(_paths), 0, Child, Output(), watching.Token);

        await watching.CancelAsync();

        (await following.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(0);

        _said.ToString().Should().Contain($"still running as process {Child.Pid}");
        DaemonControl.Stopping(_paths).Should().BeNull("nothing asked the daemon itself to stop");
    }

    [Fact]
    public async Task One_that_ends_before_it_has_written_its_note_never_started()
    {
        var daemon = Daemon(new Held(), processes: new FakeProcessInspector());

        Said("Could not listen on 8080.");

        var code = await daemon
            .FollowAsync(DaemonLog.PathFor(_paths), 0, Child, Output(), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        code.Should().NotBe(0, "a daemon that never got going has not stopped, it has failed");
    }

    /// <summary>
    /// Somebody who closed the terminal and types the command again wants to
    /// see the daemon they left, not to be told off for having one.
    /// </summary>
    [Fact]
    public async Task Asking_again_in_a_terminal_shows_the_running_daemon_and_starts_nothing()
    {
        var launcher = new StubProcessLauncher(string.Empty);
        var processes = new FakeProcessInspector().MarkLive(Child.Pid, Child.StartedAt);
        var daemon = Daemon(new Held(), launcher, processes: processes);
        using var watching = new CancellationTokenSource();

        Note(Child.Pid, Child.StartedAt, "http://127.0.0.1:8080/?token=abc");

        var showing = daemon.BackgroundAsync(new TeamDaemonCommand.Settings(), Output(), watched: true, watching.Token);

        await Task.Delay(TeamDaemonCommand.Follow * 2);
        await watching.CancelAsync();

        (await showing.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(0);

        launcher.Background.Should().BeNull("one is already running");
        _said.ToString().Should().Contain("already running");
    }

    [Fact]
    public async Task Asking_again_where_nobody_is_watching_is_refused()
    {
        var launcher = new StubProcessLauncher(string.Empty);
        var processes = new FakeProcessInspector().MarkLive(Child.Pid, Child.StartedAt);
        var daemon = Daemon(new Held(), launcher, processes: processes);

        Note(Child.Pid, Child.StartedAt, null);

        var code = await daemon.BackgroundAsync(
            new TeamDaemonCommand.Settings(), Output(), watched: false, CancellationToken.None);

        code.Should().NotBe(0, "a script asking to start one needs to know it did not");
        launcher.Background.Should().BeNull();
    }

    /// <summary>A launcher whose daemon writes one line and is gone.</summary>
    private sealed class SaysOnStart(string log, string line) : IProcessLauncher
    {
        private readonly StubProcessLauncher _rest = new(string.Empty) { Started = Child };

        public Task<Loadout.Models.Results.OperationResult<ProcessOutcome>> RunAsync(
            ProcessRequest request, TimeSpan? timeout = null, CancellationToken ct = default) =>
            _rest.RunAsync(request, timeout, ct);

        public Task<Loadout.Models.Results.OperationResult<int>> RunInteractiveAsync(
            ProcessRequest request, CancellationToken ct = default) =>
            _rest.RunInteractiveAsync(request, ct);

        public Task<Loadout.Models.Results.OperationResult<IPipedProcess>> StartPipedAsync(
            ProcessRequest request, CancellationToken ct = default) =>
            _rest.StartPipedAsync(request, ct);

        public Loadout.Models.Results.OperationResult StartDetached(ProcessRequest request) =>
            _rest.StartDetached(request);

        public Loadout.Models.Results.OperationResult<BackgroundProcess> StartBackground(ProcessRequest request)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.AppendAllText(log, line + "\n");

            return _rest.StartBackground(request);
        }
    }

    [Fact]
    public async Task The_daemon_still_starts_the_run_when_the_nomination_pass_throws()
    {
        var commands = new Asked();
        var daemon = Daemon(commands, nominations: new ThrowingPass());
        var schedule = new Loadout.Models.Teams.TeamSchedule
        {
            Id = "tools-on-finish",
            Team = "tool-works",
            Project = "loadout-cli",
            Goal = "Look at what finished.",
            On = "run-finished",
            Autonomy = "autonomous",
        };

        var code = await daemon.StartAsync(schedule, DateTimeOffset.UtcNow, Output(), CancellationToken.None);

        code.Should().Be(0);
        commands.Paths.Should().ContainSingle().Which.Should().Be("team run",
            "a pass that cannot read what finished is no reason not to start what was scheduled");
        _said.ToString().Should().Contain("nominated nothing before tools-on-finish");
    }

    private static int Occurrences(string text, string word) =>
        (text.Length - text.Replace(word, string.Empty, StringComparison.Ordinal).Length) / word.Length;

    private TeamDaemonCommand Daemon(
        ICommandCatalogue commands,
        IProcessLauncher? launcher = null,
        Loadout.Core.Tools.IToolNominationPass? nominations = null,
        FakeProcessInspector? processes = null,
        IAnsiConsole? console = null) =>
        new(
            secrets: null!,
            client: null!,
            configuration: null!,
            schedules: null!,
            journal: null!,
            commands,
            _paths,
            projects: null!,
            tasks: null!,
            git: null!,
            processes ?? new FakeProcessInspector(),
            console ?? Quiet(),
            TimeProvider.System,
            AccessibleMode.Off,
            speech: null!,
            teams: null!,
            library: null!,
            workspace: null!,
            agents: null!,
            launcher ?? new StubProcessLauncher(string.Empty),
            nominations: nominations!);

    /// <summary>A nomination pass whose folders cannot be read.</summary>
    private sealed class ThrowingPass : Loadout.Core.Tools.IToolNominationPass
    {
        public Task<IReadOnlyList<(Loadout.Core.Tools.ToolNomination Nomination,
            Loadout.Models.Results.OperationResult<Loadout.Core.Tools.ToolSubmitted>? Filed)>> BeforeAsync(
            Loadout.Models.Teams.TeamSchedule schedule, Action<string>? log = null, CancellationToken ct = default) =>
            throw new UnauthorizedAccessException("Access to the path 'runs' is denied.");
    }

    /// <summary>A command that finishes at once, remembering what it was asked.</summary>
    private sealed class Asked : ICommandCatalogue
    {
        public List<string> Paths { get; } = [];

        public IReadOnlyList<CatalogueEntry> Commands => [];

        public Task<int> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken ct = default)
        {
            Paths.Add(path);
            return Task.FromResult(0);
        }
    }

    private CommandOutput Output() =>
        new(
            AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(_said),
                Interactive = InteractionSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
            }),
            new GlobalSettings());

    /// <summary>A console that writes where the test reads, for what the daemon's log shows.</summary>
    private IAnsiConsole Shown() =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(_said),
            Interactive = InteractionSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
        });

    private static IAnsiConsole Quiet() =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(TextWriter.Null),
            Interactive = InteractionSupport.No,
        });

    /// <summary>A command that starts and does not finish until it is let go.</summary>
    private sealed class Held : ICommandCatalogue
    {
        private readonly TaskCompletionSource _asked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _go = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<CatalogueEntry> Commands => [];

        public Task Asked() => _asked.Task;

        public void Release() => _go.TrySetResult();

        public async Task<int> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken ct = default)
        {
            _asked.TrySetResult();

            await _go.Task;

            return 0;
        }
    }
}
