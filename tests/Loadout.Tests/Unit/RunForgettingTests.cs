using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Models.Platform;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Deleting what a run wrote down.
/// </summary>
/// <remarks>
/// <para>
/// Against a real directory rather than a double, because what is being
/// asserted is that files are gone from a disk, and a double that said they
/// were would be asserting its own opinion. The journal is the only copy of
/// what a run did; there is no undo behind this.
/// </para>
/// <para>
/// The refusal matters more than the deletion. A run's directory is not only a
/// record: it is the channel its nodes read, because a gate is answered by a
/// file appearing in it. Deleting one under a live run leaves processes waiting
/// on answers that can no longer arrive.
/// </para>
/// </remarks>
public sealed class RunForgettingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "loadout-forget-" + Guid.NewGuid().ToString("N")[..8]);

    private const string Started =
        """{"at":"2026-09-16T12:00:00+00:00","run":"r","node":null,"kind":"run.started","data":{"team":"bug-hunt","goal":"a goal","autonomy":"supervised"}}""";

    private const string Finished =
        """{"at":"2026-09-16T12:05:00+00:00","run":"r","node":null,"kind":"run.finished","data":{"ended":"done","cost":0.2,"rounds":1,"merged":[]}}""";

    private const string Launched =
        """{"at":"2026-09-16T12:00:02+00:00","run":"r","node":"implementer/1","kind":"node.launched","data":{"role":"role.implementer","worktree":"teams/r/implementer-1"}}""";

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
    public void A_finished_run_goes_with_everything_it_wrote()
    {
        var journal = Journal();
        var directory = Write("20260916-1200-aaaa", Started, Finished);

        // Not only the journal. A run writes briefs, policies, reports and one
        // stream per node, and forgetting one that left its streams behind
        // would leave the largest files on the disk.
        File.WriteAllText(Path.Combine(directory, "brief-lead.json"), "{}");
        File.WriteAllText(Path.Combine(directory, "stream-lead.jsonl"), "{}\n{}\n");

        var gone = journal.Forget("20260916-1200-aaaa");

        gone.Succeeded.Should().BeTrue(gone.Error);
        gone.Value!.Team.Should().Be("bug-hunt");
        gone.Value.Files.Should().Be(3);
        gone.Value.Bytes.Should().BeGreaterThan(0);

        Directory.Exists(directory).Should().BeFalse();
    }

    [Fact]
    public void A_run_that_has_not_finished_is_refused()
    {
        var journal = Journal();
        var directory = Write("20260916-1200-bbbb", Started);

        var refused = journal.Forget("20260916-1200-bbbb");

        refused.Failed.Should().BeTrue();
        refused.ExitCode.Should().Be(ExitCode.PolicyViolation);
        refused.Error.Should().Contain("team halt 20260916-1200-bbbb");

        Directory.Exists(directory).Should().BeTrue("a refusal must not half-delete anything");
    }

    [Fact]
    public void A_run_that_has_not_finished_goes_when_it_is_asked_for_outright()
    {
        var journal = Journal();
        var directory = Write("20260916-1200-cccc", Started);

        journal.Forget("20260916-1200-cccc", force: true).Succeeded.Should().BeTrue();

        Directory.Exists(directory).Should().BeFalse();
    }

    [Fact]
    public void What_it_left_behind_is_named_on_the_way_out()
    {
        var journal = Journal();

        Write("20260916-1200-dddd", Started, Launched, Finished);

        var gone = journal.Forget("20260916-1200-dddd");

        // The branch is Git's and outlives the run. This line is the last thing
        // on the machine that says which run produced it, which is the whole
        // reason it is reported rather than counted.
        gone.Value!.Unmerged.Should().Equal("teams/r/implementer-1");
    }

    [Fact]
    public void A_branch_the_run_merged_is_not_reported_as_left_behind()
    {
        var journal = Journal();

        Write(
            "20260916-1200-eeee",
            Started,
            Launched,
            """{"at":"2026-09-16T12:04:00+00:00","run":"r","node":"implementer/1","kind":"merge.done","data":{"branch":"teams/r/implementer-1","target":"main"}}""",
            """{"at":"2026-09-16T12:05:00+00:00","run":"r","node":null,"kind":"run.finished","data":{"ended":"done","cost":0.2,"rounds":1,"merged":["teams/r/implementer-1"]}}""");

        journal.Forget("20260916-1200-eeee").Value!.Unmerged.Should().BeEmpty();
    }

    [Fact]
    public void A_run_that_is_not_there_says_so_rather_than_succeeding_quietly()
    {
        var missing = Journal().Forget("20260916-1200-ffff");

        missing.Failed.Should().BeTrue();
        missing.ExitCode.Should().Be(ExitCode.ProjectNotFound);
    }

    /// <remarks>
    /// The same reasoning as everywhere else a run identifier becomes a path,
    /// and worth its own case here because this one deletes a directory
    /// recursively. See <see cref="RunIdentifierTests"/>.
    /// </remarks>
    [Theory]
    [InlineData("../../elsewhere")]
    [InlineData("..")]
    public void A_run_identifier_that_is_not_one_reaches_nothing(string identifier)
    {
        var refused = Journal().Forget(identifier);

        refused.Failed.Should().BeTrue();
        refused.ExitCode.Should().Be(ExitCode.InvalidArguments);
    }

    private string Write(string runId, params string[] lines)
    {
        var directory = Journal().DirectoryOf(runId);

        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, "journal.jsonl"), lines);

        return directory;
    }

    /// <summary>A journal writing under this test's own directory.</summary>
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
