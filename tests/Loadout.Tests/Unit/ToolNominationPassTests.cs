using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Core.Instructions;
using Loadout.Core.Teams;
using Loadout.Core.Tools;
using Loadout.Core.Workspace;
using Loadout.Models.Instructions;
using Loadout.Models.Results;
using Loadout.Models.Teams;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What calls the nominator: a run-finished schedule firing, and tool-works
/// starting on its own, each before its team is started.
/// </summary>
public sealed class ToolNominationPassTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private const string Lesson = "When the disk fills, docker system prune clears the build layers.";

    private readonly ToolStoreFixture _store = new();

    public void Dispose() => _store.Dispose();

    [Fact]
    public async Task A_fired_run_finished_schedule_files_a_nomination_from_a_finished_run()
    {
        FinishedRun("20260923-1000-a001", "alpha", "docker system prune --force");
        var schedule = Schedule("tools-on-finish", "tidy-up", ScheduleService.RunFinishedEvent);
        var journal = new RunJournal(_store.Paths);
        var runs = journal.List(50).Select(journal.Summarise).Select(one => one.Value!).ToList();

        ScheduleService.RunFinished(schedule, runs, Now).Fire.Should().BeTrue("the fixture run is one it has not seen");

        var nominated = await Pass().BeforeAsync(schedule);

        nominated.Should().ContainSingle(one => one.Nomination.Rule == 4 && one.Filed!.Succeeded,
            "the lesson in memory names the command the finished run passed with");
        Directory.EnumerateFiles(Path.Combine(_store.Paths.Paths.State, "tools", "inbox")).Should().ContainSingle();
    }

    [Fact]
    public async Task Tool_works_on_its_own_schedule_nominates_first()
    {
        FinishedRun("20260923-1000-a001", "alpha", "docker system prune --force");

        (await Pass().BeforeAsync(Schedule("tool-works-daily", ScheduleService.ToolWorksTeam, string.Empty)))
            .Should().ContainSingle(one => one.Filed!.Succeeded);
    }

    [Fact]
    public async Task Any_other_schedule_nominates_nothing()
    {
        FinishedRun("20260923-1000-a001", "alpha", "docker system prune --force");

        (await Pass().BeforeAsync(Schedule("nightly", "alpha", "commit"))).Should().BeEmpty();
        Directory.Exists(Path.Combine(_store.Paths.Paths.State, "tools", "inbox")).Should().BeFalse();
    }

    [Fact]
    public async Task An_unreadable_run_folder_does_not_stop_the_pass()
    {
        FinishedRun("20260923-1000-a001", "alpha", "docker system prune --force");
        FinishedRun("20260923-1100-b002", "beta", "git gc --aggressive");
        var locked = new RunJournal(_store.Paths).DirectoryOf("20260923-1100-b002");

        Unlistable(locked, true);

        try
        {
            FluentActions.Invoking(() => Directory.EnumerateFiles(locked).ToList())
                .Should().Throw<UnauthorizedAccessException>("the fixture has to be a folder that cannot be listed");

            var nominated = await Pass().BeforeAsync(Schedule("tools-on-finish", "tidy-up", ScheduleService.RunFinishedEvent));

            nominated.Should().ContainSingle(one => one.Filed!.Succeeded,
                "the run that can be read is still read, and only the one that cannot is passed over");
        }
        finally
        {
            Unlistable(locked, false);
        }
    }

    /// <summary>Takes away, or gives back, the right to list a folder's contents.</summary>
    private static void Unlistable(string directory, bool locked)
    {
        if (OperatingSystem.IsWindows())
        {
            var info = new DirectoryInfo(directory);
            var security = info.GetAccessControl();
            var rule = new System.Security.AccessControl.FileSystemAccessRule(
                System.Security.Principal.WindowsIdentity.GetCurrent().User!,
                System.Security.AccessControl.FileSystemRights.ListDirectory,
                System.Security.AccessControl.AccessControlType.Deny);

            if (locked)
            {
                security.AddAccessRule(rule);
            }
            else
            {
                security.RemoveAccessRule(rule);
            }

            info.SetAccessControl(security);
        }
        else
        {
            // Execute alone: a file whose name is known can still be opened,
            // but the folder cannot be listed.
            File.SetUnixFileMode(directory, locked
                ? UnixFileMode.UserExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private ToolNominationPass Pass()
    {
        var workspace = new WorkspaceManager(
            _store.Paths, new FakeGit(_store.Paths.Paths.State), new YamlStore(new NoOpFilePermissions()), TimeProvider.System);

        return new ToolNominationPass(
            _store.Registry().Registry,
            new RunJournal(_store.Paths),
            new RemedyBook(_store.Paths),
            _store.Paths,
            new LessonMemory(Lesson),
            new FakeProjects("demo", _store.Paths.Paths.State),
            workspace);
    }

    private static TeamSchedule Schedule(string id, string team, string on) => new()
    {
        Id = id,
        Team = team,
        Project = "demo",
        Goal = "Look at what finished.",
        On = on,
        Enabled = true,
        LastCommit = "2026-09-01T00:00:00.0000000+00:00",
    };

    /// <summary>A finished run of a team, with one report carrying a command that passed.</summary>
    private void FinishedRun(string runId, string team, string command)
    {
        var journal = new RunJournal(_store.Paths);
        var directory = journal.DirectoryOf(runId);
        Directory.CreateDirectory(directory);

        File.WriteAllLines(Path.Combine(directory, "journal.jsonl"),
        [
            """{"at":"2026-09-23T10:00:00+00:00","kind":"run.started","data":{"team":""" + "\"" + team + "\""
                + ""","goal":"keep it up","autonomy":"autonomous"}}""",
            """{"at":"2026-09-23T10:30:00+00:00","kind":"run.finished","data":{"ended":"done","outcome":"done"}}""",
        ]);

        var report = new Report("remediator", ReportStatus.Done, "Fixed it.", [],
            [new ReportEvidence(EvidenceKind.Command, command, EvidenceResult.Pass, "exit 0")], []);
        File.WriteAllText(Path.Combine(directory, "report-remediator-1.json"), ReportReader.Write(report));
    }

    /// <summary>Memory holding one lesson topic, and nothing else a nominator asks for.</summary>
    private sealed class LessonMemory(string lesson) : IMemoryService
    {
        public Task<OperationResult<IReadOnlyList<MemoryTopic>>> ListAsync(
            string workspaceRoot, string slug, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<MemoryTopic>>.Ok(
            [
                new MemoryTopic("disk-fills", "disk-fills.md", "The build disk fills.", MemoryKind.Lesson,
                    [lesson], [], 0, Now),
                new MemoryTopic("unrelated", "unrelated.md", "Not a lesson.", MemoryKind.Project,
                    ["docker system prune is mentioned here too, but this is not a lesson."], [], 0, Now),
            ]));

        public Task<OperationResult<MemoryAudit>> AuditAsync(
            string workspaceRoot, string slug, int staleMonths = 6, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<MemoryTopic>> WriteAsync(
            string workspaceRoot, string slug, string name, string description, MemoryKind kind,
            IReadOnlyList<string> facts, bool acknowledgedSimilar = false,
            MemoryScope scope = MemoryScope.Project, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public OperationResult ValidateWrite(string name, string description, IReadOnlyList<string> facts) =>
            throw new NotSupportedException();

        public Task<OperationResult> RebuildIndexAsync(string workspaceRoot, string slug, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<MemoryCleanup>> CleanAsync(
            string workspaceRoot, string slug, bool apply, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public IReadOnlyList<string> CleanupPaths(string workspaceRoot, string slug) =>
            throw new NotSupportedException();

        public Task<OperationResult<string?>> ReadIndexAsync(string workspaceRoot, string slug, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
