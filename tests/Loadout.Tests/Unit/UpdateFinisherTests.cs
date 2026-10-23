using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams.Daemon;
using Loadout.Core.Updates;
using Loadout.Models.Platform;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The helper Studio's update starts once the daemon has gone: it replaces
/// the binary or runs the MSI, starts the new daemon, and for an archive
/// install puts the old binary back when the new daemon does not come up.
/// </summary>
public sealed class UpdateFinisherTests : IDisposable
{
    private static readonly DateTimeOffset Started = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-finish-" + Guid.NewGuid().ToString("N"));
    private readonly IPlatformPaths _paths;
    private readonly FakeProcessInspector _processes = new();
    private readonly StringWriter _log = new();
    private readonly string _executable;
    private readonly string _payload;

    public UpdateFinisherTests()
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
        _executable = Path.Combine(_root, "install", "loadout");
        File.WriteAllText(_executable, "OLD BINARY");

        Directory.CreateDirectory(Path.Combine(_root, "staged"));
        _payload = Path.Combine(_root, "staged", "loadout");
        File.WriteAllText(_payload, "NEW BINARY");
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

    [Fact]
    public async Task An_archive_update_puts_the_new_binary_in_place_keeps_the_old_and_starts_the_daemon()
    {
        var launcher = new Scripted(this, up: _ => true);
        var plan = await PlanAsync(UpdateKind.Install, InstallChannel.Archive);

        var done = await Finisher(launcher).RunAsync(plan, _log);

        done.Should().BeTrue(_log.ToString());
        File.ReadAllText(_executable).Should().Be("NEW BINARY");
        File.ReadAllText(InstallChannels.PreviousOf(_executable)).Should().Be("OLD BINARY", "Roll back needs it");
        launcher.Started.Should().ContainSingle().Which.Executable.Should().Be("loadout-daemon");
        launcher.Started[0].Arguments.Should().Equal("daemon", "--port", "7777");
        File.Exists(plan).Should().BeFalse("a plan left behind would be carried out again at the next restart");
        _log.ToString().Should().Contain("updated: the daemon is serving again on 0.70.0");
    }

    [Fact]
    public async Task A_new_daemon_that_does_not_start_has_the_old_binary_put_back_and_started()
    {
        // The first start dies at once; the second, on the old binary, comes up.
        var launcher = new Scripted(this, up: count => count > 1);
        var plan = await PlanAsync(UpdateKind.Install, InstallChannel.Archive);

        var done = await Finisher(launcher).RunAsync(plan, _log);

        done.Should().BeFalse("the update did not take");
        File.ReadAllText(_executable).Should().Be("OLD BINARY");
        launcher.Started.Should().HaveCount(2);
        _log.ToString().Should().Contain("exited as it started")
            .And.Contain("previous binary is being put back")
            .And.Contain("put back: the daemon is serving again on 0.69.0");
    }

    [Fact]
    public async Task An_msi_install_runs_the_installer_quietly_while_the_daemon_is_down()
    {
        File.WriteAllText(_payload, "MSI");
        var launcher = new Scripted(this, up: _ => true);
        var plan = await PlanAsync(UpdateKind.Install, InstallChannel.Installer);

        var done = await Finisher(launcher).RunAsync(plan, _log);

        done.Should().BeTrue(_log.ToString());

        var ran = launcher.Ran.Should().ContainSingle().Subject;
        ran.Executable.Should().Be("msiexec");
        ran.Arguments.Should().ContainInOrder("/i", _payload, "/qn", "/norestart");
        ran.Arguments.Should().Contain("/l*v", "a failure is only reported in the log, so the MSI's own log is kept");

        File.ReadAllText(_executable).Should().Be("OLD BINARY", "the MSI replaces it, not the helper");
        launcher.Started.Should().ContainSingle();
    }

    [Fact]
    public async Task A_failed_msi_is_written_to_the_log_and_the_daemon_still_comes_back()
    {
        File.WriteAllText(_payload, "MSI");
        var launcher = new Scripted(this, up: _ => true) { InstallerExit = 1603 };
        var plan = await PlanAsync(UpdateKind.Install, InstallChannel.Installer);

        var done = await Finisher(launcher).RunAsync(plan, _log);

        done.Should().BeFalse();
        launcher.Started.Should().ContainSingle("Studio should come back, on the version it was");
        _log.ToString().Should().Contain("1603").And.Contain("msiexec.log");
    }

    [Fact]
    public async Task A_roll_back_puts_the_previous_binary_back_and_drops_the_newer_one()
    {
        File.WriteAllText(InstallChannels.PreviousOf(_executable), "OLDER BINARY");
        var launcher = new Scripted(this, up: _ => true);
        var plan = await PlanAsync(UpdateKind.RollBack, InstallChannel.Archive, payload: null);

        var done = await Finisher(launcher).RunAsync(plan, _log);

        done.Should().BeTrue(_log.ToString());
        File.ReadAllText(_executable).Should().Be("OLDER BINARY");
        File.Exists(InstallChannels.PreviousOf(_executable)).Should().BeFalse(
            "a second Roll back must not roll forward again");
    }

    [Fact]
    public async Task A_daemon_that_has_not_gone_is_left_alone_and_nothing_is_replaced()
    {
        _processes.MarkLive(7070, Started);
        var launcher = new Scripted(this, up: _ => true);
        var plan = await PlanAsync(UpdateKind.Install, InstallChannel.Archive);

        var done = await Finisher(launcher, departure: TimeSpan.FromMilliseconds(150)).RunAsync(plan, _log);

        done.Should().BeFalse();
        File.ReadAllText(_executable).Should().Be("OLD BINARY");
        launcher.Started.Should().BeEmpty("starting a second daemon beside the first would fire every schedule twice");
    }

    [Fact]
    public async Task Without_a_plan_it_changes_nothing()
    {
        var launcher = new Scripted(this, up: _ => true);

        var done = await Finisher(launcher).RunAsync(Path.Combine(_root, "none.json"), _log);

        done.Should().BeFalse();
        launcher.Started.Should().BeEmpty();
        File.ReadAllText(_executable).Should().Be("OLD BINARY");
    }

    [Fact]
    public void Channel_detection_follows_the_folder_the_msi_recorded()
    {
        var bin = Path.Combine(_root, "Programs", "loadout", "bin");
        var installed = Path.Combine(bin, "loadout.exe");

        InstallChannels.Detect(installed, bin).Should().Be(InstallChannel.Installer);
        InstallChannels.Detect(installed, bin + Path.DirectorySeparatorChar).Should().Be(InstallChannel.Installer);
        InstallChannels.Detect(installed, bin.ToUpperInvariant()).Should().Be(InstallChannel.Installer,
            "Windows paths do not care about case");

        InstallChannels.Detect(Path.Combine(_root, "tools", "loadout.exe"), bin).Should().Be(
            InstallChannel.Archive, "a copy elsewhere is updated its own way, whatever else the MSI installed");
        InstallChannels.Detect(installed, null).Should().Be(InstallChannel.Archive);
        InstallChannels.Detect(string.Empty, bin).Should().Be(InstallChannel.Archive);
    }

    [Fact]
    public void Restore_with_nothing_kept_says_so_and_leaves_the_binary_alone()
    {
        var restored = UpdateFiles.Restore(_executable);

        restored.Failed.Should().BeTrue();
        File.ReadAllText(_executable).Should().Be("OLD BINARY");
    }

    [Fact]
    public async Task A_plan_reads_back_as_it_was_written()
    {
        var path = await PlanAsync(UpdateKind.Install, InstallChannel.Installer);

        var read = UpdatePlan.Read(path);

        read.Should().NotBeNull();
        read!.Kind.Should().Be(UpdateKind.Install);
        read.Channel.Should().Be(InstallChannel.Installer);
        read.Version.Should().Be("0.70.0");
        read.Daemon.Should().Equal("loadout-daemon", "daemon", "--port", "7777");
        File.ReadAllText(path).Should().Contain("\"installer\"", "kept readable by a person looking at it");

        UpdatePlan.Clear(path).Should().BeTrue();
        UpdatePlan.Read(path).Should().BeNull();
    }

    private UpdateFinisher Finisher(Scripted launcher, TimeSpan? departure = null) =>
        new(launcher, _processes, new NoOpFilePermissions(), _paths, TimeProvider.System)
        {
            Departure = departure ?? TimeSpan.FromSeconds(5),
            Arrival = TimeSpan.FromSeconds(5),
            Glance = TimeSpan.FromMilliseconds(20),
        };

    private async Task<string> PlanAsync(UpdateKind kind, InstallChannel channel, string? payload = "default")
    {
        var path = UpdatePlan.PathFor(_paths);

        await new UpdatePlan(
                kind,
                channel,
                "0.69.0",
                kind == UpdateKind.Install ? "0.70.0" : null,
                payload == "default" ? _payload : payload,
                _executable,
                Started,
                Daemon: ["loadout-daemon", "daemon", "--port", "7777"],
                DaemonPid: 7070,
                DaemonStartedAt: Started)
            .WriteAsync(path);

        return path;
    }

    /// <summary>
    /// Runs nothing: the installer answers with a set exit code, and a daemon
    /// started in the background comes up, by writing its note, or dies at once.
    /// </summary>
    private sealed class Scripted(UpdateFinisherTests test, Func<int, bool> up) : IProcessLauncher
    {
        public List<ProcessRequest> Ran { get; } = [];

        public List<ProcessRequest> Started { get; } = [];

        public int InstallerExit { get; init; }

        public Task<OperationResult<ProcessOutcome>> RunAsync(ProcessRequest request, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            Ran.Add(request);

            return Task.FromResult(OperationResult<ProcessOutcome>.Ok(new ProcessOutcome(InstallerExit, string.Empty, string.Empty)));
        }

        public OperationResult<BackgroundProcess> StartBackground(ProcessRequest request)
        {
            Started.Add(request);

            var process = new BackgroundProcess(8000 + Started.Count, UpdateFinisherTests.Started.AddMinutes(Started.Count));

            if (up(Started.Count))
            {
                test._processes.MarkLive(process.Pid, process.StartedAt);
                Directory.CreateDirectory(Path.GetDirectoryName(DaemonNote.PathFor(test._paths))!);
                File.WriteAllText(
                    DaemonNote.PathFor(test._paths),
                    JsonSerializer.Serialize(new DaemonState(process.Pid, process.StartedAt, null, process.StartedAt)));
            }

            return OperationResult<BackgroundProcess>.Ok(process);
        }

        public Task<OperationResult<int>> RunInteractiveAsync(ProcessRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<IPipedProcess>> StartPipedAsync(ProcessRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public OperationResult StartDetached(ProcessRequest request) => throw new NotSupportedException();
    }
}
