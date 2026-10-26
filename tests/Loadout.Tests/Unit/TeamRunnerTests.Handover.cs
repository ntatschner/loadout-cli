using FluentAssertions;
using Loadout.Agents.Teams;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Runs whose remaining criteria need a push, CI or a person: tagged when the
/// lead gives its readings, handed over instead of looping, and judged again
/// once a person has checked what was handed to them.
/// </summary>
public sealed partial class TeamRunnerTests
{
    // The shape of run 20261023-1740-2677: five criteria, three the run met,
    // one needing a hand check in Studio and one needing CI on Unix.
    private const string OwnerLock = "Every run holds an exclusive lock on an owner file in its run directory";
    private const string CloseRefuses = "`team runs close` refuses a run whose lock is held and closes one with no owner";
    private const string EndRunBadge = "The dashboard shows a not-responding badge with an End run action that unlocks Forget";
    private const string OldRuns = "A run with no lock file and no recent writes shows as possibly not responding";
    private const string UnixLegs = "A contract test passes on Windows and Unix CI in which close is refused while a real runner lives";

    private const string CheckInStudio = "Check End run by hand in Studio on a run whose driver has died";

    private static readonly string[] Stuck = [OwnerLock, CloseRefuses, EndRunBadge, OldRuns, UnixLegs];

    /// <summary>The lead's readings, with End run tagged and the CI one left to its wording.</summary>
    private static ReportReading[] TaggedReadings(params string[] criteria) =>
    [
        .. criteria.Select(one => one == EndRunBadge
            ? new ReportReading(one, $"read as: {one}", CriterionNeeds.Person, CheckInStudio)
            : new ReportReading(one, $"read as: {one}")),
    ];

    private static Report LeadPlans(params string[] criteria) =>
        LeadRequests(AskImplementer()) with { Readings = TaggedReadings(criteria) };

    /// <summary>
    /// What the stuck run's lead said at the end: blocked on two questions, the
    /// local criteria met and the other two not, asking for nothing.
    /// </summary>
    private static Report LeadStuck(string[] criteria, params string[] met) => new(
        "lead", ReportStatus.Blocked, "Three are met; the other two need the branches combined and pushed.",
        [new ReportDeliverable(DeliverableKind.Commit, "a4f21c9")], [Passed], [],
        Blocker: new ReportBlocker("the branches need combining and pushing", "a push, and a check of End run in Studio"),
        Questions:
        [
            new ReportQuestion("How do you want the verified branches combined?", ["Merge through the gate", "By hand into one PR"], "Merge through the gate"),
            new ReportQuestion("Does a separate process holding the lock count as a real runner?", ["Yes", "No"], "Yes"),
        ],
        OutwardRequested: ["Push the combined branch and open a PR"],
        Coverage:
        [
            .. criteria.Select(one => met.Contains(one)
                ? new ReportCoverage(one, CoverageVerdict.Met, "implementer/1's report: dotnet test, 1612 passed")
                : new ReportCoverage(one, CoverageVerdict.Unmet, "needs what this run cannot do")),
        ],
        GoalUnderstood: "detect and clear runs whose driver has died",
        Readings: TaggedReadings(criteria));

    [Fact]
    public async Task A_run_left_only_with_what_needs_a_push_or_a_person_hands_it_over_instead_of_looping()
    {
        _launcher.Script("role.project-lead", Init("lead-1"),
            Result(LeadPlans(Stuck), 0.50m),
            Result(LeadStuck(Stuck, OwnerLock, CloseRefuses, OldRuns), 1.00m),

            // Never reached: the round it would be is the one the run paid for twice.
            Result(LeadStuck(Stuck, OwnerLock, CloseRefuses, OldRuns), 7.92m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.20m));

        var outcome = (await RunAsync(criteria: Stuck)).Value!;

        outcome.Ended.Should().StartWith(RunHandovers.EndedOpening).And.NotStartWith("no progress");
        RunOutcomes.From(outcome.Ended).Should().Be(RunOutcome.NeedsDecision);

        _launcher.Written("role.project-lead").Should().HaveCount(2, "no lead round is taken once nothing left is the run's to do");

        var journal = await JournalAsync(outcome);
        var handed = journal.Should().ContainSingle(line => line.Contains("\"kind\":\"run.handover\"")).Which;

        handed.Should().Contain(CheckInStudio).And.Contain("needs-person");
        handed.Should().Contain("\"needs\":\"needs-ci\"").And.Contain("so CI runs, and see that it passes");
        handed.Should().NotContain(OwnerLock, "a criterion the run met is not handed to anybody");

        var summary = new RunJournal(_paths).Summarise(outcome.RunId).Value!;

        summary.Outcome.Should().Be("needs-decision");
        summary.Handover!.Items.Select(one => one.Criterion).Should().Equal(EndRunBadge, UnixLegs);
        summary.Handover.Items[1].By.Should().Be("keyword", "the lead left it untagged and its wording says CI");
    }

    [Fact]
    public async Task A_lead_still_asking_for_work_is_not_handed_over_whatever_is_left()
    {
        // The criterion needing CI may need its test written first, and a lead
        // asking for that is not finished.
        _launcher.Script("role.project-lead", Init("lead-1"),
            Result(LeadPlans(OwnerLock, UnixLegs), 0.50m),
            Result(LeadStuck([OwnerLock, UnixLegs], OwnerLock) with { Requests = [AskImplementer()] }, 1.00m),
            Result(LeadDoneCovering(OwnerLock, UnixLegs), 1.50m),
            Result(LeadDoneCovering(OwnerLock, UnixLegs), 1.60m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.20m));
        _launcher.Script("role.implementer", Init("impl-2"), Result(ImplementerDone(), 0.20m));

        var outcome = (await RunAsync(criteria: [OwnerLock, UnixLegs])).Value!;

        outcome.Ended.Should().Be("done");
        (await JournalAsync(outcome)).Should().NotContain(line => line.Contains("\"kind\":\"run.handover\""));
    }

    [Fact]
    public async Task The_readings_question_tags_every_criterion_before_any_worker_starts()
    {
        var asked = AskedAbout(TeamRunner.ReadingsQuestionStart);

        _launcher.Script("role.project-lead", Init("lead-1"),
            Result(LeadPlans(Stuck), 0.50m),
            Result(LeadStuck(Stuck, OwnerLock, CloseRefuses, OldRuns), 1.00m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.20m));

        await RunAsync(criteria: Stuck);

        var question = asked.Should().ContainSingle().Which.Question;

        question.Should().Contain($"\"{OwnerLock}\" read as: read as: {OwnerLock} [local]");
        question.Should().Contain($"[needs-person: {CheckInStudio} The run will hand this one to you.]");
        question.Should().Contain("[needs-ci, from its wording: Push the run's work so CI runs");

        foreach (var criterion in Stuck)
        {
            question.Should().MatchRegex($"{System.Text.RegularExpressions.Regex.Escape(criterion)}\" read as: [^•]*\\[(local|needs-push|needs-ci|needs-person)");
        }
    }

    [Fact]
    public async Task On_a_run_nobody_watches_the_keyword_rule_tags_what_the_lead_left_untagged()
    {
        _console.CanAsk = false;

        _launcher.Script("role.project-lead", Init("lead-1"),
            Result(LeadPlans(Stuck), 0.50m),
            Result(LeadStuck(Stuck, OwnerLock, CloseRefuses, OldRuns), 1.00m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.20m));

        var outcome = (await RunAsync(criteria: Stuck, autonomy: "autonomous")).Value!;

        var reading = (await JournalAsync(outcome)).Should().ContainSingle(line => line.Contains("\"kind\":\"reading\"")).Which;

        reading.Should().Contain($"\"criterion\":\"{UnixLegs}\",\"reading\":\"read as: {UnixLegs}\",\"needs\":\"needs-ci\",\"tagged\":\"keyword\"");
        reading.Should().Contain("\"needs\":\"needs-person\",\"tagged\":\"lead\"");
        outcome.Ended.Should().StartWith(RunHandovers.EndedOpening);
    }

    [Fact]
    public async Task Ticking_what_was_handed_over_records_review_evidence_and_the_run_is_judged_done()
    {
        var team = await IteratingProjectAsync();
        string[] criteria = [OwnerLock, EndRunBadge];

        _launcher.Script("role.project-lead", Init("lead-1"),
            Result(LeadPlans(criteria), 0.50m),
            Result(LeadStuck(criteria, OwnerLock), 1.00m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.20m));

        var first = (await RunAsync(team, criteria: criteria)).Value!;

        first.Ended.Should().StartWith(RunHandovers.EndedOpening);

        var journal = new RunJournal(_paths);
        var at = DateTimeOffset.Parse("2026-10-26T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        var ticked = RunHandoverChecks.Record(journal, first.RunId, ["1"], "nigel", at);

        ticked.Succeeded.Should().BeTrue(ticked.Error);
        ticked.Value!.Settled.Should().BeTrue();

        var check = (await JournalAsync(first)).Should().ContainSingle(line => line.Contains("\"kind\":\"criterion.checked\"")).Which;

        check.Should().Contain("\"by\":\"nigel\"").And.Contain("\"kind\":\"review\"").And.Contain(EndRunBadge);

        // Picked up, it is judged again with no turn for the lead: there is
        // nothing left in it that is the lead's to do.
        _launcher.Script("role.project-lead", Init("lead-1"));

        var picked = (await ResumeAsync(first.RunId, team)).Value!;

        picked.Ended.Should().Be("done");
        _launcher.Written("role.project-lead").Should().HaveCount(2, "judging it again is not a lead turn");
        picked.FinalReport!.Coverage.Should().OnlyContain(one => one.Verdict == CoverageVerdict.Met);

        var summary = journal.Summarise(first.RunId).Value!;

        summary.Outcome.Should().Be("done");
        summary.Outstanding.Should().BeEmpty("the check is review evidence that the criterion is met");
        summary.Coverage.Single(one => one.Criterion == EndRunBadge).Because.Should().Contain("Checked by hand by nigel");
        (await JournalAsync(first)).Should().Contain(line => line.Contains("\"kind\":\"run.rejudged\""));
    }

    [Fact]
    public async Task A_handover_with_one_still_open_is_not_judged_again_on_a_pick_up()
    {
        var team = await IteratingProjectAsync();

        _launcher.Script("role.project-lead", Init("lead-1"),
            Result(LeadPlans(Stuck), 0.50m),
            Result(LeadStuck(Stuck, OwnerLock, CloseRefuses, OldRuns), 1.00m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.20m));

        var first = (await RunAsync(team, criteria: Stuck)).Value!;

        var ticked = RunHandoverChecks.Record(new RunJournal(_paths), first.RunId, [EndRunBadge], "nigel", DateTimeOffset.UtcNow);

        ticked.Value!.Settled.Should().BeFalse("the one that needs CI is still open");

        // The lead is picked up as before, and says the same again.
        _launcher.Script("role.project-lead", Init("lead-1"),
            Result(LeadStuck(Stuck, OwnerLock, CloseRefuses, OldRuns), 1.50m));

        var picked = (await ResumeAsync(first.RunId, team)).Value!;

        picked.Ended.Should().StartWith(RunHandovers.EndedOpening);
        _launcher.Written("role.project-lead").Should().HaveCount(3);

        // What was checked stays met, though the lead still calls it unmet:
        // only the one still open is handed over again, and the lead was told.
        picked.Ended.Should().Contain("handed over 1 criterion(s)");
        (await JournalAsync(first)).Last(line => line.Contains("\"kind\":\"run.handover\"")).Should().NotContain("End run action");
        _launcher.Written("role.project-lead")[2].Should().Contain("checked these by hand").And.Contain("checked by nigel");
    }

    [Fact]
    public async Task A_lead_that_accounts_for_nothing_is_not_handed_over()
    {
        // Every criterion needs CI, and the lead asks for nothing on its first
        // turn - but it has said nothing about where any of them stand, so it
        // has not got as far as writing the work CI would run.
        _launcher.Script("role.project-lead", Init("lead-1"),
            Result(new Report(
                "lead", ReportStatus.Blocked, "Unsure where to start.", [], [], [],
                Blocker: new ReportBlocker("the goal is unclear", "a clearer goal"),
                Readings: TaggedReadings(UnixLegs)), 0.50m));

        var outcome = (await RunAsync(criteria: [UnixLegs])).Value!;

        outcome.Ended.Should().Be("blocked");
        (await JournalAsync(outcome)).Should().NotContain(line => line.Contains("\"kind\":\"run.handover\""));
    }
}
