using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Models.Platform;
using Loadout.Models.Teams;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Ending a run whose driver has died.
/// </summary>
/// <remarks>
/// <para>
/// Against a real journal on disk, because what is asserted is what the file
/// says afterwards and what remove, prune and resume make of it. The owner is
/// a double: arranging "the process driving this run has gone" for real
/// means killing one.
/// </para>
/// <para>
/// The refusals matter as much as the close. Writing a finish under a run
/// that is still going would tell everything reading it that the run is over
/// while its nodes are still working.
/// </para>
/// </remarks>
public sealed class RunCloserTests : IDisposable
{
    private const string RunId = "20261023-1200-abcd";

    private static readonly DateTimeOffset Now = new(2026, 10, 23, 14, 30, 0, TimeSpan.Zero);

    private const string Started =
        """{"at":"2026-10-23T12:00:00+00:00","run":"r","node":null,"kind":"run.started","data":{"team":"bug-hunt","goal":"a goal","autonomy":"supervised"}}""";

    private const string Lead =
        """{"at":"2026-10-23T12:00:02+00:00","run":"r","node":"lead","kind":"node.launched","data":{"role":"role.project-lead"}}""";

    private const string Worker =
        """{"at":"2026-10-23T12:01:00+00:00","run":"r","node":"implementer/1","kind":"node.launched","data":{"role":"role.implementer","worktree":"teams/r/implementer-1"}}""";

    private const string WorkerTurn =
        """{"at":"2026-10-23T12:02:00+00:00","run":"r","node":"implementer/1","kind":"node.turn","data":{"attempt":1,"completed":true,"turns":5,"cost":0.25,"denials":0}}""";

    private const string Finished =
        """{"at":"2026-10-23T12:05:00+00:00","run":"r","node":null,"kind":"run.finished","data":{"ended":"done","cost":0.2,"rounds":1,"merged":[]}}""";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "loadout-close-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly FakeRunOwnership _owners = new();

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
    public void Close_refuses_a_live_run_and_writes_nothing()
    {
        var directory = Write(Started, Lead, Worker);
        _owners.Script(directory, new OwnerReading.Held());
        var before = File.ReadAllText(Journal(directory));

        var refused = Closer().Close(RunId, confirmed: true, "test");

        refused.Failed.Should().BeTrue();
        refused.ExitCode.Should().Be(ExitCode.PolicyViolation);
        refused.Error.Should().Contain("team halt", "a run still going is stopped, not closed");
        File.ReadAllText(Journal(directory)).Should().Be(before);

        // Refused on the reading, not left to the lock to refuse: a take of a
        // live run's lock would make its own runner wait.
        _owners.Taken.Should().BeEmpty();
    }

    [Fact]
    public void Close_refuses_a_run_whose_owner_cannot_be_read()
    {
        var directory = Write(Started, Lead, Worker);
        _owners.Script(directory, new OwnerReading.Unknown("Access to the path is denied."));
        var before = File.ReadAllText(Journal(directory));

        var refused = Closer().Close(RunId, confirmed: true, "test");

        refused.Failed.Should().BeTrue();
        refused.ExitCode.Should().Be(ExitCode.GeneralFailure, "not the live refusal's code: nothing says the run is live");
        refused.Error.Should().Contain(Path.Combine(directory, "owner.lock"))
            .And.Contain("Access to the path is denied.");
        File.ReadAllText(Journal(directory)).Should().Be(before);
        _owners.Taken.Should().BeEmpty();
    }

    [Fact]
    public void Close_ends_working_nodes_and_writes_abandoned()
    {
        var directory = Write(Started, Lead, Worker, WorkerTurn);
        _owners.Script(directory, new OwnerReading.Free(_owners.Owner));

        var closed = Closer().Close(RunId, confirmed: false, "process 4242 driving it had gone");

        closed.Succeeded.Should().BeTrue(closed.Error);
        closed.Value!.Ended.Should().BeEquivalentTo("lead", "implementer/1");

        var run = Runs().Summarise(RunId).Value!;
        run.Running.Should().BeFalse();
        run.Ended.Should().Be("abandoned: process 4242 driving it had gone");
        RunOutcomes.Of(run).Should().Be(RunOutcome.Abandoned);
        run.CostUsd.Should().Be(0.25m);
        run.Nodes.Should().NotContain(node => node.State == "working");
        _owners.Read(directory).Should().BeOfType<OwnerReading.Free>("the lock is let go once the finish is written");
    }

    [Fact]
    public void Close_passes_through_a_take_that_failed_for_its_own_reason()
    {
        var directory = Write(Started, Lead, Worker);
        _owners.Script(directory, new OwnerReading.Free(_owners.Owner));
        _owners.Broken[directory] = ("Access to the path 'owner.lock' is denied.", ExitCode.GeneralFailure);
        var before = File.ReadAllText(Journal(directory));

        var refused = Closer().Close(RunId, confirmed: false, "test");

        refused.Failed.Should().BeTrue();
        refused.ExitCode.Should().Be(ExitCode.GeneralFailure, "nothing took the run back; the lock could not be opened");
        refused.Error.Should().Be("Access to the path 'owner.lock' is denied.");
        File.ReadAllText(Journal(directory)).Should().Be(before);
    }

    [Fact]
    public void Close_refused_when_something_took_the_run_back()
    {
        var directory = Write(Started, Lead, Worker);
        _owners.Script(directory, new OwnerReading.Free(_owners.Owner));
        _owners.Refused.Add(directory);

        var refused = Closer().Close(RunId, confirmed: false, "test");

        refused.ExitCode.Should().Be(ExitCode.PolicyViolation);
        refused.Error.Should().StartWith($"Something took run {RunId} back");
    }

    [Fact]
    public void Close_of_old_quiet_run_needs_confirmation()
    {
        var directory = Quiet(Write(Started, Lead));
        var before = File.ReadAllText(Journal(directory));

        var refused = Closer().Close(RunId, confirmed: false, "test");

        refused.Failed.Should().BeTrue();
        refused.ExitCode.Should().Be(ExitCode.InvalidArguments);
        refused.Error.Should().Contain("--yes");
        File.ReadAllText(Journal(directory)).Should().Be(before);
        _owners.Taken.Should().BeEmpty();
    }

    [Fact]
    public void Close_of_old_quiet_run_with_confirmation_writes_abandoned()
    {
        var directory = Quiet(Write(Started, Lead));

        var closed = Closer().Close(RunId, confirmed: true, "nothing written for 2 hour(s)");

        closed.Succeeded.Should().BeTrue(closed.Error);
        _owners.Taken.Should().Equal(directory);
        RunOutcomes.Of(Runs().Summarise(RunId).Value!).Should().Be(RunOutcome.Abandoned);
    }

    [Fact]
    public void Close_of_a_finished_run_is_refused()
    {
        var directory = Write(Started, Lead, Finished);
        var before = File.ReadAllText(Journal(directory));

        var refused = Closer().Close(RunId, confirmed: true, "test");

        refused.Failed.Should().BeTrue();
        refused.ExitCode.Should().Be(ExitCode.InvalidArguments);
        File.ReadAllText(Journal(directory)).Should().Be(before);
    }

    [Fact]
    public void Closed_run_is_accepted_by_remove_without_force()
    {
        Dead(Write(Started, Lead, Worker));
        Runs().Forget(RunId, force: false).Failed.Should().BeTrue("a run still going is refused before closing");

        Closer().Close(RunId, confirmed: false, "gone").Succeeded.Should().BeTrue();

        var forgotten = Runs().Forget(RunId, force: false);

        forgotten.Succeeded.Should().BeTrue(forgotten.Error);
    }

    [Fact]
    public void Closed_run_is_chosen_by_prune()
    {
        Dead(Write(Started, Lead, Worker));
        Closer().Close(RunId, confirmed: false, "gone").Succeeded.Should().BeTrue();

        var chosen = RunRetention.Choose(
            [Runs().Summarise(RunId).Value!], keep: 0, olderThan: null, Now, includeUnmerged: true,
            outcomes: [RunOutcome.Abandoned]);

        chosen.Forgetting.Select(run => run.RunId).Should().Equal(RunId);
    }

    [Fact]
    public void Closed_run_can_be_resumed()
    {
        Dead(Write(Started, Lead));
        RunResumption.Read(Runs(), RunId).Resumption.Should().BeNull("a run still going cannot be picked up");

        Closer().Close(RunId, confirmed: false, "gone").Succeeded.Should().BeTrue();

        var (resumption, why) = RunResumption.Read(Runs(), RunId);

        resumption.Should().NotBeNull(why);
    }

    private RunCloser Closer() => new(Runs(), _owners, new FixedTime(Now));

    private void Dead(string directory) => _owners.Script(directory, new OwnerReading.Free(_owners.Owner));

    /// <summary>A run with no owner record whose journal was last written two hours ago.</summary>
    private static string Quiet(string directory)
    {
        File.SetLastWriteTimeUtc(Journal(directory), Now.AddHours(-2).UtcDateTime);

        return directory;
    }

    private static string Journal(string directory) => Path.Combine(directory, "journal.jsonl");

    private string Write(params string[] lines)
    {
        var directory = Runs().DirectoryOf(RunId);

        Directory.CreateDirectory(directory);
        File.WriteAllLines(Journal(directory), lines);

        return directory;
    }

    private RunJournal Runs()
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

        return new RunJournal(paths, new FixedTime(Now));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
