using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Diagnostics;
using Loadout.Models.Platform;
using Loadout.Models.Teams;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Whether anything is firing the schedules, as doctor reports it.
/// </summary>
/// <remarks>
/// <para>
/// The finding worth having is not "the daemon is not running". It is "you
/// wrote down what you wanted and it has been quietly not happening", which
/// is a machine somebody has stopped trusting without knowing why.
/// </para>
/// <para>
/// The other half is not crying wolf. A person with no schedules is told
/// nothing at all, because a report that warns about an optional thing nobody
/// opted into teaches people to skim past the warnings that matter.
/// </para>
/// </remarks>
public sealed class DaemonDoctorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-doc-" + Guid.NewGuid().ToString("N"));
    private readonly IPlatformPaths _paths;
    private readonly IScheduleService _schedules;

    public DaemonDoctorTests()
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

        _schedules = new ScheduleService(_paths, new YamlStore(new NoOpFilePermissions()));
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

    private async Task ScheduleAsync() =>
        (await _schedules.SaveAsync(new TeamSchedule
        {
            Id = "nightly",
            Project = "demo",
            Team = "iterating-project",
            Goal = "Run the suite.",
            Autonomy = "autonomous",
            At = new TimeOnly(3, 0),
        })).Succeeded.Should().BeTrue();

    private void Note(bool alive)
    {
        var path = DaemonNote.PathFor(_paths);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        File.WriteAllText(path, JsonSerializer.Serialize(new DaemonState(
            alive ? 1234 : 4321,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            "http://127.0.0.1:51999/?token=abc",
            DateTimeOffset.UtcNow.AddMinutes(-5))));
    }

    private Task<IReadOnlyList<DiagnosticCheck>> AskAsync(bool alive) =>
        new DaemonDiagnosticContributor(_schedules, _paths, new FakeProcessInspector { Running = alive })
            .ContributeAsync();

    [Fact]
    public async Task A_machine_with_nothing_scheduled_is_told_nothing()
    {
        // Not having opted into an optional thing is not a degradation, and a
        // report that says it is teaches people to skim.
        (await AskAsync(alive: false)).Should().BeEmpty();
    }

    [Fact]
    public async Task Schedules_with_nothing_to_fire_them_are_a_warning()
    {
        await ScheduleAsync();

        var checks = await AskAsync(alive: false);

        checks.Should().ContainSingle()
            .Which.Severity.Should().Be(DiagnosticSeverity.Warning);

        checks[0].Detail.Should().Contain("1 schedule(s)").And.Contain("loadout team daemon");
    }

    [Fact]
    public async Task A_daemon_that_went_away_is_said_in_the_same_line()
    {
        // A machine that restarted has a note left behind. It is not a fault,
        // and two lines about one absence is one too many.
        await ScheduleAsync();
        Note(alive: false);

        var checks = await AskAsync(alive: false);

        checks.Should().ContainSingle()
            .Which.Detail.Should().Contain("is not any more");
    }

    [Fact]
    public async Task A_running_one_says_where_its_dashboard_is()
    {
        await ScheduleAsync();
        Note(alive: true);

        var checks = await AskAsync(alive: true);

        checks.Should().ContainSingle();
        checks[0].Severity.Should().Be(DiagnosticSeverity.Info);
        checks[0].Detail.Should().Contain("http://127.0.0.1:51999");
    }

    /// <summary>
    /// A held daemon looks exactly like a working one from outside: it is
    /// running and it serves the page. Only this line says nothing fires.
    /// </summary>
    [Fact]
    public async Task A_paused_one_says_that_nothing_fires()
    {
        await ScheduleAsync();
        Note(alive: true);
        await DaemonControl.PauseAsync(_paths);

        var checks = await AskAsync(alive: true);

        checks.Should().ContainSingle()
            .Which.Detail.Should().Contain("Paused").And.Contain("loadout team daemon resume");

        DaemonControl.Resume(_paths);

        (await AskAsync(alive: true)).Single().Detail.Should().NotContain("Paused");
    }

    [Fact]
    public async Task A_running_one_is_reported_even_with_nothing_scheduled()
    {
        // The other direction of the same courtesy: somebody who started it
        // should be able to see that it is there.
        Note(alive: true);

        var checks = await AskAsync(alive: true);

        checks.Should().ContainSingle().Which.Detail.Should().Contain("Running since");
    }

    [Fact]
    public void A_note_nobody_can_read_is_the_same_as_no_note()
    {
        var path = DaemonNote.PathFor(_paths);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "this is not json");

        DaemonNote.Read(_paths).Should().BeNull();
    }

    private sealed class FakeProcessInspector : IProcessInspector
    {
        public bool Running { get; init; }

        public int CurrentProcessId => 1234;

        public DateTimeOffset CurrentProcessStartedAt => DateTimeOffset.UtcNow.AddMinutes(-5);

        public bool IsRunning(int processId, DateTimeOffset startedAt) => Running;
    }
}
