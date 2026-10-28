using FluentAssertions;
using Loadout.Agents.Teams.Steps;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// A node that writes nothing for longer than its team allows.
/// </summary>
/// <remarks>
/// The team's limit is set to well under a second, and a hung node is a
/// scripted pipe that writes its lines and then nothing until it is killed, so
/// each silence costs the test that long and no more. Every healthy node
/// answers at once, far inside the limit.
/// </remarks>
public sealed partial class TeamRunnerTests
{
    private async Task<TeamDefinition> QuietTeamAsync()
    {
        var team = await IteratingProjectAsync();
        team.Rules.QuietLimit = "0.01m";

        return team;
    }

    [Fact]
    public async Task A_lead_that_goes_silent_is_killed_and_resumed_once_on_its_own_conversation_with_a_note()
    {
        _launcher.ScriptHung("role.project-lead", Init("lead-1"));
        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadDone(), 0.05m));

        var outcome = (await RunAsync(await QuietTeamAsync())).Value!;

        outcome.Ended.Should().Be("done");

        _launcher.Requests.Should().HaveCount(2);
        _launcher.Requests[1].Request.ResumeSessionId.Should().Be("lead-1", "the retry picks up the same conversation");

        // The first message was its brief; the resumed session is told only
        // that it was cut off, because it already has the brief.
        _launcher.Written("role.project-lead").Should().HaveCount(2);
        _launcher.Written("role.project-lead")[1].Should().Be(NodeSessions.CutOff);

        // Both launches were ended: the hung one when it was killed, the new
        // one when the run did.
        _launcher.Completed.Should().HaveCount(2);

        var journal = await File.ReadAllLinesAsync(Path.Combine(outcome.Directory!, "journal.jsonl"));

        journal.Should().ContainSingle(line => line.Contains("\"kind\":\"node.silent\""))
            .Which.Should().Contain("\"node\":\"lead\"").And.Contain("\"session\":\"lead-1\"").And.Contain("\"lines\":[");
        journal.Should().ContainSingle(line => line.Contains("\"kind\":\"node.retried\""))
            .Which.Should().Contain("\"how\":\"resumed\"");
        journal.Should().NotContain(line => line.Contains("\"kind\":\"node.stuck\""));
    }

    [Fact]
    public async Task A_lead_whose_conversation_cannot_be_resumed_is_started_afresh_in_its_place_and_that_is_the_one_retry()
    {
        _launcher.ScriptHung("role.project-lead", Init("lead-1"));

        // The resume starts and ends at once without a word: the session it
        // was asked to pick up could not be read.
        _launcher.Script("role.project-lead");
        _launcher.Script("role.project-lead", Init("lead-2"), Result(LeadDone(), 0.05m));

        var outcome = (await RunAsync(await QuietTeamAsync())).Value!;

        outcome.Ended.Should().Be("done");
        _launcher.Requests.Should().HaveCount(3);
        _launcher.Requests[1].Request.ResumeSessionId.Should().Be("lead-1");
        _launcher.Requests[2].Request.ResumeSessionId.Should().BeNull("afresh");

        // Afresh, so it is given its brief again behind the note.
        var written = _launcher.Written("role.project-lead");
        written[^1].Should().StartWith(NodeSessions.CutOff).And.Contain("# Brief for node lead");

        var journal = await File.ReadAllLinesAsync(Path.Combine(outcome.Directory!, "journal.jsonl"));
        journal.Should().Contain(line => line.Contains("\"kind\":\"node.retried\"") && line.Contains("\"how\":\"fresh\""));
    }

    [Fact]
    public async Task A_lead_that_goes_silent_twice_ends_its_run_stalled_and_not_running()
    {
        _launcher.ScriptHung("role.project-lead", Init("lead-1"));
        _launcher.ScriptHung("role.project-lead", Init("lead-1"));

        var outcome = (await RunAsync(await QuietTeamAsync())).Value!;

        outcome.Ended.Should().StartWith("stalled:");
        RunOutcomes.From(outcome.Ended).Should().Be(RunOutcome.Stalled);

        var journal = await File.ReadAllLinesAsync(Path.Combine(outcome.Directory!, "journal.jsonl"));

        journal.Count(line => line.Contains("\"kind\":\"node.silent\"")).Should().Be(2);
        journal.Should().Contain(line => line.Contains("\"kind\":\"node.retried\""));
        journal.Should().Contain(line => line.Contains("\"kind\":\"node.stuck\"") && line.Contains("\"node\":\"lead\""));
        journal.Should().Contain(line => line.Contains("\"kind\":\"run.stalled\""));
        journal.Last().Should().Contain("\"kind\":\"run.finished\"").And.Contain("\"outcome\":\"stalled\"");

        // Read back the way every view reads it: finished, stalled, and in the
        // Blocked section suggesting carry on.
        var summary = new RunJournal(_paths).Summarise(outcome.RunId).Value!;

        summary.Running.Should().BeFalse();
        RunOutcomes.Of(summary).Should().Be(RunOutcome.Stalled);
        summary.Nodes.Should().Contain(node => node.Node == "lead" && node.State == "stuck");

        var blocked = BlockedRuns.Judge(summary, null)!;
        blocked.Kind.Should().Be("stalled");
        blocked.Suggested.Action.Should().Be(BlockedAction.CarryOn);
    }

    [Fact]
    public async Task A_worker_that_goes_silent_twice_is_reported_unfinished_and_the_lead_carries_on()
    {
        _launcher.Script(
            "role.project-lead",
            Init("lead-1"), Result(LeadRequests(AskImplementer()), 0.05m), Result(LeadDone(), 0.09m), Result(LeadDone(), 0.09m));

        _launcher.ScriptHung("role.implementer", Init("impl-1"));
        _launcher.ScriptHung("role.implementer", Init("impl-1"));

        var outcome = (await RunAsync(await QuietTeamAsync())).Value!;

        outcome.Ended.Should().Be("done", "the lead carried on and reported done over it");

        var branch = $"teams-{outcome.RunId}-implementer";

        // The lead was told, with the branch, that it was left unfinished.
        _launcher.Written("role.project-lead")[1].Should().Contain("went silent twice").And.Contain(branch).And.Contain("unfinished");

        var summary = new RunJournal(_paths).Summarise(outcome.RunId).Value!;

        summary.Unfinished.Should().Equal(branch);

        // Once it has ended, it waits in the Blocked section naming the branch.
        var blocked = BlockedRuns.Judge(summary, null)!;
        blocked.Kind.Should().Be("unfinished");
        blocked.Suggested.Action.Should().Be(BlockedAction.CarryOn);
        blocked.Suggested.Reason.Should().Contain(branch);
    }

    [Fact]
    public async Task A_team_that_says_never_has_no_silence_limit()
    {
        var team = await IteratingProjectAsync();
        team.Rules.QuietLimit = "never";

        team.Rules.Quiet.Should().BeNull();
        new TeamDefinition().Rules.Quiet.Should().Be(TeamRules.DefaultQuietLimit);
    }
}
