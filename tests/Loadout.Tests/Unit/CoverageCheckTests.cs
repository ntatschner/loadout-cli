using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Whether a lead may call a run done.
/// </summary>
/// <remarks>
/// <para>
/// A run's goal was one sentence and one claim: the lead reported
/// <c>done</c> and nothing argued. That is fine while somebody is watching. It
/// was also the whole of the check on an autonomous run, which is the case
/// where nobody is — so a lead could miss an entire area of the goal and the
/// run would end reporting success.
/// </para>
/// <para>
/// This is the same rule that already governs a worker's report, one level up.
/// A worker's <c>done</c> needs evidence that passed; a lead's <c>done</c>
/// needs every criterion answered, every answer met, and every met saying what
/// shows it.
/// </para>
/// </remarks>
public sealed class CoverageCheckTests
{
    private const string One = "the suite passes on a clean checkout";
    private const string Two = "the README names every command that ships";

    /// <param name="parent">Null for the lead. A worker has one, and answers only for its own brief.</param>
    private static Brief Briefed(string? parent, params string[] criteria) => new(
        "20260921-1200-aaaa",
        parent is null ? "lead" : "implementer/1",
        parent,
        "role.project-lead",
        "make the docs true",
        DeliverableKind.Answer,
        [],
        new BriefConstraints("coordinate"),
        ["the goal is met"],
        Criteria: criteria.Length > 0 ? criteria : null);

    private static Report Reported(string node, params ReportCoverage[] coverage) => new(
        node,
        ReportStatus.Done,
        "did the thing",
        [],
        [new ReportEvidence(EvidenceKind.Test, "dotnet test", EvidenceResult.Pass)],
        [],
        Coverage: coverage.Length > 0 ? coverage : null);

    [Fact]
    public void A_lead_that_answers_every_criterion_is_accepted()
    {
        var verdict = ReportCheck.Check(
            Reported(
                "lead",
                new ReportCoverage(One, CoverageVerdict.Met, "verifier/1 reported 2843 passing"),
                new ReportCoverage(Two, CoverageVerdict.Met, "docs-auditor/1 checked all 159")),
            Briefed(null, One, Two));

        verdict.Outcome.Should().Be(ReportOutcome.Accepted);
    }

    [Fact]
    public void A_criterion_nobody_mentioned_sends_the_report_back()
    {
        // The case this whole thing exists for: a lead that did half the goal
        // and reported done, with nothing in the run saying which half.
        var verdict = ReportCheck.Check(
            Reported("lead", new ReportCoverage(One, CoverageVerdict.Met, "the suite ran")),
            Briefed(null, One, Two));

        verdict.Outcome.Should().Be(ReportOutcome.Returned);
        verdict.Reasons.Should().ContainSingle()
            .Which.Should().Contain(Two).And.Contain("nothing was said about");
    }

    [Fact]
    public void A_criterion_reported_met_with_nothing_behind_it_sends_the_report_back()
    {
        var verdict = ReportCheck.Check(
            Reported(
                "lead",
                new ReportCoverage(One, CoverageVerdict.Met, "the suite ran"),
                new ReportCoverage(Two, CoverageVerdict.Met, "   ")),
            Briefed(null, One, Two));

        verdict.Outcome.Should().Be(ReportOutcome.Returned);
        verdict.Reasons.Should().ContainSingle()
            .Which.Should().Contain("nothing behind it");
    }

    [Theory]
    [InlineData(CoverageVerdict.Unmet, "is not met")]
    [InlineData(CoverageVerdict.NotAttempted, "Nothing was attempted")]
    public void An_honest_answer_that_is_not_met_still_blocks_a_done(
        CoverageVerdict verdict, string said)
    {
        // Honesty is not the same as being finished. A lead saying a criterion
        // was not met has done the right thing and still has not finished, and
        // the reason says what to do instead of just refusing.
        var judged = ReportCheck.Check(
            Reported(
                "lead",
                new ReportCoverage(One, CoverageVerdict.Met, "the suite ran"),
                new ReportCoverage(Two, verdict)),
            Briefed(null, One, Two));

        judged.Outcome.Should().Be(ReportOutcome.Returned);
        judged.Reasons.Should().ContainSingle().Which.Should().Contain(said);
    }

    [Fact]
    public void A_criterion_answered_twice_keeps_the_first_answer()
    {
        // So a lead cannot overwrite an honest unmet with a later met by
        // repeating the criterion further down its own list.
        var verdict = ReportCheck.Check(
            Reported(
                "lead",
                new ReportCoverage(One, CoverageVerdict.Unmet),
                new ReportCoverage(One, CoverageVerdict.Met, "actually it was fine")),
            Briefed(null, One));

        verdict.Outcome.Should().Be(ReportOutcome.Returned);
        verdict.Reasons.Should().ContainSingle().Which.Should().Contain("is not met");
    }

    [Fact]
    public void Criteria_are_matched_past_case_and_spacing()
    {
        // The lead is repeating back a string it was given, and the one thing a
        // model reliably does to a string it repeats is change its case or its
        // spacing. Matching exactly would fail a lead that did the work.
        var verdict = ReportCheck.Check(
            Reported("lead", new ReportCoverage("  The Suite Passes On A Clean Checkout  ",
                CoverageVerdict.Met, "verifier/1")),
            Briefed(null, One));

        verdict.Outcome.Should().Be(ReportOutcome.Accepted);
    }

    [Fact]
    public void A_worker_is_not_asked_to_answer_for_the_goal()
    {
        // Every node is told the criteria, for the same reason every node is
        // told the team's standing goal. Only the lead answers for them: a
        // worker held to the whole goal would be returned for ever on work it
        // was never asked to do.
        var verdict = ReportCheck.Check(
            new Report(
                "implementer/1",
                ReportStatus.Done,
                "wrote the thing",
                [new ReportDeliverable(DeliverableKind.Commit, "abc1234")],
                [new ReportEvidence(EvidenceKind.Test, "dotnet test", EvidenceResult.Pass)],
                []),
            Briefed("lead", One, Two) with { Deliverable = DeliverableKind.Commit });

        verdict.Outcome.Should().Be(ReportOutcome.Accepted);
    }

    [Fact]
    public void A_run_with_no_criteria_behaves_exactly_as_it_did_before()
    {
        // Every team file already written, and every run that does not want
        // criteria. The absence of them must not become a way to fail.
        ReportCheck.Check(Reported("lead"), Briefed(null))
            .Outcome.Should().Be(ReportOutcome.Accepted);
    }

    [Fact]
    public void Coverage_is_only_asked_of_a_done()
    {
        // A lead reporting blocked is telling you it is not finished, which is
        // the answer the criteria would have extracted anyway. Returning it for
        // incomplete coverage would be refusing an honest report.
        var verdict = ReportCheck.Check(
            new Report(
                "lead",
                ReportStatus.Blocked,
                "could not get there",
                [],
                [],
                [],
                Blocker: new ReportBlocker("the suite does not build", "a fix to the build")),
            Briefed(null, One, Two));

        verdict.Outcome.Should().Be(ReportOutcome.Accepted);
    }

    /// <summary>
    /// Coverage survives the journey a real one actually makes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other test in this file builds a <see cref="ReportCoverage"/> in
    /// C# and hands it straight to the check. A real one is JSON, written by an
    /// agent against the schema, and read back by
    /// <see cref="ReportReader"/>. If anything on that path drops it - a
    /// property name that does not match, an enum member spelt differently in
    /// the schema and the converter - then <c>report.Coverage</c> is null for
    /// every real run, every criterion reads as unanswered, and every
    /// autonomous run ends unmet.
    /// </para>
    /// <para>
    /// And every test here would still pass, because none of them use the
    /// reader. This repository has been caught twice by a double that accepted
    /// what the real collaborator would refuse; this is the same shape of
    /// mistake with the arrow pointing the other way.
    /// </para>
    /// <para>
    /// So the JSON below is written as the schema describes it, not as C#
    /// would serialise it: <c>not-attempted</c> with its hyphen, and
    /// <c>because</c> absent rather than null on the entry that has none.
    /// </para>
    /// </remarks>
    [Fact]
    public void Coverage_written_as_the_schema_describes_it_reads_back()
    {
        const string json = """
            {
              "contract": "report/1",
              "node": "lead",
              "status": "done",
              "summary": "made the docs true",
              "deliverables": [],
              "evidence": [{ "kind": "test", "ref": "dotnet test", "result": "pass" }],
              "outward_taken": [],
              "coverage": [
                { "criterion": "the suite passes on a clean checkout",
                  "verdict": "met",
                  "because": "verifier/1 reported 2843 passing" },
                { "criterion": "the README names every command that ships",
                  "verdict": "not-attempted" }
              ]
            }
            """;

        var read = ReportReader.Read(json);

        read.Succeeded.Should().BeTrue(read.Error);

        var report = read.Value!;

        report.Coverage.Should().NotBeNull("a real report's coverage arrives as JSON, not as C#");
        report.Coverage!.Should().HaveCount(2);

        report.Coverage[0].Criterion.Should().Be(One);
        report.Coverage[0].Verdict.Should().Be(CoverageVerdict.Met);
        report.Coverage[0].Because.Should().Be("verifier/1 reported 2843 passing");

        // The hyphenated member name, which is the one a mismatch would hide.
        report.Coverage[1].Verdict.Should().Be(CoverageVerdict.NotAttempted);
        report.Coverage[1].Because.Should().BeNull();

        // And the whole point: the check reaches the same verdict on a report
        // that came through the reader as on one built in a test.
        var verdict = ReportCheck.Check(report, Briefed(null, One, Two));

        verdict.Outcome.Should().Be(ReportOutcome.Returned);
        verdict.Reasons.Should().ContainSingle().Which.Should().Contain("Nothing was attempted");

        Loadout.Agents.Teams.TeamRunner.Outstanding([One, Two], report).Should().Equal(Two);
    }

    [Fact]
    public void A_report_with_no_coverage_at_all_still_reads()
    {
        // Which is every report written before this existed, and every worker's
        // report now. The reader must not require the key.
        const string json = """
            {
              "contract": "report/1",
              "node": "implementer/1",
              "status": "done",
              "summary": "wrote it",
              "deliverables": [{ "kind": "commit", "ref": "abc1234" }],
              "evidence": [{ "kind": "test", "ref": "dotnet test", "result": "pass" }],
              "outward_taken": []
            }
            """;

        var read = ReportReader.Read(json);

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value!.Coverage.Should().BeNull();
    }

    /// <summary>
    /// What is still out when the run decides whether to call itself done.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A second reader of the same answer, and it has to agree with the check
    /// or the run contradicts itself. ReportCheck refuses a report; this
    /// decides what the run records after the refusals have run out.
    /// </para>
    /// <para>
    /// They run out. A returned report goes back to the node once and the
    /// second answer is the node's, whatever it says - which is right for a
    /// worker and leaves a hole at the top of a run: a lead that never fills in
    /// coverage is asked twice and the run then records "done" while the
    /// journal beside it says two of three criteria were met.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_lead_that_never_accounted_for_the_goal_leaves_every_criterion_outstanding()
    {
        Loadout.Agents.Teams.TeamRunner.Outstanding([One, Two], Reported("lead"))
            .Should().Equal(One, Two);
    }

    [Fact]
    public void Only_a_criterion_reported_met_is_settled()
    {
        var outstanding = Loadout.Agents.Teams.TeamRunner.Outstanding(
            [One, Two, "the changelog names it"],
            Reported(
                "lead",
                new ReportCoverage(One, CoverageVerdict.Met, "verifier/1"),
                new ReportCoverage(Two, CoverageVerdict.Unmet),
                new ReportCoverage("the changelog names it", CoverageVerdict.NotAttempted)));

        // Honest and unmet is still unmet. The point of recording it is that a
        // run nobody watched cannot be read afterwards as having met a goal it
        // did not.
        outstanding.Should().Equal(Two, "the changelog names it");
    }

    [Fact]
    public void A_lead_that_answered_everything_leaves_nothing_outstanding() =>
        Loadout.Agents.Teams.TeamRunner.Outstanding(
            [One, Two],
            Reported(
                "lead",
                new ReportCoverage(One, CoverageVerdict.Met, "verifier/1"),
                new ReportCoverage(Two, CoverageVerdict.Met, "docs-auditor/1")))
            .Should().BeEmpty();

    [Fact]
    public void A_run_with_no_criteria_has_nothing_outstanding()
    {
        // Every run written before criteria existed. Its ending must not change.
        Loadout.Agents.Teams.TeamRunner.Outstanding(null, Reported("lead")).Should().BeEmpty();
        Loadout.Agents.Teams.TeamRunner.Outstanding([], Reported("lead")).Should().BeEmpty();
    }

    [Fact]
    public void The_two_readers_agree_about_a_criterion_answered_twice()
    {
        // ReportCheck keeps the first answer, so this must too. If they
        // disagreed, a report the check refused could still be recorded as a
        // run that met its goal, which is the contradiction both exist to stop.
        var report = Reported(
            "lead",
            new ReportCoverage(One, CoverageVerdict.Unmet),
            new ReportCoverage(One, CoverageVerdict.Met, "actually it was fine"));

        ReportCheck.Check(report, Briefed(null, One)).Outcome.Should().Be(ReportOutcome.Returned);
        Loadout.Agents.Teams.TeamRunner.Outstanding([One], report).Should().Equal(One);
    }

    [Fact]
    public void The_two_readers_agree_about_case_and_spacing()
    {
        var report = Reported(
            "lead",
            new ReportCoverage("  The Suite Passes On A Clean Checkout  ",
                CoverageVerdict.Met, "verifier/1"));

        ReportCheck.Check(report, Briefed(null, One)).Outcome.Should().Be(ReportOutcome.Accepted);
        Loadout.Agents.Teams.TeamRunner.Outstanding([One], report).Should().BeEmpty();
    }

    [Fact]
    public void Every_unanswered_criterion_is_named_rather_than_counted()
    {
        // Three reasons, not "3 criteria were not covered". The lead has to act
        // on this, and a count tells it nothing about which.
        var verdict = ReportCheck.Check(
            Reported("lead"),
            Briefed(null, One, Two, "the changelog mentions it"));

        verdict.Reasons.Should().HaveCount(3);
        verdict.Reasons.Should().Contain(one => one.Contains(One, StringComparison.Ordinal));
        verdict.Reasons.Should().Contain(one => one.Contains(Two, StringComparison.Ordinal));
    }
}
