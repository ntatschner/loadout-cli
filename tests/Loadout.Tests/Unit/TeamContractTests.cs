using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The two documents a team run is made of, and the rules applied to one of
/// them.
/// </summary>
/// <remarks>
/// <para>
/// The wire names are the contract. A role's instructions tell a model to
/// put its findings under <c>evidence</c> with a <c>result</c> of
/// <c>pass</c>, and the model writes exactly those words, so the records
/// have to read and write exactly those words and no others. The
/// round-trip test uses every field once for that reason.
/// </para>
/// <para>
/// The check is the consequence the roles promise: returned once with the
/// reason, or halted for an outward action. Each rule has a case here that
/// fails without it.
/// </para>
/// </remarks>
public sealed class TeamContractTests
{
    private static readonly Brief ImplementerBrief = new(
        Run: "20260915-0900-1a2b",
        Node: "implementer/1",
        Parent: "lead",
        Role: "role.implementer",
        Task: "Add --since to loadout usage.",
        Deliverable: DeliverableKind.Commit,
        Inputs: ["plan:plans/round-1.md"],
        Constraints: new BriefConstraints(
            Mode: "implement",
            BudgetUsd: 4m,
            MaxTurns: 40,
            Worktree: "teams/impl-1",
            OutwardAllowed: []),
        DoneWhen: ["the option exists", "a test fails without the change and passes with it"]);

    private static Report Done(params ReportEvidence[] evidence) => new(
        Node: "implementer/1",
        Status: ReportStatus.Done,
        Summary: "Added the option.",
        Deliverables: [new ReportDeliverable(DeliverableKind.Commit, "a4f21c9", "on teams/impl-1")],
        Evidence: evidence,
        OutwardTaken: []);

    private static readonly ReportEvidence Passed = new(EvidenceKind.Test, "dotnet test", EvidenceResult.Pass, "1612 passed");

    [Fact]
    public void A_report_using_every_field_reads_back_as_it_was_written()
    {
        var report = new Report(
            Node: "lead",
            Status: ReportStatus.NeedsDecision,
            Summary: "Round two done; one call is yours.",
            Deliverables: [new ReportDeliverable(DeliverableKind.Plan, "plans/round-2.md")],
            Evidence: [Passed, new ReportEvidence(EvidenceKind.Review, "git diff main..teams/impl-1", EvidenceResult.NotApplicable)],
            OutwardTaken: [],
            Blocker: new ReportBlocker("nothing", "nothing"),
            Questions: [new ReportQuestion("Merge now?", ["yes", "no"], "yes")],
            Requests: [new ReportRequest("verifier", "Verify a4f21c9", DeliverableKind.Decision, ["report:reviewer"])],
            OutwardRequested: ["git push origin main"],
            Next: "Merge on your say-so.");

        var json = ReportReader.Write(report);

        // The words the roles name, spelled as the roles name them.
        json.Should().Contain("\"contract\":\"report/1\"");
        json.Should().Contain("\"status\":\"needs-decision\"");
        json.Should().Contain("\"result\":\"n/a\"");
        json.Should().Contain("\"outward_taken\":[]");
        json.Should().Contain("\"unblocked_by\":\"nothing\"");
        json.Should().NotContain("Status\":");

        var read = ReportReader.Read(json);

        read.Succeeded.Should().BeTrue(read.Error);

        // Structural, not record equality: the lists come back as different
        // list types, and it is their contents that are the contract.
        read.Value.Should().BeEquivalentTo(report);
    }

    [Fact]
    public void A_brief_is_written_with_the_names_the_roles_use()
    {
        var json = ReportReader.Write(ImplementerBrief);

        json.Should().Contain("\"contract\":\"brief/1\"");
        json.Should().Contain("\"deliverable\":\"commit\"");
        json.Should().Contain("\"done_when\":[");
        json.Should().Contain("\"constraints\":{\"mode\":\"implement\",\"budget_usd\":4,\"max_turns\":40,\"worktree\":\"teams/impl-1\",\"outward_allowed\":[]");

        JsonSerializer.Deserialize<Brief>(json).Should().BeEquivalentTo(ImplementerBrief);
    }

    [Fact]
    public void The_example_report_in_the_roles_readme_is_a_report()
    {
        const string Example = """
            {
              "contract": "report/1",
              "node": "implementer/1",
              "status": "done",
              "summary": "Added --since to loadout usage.",
              "deliverables": [ { "kind": "commit", "ref": "a4f21c9", "note": "on worktree branch teams/impl-1" } ],
              "evidence": [
                { "kind": "test", "ref": "dotnet test --filter UsageSinceTests", "result": "fail", "note": "before the change, as expected" },
                { "kind": "test", "ref": "dotnet test", "result": "pass", "note": "1612 passed, 0 failed, 20 skipped" }
              ],
              "outward_taken": [],
              "outward_requested": [],
              "next": "Reviewer can take a4f21c9."
            }
            """;

        var read = ReportReader.Read(Example);

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value!.Status.Should().Be(ReportStatus.Done);
        read.Value.Evidence.Should().HaveCount(2);
        ReportCheck.Check(read.Value, ImplementerBrief).Outcome.Should().Be(ReportOutcome.Accepted);
    }

    [Theory]
    [InlineData(null, "without a report")]
    [InlineData("", "without a report")]
    [InlineData("not json", "not valid JSON")]
    [InlineData("[1,2]", "not a JSON object")]
    [InlineData("""{"node":"x"}""", "does not say which contract")]
    [InlineData("""{"contract":"report/9","node":"x"}""", "follows 'report/9'")]
    [InlineData("""{"contract":"report/1","node":"x","status":"maybe"}""", "does not fit report/1")]
    public void What_is_not_a_report_is_refused_with_a_sentence_that_says_why(string? json, string reason)
    {
        var read = ReportReader.Read(json);

        read.Failed.Should().BeTrue();
        read.Error.Should().Contain(reason);
    }

    [Fact]
    public void The_schema_is_valid_json_and_requires_what_the_record_requires()
    {
        using var schema = JsonDocument.Parse(ReportSchema.Version1);

        var required = schema.RootElement.GetProperty("required").EnumerateArray().Select(r => r.GetString()).ToList();

        required.Should().BeEquivalentTo(
            ["contract", "node", "status", "summary", "deliverables", "evidence", "outward_taken"]);

        schema.RootElement.GetProperty("properties").GetProperty("contract").GetProperty("const").GetString()
            .Should().Be(Report.Version);
    }

    /// <remarks>
    /// Length is not structure, and enforcing it here refused the whole report
    /// and made the model write it again - inside its own turn, where nothing
    /// in this repository can reach the retry. Watched live: 3163 characters
    /// refused, then 1790 refused, then 1609 refused, three full rewrites about
    /// forty-five seconds apart. The brief asks for a short summary and the
    /// places that print one trim it.
    /// </remarks>
    [Fact]
    public void The_schema_puts_no_length_rule_on_the_summary()
    {
        using var schema = JsonDocument.Parse(ReportSchema.Version1);

        var summary = schema.RootElement.GetProperty("properties").GetProperty("summary");

        summary.GetProperty("type").GetString().Should().Be("string");

        summary.TryGetProperty("maxLength", out _).Should().BeFalse(
            "a long summary is not a malformed report, and refusing one costs a rewrite");
    }

    [Fact]
    public void Done_with_passing_evidence_and_a_deliverable_is_accepted()
    {
        ReportCheck.Check(Done(Passed), ImplementerBrief).Should().Be(ReportVerdict.Accepted);
    }

    [Fact]
    public void Done_without_passing_evidence_is_returned()
    {
        var failedOnly = Done(new ReportEvidence(EvidenceKind.Test, "dotnet test", EvidenceResult.Fail));

        var verdict = ReportCheck.Check(failedOnly, ImplementerBrief);

        verdict.Outcome.Should().Be(ReportOutcome.Returned);
        verdict.Reasons.Should().ContainSingle().Which.Should().Contain("done needs at least one evidence entry whose result is pass");
    }

    [Fact]
    public void Done_without_a_deliverable_is_returned_unless_the_brief_asked_for_an_answer()
    {
        var noDeliverable = Done(Passed) with { Deliverables = [] };

        ReportCheck.Check(noDeliverable, ImplementerBrief).Outcome.Should().Be(ReportOutcome.Returned);

        var answerBrief = ImplementerBrief with { Deliverable = DeliverableKind.Answer };

        ReportCheck.Check(noDeliverable, answerBrief).Outcome.Should().Be(ReportOutcome.Accepted);
    }

    [Fact]
    public void Blocked_without_a_blocker_is_returned()
    {
        var blocked = Done(Passed) with { Status = ReportStatus.Blocked };

        ReportCheck.Check(blocked, ImplementerBrief).Reasons.Should().ContainSingle().Which.Should().Contain("needs a blocker");

        var withBlocker = blocked with { Blocker = new ReportBlocker("no test can reach it", "a seam for the clock") };

        ReportCheck.Check(withBlocker, ImplementerBrief).Outcome.Should().Be(ReportOutcome.Accepted);
    }

    [Fact]
    public void Needs_decision_without_a_question_is_returned()
    {
        var undecided = Done(Passed) with { Status = ReportStatus.NeedsDecision };

        ReportCheck.Check(undecided, ImplementerBrief).Reasons.Should().ContainSingle().Which.Should().Contain("needs at least one question");
    }

    [Fact]
    public void Failed_is_accepted_as_long_as_it_says_what_was_tried()
    {
        var failed = Done() with { Status = ReportStatus.Failed, Deliverables = [] };

        ReportCheck.Check(failed, ImplementerBrief).Outcome.Should().Be(ReportOutcome.Accepted);
        ReportCheck.Check(failed with { Summary = " " }, ImplementerBrief).Outcome.Should().Be(ReportOutcome.Returned);
    }

    [Fact]
    public void An_outward_action_the_brief_did_not_allow_halts_the_run_whatever_else_is_right()
    {
        var pushed = Done(Passed) with { OutwardTaken = ["git push origin teams/impl-1"] };

        var verdict = ReportCheck.Check(pushed, ImplementerBrief);

        verdict.Outcome.Should().Be(ReportOutcome.Halted);
        verdict.Reasons.Should().ContainSingle().Which.Should().Contain("git push origin teams/impl-1");

        var allowedBrief = ImplementerBrief with
        {
            Constraints = ImplementerBrief.Constraints with { OutwardAllowed = ["git push origin teams/impl-1"] },
        };

        ReportCheck.Check(pushed, allowedBrief).Outcome.Should().Be(ReportOutcome.Accepted);
    }

    [Fact]
    public void A_report_from_the_wrong_node_is_returned()
    {
        var impostor = Done(Passed) with { Node = "reviewer" };

        ReportCheck.Check(impostor, ImplementerBrief).Reasons.Should().ContainSingle().Which.Should().Contain("'reviewer'");
    }
}
