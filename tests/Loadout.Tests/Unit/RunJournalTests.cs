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

    private const string Round =
        """{"at":"2026-09-15T22:54:22+00:00","run":"r","node":null,"kind":"round.started","data":{"round":2,"of":5}}""";

    private static DateTimeOffset When(string time) =>
        DateTimeOffset.Parse($"2026-09-15T{time}+00:00", System.Globalization.CultureInfo.InvariantCulture);

    /// <remarks>
    /// Sorted by time, because a journal is appended to as things happen and
    /// is therefore always in that order. A test that handed the fold events
    /// out of order would be testing something no run can produce.
    /// </remarks>
    private static RunSummary Fold(params string[] extra) =>
        RunJournal.Fold(Run, "C:/runs/" + Run, [.. Lines.Concat(extra)
            .Select(RunJournal.Parse)
            .Where(e => e is not null)
            .OrderBy(e => e!.At)!]);

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
    public void A_node_still_working_says_what_it_is_doing_and_how_long_it_has_been_at_it()
    {
        // The gap this fills: a turn runs for minutes and the run said
        // nothing in between, so watching one showed a node "working" and
        // never what on.
        var doing = """{"at":"2026-09-15T22:56:00+00:00","run":"r","node":"lead","kind":"node.doing","data":{"doing":"Read docs/commands.md"}}""";

        var lead = Fold(doing).Nodes[0];

        lead.Doing.Should().Be("Read docs/commands.md");
        lead.Started.Should().Be(When("22:54:23"));
        lead.Took.Should().Be(TimeSpan.FromSeconds(97), "launched at 22:54:23, last heard from at 22:56:00");
    }

    [Fact]
    public void A_node_that_has_reported_is_no_longer_doing_anything()
    {
        // Left standing, the last thing anybody saw makes a finished run
        // look busy for ever. The lead here reports and does not end, so
        // this is the report clearing it and nothing else.
        var doing = """{"at":"2026-09-15T22:54:30+00:00","run":"r","node":"lead","kind":"node.doing","data":{"doing":"Read docs/commands.md"}}""";

        Fold(doing).Nodes[0].Doing.Should().BeNull();
    }

    [Fact]
    public void A_node_that_has_gone_is_no_longer_doing_anything_either()
    {
        // Between its report and its exit, which is where the implementer
        // was when the coordinator last heard from it.
        var doing = """{"at":"2026-09-15T22:55:40.5+00:00","run":"r","node":"implementer/1","kind":"node.doing","data":{"doing":"Bash git commit"}}""";

        Fold(doing).Nodes[1].Doing.Should().BeNull();
    }

    [Fact]
    public void A_run_still_going_knows_which_round_it_is_on()
    {
        var run = Fold(Round);

        run.Rounds.Should().Be(2, "nothing has written the ending's count yet");
        run.RoundLimit.Should().Be(5);
    }

    [Fact]
    public void What_is_left_is_a_ceiling_from_the_rounds_it_has_used()
    {
        // Two rounds in 81 seconds, three rounds left. A prediction would be
        // a different claim and the journal cannot support one: what the
        // lead asks for next is not known to anybody.
        var run = Fold(Round);

        run.Elapsed.Should().Be(TimeSpan.FromSeconds(81));
        run.AtMostRemaining.Should().Be(TimeSpan.FromSeconds(81) / 2 * 3);
    }

    [Fact]
    public void A_run_with_nothing_to_go_on_estimates_nothing()
    {
        Fold().AtMostRemaining.Should().BeNull("no round has been recorded, so there is no rate");
        Fold(Round, Finished).AtMostRemaining.Should().BeNull("a run that has ended has nothing left to take");
    }

    [Fact]
    public void Every_exchange_is_kept_one_by_one_and_not_only_summed()
    {
        // Two nodes that cost the same are the same number and can be quite
        // different problems. The totals cannot tell them apart; these can.
        var run = Fold();

        run.Turns.Should().HaveCount(2);

        run.Turns[0].Node.Should().Be("lead");
        run.Turns[0].Exchanges.Should().Be(2);
        run.Turns[0].CostUsd.Should().Be(0.017m);
        run.Turns[0].Denials.Should().Be(0);
        run.Turns[0].Completed.Should().BeTrue();

        run.Turns[1].Node.Should().Be("implementer/1");
        run.Turns[1].Exchanges.Should().Be(5);
        run.Turns[1].Denials.Should().Be(3);
    }

    [Fact]
    public void An_exchange_carries_the_verdict_on_the_report_it_produced()
    {
        // The verdict arrives as its own event a moment later. Left beside the
        // exchange rather than on it, somebody has to join the two up by eye.
        var run = Fold();

        run.Turns[0].Status.Should().Be("blocked");
        run.Turns[0].Outcome.Should().Be("accepted");

        run.Turns[1].Status.Should().Be("done");
    }

    [Fact]
    public void A_second_attempt_takes_the_verdict_and_the_first_keeps_none()
    {
        // A node whose first report could not be read is asked again. Both
        // exchanges were paid for and both are kept; only the second one
        // produced a report, so only the second one carries a verdict.
        const string Again =
            """{"at":"2026-09-15T22:56:00+00:00","run":"r","node":"lead","kind":"node.turn","data":{"attempt":2,"completed":true,"turns":1,"cost":0.004,"denials":0}}""";

        const string Verdict =
            """{"at":"2026-09-15T22:56:01+00:00","run":"r","node":"lead","kind":"report.checked","data":{"status":"done","outcome":"accepted","Reasons":[]}}""";

        var lead = Fold(Again, Verdict).Turns.Where(turn => turn.Node == "lead").ToList();

        lead.Should().HaveCount(2);
        lead[1].Attempt.Should().Be(2);
        lead[1].Status.Should().Be("done");

        // The first attempt keeps the verdict its own report earned, which is
        // the earlier one - not the later report's.
        lead[0].Status.Should().Be("blocked");
    }

    [Fact]
    public void An_exchange_remembers_which_round_the_run_was_in()
    {
        // Round two starts before the lead's turn, so the lead's exchange is
        // in round two and the implementer's, later still, is as well.
        var run = Fold(Round);

        run.Turns.Should().OnlyContain(turn => turn.Round == 2);
    }

    [Fact]
    public void A_node_pinned_to_a_model_says_which_one()
    {
        const string Pinned =
            """{"at":"2026-09-15T22:54:23+00:00","run":"r","node":"pinned","kind":"node.launched","data":{"role":"role.implementer","model":"claude-opus-5"}}""";

        var run = RunJournal.Fold(Run, "C:/runs/" + Run,
            [.. new[] { Pinned }.Select(RunJournal.Parse).Where(e => e is not null)!]);

        run.Nodes.Should().ContainSingle().Which.Model.Should().Be("claude-opus-5");
    }

    [Fact]
    public void A_node_that_was_not_pinned_says_nothing_rather_than_guessing()
    {
        // Null here means "whatever the agent picks for itself", which is a
        // real answer. Nothing in the journal knows what that turned out to be.
        Fold().Nodes.Should().OnlyContain(node => node.Model == null);
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
