using System.Net;
using System.Text.Json;
using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Core.Updates;
using Loadout.Models;
using Loadout.Models.Platform;
using Loadout.Models.Results;
using Loadout.Models.Updates;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Updating from Studio: 'update --restart-daemon' verifies the download and
/// leaves installing it to the daemon's restart; the daemon refuses new runs
/// while it waits and hands the restart to the helper; Studio reads what is
/// available and what is pending.
/// </summary>
public sealed class UpdateFromStudioTests : IDisposable
{
    private static readonly DateTimeOffset Started = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-updstudio-" + Guid.NewGuid().ToString("N"));
    private readonly IPlatformPaths _paths;
    private readonly Stager _updates;

    public UpdateFromStudioTests()
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

        Directory.CreateDirectory(Path.Combine(_root, "install"));
        var executable = Path.Combine(_root, "install", "loadout");
        File.WriteAllText(executable, "NEWER BINARY");

        _updates = new Stager(executable, Path.Combine(_root, "staged-loadout"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Theory]
    [InlineData(false, "restart")]
    [InlineData(true, "restart now")]
    public async Task Restart_daemon_verifies_now_and_leaves_the_install_to_the_daemon_s_restart(bool now, string asked)
    {
        Daemon(hosted: 2);
        var console = new TestConsole();

        var exit = await Command(console, Live()).ExecuteAsync(
            null!, new UpdateCommand.Settings { RestartDaemon = true, Yes = true, Now = now }, CancellationToken.None);

        exit.Should().Be(0, console.Output);
        _updates.Staged.Should().Be(InstallChannel.Archive);

        var plan = UpdatePlan.Read(_paths);

        plan.Should().NotBeNull("the daemon carries it out when it restarts");
        plan!.Kind.Should().Be(UpdateKind.Install);
        plan.Version.Should().Be("0.70.0");
        plan.Payload.Should().Be(_updates.Payload);
        plan.Executable.Should().Be(_updates.Executable);
        plan.Daemon.Should().BeNull("only the daemon knows how it was started, and it says so as it hands over");

        File.ReadAllText(Path.Combine(DaemonControl.DirectoryFor(_paths), DaemonControl.StopFile)).Should().Be(asked);
        File.ReadAllText(_updates.Executable).Should().Be("NEWER BINARY", "nothing is replaced while the daemon runs");
        console.Output.ReplaceLineEndings(" ").Should().Contain("2 sessions it hosts for Studio end when it does");
    }

    [Fact]
    public async Task With_no_daemon_running_there_is_nothing_to_hand_the_update_to()
    {
        var console = new TestConsole();

        var exit = await Command(console, new FakeProcessInspector()).ExecuteAsync(
            null!, new UpdateCommand.Settings { RestartDaemon = true, Yes = true }, CancellationToken.None);

        exit.Should().Be((int)ExitCode.InvalidArguments);
        UpdatePlan.Read(_paths).Should().BeNull();
        DaemonControl.Stopping(_paths).Should().BeNull();
        _updates.Staged.Should().BeNull("nothing is downloaded for an update that cannot happen");
    }

    [Fact]
    public async Task Now_on_its_own_is_refused_because_it_says_when_the_daemon_restarts()
    {
        var exit = await Command(new TestConsole(), Live()).ExecuteAsync(
            null!, new UpdateCommand.Settings { Now = true, Yes = true }, CancellationToken.None);

        exit.Should().Be((int)ExitCode.InvalidArguments);
    }

    [Fact]
    public async Task A_dry_run_downloads_nothing_and_asks_the_daemon_nothing()
    {
        Daemon();

        var exit = await Command(new TestConsole(), Live()).ExecuteAsync(
            null!, new UpdateCommand.Settings { RestartDaemon = true, Yes = true, DryRun = true }, CancellationToken.None);

        exit.Should().Be(0);
        _updates.Staged.Should().BeNull();
        UpdatePlan.Read(_paths).Should().BeNull();
        DaemonControl.Stopping(_paths).Should().BeNull();
    }

    [Fact]
    public async Task A_download_that_fails_verification_leaves_the_daemon_alone()
    {
        Daemon();
        _updates.StageFails = true;

        var exit = await Command(new TestConsole(), Live()).ExecuteAsync(
            null!, new UpdateCommand.Settings { RestartDaemon = true, Yes = true }, CancellationToken.None);

        exit.Should().NotBe(0);
        UpdatePlan.Read(_paths).Should().BeNull();
        DaemonControl.Stopping(_paths).Should().BeNull("a restart for an update that will not happen is a restart nobody asked for");
    }

    [Fact]
    public async Task Rolling_back_a_copy_the_msi_installed_is_refused()
    {
        _updates.Channel = InstallChannel.Installer;
        File.WriteAllText(InstallChannels.PreviousOf(_updates.Executable), "OLDER BINARY");

        var exit = await Command(new TestConsole(), Live()).ExecuteAsync(
            null!, new UpdateCommand.Settings { RollBack = true, Yes = true }, CancellationToken.None);

        exit.Should().Be((int)ExitCode.InvalidArguments);
        File.ReadAllText(_updates.Executable).Should().Be("NEWER BINARY");
    }

    [Fact]
    public async Task Rolling_back_with_nothing_kept_says_so()
    {
        var console = new TestConsole();

        var exit = await Command(console, Live()).ExecuteAsync(
            null!, new UpdateCommand.Settings { RollBack = true, Yes = true }, CancellationToken.None);

        exit.Should().Be((int)ExitCode.InvalidArguments);
        File.ReadAllText(_updates.Executable).Should().Be("NEWER BINARY");
        UpdatePlan.Read(_paths).Should().BeNull();
    }

    [Fact]
    public async Task Rolling_back_through_the_daemon_waits_for_its_restart()
    {
        Daemon();
        File.WriteAllText(InstallChannels.PreviousOf(_updates.Executable), "OLDER BINARY");

        var exit = await Command(new TestConsole(), Live()).ExecuteAsync(
            null!, new UpdateCommand.Settings { RollBack = true, RestartDaemon = true, Yes = true }, CancellationToken.None);

        exit.Should().Be(0);
        UpdatePlan.Read(_paths)!.Kind.Should().Be(UpdateKind.RollBack);
        DaemonControl.Stopping(_paths).Should().Be(new DaemonStopRequest(Now: false, Restart: true));
        File.ReadAllText(_updates.Executable).Should().Be("NEWER BINARY", "the helper puts it back once the daemon has gone");
    }

    [Fact]
    public async Task Rolling_back_in_a_terminal_puts_the_previous_binary_back_at_once()
    {
        File.WriteAllText(InstallChannels.PreviousOf(_updates.Executable), "OLDER BINARY");

        var exit = await Command(new TestConsole(), new FakeProcessInspector()).ExecuteAsync(
            null!, new UpdateCommand.Settings { RollBack = true, Yes = true }, CancellationToken.None);

        exit.Should().Be(0);
        File.ReadAllText(_updates.Executable).Should().Be("OLDER BINARY");
    }

    [Fact]
    public async Task While_an_update_waits_the_daemon_refuses_a_run_from_the_page_and_says_why()
    {
        var daemon = Daemon(new StubProcessLauncher(string.Empty));

        daemon.Holding().Should().BeNull("nothing has been asked of it");

        await DaemonControl.StopAsync(_paths, new DaemonStopRequest(Now: false, Restart: true));

        daemon.Holding().Should().Contain("restarting").And.Contain("starts nothing new");

        await Plan().WriteAsync(UpdatePlan.PathFor(_paths));

        daemon.Holding().Should().StartWith("An update is pending").And.Contain("updating to 0.70.0");

        var began = await daemon.BeganAsync(new StartRequest("feature", "api", "a task"), Output(), CancellationToken.None);

        began.Failed.Should().BeTrue();
        began.Error.Should().StartWith("An update is pending");
    }

    [Fact]
    public async Task A_team_run_typed_into_the_commands_page_is_refused_while_the_update_waits()
    {
        var commands = new OneCommand();
        var jobs = new CommandJobs(commands, TimeProvider.System)
        {
            Holding = path => path == "team run" ? "An update is pending." : null,
        };

        var refused = await jobs.StartAsync("team run", [], watchOnly: false, CancellationToken.None);

        refused.Failed.Should().BeTrue();
        refused.Error.Should().Be("An update is pending.");
        commands.Started.Should().BeEmpty();
    }

    [Fact]
    public async Task Handing_over_writes_how_to_start_the_daemon_and_starts_the_helper_on_the_plan()
    {
        var launcher = new StubProcessLauncher(string.Empty);
        var daemon = Daemon(launcher);
        var path = UpdatePlan.PathFor(_paths);

        await Plan().WriteAsync(path);

        var handed = daemon.HandOver(Output(), "http://127.0.0.1:51999/?token=abc", new TeamDaemonCommand.Settings(), UpdatePlan.Read(path)!);

        handed.Should().BeTrue();

        var plan = UpdatePlan.Read(path)!;

        plan.DaemonPid.Should().Be(4242, "the helper waits for this daemon to go before replacing anything");
        plan.Daemon.Should().NotBeNull();
        string.Join(' ', plan.Daemon!).Should().Contain("daemon --port 51999").And.Contain(DaemonLog.Flag);
        plan.Daemon.Should().NotContain("--after", "the helper starts it once this one has gone, so it has nobody to wait for");

        var helper = launcher.Background!;

        helper.Arguments.TakeLast(3).Should().Equal("update", "--finish", path);
        Path.GetFileName(helper.Executable).Should().NotBe("loadout.exe",
            "the MSI ends every process of that name, and this one is running it");
    }

    [Fact]
    public async Task Studio_reads_what_is_pending_and_what_a_restart_now_would_cut_off()
    {
        var daemon = Daemon(new StubProcessLauncher(string.Empty), updates: _updates);

        await Plan().WriteAsync(UpdatePlan.PathFor(_paths));
        await DaemonControl.StopAsync(_paths, new DaemonStopRequest(Now: false, Restart: true));

        var read = JsonDocument.Parse(JsonSerializer.Serialize(await daemon.UpdateAsync(fresh: true, CancellationToken.None))).RootElement;

        read.GetProperty("isNewer").GetBoolean().Should().BeTrue();
        read.GetProperty("available").GetString().Should().Be("0.70.0");
        read.GetProperty("channel").GetString().Should().Be("archive");
        read.GetProperty("runs").GetInt32().Should().Be(0);
        read.GetProperty("pending").GetProperty("kind").GetString().Should().Be("install");
        read.GetProperty("pending").GetProperty("version").GetString().Should().Be("0.70.0");
    }

    [Fact]
    public async Task The_update_route_is_the_daemon_s_alone_and_checks_now_only_when_asked()
    {
        var asked = new List<bool>();

        using var server = new DashboardServer(new NoRunsHere());
        server.Start(0).Succeeded.Should().BeTrue();

        using var stopping = new CancellationTokenSource();
        _ = server.ListenAsync(stopping.Token);

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("X-Loadout-Token", server.Token);
        var root = $"http://127.0.0.1:{server.Port}";

        (await client.GetAsync(root + "/api/v1/update")).StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a plain dashboard updates nothing");

        server.Update = (fresh, _) =>
        {
            asked.Add(fresh);
            return Task.FromResult<object>(new { isNewer = true });
        };

        (await client.GetAsync(root + "/api/v1/update")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(root + "/api/v1/update?check=now")).StatusCode.Should().Be(HttpStatusCode.OK);

        asked.Should().Equal(false, true);

        await stopping.CancelAsync();
    }

    private UpdatePlan Plan() =>
        new(UpdateKind.Install, InstallChannel.Archive, "0.69.0", "0.70.0", _updates.Payload, _updates.Executable, Started);

    private FakeProcessInspector Live() => new FakeProcessInspector().MarkLive(7070, Started);

    private void Daemon(int hosted = 0)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DaemonNote.PathFor(_paths))!);
        File.WriteAllText(
            DaemonNote.PathFor(_paths),
            JsonSerializer.Serialize(new DaemonState(7070, Started, null, Started, hosted)));
    }

    private UpdateCommand Command(TestConsole console, FakeProcessInspector processes)
    {
        console.Profile.Width = 400;

        return new UpdateCommand(
            _updates,
            console,
            _paths,
            processes,
            new UpdateFinisher(new StubProcessLauncher(string.Empty), processes, new NoOpFilePermissions(), _paths, TimeProvider.System),
            TimeProvider.System);
    }

    private TeamDaemonCommand Daemon(IProcessLauncher launcher, IUpdateService? updates = null) =>
        new(
            secrets: null!,
            client: null!,
            configuration: null!,
            schedules: null!,
            journal: null!,
            new OneCommand.Runner(),
            _paths,
            projects: null!,
            tasks: null!,
            git: null!,
            new FakeProcessInspector(),
            AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(TextWriter.Null), Interactive = InteractionSupport.No }),
            TimeProvider.System,
            AccessibleMode.Off,
            speech: null!,
            teams: null!,
            library: null!,
            workspace: null!,
            agents: null!,
            launcher,
            nominations: null!,
            ideas: null!,
            dumps: null!,
            bin: null!,
            lifetime: null!,
            memory: null!,
            importer: null!,
            sheet: null!,
            updates: updates);

    private static CommandOutput Output() =>
        new(
            AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(TextWriter.Null), Interactive = InteractionSupport.No }),
            new GlobalSettings());

    /// <summary>A release source offering 0.70.0, whose staging writes a file and nothing else.</summary>
    private sealed class Stager(string executable, string payload) : IUpdateService
    {
        public string Executable { get; } = executable;

        public string Payload { get; } = payload;

        public InstallChannel Channel { get; set; } = InstallChannel.Archive;

        public InstallChannel? Staged { get; private set; }

        public bool StageFails { get; set; }

        public Task<OperationResult<UpdateCheck>> CheckAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<UpdateCheck>.Ok(new UpdateCheck(
                "0.69.0", "0.70.0", true, new ReleaseArtifact { Url = "file:///x", Sha256 = "00" }, "What changed.")));

        public Task<OperationResult<string>> ApplyAsync(UpdateCheck check, CancellationToken ct = default) =>
            throw new InvalidOperationException("Studio's update never replaces the binary from the command");

        public Task<OperationResult<string>> StageAsync(UpdateCheck check, InstallChannel channel, CancellationToken ct = default)
        {
            Staged = channel;

            if (StageFails)
            {
                return Task.FromResult(OperationResult<string>.Fail("The download does not match the published SHA-256.", ExitCode.PolicyViolation));
            }

            File.WriteAllText(Payload, "STAGED");

            return Task.FromResult(OperationResult<string>.Ok(Payload));
        }
    }

    /// <summary>A catalogue with 'team run' in it, which starts nothing.</summary>
    private sealed class OneCommand : IStudioCommands
    {
        public IReadOnlyList<StudioCommand> Catalogue { get; } =
        [
            new("team run", "Run", "Teams", "", true, new StudioReach("teams", StudioEffect.Changes), [], []),
        ];

        public List<IReadOnlyList<string>> Started { get; } = [];

        public Task<OperationResult<IPipedProcess>> StartAsync(IReadOnlyList<string> words, CancellationToken ct)
        {
            Started.Add(words);

            return Task.FromResult(OperationResult<IPipedProcess>.Ok(new StubProcessLauncher.StubPipedProcess(string.Empty, 0)));
        }

        /// <summary>The daemon's own catalogue, which nothing here runs.</summary>
        public sealed class Runner : ICommandCatalogue
        {
            public IReadOnlyList<CatalogueEntry> Commands => [];

            public Task<int> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken ct = default) =>
                throw new InvalidOperationException("nothing should run");
        }
    }

    private sealed class NoRunsHere : IRunJournal
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
