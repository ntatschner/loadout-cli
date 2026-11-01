using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>A finished run holding what its lead left for later.</summary>
public sealed partial class TeamRunnerTests
{
    [Fact]
    public async Task A_run_whose_final_report_leaves_work_for_later_holds_each_piece_as_a_proposal()
    {
        var done = LeadDone() with
        {
            FollowUps =
            [
                new ReportFollowUp("A call-site test for the update action", "Only the model is tested."),
                new ReportFollowUp("  ", "A blank title is no piece of work."),
                new ReportFollowUp("A seam to test the try/finally"),
            ],
        };

        _launcher.Script("role.project-lead", Init("lead-1"), Result(done, 0.05m));

        var outcome = (await RunAsync()).Value!;
        var run = new RunJournal(_paths).Summarise(outcome.RunId).Value!;

        run.FollowUps.Should().Equal(
            new RunFollowUp("F1", "A call-site test for the update action", "Only the model is tested.", FollowUpState.Proposed),
            new RunFollowUp("F2", "A seam to test the try/finally", null, FollowUpState.Proposed));
        RunFollowUps.Waiting(run).Should().Be(2);

        var lines = await File.ReadAllLinesAsync(Path.Combine(outcome.Directory!, "journal.jsonl"));
        Array.FindIndex(lines, line => line.Contains("\"kind\":\"followup.proposed\""))
            .Should().BeGreaterThan(Array.FindIndex(lines, line => line.Contains("\"kind\":\"run.finished\"")),
                "a follow-up is only offered for a run that is over");
    }

    [Fact]
    public async Task A_run_that_leaves_nothing_for_later_proposes_nothing()
    {
        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadDone(), 0.05m));

        var outcome = (await RunAsync()).Value!;

        new RunJournal(_paths).Summarise(outcome.RunId).Value!.FollowUps.Should().BeEmpty();
    }

    [Fact]
    public async Task The_lead_is_told_to_list_what_it_leaves_for_later_when_it_finishes()
    {
        _launcher.Script(
            "role.project-lead",
            Init("lead-1"), Result(LeadRequests(AskImplementer()), 0.05m), Result(LeadDone(), 0.09m), Result(LeadDone(), 0.09m));
        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.02m));

        await RunAsync();

        _launcher.Written("role.project-lead")[1].Should().Contain("list in follow_ups");
    }
}
