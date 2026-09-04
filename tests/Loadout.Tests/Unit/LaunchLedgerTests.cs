using FluentAssertions;
using Loadout.Core.Sessions;
using Loadout.Models.Instructions;
using Loadout.Models.Platform;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What the launcher wrote down about a launch, and what it refused to.
/// </summary>
/// <remarks>
/// <para>
/// Everything here runs against a temporary state directory built from a faked
/// environment, so the ledger under test is never the one on the machine running
/// the suite. A test that appended to a developer's real record would be both a
/// nuisance and a false pass on a machine that already had one.
/// </para>
/// <para>
/// The endings matter more than the beginnings. A start with no end is the
/// ordinary result of closing a terminal, and a reader that treated it as
/// corruption would report nothing on exactly the days somebody wanted to know
/// what had happened.
/// </para>
/// </remarks>
public sealed class LaunchLedgerTests : IDisposable
{
    private const string GitHubTokenShape = "ghp_abcdefghijklmnopqrstuvwxyz0123";

    private readonly string _root;
    private readonly FixedTime _time = new();
    private readonly LaunchLedger _ledger;

    public LaunchLedgerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-ledger-" + Guid.NewGuid().ToString("N"));

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

        _ledger = new LaunchLedger(paths, permissions, _time);
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
    public async Task A_launch_records_what_the_session_was_given()
    {
        await _ledger.RecordStartAsync(Launch("fix the upload path"));

        var read = await _ledger.ReadAsync(DateTimeOffset.MinValue);

        read.Succeeded.Should().BeTrue(read.Error);

        var record = read.Value!.Should().ContainSingle().Subject;

        record.ProjectSlug.Should().Be("starstats");
        record.ProjectName.Should().Be("StarStats");
        record.Agent.Should().Be("claude");
        record.Task.Should().Be("fix the upload path");
        record.Profile.Should().Be("narrow");
        record.Worktree.Should().Be("release");
        record.Mode.Should().Be("implement");

        // The whole point of the ledger: which specialists were composed, and
        // what they cost. Neither is answerable from a transcript.
        record.Specialists.Should().Equal("foundation.change-safety", "language.csharp");
        record.EstimatedTokens.Should().Be(2400);
        record.TokenBudget.Should().Be(12000);
    }

    [Fact]
    public async Task An_ending_joins_to_its_start()
    {
        var id = await _ledger.RecordStartAsync(Launch("fix the upload path"));

        _time.Now = _time.Now.AddMinutes(31);

        await _ledger.RecordEndAsync(id, 0);

        var record = (await _ledger.ReadAsync(DateTimeOffset.MinValue)).Value!
            .Should().ContainSingle().Subject;

        record.IsComplete.Should().BeTrue();
        record.ExitCode.Should().Be(0);
        record.Duration.Should().Be(TimeSpan.FromMinutes(31));
    }

    [Fact]
    public async Task A_launch_that_never_closed_is_still_reported()
    {
        await _ledger.RecordStartAsync(Launch("look into the stall"));

        var record = (await _ledger.ReadAsync(DateTimeOffset.MinValue)).Value!
            .Should().ContainSingle().Subject;

        // Killed, closed terminal, machine shut down. All ordinary, and all of
        // them leave this shape.
        record.IsComplete.Should().BeFalse();
        record.EndedAt.Should().BeNull();
        record.ExitCode.Should().BeNull();
        record.Duration.Should().BeNull();
    }

    [Fact]
    public async Task An_agent_that_never_ran_is_told_apart_from_one_still_running()
    {
        var id = await _ledger.RecordStartAsync(Launch("start something"));

        await _ledger.RecordEndAsync(id, exitCode: null);

        var record = (await _ledger.ReadAsync(DateTimeOffset.MinValue)).Value!
            .Should().ContainSingle().Subject;

        // Both have no exit code. Only the ending tells them apart, which is
        // why the record carries the moment rather than a flag.
        record.IsComplete.Should().BeTrue();
        record.ExitCode.Should().BeNull();
    }

    [Fact]
    public async Task A_task_that_matches_a_credential_pattern_is_not_written_down()
    {
        await _ledger.RecordStartAsync(Launch($"rotate {GitHubTokenShape} everywhere"));

        var record = (await _ledger.ReadAsync(DateTimeOffset.MinValue)).Value!
            .Should().ContainSingle().Subject;

        record.Task.Should().BeNull();
        record.TaskWithheld.Should().Be("GitHub token");

        // The record is one thing; the file is the thing that would leak. The
        // pattern name may be written down, the value may not, and nothing that
        // merely reports on the record can establish that.
        var written = await File.ReadAllTextAsync(_ledger.Path);

        written.Should().NotContain(GitHubTokenShape);
        written.Should().Contain("GitHub token");
    }

    [Fact]
    public async Task Launches_before_the_window_are_left_out()
    {
        await _ledger.RecordStartAsync(Launch("last month"));

        _time.Now = _time.Now.AddDays(40);

        var cutoff = _time.Now.AddDays(-7);

        await _ledger.RecordStartAsync(Launch("this week"));

        var read = await _ledger.ReadAsync(cutoff);

        read.Value!.Should().ContainSingle().Which.Task.Should().Be("this week");
    }

    [Fact]
    public async Task An_ending_whose_start_fell_outside_the_window_is_dropped_rather_than_invented()
    {
        var id = await _ledger.RecordStartAsync(Launch("last month"));

        _time.Now = _time.Now.AddDays(40);

        await _ledger.RecordEndAsync(id, 0);

        var read = await _ledger.ReadAsync(_time.Now.AddDays(-7));

        // The ending is inside the window and its start is not. Attaching it to
        // anything would be attaching it to the wrong launch.
        read.Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task A_line_that_cannot_be_parsed_costs_that_line_and_not_the_file()
    {
        await _ledger.RecordStartAsync(Launch("before"));

        await File.AppendAllTextAsync(_ledger.Path, "{\"Kind\":\"start\",\"Id\":" + Environment.NewLine);

        await _ledger.RecordStartAsync(Launch("after"));

        var read = await _ledger.ReadAsync(DateTimeOffset.MinValue);

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value!.Select(record => record.Task).Should().Equal("before", "after");
    }

    [Fact]
    public async Task A_ledger_that_cannot_be_written_does_not_stop_a_launch()
    {
        // A directory where the file belongs. Nothing can append to it, which is
        // the cheapest way to produce the failure the class promises to swallow.
        Directory.CreateDirectory(_ledger.Path);

        var id = await _ledger.RecordStartAsync(Launch("carry on regardless"));

        id.Should().NotBeEmpty();

        // And the ending must not throw either, since it runs on the way out of
        // a session that has already done its work.
        await _ledger.RecordEndAsync(id, 0);
    }

    private static NewLaunch Launch(string task) => new(
        "starstats",
        "StarStats",
        "claude",
        task,
        "narrow",
        "release",
        Instructions());

    private static EffectiveInstructions Instructions() => new(
        "implement",
        [
            Selection("foundation.change-safety", SpecialistKind.Foundation, SpecialistTrigger.Foundation),
            Selection("language.csharp", SpecialistKind.Language, SpecialistTrigger.Mode),
        ],
        [],
        [],
        new InstructionContextBudget(9600, 2400, 12000, 80));

    private static SpecialistSelection Selection(
        string id,
        SpecialistKind kind,
        SpecialistTrigger trigger) =>
        new(
            new SpecialistDocument(
                id,
                kind,
                id,
                "summary",
                SpecialistActivation.None,
                "body",
                Bytes: 400),
            trigger,
            "because",
            Confidence: 100);

    /// <summary>A clock the test moves, so windows can be asserted without waiting.</summary>
    private sealed class FixedTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
