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

    /// <remarks>
    /// The shape of a run that failed at launch: the lead started, was never
    /// heard from, and its process exited one with the reason on stderr. Two
    /// real runs did exactly this, on a report schema the agent refused.
    /// </remarks>
    private const string DiedAtLaunch =
        """{"at":"2026-09-15T22:54:25+00:00","run":"r","node":"lead","kind":"node.ended","data":{"exit":1,"killed":false,"stderr":"Error: --json-schema is not a valid JSON Schema"}}""";

    [Fact]
    public void A_node_whose_process_has_gone_is_not_still_working()
    {
        // It launched, said nothing, and its process exited one. Reporting it
        // as "working" is the summary saying the opposite of the one thing it
        // exists to say - and a run that failed in a second showed its lead as
        // working for as long as anybody cared to look.
        var run = RunJournal.Fold(Run, "C:/runs/" + Run,
        [
            .. new[] { Lines[0], Lines[1], DiedAtLaunch }
                .Select(RunJournal.Parse)
                .Where(e => e is not null)
                .OrderBy(e => e!.At)!,
        ]);

        var lead = run.Nodes.Should().ContainSingle().Subject;

        lead.State.Should().Be("failed");
        lead.Trouble.Should().Be("Error: --json-schema is not a valid JSON Schema");
    }

    [Fact]
    public void A_node_that_reported_keeps_what_it_reported_when_it_ends()
    {
        // done, blocked, failed and needs-decision are the node's own account
        // of itself. Ending afterwards is ordinary and says nothing new.
        var run = Fold(Merge, Finished);

        run.Nodes.Single(one => one.Node == "implementer/1").State.Should().Be("done");
    }

    [Fact]
    public void A_clean_exit_says_ended_rather_than_failed_and_carries_no_fault()
    {
        var run = RunJournal.Fold(Run, "C:/runs/" + Run,
        [
            .. new[]
                {
                    Lines[0],
                    Lines[1],
                    """{"at":"2026-09-15T22:54:25+00:00","run":"r","node":"lead","kind":"node.ended","data":{"exit":0,"killed":false,"stderr":"a note to stderr"}}""",
                }
                .Select(RunJournal.Parse)
                .Where(e => e is not null)
                .OrderBy(e => e!.At)!,
        ]);

        var lead = run.Nodes.Should().ContainSingle().Subject;

        lead.State.Should().Be("ended");

        // A node can write to stderr and exit cleanly. That is not a fault and
        // is not worth putting in front of somebody as one.
        lead.Trouble.Should().BeNull();
    }

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
    public void Two_events_saying_the_same_thing_say_it_the_same_way()
    {
        // What a page groups on. The rendered line carries the clock in front
        // of it and so is different every time, which is what made the first
        // attempt at collapsing repeats collapse nothing at all: a node that
        // read one file forty times still wrote forty rows.
        var first = RunJournal.Parse(
            """{"at":"2026-09-15T22:54:20+00:00","node":"lead","kind":"node.doing","data":{"doing":"Read docs/teams.md"}}""");

        var again = RunJournal.Parse(
            """{"at":"2026-09-15T22:54:39+00:00","node":"lead","kind":"node.doing","data":{"doing":"Read docs/teams.md"}}""");

        RunJournal.Wording(first!).Should().Be(RunJournal.Wording(again!));
        RunJournal.Describe(first!).Should().NotBe(RunJournal.Describe(again!));

        // And the whole line still says when and who, because that is what
        // somebody reading rather than counting needs.
        RunJournal.Describe(first!).Should().Contain("lead").And.Contain(RunJournal.Wording(first!));
    }

    [Theory]
    [InlineData("tool", "target")]
    [InlineData("Tool", "Target")]
    public void A_decision_a_person_made_says_what_was_asked_and_what_was_said(
        string tool,
        string target)
    {
        // The most consequential moment in a run - a node stopping, and a
        // person deciding - printed as the bare words "node.asked", with
        // nothing about what was wanted. Both spellings, because the journals
        // already written carry both: these events were written from C#
        // anonymous-object shorthand before that was corrected.
        var asked = RunJournal.Parse(
            "{\"at\":\"2026-09-17T11:18:43+00:00\",\"node\":\"implementer/1\","
            + "\"kind\":\"node.asked\",\"data\":{\"" + tool + "\":\"Bash\",\""
            + target + "\":\"dotnet test\"}}");

        var answered = RunJournal.Parse(
            "{\"at\":\"2026-09-17T11:18:55+00:00\",\"node\":\"implementer/1\","
            + "\"kind\":\"node.answered\",\"data\":{\"" + tool + "\":\"Bash\",\"allowed\":true}}");

        RunJournal.Wording(asked!).Should().Be("stopped to ask: Bash dotnet test");
        RunJournal.Wording(answered!).Should().Be("was allowed Bash");
    }

    [Fact]
    public void A_decision_that_went_against_a_node_says_so()
    {
        var refused = RunJournal.Parse(
            """{"at":"2026-09-17T11:18:55+00:00","node":"lead","kind":"node.answered","data":{"tool":"WebFetch","allowed":false}}""");

        RunJournal.Wording(refused!).Should().Be("was refused WebFetch");
    }

    [Fact]
    public void A_run_that_finished_says_what_it_took()
    {
        // "finished: round limit: 3 rounds" and nothing else. The two numbers
        // anybody wants off the last line of a log - how many rounds it
        // actually took and what it cost - were on it already, unread.
        var finished = RunJournal.Parse(
            """{"at":"2026-09-17T11:23:57+00:00","kind":"run.finished","data":{"ended":"round limit: 3 rounds","cost":0.8521129,"rounds":4,"merged":[]}}""");

        RunJournal.Wording(finished!).Should().Be("finished: round limit: 3 rounds - 4 round(s), $0.85");
    }

    [Fact]
    public void A_reminder_names_what_it_is_still_waiting_on()
    {
        // The reminder's entire content is which roles have decided nothing,
        // and that was the part the line dropped.
        var reminded = RunJournal.Parse(
            """{"at":"2026-09-17T11:22:28+00:00","node":"lead","kind":"gate.reminded","data":{"pending":["reviewer decided nothing","verifier decided nothing"]}}""");

        RunJournal.Wording(reminded!).Should()
            .Be("told it finished without the merge gate: reviewer decided nothing, verifier decided nothing");
    }

    [Fact]
    public void A_report_that_was_not_accepted_says_why()
    {
        var rejected = RunJournal.Parse(
            """{"at":"2026-09-17T11:22:28+00:00","node":"lead","kind":"report.checked","data":{"status":"done","outcome":"rejected","reasons":["claimed a commit that is not there"]}}""");

        RunJournal.Wording(rejected!).Should()
            .Be("reported done, rejected: claimed a commit that is not there");

        // An accepted one carries an empty list, and a colon with nothing
        // after it reads as something missing.
        var accepted = RunJournal.Parse(
            """{"at":"2026-09-17T11:22:28+00:00","node":"lead","kind":"report.checked","data":{"status":"done","outcome":"accepted","Reasons":[]}}""");

        RunJournal.Wording(accepted!).Should().Be("reported done, accepted");
    }

    [Fact]
    public void Events_are_read_in_the_order_they_happened()
    {
        // A node's permission decisions are harvested out of a side file at
        // the end of its turn, so they are written down after events that
        // happened later. One real run put two of them three minutes late,
        // below the line saying the node had ended.
        var late = RunJournal.Parse(
            """{"at":"2026-09-17T11:18:55+00:00","node":"implementer/1","kind":"permission.asked","data":{"tool":"Bash","allowed":true}}""");

        var ended = RunJournal.Parse(
            """{"at":"2026-09-17T11:22:16+00:00","node":"implementer/1","kind":"node.ended","data":{"exit":0}}""");

        var order = RunJournal.InOrder([ended!, late!]);

        order.Should().HaveCount(2);
        order[0].Kind.Should().Be("permission.asked");
        order[1].Kind.Should().Be("node.ended");
    }

    [Fact]
    public void Two_events_in_the_same_instant_keep_the_order_they_were_written()
    {
        // Nothing else can tell them apart, and a run finishing before the
        // line that says what finished it is worse than either order.
        var first = RunJournal.Parse(
            """{"at":"2026-09-17T11:23:57+00:00","node":"lead","kind":"node.ended","data":{"exit":0}}""");

        var second = RunJournal.Parse(
            """{"at":"2026-09-17T11:23:57+00:00","kind":"run.finished","data":{"ended":"done"}}""");

        RunJournal.InOrder([first!, second!])
            .Select(one => one.Kind).Should().ContainInOrder("node.ended", "run.finished");
    }

    [Fact]
    public void The_shape_of_a_run_is_what_is_left_when_the_commentary_goes()
    {
        // One four-minute run wrote 81 events, 42 of them a node's own tool
        // calls and sentences. Those are worth having and they are not an
        // answer to "what happened", which is what a log is read for first.
        var commentary = new[]
        {
            """{"at":"2026-09-17T11:17:19+00:00","node":"planner","kind":"node.doing","data":{"doing":"Bash git status"}}""",
            """{"at":"2026-09-17T11:17:29+00:00","node":"planner","kind":"node.said","data":{"line":"Now I'll create a plan."}}""",
        };

        commentary.Select(RunJournal.Parse)
            .Should().OnlyContain(entry => !RunJournal.Happened(entry!));

        // Everything else stays, including a kind nothing here knows about:
        // the runner will grow them, and dropping one silently would leave a
        // run that did something the log never mentions.
        var shape = new[]
        {
            """{"at":"2026-09-17T11:16:37+00:00","node":"lead","kind":"node.launched","data":{"role":"role.project-lead"}}""",
            """{"at":"2026-09-17T11:18:43+00:00","node":"lead","kind":"node.asked","data":{"tool":"Bash"}}""",
            """{"at":"2026-09-17T11:22:11+00:00","node":"lead","kind":"node.turn","data":{"attempt":1,"turns":19,"cost":0.26}}""",
            """{"at":"2026-09-17T11:23:57+00:00","kind":"something.new","data":{}}""",
        };

        shape.Select(RunJournal.Parse)
            .Should().OnlyContain(entry => RunJournal.Happened(entry!));
    }

    [Fact]
    public void An_event_kind_nothing_knows_about_still_reads_as_itself()
    {
        // The runner will grow kinds this does not know. Naming it is worse
        // than nothing only if it pretends to explain it.
        var entry = RunJournal.Parse("""{"at":"2026-09-15T22:58:03+00:00","node":"x","kind":"something.new","data":{}}""");

        RunJournal.Describe(entry!).Should().Contain("something.new");
    }

    /// <summary>
    /// A lead's account of the run's criteria, read off a real journal line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first line below is verbatim from the first real run with criteria,
    /// and it spells them <c>Criterion</c> and <c>Because</c> — because the
    /// writer used anonymous-object shorthand, which is the fault
    /// <see cref="RunEvent.Text"/>'s own remarks warn about, made again in the
    /// same file that warns about it.
    /// </para>
    /// <para>
    /// The run produced a perfectly good coverage block and <c>team status</c>
    /// showed no criteria at all. Nothing failed: a property spelt differently
    /// is indistinguishable from one that is absent, so the account of whether
    /// the goal had been met was dropped in silence.
    /// </para>
    /// <para>
    /// The writer is corrected, so the second line is what a journal says now.
    /// Both are read, because the journals already written are the ones least
    /// worth being wrong about.
    /// </para>
    /// </remarks>
    [Fact]
    public void Coverage_is_read_however_the_journal_spelt_it()
    {
        const string shorthand =
            """{"at":"2026-09-21T20:12:00+00:00","run":"r","node":"lead","kind":"report.checked","data":{"status":"done","outcome":"accepted","coverage":[{"Criterion":"every .txt file is named","verdict":"met","Because":"docs-auditor/1 listed all three"}]}}""";

        const string corrected =
            """{"at":"2026-09-21T20:13:00+00:00","run":"r","node":"lead","kind":"report.checked","data":{"status":"done","outcome":"accepted","coverage":[{"criterion":"every .txt file is named","verdict":"met","because":"docs-auditor/1 listed all three"}]}}""";

        foreach (var line in new[] { shorthand, corrected })
        {
            var entry = RunJournal.Parse(line);

            entry.Should().NotBeNull();

            var covered = entry!.Covered();

            covered.Should().ContainSingle(
                "a coverage entry the journal carries must reach a reader whichever "
                + "spelling it was written with");

            covered[0].Criterion.Should().Be("every .txt file is named");
            covered[0].Verdict.Should().Be("met");
            covered[0].Met.Should().BeTrue();
            covered[0].Because.Should().Be("docs-auditor/1 listed all three");
        }
    }

    /// <remarks>
    /// The fold is what <c>team status</c> and the dashboard actually read, so
    /// the round trip is asserted through it rather than through
    /// <see cref="RunEvent.Covered"/> alone.
    /// </remarks>
    [Fact]
    public void The_latest_account_reaches_the_summary()
    {
        var summary = Fold(
            """{"at":"2026-09-15T22:56:00+00:00","run":"r","node":"lead","kind":"report.checked","data":{"status":"done","outcome":"returned","coverage":[{"criterion":"the docs are true","verdict":"not-attempted"}]}}""",
            """{"at":"2026-09-15T22:57:00+00:00","run":"r","node":"lead","kind":"report.checked","data":{"status":"done","outcome":"accepted","coverage":[{"criterion":"the docs are true","verdict":"met","because":"docs-auditor/1 checked every page"}]}}""");

        // The later answer wins: a lead sent back for an unanswered criterion
        // reports again, and the second answer is the one that is true.
        summary.Coverage.Should().ContainSingle();
        summary.Coverage[0].Verdict.Should().Be("met");
        summary.Outstanding.Should().BeEmpty();
    }

    [Fact]
    public void A_run_without_criteria_has_no_coverage_and_nothing_outstanding()
    {
        var summary = Fold();

        summary.Coverage.Should().BeEmpty();
        summary.Outstanding.Should().BeEmpty();
    }
}
