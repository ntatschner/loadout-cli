using System.Text.Json;
using FluentAssertions;
using Loadout.Agents.Backfill;
using Loadout.Agents.Tasks;
using Loadout.Core.Backfill;
using Loadout.Core.Ideas;
using Loadout.Core.Tasks;
using Loadout.Models.Ideas;
using Loadout.Models.Tasks;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Bringing open ideas and tasks up to the current contract: which records a
/// pass looks at and leaves alone, what it writes and holds, what the lists
/// mark, and that a dry run changes nothing.
/// </summary>
public sealed class ContractBackfillTests : IDisposable
{
    private const string Good =
        """{"contract":"donewhen/1","doneWhen":["The status page lists every service","An alert reaches the phone within a minute"],"why":"From the plan's two layers."}""";

    private readonly TemporaryWorkspace _space = new();

    public void Dispose() => _space.Dispose();

    private ContractBackfiller Backfiller(ScriptedDetachedLauncher launcher)
    {
        var projects = new FakeProjects("website", _space.Scratch("website"));

        return new ContractBackfiller(
            _space.Tasks,
            _space.Ideas,
            _space.Locks,
            new DoneWhenProposer(_space.Tasks, _space.Ideas, projects, _space.Workspace, launcher, _space.Paths),
            projects,
            launcher,
            _space.Paths);
    }

    private static IdeaReply Plan(params string[] doneWhen) => new()
    {
        Contract = IdeaSchema.Version,
        Understanding = "A page showing what is up.",
        Plan = new IdeaReply.ReplyPlan
        {
            Title = "Status page",
            Layers =
            [
                new IdeaReply.ReplyLayer
                {
                    Name = "Storage",
                    Options =
                    [
                        new IdeaReply.ReplyOption { Title = "SQLite", Recommended = true },
                        new IdeaReply.ReplyOption { Title = "Postgres" },
                    ],
                },
            ],
            Additions = [new IdeaReply.ReplyAddition { Title = "Alerts", Why = "So you hear first." }],
            Project = new IdeaReply.ReplyProject { Slug = "website" },
            DoneWhen = [.. doneWhen],
        },
    };

    /// <summary>An idea on the website list with a plan, as proposed, and no done-when unless given.</summary>
    private async Task<IdeaPlace> PlannedAsync(string id, params string[] doneWhen)
    {
        (await _space.Ideas.CaptureAsync("website", $"The {id} idea", "nigel", id)).Succeeded.Should().BeTrue();
        var place = new IdeaPlace("website", id);
        var recorded = await _space.Ideas.RecordReplyAsync(place, Plan(doneWhen));
        recorded.Succeeded.Should().BeTrue(recorded.Error);

        return place;
    }

    private async Task<TaskItem> TaskAsync(string id) =>
        (await _space.Tasks.ListAsync("website")).Value!.Single(item => item.Id == id);

    private async Task<IdeaRecord> RecordAsync(IdeaPlace place) => (await _space.Ideas.ReadAsync(place)).Value!;

    private string Files() => string.Join(
        "\n---\n",
        Directory.EnumerateFiles(_space.Workspace.LocalPath, "*.yaml", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => path + "\n" + File.ReadAllText(path)));

    [Fact]
    public void The_manifest_names_every_plan_field_the_schema_has_and_nothing_else()
    {
        using var schema = JsonDocument.Parse(IdeaSchema.Version3);
        var fields = schema.RootElement.GetProperty("properties").GetProperty("plan").GetProperty("properties")
            .EnumerateObject().Select(property => property.Name);

        IdeaContract.PlanFields.Select(field => field.Name).Should().Equal(fields);
    }

    [Fact]
    public async Task A_plan_without_a_done_when_is_marked_missing_and_one_with_it_or_without_a_plan_is_not()
    {
        await _space.ProjectAsync("website");
        var bare = await PlannedAsync("bare");
        var whole = await PlannedAsync("whole", "It is live", "It alerts");
        (await _space.Ideas.CaptureAsync("website", "Still a thought", "nigel", "thought")).Succeeded.Should().BeTrue();

        ContractBackfill.MarkOf(await RecordAsync(bare)).Should().Be(ContractMark.Missing);
        ContractBackfill.MarkOf(await RecordAsync(whole)).Should().Be(ContractMark.None);
        ContractBackfill.MarkOf(await RecordAsync(new IdeaPlace("website", "thought"))).Should().Be(ContractMark.None, "an idea still in questioning is left out");

        var listed = (await _space.Ideas.ListAsync()).Value!.ToDictionary(idea => idea.Place.Id, idea => idea.Mark);
        listed["bare"].Should().Be(ContractMark.Missing);
        listed["whole"].Should().Be(ContractMark.None);
    }

    [Fact]
    public async Task Only_open_and_blocked_work_without_a_done_when_is_marked()
    {
        await _space.ProjectAsync("website");
        await _space.Tasks.DeclareAsync("website", "open", TaskState.Open, "nigel", "Open");
        await _space.Tasks.DeclareAsync("website", "blocked", TaskState.Blocked, "nigel", "Blocked");
        await _space.Tasks.DeclareAsync("website", "doing", TaskState.Doing, "nigel", "Doing");
        await _space.Tasks.DeclareAsync("website", "finished", TaskState.Open, "nigel", "Has one", doneWhen: ["It works", "It ships"]);
        await _space.Tasks.DeclareAsync("website", "thought", TaskState.Open, "nigel", "A thought", kind: TaskKind.Idea);

        var marks = (await _space.Tasks.ListAsync("website")).Value!.ToDictionary(item => item.Id, ContractBackfill.MarkOf);

        marks.Should().BeEquivalentTo(new Dictionary<string, ContractMark>
        {
            ["open"] = ContractMark.Missing,
            ["blocked"] = ContractMark.Missing,
            ["doing"] = ContractMark.None,
            ["finished"] = ContractMark.None,
            ["thought"] = ContractMark.None,
        });
    }

    [Fact]
    public async Task A_dry_run_lists_each_gap_and_skip_with_its_reason_counts_the_calls_and_changes_nothing()
    {
        await _space.ProjectAsync("website");
        await PlannedAsync("bare");
        var asking = await PlannedAsync("asking");
        (await _space.Ideas.RecordReplyAsync(asking, new IdeaReply
        {
            Contract = IdeaSchema.Version,
            Questions = [new IdeaReply.ReplyQuestion { Question = "Public?" }],
        })).Succeeded.Should().BeTrue();
        await _space.Tasks.DeclareAsync("website", "open", TaskState.Open, "nigel", "Open");
        await _space.Tasks.DeclareAsync("website", "blocked", TaskState.Blocked, "nigel", "Blocked");
        await _space.Tasks.DeclareAsync("website", "doing", TaskState.Doing, "nigel", "Doing");
        var before = Files();
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Good));

        var run = await Backfiller(launcher).RunAsync(new BackfillRequest(["website"], DryRun: true));

        run.Succeeded.Should().BeTrue(run.Error);
        var outcome = run.Value!;
        outcome.Found.Select(item => (item.Id, item.Fills)).Should().BeEquivalentTo(
            [("bare", true), ("asking", false), ("open", true), ("blocked", true)]);
        outcome.Found.Single(item => item.Id == "asking").Skipped.Should().Contain("questions waiting on an answer");
        outcome.Found.Should().OnlyContain(item => item.Missing.SequenceEqual(new[] { IdeaContract.DoneWhen }));
        outcome.Calls.Should().Be(3);
        outcome.Results.Should().BeEmpty();

        launcher.Started.Should().Be(0, "a dry run asks nothing");
        Files().Should().Be(before, "a dry run changes nothing");
    }

    [Fact]
    public async Task An_idea_with_answers_its_plan_has_not_taken_in_is_skipped_so_a_new_revision_cannot_hide_them()
    {
        await _space.ProjectAsync("website");
        var place = await PlannedAsync("answered");
        (await _space.Ideas.RecordReplyAsync(place, new IdeaReply
        {
            Contract = IdeaSchema.Version,
            Questions = [new IdeaReply.ReplyQuestion { Question = "Public?" }],
        })).Succeeded.Should().BeTrue();
        var question = (await RecordAsync(place)).Rounds.Last().Questions.Single().Id;
        (await _space.Ideas.AnswerAsync(place, question, "Yes")).Succeeded.Should().BeTrue();
        IdeaWork.StageOf(await RecordAsync(place)).Should().Be(IdeaStage.Ready);

        var run = await Backfiller(new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Good)))
            .RunAsync(new BackfillRequest(["website"]));

        run.Value!.Found.Single().Skipped.Should().Contain("changes the plan has not taken in");
        run.Value.Calls.Should().Be(0);
        IdeaWork.StageOf(await RecordAsync(place)).Should().Be(IdeaStage.Ready);
    }

    [Fact]
    public async Task A_pass_fills_only_the_missing_field_as_a_new_revision_marked_gap_filled_until_it_is_accepted()
    {
        await _space.ProjectAsync("website");
        var place = await PlannedAsync("bare");
        var before = (await RecordAsync(place)).Plan!;
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Good));

        var run = await Backfiller(launcher).RunAsync(new BackfillRequest(["website"]));

        run.Value!.Results.Single().Filled.Should().BeTrue(run.Value.Results.Single().Error);
        var after = (await RecordAsync(place)).Plan!;
        after.Revision.Should().Be(before.Revision + 1);
        after.DoneWhen.Should().Equal("The status page lists every service", "An alert reaches the phone within a minute");
        after.GapFilled.Should().Equal(IdeaContract.DoneWhen);
        after.Should().BeEquivalentTo(
            before,
            options => options.Excluding(plan => plan.Revision).Excluding(plan => plan.ProposedUtc)
                .Excluding(plan => plan.DoneWhen).Excluding(plan => plan.GapFilled),
            "nothing but the missing field changes");
        launcher.Pipe!.Written.ToString().Should().Contain("Do not change anything else");
        IdeaWork.StageOf(await RecordAsync(place)).Should().Be(IdeaStage.Proposed);
        ContractBackfill.MarkOf(await RecordAsync(place)).Should().Be(ContractMark.None);

        (await _space.Ideas.AcceptAsync(place, "website", "nigel")).Succeeded.Should().BeTrue();

        (await RecordAsync(place)).Plan!.GapFilled.Should().BeEmpty("accepting is where the person confirms them");
        (await TaskAsync("bare")).DoneWhen.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_task_gets_a_held_proposal_not_a_done_when_and_nothing_about_its_claim_changes()
    {
        await _space.ProjectAsync("website");
        await _space.Tasks.DeclareAsync("website", "open", TaskState.Open, "nigel", "Open");
        var before = await TaskAsync("open");

        var run = await Backfiller(new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Good)))
            .RunAsync(new BackfillRequest(["website"]));

        run.Value!.Results.Single().Filled.Should().BeTrue(run.Value.Results.Single().Error);
        var after = await TaskAsync("open");
        after.DoneWhen.Should().BeEmpty("nothing unconfirmed becomes a finish line");
        after.Proposal!.DoneWhen.Should().HaveCount(2);
        after.Proposal.Why.Should().Be("From the plan's two layers.");
        after.DeclaredBy.Should().Be(before.DeclaredBy);
        after.History.Should().HaveCount(before.History.Count);
        ContractBackfill.MarkOf(after).Should().Be(ContractMark.ProposalWaiting);

        var again = await Backfiller(new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Good)))
            .RunAsync(new BackfillRequest(["website"], DryRun: true));

        again.Value!.Found.Single().Skipped.Should().Contain("waiting for someone to confirm");
        again.Value.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(TaskState.Doing, false)]
    [InlineData(TaskState.Done, false)]
    [InlineData(TaskState.Dropped, false)]
    [InlineData(TaskState.Open, true)]
    public async Task A_held_proposal_goes_once_the_work_starts_or_ends_and_stays_when_a_blocked_task_reopens(TaskState next, bool kept)
    {
        await _space.ProjectAsync("website");
        await _space.Tasks.DeclareAsync("website", "stuck", TaskState.Blocked, "nigel", "Stuck");
        (await _space.Tasks.HoldProposalAsync("website", "stuck", new TaskProposal { DoneWhen = ["It works", "It ships"] })).Succeeded.Should().BeTrue();

        await _space.Tasks.DeclareAsync("website", "stuck", next, "nigel");

        ((await TaskAsync("stuck")).Proposal is not null).Should().Be(kept);
    }

    [Fact]
    public async Task Writing_a_done_when_by_hand_records_it_and_throws_the_proposal_away()
    {
        await _space.ProjectAsync("website");
        await _space.Tasks.DeclareAsync("website", "open", TaskState.Open, "nigel", "Open");
        await _space.Tasks.HoldProposalAsync("website", "open", new TaskProposal { DoneWhen = ["It works", "It ships"] });

        await _space.Tasks.DeclareAsync("website", "open", TaskState.Open, "nigel", doneWhen: ["It works, edited", "It ships"]);

        var task = await TaskAsync("open");
        task.DoneWhen.Should().Equal("It works, edited", "It ships");
        task.Proposal.Should().BeNull();
        ContractBackfill.MarkOf(task).Should().Be(ContractMark.None);
    }

    [Fact]
    public async Task A_failure_is_recorded_on_the_record_keeps_it_missing_and_is_tried_again_by_the_next_pass()
    {
        await _space.ProjectAsync("website");
        var place = await PlannedAsync("bare");
        await _space.Tasks.DeclareAsync("website", "open", TaskState.Open, "nigel", "Open");
        var failing = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(null, error: true));

        var run = await Backfiller(failing).RunAsync(new BackfillRequest(["website"]));

        run.Value!.Results.Should().HaveCount(2).And.OnlyContain(result => !result.Filled);
        (await RecordAsync(place)).GapFillFailure!.Error.Should().NotBeEmpty();
        (await RecordAsync(place)).Failure.Should().BeNull("a gap-fill is not a round");
        (await TaskAsync("open")).Proposal!.Error.Should().NotBeEmpty();
        ContractBackfill.MarkOf(await TaskAsync("open")).Should().Be(ContractMark.Missing);
        ContractBackfill.MarkOf(await RecordAsync(place)).Should().Be(ContractMark.Missing);

        var next = await Backfiller(new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Good)))
            .RunAsync(new BackfillRequest(["website"], DryRun: true));

        next.Value!.Found.Should().HaveCount(2).And.OnlyContain(item => item.Fills && item.LastFailure != null);
        next.Value.Calls.Should().Be(2);

        var retried = await Backfiller(new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Good)))
            .RunAsync(new BackfillRequest(["website"]));

        retried.Value!.Results.Should().OnlyContain(result => result.Filled);
        (await RecordAsync(place)).GapFillFailure.Should().BeNull();
        (await TaskAsync("open")).Proposal!.Error.Should().BeEmpty();
    }

    [Fact]
    public async Task A_limit_caps_the_records_asked_about_and_leaves_the_rest_for_a_later_pass()
    {
        await _space.ProjectAsync("website");
        await _space.Tasks.DeclareAsync("website", "one", TaskState.Open, "nigel", "One");
        await _space.Tasks.DeclareAsync("website", "two", TaskState.Open, "nigel", "Two");
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Good));

        var run = await Backfiller(launcher).RunAsync(new BackfillRequest(["website"], Limit: 1));

        run.Value!.Found.Should().HaveCount(2);
        run.Value.Calls.Should().Be(1);
        launcher.Started.Should().Be(1);
        (await TaskAsync("one")).Proposal.Should().NotBeNull();
        (await TaskAsync("two")).Proposal.Should().BeNull();
    }

    [Fact]
    public async Task A_batch_from_outside_cannot_hold_a_proposal()
    {
        await _space.ProjectAsync("website");
        await _space.Tasks.DeclareAsync("website", "open", TaskState.Open, "nigel", "Open");

        TaskOperations.Parse("""[{"op":"hold","id":"open"}]""", "website", "nigel").Failed.Should().BeTrue();

        var batch = await _space.Tasks.ApplyAsync(
            [new TaskOperation("hold", "website", "open", Proposal: new TaskProposal { DoneWhen = ["It works", "It ships"] })]);

        batch.Value!.Outcomes.Single().Applied.Should().BeFalse();
        (await TaskAsync("open")).Proposal.Should().BeNull();
    }
}
