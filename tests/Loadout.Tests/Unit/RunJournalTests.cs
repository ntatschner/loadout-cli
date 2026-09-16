using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Reading a run back from what it wrote while it happened.
/// </summary>
/// <remarks>
/// <para>
/// The journal is appended to as things happen, so a reader of it meets two
/// things a finished file never has: a last line that is half written, and a
/// run with no ending. Both are ordinary while watching one, and a reader
/// that threw on either could not be used for watching at all.
/// </para>
/// <para>
/// The lines below are the shapes a real run produced, so a change to what
/// the runner writes shows up here rather than as an empty status.
/// </para>
/// </remarks>
public sealed class RunJournalTests
{
    private const string Run = "20260915-2354-7ede";

    private static readonly string[] Lines =
    [
        """{"at":"2026-09-15T22:54:20+00:00","run":"r","node":null,"kind":"run.started","data":{"team":"iterating-project","goal":"Add farewell.txt","autonomy":"autonomous"}}""",
        """{"at":"2026-09-15T22:54:23+00:00","run":"r","node":"lead","kind":"node.launched","data":{"launch":"L1","role":"role.project-lead","directory":"D:/repo","worktree":null}}""",
        """{"at":"2026-09-15T22:54:41+00:00","run":"r","node":"lead","kind":"node.turn","data":{"attempt":1,"completed":true,"turns":2,"cost":0.017,"subtype":"success","denials":0,"unparsed":0}}""",
        """{"at":"2026-09-15T22:54:41+00:00","run":"r","node":"lead","kind":"report.checked","data":{"status":"blocked","outcome":"accepted","Reasons":[]}}""",
        """{"at":"2026-09-15T22:55:02+00:00","run":"r","node":"implementer/1","kind":"node.launched","data":{"launch":"L2","role":"role.implementer","directory":"C:/trees/one","worktree":"teams/r/implementer-1"}}""",
        """{"at":"2026-09-15T22:55:40+00:00","run":"r","node":"implementer/1","kind":"node.turn","data":{"attempt":1,"completed":true,"turns":5,"cost":0.072,"subtype":"success","denials":3,"unparsed":0}}""",
        """{"at":"2026-09-15T22:55:40+00:00","run":"r","node":"implementer/1","kind":"report.checked","data":{"status":"done","outcome":"accepted","Reasons":[]}}""",
        """{"at":"2026-09-15T22:55:41+00:00","run":"r","node":"implementer/1","kind":"node.ended","data":{"exit":0,"killed":false,"stderr":""}}""",
    ];

    private const string Merge =
        """{"at":"2026-09-15T22:58:02+00:00","run":"r","node":"implementer/1","kind":"merge.done","data":{"branch":"teams/r/implementer-1","target":"main","FastForward":true}}""";

    private const string Finished =
        """{"at":"2026-09-15T22:58:03+00:00","run":"r","node":null,"kind":"run.finished","data":{"ended":"done","cost":0.2503,"rounds":5,"merged":["teams/r/implementer-1"]}}""";

    private static RunSummary Fold(params string[] extra) =>
        RunJournal.Fold(Run, "C:/runs/" + Run, [.. Lines.Concat(extra).Select(RunJournal.Parse).Where(e => e is not null)!]);

    [Fact]
    public void A_finished_run_reads_back_as_what_each_node_did()
    {
        var run = Fold(Merge, Finished);

        run.Team.Should().Be("iterating-project");
        run.Goal.Should().Be("Add farewell.txt");
        run.Autonomy.Should().Be("autonomous");
        run.Ended.Should().Be("done");
        run.Running.Should().BeFalse();
        run.Rounds.Should().Be(5);
        run.CostUsd.Should().Be(0.2503m, "the run's own figure at the end, not the nodes' sum");
        run.Merged.Should().Equal("teams/r/implementer-1");
        run.Branches.Should().Equal("teams/r/implementer-1");

        run.Nodes.Should().HaveCount(2);

        var lead = run.Nodes[0];
        lead.Node.Should().Be("lead");
        lead.Role.Should().Be("role.project-lead");
        lead.State.Should().Be("blocked");
        lead.Branch.Should().BeNull();

        var worker = run.Nodes[1];
        worker.Node.Should().Be("implementer/1");
        worker.State.Should().Be("done");
        worker.Turns.Should().Be(5);
        worker.CostUsd.Should().Be(0.072m);
        worker.Denials.Should().Be(3);
        worker.Branch.Should().Be("teams/r/implementer-1");
    }

    [Fact]
    public void A_run_still_going_reads_as_running_and_costs_what_its_nodes_have_spent()
    {
        var run = Fold();

        run.Running.Should().BeTrue();
        run.Ended.Should().BeNull();
        run.CostUsd.Should().Be(0.017m + 0.072m, "nothing has written the run's own total yet");
        run.LastSeen.Should().Be(DateTimeOffset.Parse("2026-09-15T22:55:41+00:00", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_merge_is_read_from_its_own_line_as_well_as_from_the_ending()
    {
        // Watching a run, the ending has not been written yet and the merge
        // has; both say the same thing and either will do.
        Fold(Merge).Merged.Should().Equal("teams/r/implementer-1");

        Fold(Merge, Finished).Merged.Should().ContainSingle(
            "a run read after it finished has both lines, and one branch merged once")
            .Which.Should().Be("teams/r/implementer-1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("""{"at":"2026-09-15T22:58:03+00:00","run":"r","node":null,"kind":"run.fini""")]
    [InlineData("not json at all")]
    [InlineData("""{"at":"2026-09-15T22:58:03+00:00","run":"r"}""")]
    [InlineData("[1,2,3]")]
    public void A_line_that_is_not_an_event_is_stepped_over(string line)
    {
        RunJournal.Parse(line).Should().BeNull();
    }

    [Fact]
    public void Every_line_a_run_writes_has_something_a_person_can_read()
    {
        var described = Lines.Concat([Merge, Finished])
            .Select(RunJournal.Parse)
            .Select(entry => RunJournal.Describe(entry!))
            .ToList();

        described.Should().AllSatisfy(line => line.Should().NotBeNullOrWhiteSpace());
        described[0].Should().Contain("started iterating-project, autonomous: Add farewell.txt");
        described[4].Should().Contain("implementer/1").And.Contain("launched as role.implementer on teams/r/implementer-1");
        described[5].Should().Contain("5 exchange(s)").And.Contain("3 denial(s)");
        described[^2].Should().Contain("merged teams/r/implementer-1 into main");
        described[^1].Should().Contain("finished: done");
    }

    [Fact]
    public void An_event_kind_nothing_knows_about_still_reads_as_itself()
    {
        // The runner will grow kinds this does not know. Naming it is worse
        // than nothing only if it pretends to explain it.
        var entry = RunJournal.Parse("""{"at":"2026-09-15T22:58:03+00:00","node":"x","kind":"something.new","data":{}}""");

        RunJournal.Describe(entry!).Should().Contain("something.new");
    }
}
