using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Models.Tasks;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Deciding about the follow-ups a finished run left for later, against a real
/// journal and real task lists: accepting one makes a task with the run as
/// its source, dismissing one makes none, and either stops it waiting.
/// </summary>
public sealed class RunFollowUpsTests : IDisposable
{
    private const string RunId = "20261030-2227-8d16";

    private readonly TemporaryWorkspace _space = new();
    private readonly RunJournal _journal;
    private readonly RunFollowUps _followUps;

    public RunFollowUpsTests()
    {
        _journal = new RunJournal(_space.Paths);
        _followUps = new RunFollowUps(_journal, _space.Tasks, TimeProvider.System);
    }

    public void Dispose() => _space.Dispose();

    private void Write(params string[] lines)
    {
        var directory = _journal.DirectoryOf(RunId);
        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, "journal.jsonl"), lines);
    }

    private const string Started =
        """{"at":"2026-10-30T22:27:00+00:00","run":"r","node":null,"kind":"run.started","data":{"team":"iterating-project","goal":"An About page","autonomy":"supervised","rounds":25,"project":"website"}}""";

    private const string Finished =
        """{"at":"2026-10-31T10:00:00+00:00","run":"r","node":null,"kind":"run.finished","data":{"ended":"done","cost":4.0,"rounds":25,"merged":[]}}""";

    private static string Proposed(string id, string title, string why) =>
        """{"at":"2026-10-31T10:00:01+00:00","run":"r","node":null,"kind":"followup.proposed","data":{"id":""" + "\"" + id
        + "\",\"title\":\"" + title + "\",\"why\":\"" + why + "\"}}";

    private RunSummary Run() => _journal.Summarise(RunId).Value!;

    [Fact]
    public async Task Accepting_one_makes_a_task_on_the_runs_project_with_the_run_as_its_source()
    {
        await _space.ProjectAsync("website");
        Write(Started, Finished, Proposed("F1", "A call-site test for the update action", "Only the model is tested."), Proposed("F2", "A seam for the try/finally", "Untested."));

        var accepted = await _followUps.AcceptAsync(RunId, "f1", "nigel", dryRun: false);

        accepted.Succeeded.Should().BeTrue(accepted.Error);
        var task = (await _space.Tasks.ListAsync("website")).Value!.Single();
        task.Title.Should().Be("A call-site test for the update action");
        task.Detail.Should().Be("Only the model is tested.");
        task.Source.Should().BeEquivalentTo(new TaskSource { Kind = TaskSourceKind.Run, Id = RunId });
        task.DeclaredBy.Should().Be("nigel");

        Run().FollowUps[0].Should().Be(new RunFollowUp("F1", "A call-site test for the update action", "Only the model is tested.", FollowUpState.Accepted, task.Id, "nigel"));
        RunFollowUps.Waiting(Run()).Should().Be(1, "F2 is still waiting");
    }

    [Fact]
    public async Task Once_every_one_is_accepted_or_dismissed_nothing_waits_and_none_is_offered_again()
    {
        await _space.ProjectAsync("website");
        Write(Started, Finished, Proposed("F1", "A call-site test", "x"), Proposed("F2", "A seam", "y"));

        (await _followUps.AcceptAsync(RunId, "F1", "nigel", dryRun: false)).Succeeded.Should().BeTrue();
        _followUps.Dismiss(RunId, "F2", "nigel", "covered by the refactor", dryRun: false).Succeeded.Should().BeTrue();

        RunFollowUps.Waiting(Run()).Should().Be(0);
        Run().FollowUps[1].Should().Match<RunFollowUp>(one => one.State == FollowUpState.Dismissed && one.Reason == "covered by the refactor");
        (await _space.Tasks.ListAsync("website")).Value!.Should().ContainSingle("a dismissed follow-up makes no task");

        var again = await _followUps.AcceptAsync(RunId, "F1", "nigel", dryRun: false);
        again.Failed.Should().BeTrue();
        again.Error.Should().Contain("already accepted");
        _followUps.Dismiss(RunId, "F2", "nigel", null, dryRun: false).Error.Should().Contain("already dismissed");
        _followUps.Dismiss(RunId, "F9", "nigel", null, dryRun: false).Error.Should().Contain("has no follow-up 'F9'").And.Contain("F1, F2");
        (await _space.Tasks.ListAsync("website")).Value!.Should().ContainSingle("a refused accept makes no second task");
    }

    [Fact]
    public async Task A_dry_run_says_what_it_would_make_and_changes_nothing()
    {
        await _space.ProjectAsync("website");
        Write(Started, Finished, Proposed("F1", "A call-site test", "x"));
        var before = await File.ReadAllBytesAsync(Path.Combine(_journal.DirectoryOf(RunId), "journal.jsonl"));

        var accepted = await _followUps.AcceptAsync(RunId, "F1", "nigel", dryRun: true);
        var dismissed = _followUps.Dismiss(RunId, "F1", "nigel", null, dryRun: true);

        accepted.Value!.Title.Should().Be("A call-site test");
        dismissed.Value!.State.Should().Be(FollowUpState.Dismissed);
        (await File.ReadAllBytesAsync(Path.Combine(_journal.DirectoryOf(RunId), "journal.jsonl"))).Should().Equal(before);
        (await _space.Tasks.ListAsync("website")).Value!.Should().BeEmpty();
    }

    [Fact]
    public void A_run_that_proposed_nothing_says_so()
    {
        Write(Started, Finished);

        var dismissed = _followUps.Dismiss(RunId, "F1", "nigel", null, dryRun: false);

        dismissed.ExitCode.Should().Be(ExitCode.InvalidArguments);
        dismissed.Error.Should().Contain("proposed no follow-ups");
    }
}
