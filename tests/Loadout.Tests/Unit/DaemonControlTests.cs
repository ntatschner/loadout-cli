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

        var started = launcher.Detached;

        started.Should().NotBeNull("a restart has to start something");

        var arguments = string.Join(' ', started!.Arguments);

        arguments.Should().Contain("team daemon")
            .And.Contain("--after 4242", "the successor has to know which daemon to wait for")
            .And.Contain("--port 51999")
            .And.Contain("--listen 0.0.0.0");
    }

    [Fact]
    public void A_restart_of_one_serving_nothing_serves_nothing()
    {
        var launcher = new StubProcessLauncher(string.Empty);
        var daemon = Daemon(new Held(), launcher);

        daemon.Succeed(Output(), null, new TeamDaemonCommand.Settings { NoDashboard = true });

        string.Join(' ', launcher.Detached!.Arguments)
            .Should().Contain("--no-dashboard").And.NotContain("--port");
    }

    private static int Occurrences(string text, string word) =>
        (text.Length - text.Replace(word, string.Empty, StringComparison.Ordinal).Length) / word.Length;

    private TeamDaemonCommand Daemon(ICommandCatalogue commands, IProcessLauncher? launcher = null) =>
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
            new FakeProcessInspector(),
            Quiet(),
            TimeProvider.System,
            AccessibleMode.Off,
            speech: null!,
            teams: null!,
            library: null!,
            workspace: null!,
            agents: null!,
            launcher ?? new StubProcessLauncher(string.Empty),
            nominations: null!);

    private CommandOutput Output() =>
        new(
            AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(_said),
                Interactive = InteractionSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
            }),
            new GlobalSettings());

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
