using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Models.Platform;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The bin removed runs and teams wait in before they go for good.
/// </summary>
/// <remarks>
/// <para>
/// Against a real directory, for the reason <see cref="RunForgettingTests"/>
/// gives: what is being asserted is where files are on a disk, and a double
/// that said so would be asserting its own opinion.
/// </para>
/// <para>
/// The refusals matter as much as the moves. The bin exists so that removing
/// the wrong thing can be undone, and a restore that quietly replaced a run or
/// a team already there would be a second way of losing the thing it was
/// meant to protect.
/// </para>
/// </remarks>
public sealed class TeamBinTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "loadout-bin-" + Guid.NewGuid().ToString("N")[..8]);

    private const string Started =
        """{"at":"2026-09-16T12:00:00+00:00","run":"r","node":null,"kind":"run.started","data":{"team":"bug-hunt","goal":"a goal","autonomy":"supervised"}}""";

    private const string Launched =
        """{"at":"2026-09-16T12:00:02+00:00","run":"r","node":"implementer/1","kind":"node.launched","data":{"role":"role.implementer","worktree":"teams/r/implementer-1"}}""";

    private const string Finished =
        """{"at":"2026-09-16T12:05:00+00:00","run":"r","node":null,"kind":"run.finished","data":{"ended":"done","cost":0.2,"rounds":1,"merged":[]}}""";

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or DirectoryNotFoundException)
        {
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_forgotten_run_goes_to_the_bin_with_a_record_of_what_it_was()
    {
        var journal = Journal();
        var directory = Write("20260916-1200-aaaa", Started, Launched, Finished);

        var gone = journal.Forget("20260916-1200-aaaa");

        gone.Succeeded.Should().BeTrue(gone.Error);
        Directory.Exists(directory).Should().BeFalse("it has left the runs");

        var binned = Path.Combine(Bin().Root, "runs", "20260916-1200-aaaa");

        gone.Value!.Bin.Should().Be(binned);
        File.Exists(Path.Combine(binned, "journal.jsonl")).Should().BeTrue("everything it wrote went with it");

        using var record = JsonDocument.Parse(File.ReadAllText(Path.Combine(binned, TeamBin.RecordName)));

        record.RootElement.GetProperty("kind").GetString().Should().Be("run");
        record.RootElement.GetProperty("run").GetString().Should().Be("20260916-1200-aaaa");
        record.RootElement.GetProperty("team").GetString().Should().Be("bug-hunt");
        record.RootElement.GetProperty("removed").GetDateTimeOffset().Should().Be(Now);
        record.RootElement.GetProperty("unmerged")[0].GetString().Should().Be("teams/r/implementer-1");

        var entry = Bin().List().Should().ContainSingle().Subject;

        entry.Kind.Should().Be(BinKind.Run);
        entry.Name.Should().Be("20260916-1200-aaaa");
        entry.Unmerged.Should().Equal("teams/r/implementer-1");
        entry.Bytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_run_restored_from_the_bin_reads_as_it_did_and_leaves_no_record_behind()
    {
        var journal = Journal();
        var directory = Write("20260916-1200-bbbb", Started, Finished);

        journal.Forget("20260916-1200-bbbb").Succeeded.Should().BeTrue();

        var restored = Bin().RestoreRun("20260916-1200-bbbb", journal.DirectoryOf("20260916-1200-bbbb"));

        restored.Succeeded.Should().BeTrue(restored.Error);
        journal.Summarise("20260916-1200-bbbb").Value!.Team.Should().Be("bug-hunt");

        // The record belongs to the bin. Brought back inside the run, it would
        // say the run was binned on a date it no longer is.
        File.Exists(Path.Combine(directory, TeamBin.RecordName)).Should().BeFalse();
        Bin().List().Should().BeEmpty();
    }

    [Fact]
    public void A_run_is_not_restored_over_one_with_the_same_identifier()
    {
        var journal = Journal();

        Write("20260916-1200-cccc", Started, Finished);
        journal.Forget("20260916-1200-cccc").Succeeded.Should().BeTrue();

        var directory = Write("20260916-1200-cccc", Started);

        var refused = Bin().RestoreRun("20260916-1200-cccc", directory);

        refused.Failed.Should().BeTrue();
        refused.ExitCode.Should().Be(ExitCode.InvalidArguments);
        Bin().List().Should().ContainSingle("a refusal leaves the binned copy where it was");
        File.ReadAllLines(Path.Combine(directory, "journal.jsonl")).Should().Equal(Started);
    }

    [Fact]
    public void A_dry_run_restore_says_yes_and_moves_nothing()
    {
        var journal = Journal();

        Write("20260916-1200-dddd", Started, Finished);
        journal.Forget("20260916-1200-dddd").Succeeded.Should().BeTrue();

        Bin().RestoreRun("20260916-1200-dddd", journal.DirectoryOf("20260916-1200-dddd"), dryRun: true)
            .Succeeded.Should().BeTrue();

        Directory.Exists(journal.DirectoryOf("20260916-1200-dddd")).Should().BeFalse();
        Bin().List().Should().ContainSingle();
    }

    [Fact]
    public void A_run_that_is_not_in_the_bin_says_so()
    {
        var missing = Bin().RestoreRun("20260916-1200-eeee", Journal().DirectoryOf("20260916-1200-eeee"));

        missing.Failed.Should().BeTrue();
        missing.ExitCode.Should().Be(ExitCode.ProjectNotFound);
    }

    [Fact]
    public void A_removed_team_comes_back_to_where_it_was_the_newest_copy_first()
    {
        var file = Team("quick-review", "name: quick-review\n# first\n");

        Bin().PutTeam("quick-review", file, Now.AddDays(-2)).Succeeded.Should().BeTrue();
        File.Exists(file).Should().BeFalse();

        Team("quick-review", "name: quick-review\n# second\n");
        Bin().PutTeam("quick-review", file, Now.AddDays(-1)).Succeeded.Should().BeTrue();

        Bin().List().Should().HaveCount(2).And.OnlyContain(one => one.Kind == BinKind.Team);

        var restored = Bin().RestoreTeam("quick-review");

        restored.Succeeded.Should().BeTrue(restored.Error);
        File.ReadAllText(file).Should().Contain("# second", "the one removed most recently is the one wanted back");
        Bin().List().Should().ContainSingle().Which.Removed.Should().Be(Now.AddDays(-2));
    }

    [Fact]
    public void A_team_is_not_restored_over_a_file_already_there()
    {
        var file = Team("quick-review", "name: quick-review\n# binned\n");

        Bin().PutTeam("quick-review", file, Now).Succeeded.Should().BeTrue();

        Team("quick-review", "name: quick-review\n# new\n");

        var refused = Bin().RestoreTeam("quick-review");

        refused.Failed.Should().BeTrue();
        refused.ExitCode.Should().Be(ExitCode.InvalidArguments);
        File.ReadAllText(file).Should().Contain("# new");
        Bin().List().Should().ContainSingle();
    }

    [Fact]
    public void Only_what_has_been_in_the_bin_longer_than_it_is_kept_is_swept()
    {
        Bin().PutTeam("old-team", Team("old-team", "name: old-team\n"), Now.AddDays(-31)).Succeeded.Should().BeTrue();
        Bin().PutTeam("new-team", Team("new-team", "name: new-team\n"), Now.AddDays(-29)).Succeeded.Should().BeTrue();

        var gone = Bin().Sweep(Now, TeamBin.DefaultDays);

        gone.Select(one => one.Name).Should().Equal("old-team");
        Bin().List().Select(one => one.Name).Should().Equal("new-team");
    }

    [Fact]
    public void Zero_days_keeps_everything_until_it_is_emptied_by_hand()
    {
        Bin().PutTeam("old-team", Team("old-team", "name: old-team\n"), Now.AddYears(-1)).Succeeded.Should().BeTrue();

        Bin().Sweep(Now, 0).Should().BeEmpty();
        Bin().List().Single().DaysLeft(Now, 0).Should().BeNull();

        TeamBin.Days(null).Should().Be(30, "nobody setting it is the default");
        TeamBin.Days(0).Should().Be(0);
    }

    [Fact]
    public void Emptying_by_age_chooses_only_the_older_entries()
    {
        Bin().PutTeam("old-team", Team("old-team", "name: old-team\n"), Now.AddDays(-10)).Succeeded.Should().BeTrue();
        Bin().PutTeam("new-team", Team("new-team", "name: new-team\n"), Now.AddDays(-1)).Succeeded.Should().BeTrue();

        Bin().Choose(Now, TimeSpan.FromDays(7)).Select(one => one.Name).Should().Equal("old-team");
        Bin().Choose(Now, null).Should().HaveCount(2);
    }

    [Fact]
    public void Days_left_rounds_up_so_something_still_there_never_says_none()
    {
        var entry = new BinEntry(BinKind.Run, "r", "d", Now.AddDays(-29).AddHours(-23), "t", null, [], 0);

        entry.DaysLeft(Now, 30).Should().Be(1);
        entry.Expired(Now, 30).Should().BeFalse();
        entry.Expired(Now.AddHours(2), 30).Should().BeTrue();
    }

    [Fact]
    public void Nothing_outside_the_bin_is_deleted_by_it()
    {
        var elsewhere = Directory.CreateDirectory(Path.Combine(_root, "elsewhere")).FullName;

        var refused = Bin().Delete(new BinEntry(BinKind.Run, "r", elsewhere, Now, "t", null, [], 0));

        refused.Failed.Should().BeTrue();
        Directory.Exists(elsewhere).Should().BeTrue();
    }

    private string Write(string runId, params string[] lines)
    {
        var directory = Journal().DirectoryOf(runId);

        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, "journal.jsonl"), lines);

        return directory;
    }

    private string Team(string name, string text)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "workspace", "global", "teams")).FullName;
        var path = Path.Combine(directory, name + ".yaml");

        File.WriteAllText(path, text);

        return path;
    }

    private LinuxPaths Paths()
    {
        var paths = new LinuxPaths(
            new FakeEnvironmentProvider(
                Path.Combine(_root, "home"),
                new Dictionary<string, string>
                {
                    ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                    ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                    ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                    ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
                }),
            new NoOpFilePermissions(),
            new HostPlatform(
                HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST"));

        paths.EnsureDirectoriesExist();

        return paths;
    }

    private RunJournal Journal() => new(Paths(), new Clock(Now));

    private TeamBin Bin() => new(Paths());

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
