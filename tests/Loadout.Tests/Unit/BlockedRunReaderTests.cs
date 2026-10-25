using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Models.Platform;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Reading blocked runs off a disk, and writing down what was decided about them.
/// </summary>
/// <remarks>
/// Against a real directory, because the decision lives in the run's own
/// journal and what is being asserted is that a line reached it and folds back.
/// </remarks>
public sealed class BlockedRunReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "loadout-blocked-" + Guid.NewGuid().ToString("N")[..8]);

    private static string Started(string? follows = null) =>
        """{"at":"2026-10-25T09:00:00+00:00","run":"r","node":null,"kind":"run.started","data":{"team":"iterating-project","goal":"Fix the login","autonomy":"autonomous","rounds":3,"project":"loadout-cli","task":"fix-login","budget":5.0"""
        + (follows is null ? string.Empty : $",\"follows\":\"{follows}\"")
        + "}}";

    private static string Finished(string ended) =>
        "{\"at\":\"2026-10-25T10:00:00+00:00\",\"run\":\"r\",\"node\":null,\"kind\":\"run.finished\",\"data\":{\"ended\":\""
        + ended + "\",\"cost\":4.8,\"rounds\":3,\"merged\":[]}}";

    private const string Stopped =
        """{"at":"2026-10-25T10:00:00+00:00","run":"r","node":null,"kind":"run.finished","data":{"ended":"stopped by request","cost":1.0,"rounds":1,"merged":[]}}""";

    /// <remarks>Written the way the runner writes the lead's final report, so the fixture cannot drift from it.</remarks>
    private static string Report => ReportReader.Write(new Loadout.Models.Teams.Report(
        "lead",
        Loadout.Models.Teams.ReportStatus.Blocked,
        "Got as far as the schema.",
        [],
        [],
        [],
        Blocker: new Loadout.Models.Teams.ReportBlocker("no test database", "a connection string"),
        Next: "Point it at a database."));

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
    public void Lists_runs_that_ended_short_and_leaves_out_a_stopped_one()
    {
        var journal = Journal();
        Write(journal, "20261025-0900-aaaa", Started(), Finished("round limit: 3 rounds"));
        Write(journal, "20261025-0901-bbbb", Started(), Finished("failed"));
        Write(journal, "20261025-0902-cccc", Started(), Stopped);
        Write(journal, "20261025-0903-dddd", Started(), Finished("done"));

        var listed = new BlockedRunReader(journal).List();

        listed.Select(one => one.Run.RunId).Should().BeEquivalentTo("20261025-0900-aaaa", "20261025-0901-bbbb");
        listed.Should().OnlyContain(one => one.Suggested.Reason.Length > 0);
    }

    [Fact]
    public void A_run_whose_process_has_gone_is_listed_as_dead()
    {
        var journal = Journal();
        Write(journal, "20261025-0900-aaaa", Started());

        new BlockedRunReader(journal).List().Should().BeEmpty("nothing said its process has gone");

        var listed = new BlockedRunReader(journal, dead: _ => true).List();

        listed.Should().ContainSingle().Which.Kind.Should().Be("dead");
    }

    [Fact]
    public void Reads_the_leads_blocker_from_its_final_report()
    {
        var journal = Journal();
        var directory = Write(journal, "20261025-0900-aaaa", Started(), Finished("blocked"));
        File.WriteAllText(Path.Combine(directory, "final-report.json"), Report);

        var found = new BlockedRunReader(journal).Find("20261025-0900-aaaa");

        found.Succeeded.Should().BeTrue(found.Error);
        found.Value!.Lead!.Blocker.Should().Be("no test database");
        found.Value.Suggested.Action.Should().Be(BlockedAction.CarryOn);
        found.Value.Suggested.Reason.Should().Contain("a connection string");
    }

    [Fact]
    public void A_recorded_decision_folds_back_and_takes_the_run_out_of_the_list()
    {
        var journal = Journal();
        Write(journal, "20261025-0900-aaaa", Started(), Finished("round limit: 3 rounds"));
        var reader = new BlockedRunReader(journal);

        var recorded = reader.Record("20261025-0900-aaaa", new RunResolution(
            DateTimeOffset.Parse("2026-10-25T11:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture),
            "raise", "nrtat", "keep going", Cap: "rounds", Figure: 3));

        recorded.Succeeded.Should().BeTrue(recorded.Error);

        var run = journal.Summarise("20261025-0900-aaaa").Value!;

        run.Resolved.Should().NotBeNull();
        run.Resolved!.Action.Should().Be("raise");
        run.Resolved.Answer.Should().Be("keep going");
        run.Resolved.Figure.Should().Be(3);
        reader.List().Should().BeEmpty();
    }

    [Fact]
    public void A_second_decision_is_refused_unless_it_names_the_run_that_carried_on()
    {
        var journal = Journal();
        Write(journal, "20261025-0900-aaaa", Started(), Finished("blocked"));
        var reader = new BlockedRunReader(journal);
        var at = DateTimeOffset.UnixEpoch;

        reader.Record("20261025-0900-aaaa", new RunResolution(at, "carry-on", "nrtat")).Succeeded.Should().BeTrue();

        var twice = reader.Record("20261025-0900-aaaa", new RunResolution(at, "drop", "someone else"));

        twice.Succeeded.Should().BeFalse();
        twice.ExitCode.Should().Be(ExitCode.InvalidArguments);
        twice.Error.Should().Contain("already dealt with");

        reader.Record("20261025-0900-aaaa", new RunResolution(at, "carry-on", "nrtat", Next: "20261025-1100-eeee"), again: true)
            .Succeeded.Should().BeTrue();

        journal.Summarise("20261025-0900-aaaa").Value!.Resolved!.Next.Should().Be("20261025-1100-eeee");
    }

    [Fact]
    public void A_run_that_carries_on_counts_the_chain_behind_it()
    {
        var journal = Journal();
        Write(journal, "20261025-0900-aaaa", Started(), Finished("blocked"));
        Write(journal, "20261025-1000-bbbb", Started(follows: "20261025-0900-aaaa"), Finished("blocked"));
        Write(journal, "20261025-1100-cccc", Started(follows: "20261025-1000-bbbb"), Finished("blocked"));

        var reader = new BlockedRunReader(journal);
        var last = reader.Find("20261025-1100-cccc").Value!;

        last.Run.Follows.Should().Be("20261025-1000-bbbb");
        last.Chain.Should().Be(3);
        last.Suggested.Rule.Should().Be("chain");

        reader.ChainsFor("loadout-cli", "fix-login").Should().ContainSingle()
            .Which.Select(one => one.RunId).Should().Equal("20261025-0900-aaaa", "20261025-1000-bbbb", "20261025-1100-cccc");
    }

    [Fact]
    public void A_run_not_held_says_why()
    {
        var journal = Journal();
        Write(journal, "20261025-0900-aaaa", Started(), Stopped);

        var found = new BlockedRunReader(journal).Find("20261025-0900-aaaa");

        found.Succeeded.Should().BeFalse();
        found.Error.Should().Contain("stopped by a person");
    }

    private static string Write(RunJournal journal, string runId, params string[] lines)
    {
        var directory = journal.DirectoryOf(runId);

        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, "journal.jsonl"), lines);

        return directory;
    }

    private RunJournal Journal()
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

        return new RunJournal(paths);
    }
}
