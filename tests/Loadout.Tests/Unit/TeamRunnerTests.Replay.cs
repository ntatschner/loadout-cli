using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Loadout.Agents.Teams;
using Loadout.Core.Git;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Replays: whole scripted runs, recorded and compared line for line with a
/// record committed beside this file.
/// </summary>
/// <remarks>
/// <para>
/// The other tests here each ask one question of a run. These ask none: they
/// pin everything a run wrote down - the journal, the task declarations, the
/// git calls, the run's directory and the outcome - so that a change meant to
/// move code and not behaviour can be shown to have moved nothing.
/// </para>
/// <para>
/// Each script is copied from the test named in its comment. A new baseline is
/// written with LOADOUT_REPLAY_ACCEPT=1, and a diff of the expected files is
/// then the whole account of what changed.
/// </para>
/// </remarks>
public sealed partial class TeamRunnerTests
{
    private readonly SteppingClock _clock = new();
    private readonly FakeTasks _replayTasks = new();
    private readonly List<string> _gitCalls = [];

    private static readonly JsonSerializerOptions Line = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [Theory]
    [Trait("Category", "Replay")]
    [InlineData("one-round-done")]
    [InlineData("gated-merge")]
    [InlineData("permission-answered")]
    [InlineData("quiet-rounds")]
    [InlineData("gate-reminded")]
    [InlineData("gate-refused")]
    [InlineData("merge-conflict-resolved")]
    [InlineData("decision-question")]
    [InlineData("budget-raised")]
    [InlineData("resumed")]
    [InlineData("folded-asks")]
    [InlineData("readings-accepted")]
    [InlineData("manual-brief-revised")]
    [InlineData("merge-conflicted-twice")]
    [InlineData("criteria-proposed")]
    [InlineData("goal-unmet")]
    [InlineData("node-doing")]
    [InlineData("request-refused")]
    [InlineData("report-unreadable")]
    [InlineData("run-told")]
    [InlineData("readings-sent-back")]
    [InlineData("run-paused")]
    [InlineData("node-told-and-said")]
    [InlineData("node-failed")]
    [InlineData("merge-failed")]
    [InlineData("parallel-workers")]
    public async Task TeamRunnerReplay(string scenario)
    {
        var record = scenario switch
        {
            "one-round-done" => await OneRoundDoneAsync(),
            "gated-merge" => await GatedMergeAsync(),
            "permission-answered" => await PermissionAnsweredAsync(),
            "quiet-rounds" => await QuietRoundsAsync(),
            "gate-reminded" => await GateRemindedAsync(),
            "gate-refused" => await GateRefusedAsync(),
            "merge-conflict-resolved" => await MergeConflictResolvedAsync(),
            "decision-question" => await DecisionQuestionAsync(),
            "budget-raised" => await BudgetRaisedAsync(),
            "resumed" => await ResumedAsync(),
            "folded-asks" => await FoldedAsksAsync(),
            "readings-accepted" => await ReadingsAcceptedAsync(),
            "manual-brief-revised" => await ManualBriefRevisedAsync(),
            "merge-conflicted-twice" => await MergeConflictedTwiceAsync(),
            "criteria-proposed" => await CriteriaProposedAsync(),
            "goal-unmet" => await GoalUnmetAsync(),
            "node-doing" => await NodeDoingAsync(),
            "request-refused" => await RequestRefusedAsync(),
            "report-unreadable" => await ReportUnreadableAsync(),
            "run-told" => await RunToldAsync(),
            "readings-sent-back" => await ReadingsSentBackAsync(),
            "run-paused" => await RunPausedAsync(),
            "node-told-and-said" => await NodeToldAndSaidAsync(),
            "node-failed" => await NodeFailedAsync(),
            "merge-failed" => await MergeFailedAsync(),
            "parallel-workers" => PerNode(await ParallelWorkersAsync()),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "no such scenario"),
        };

        var path = ExpectedPath(scenario);

        if (Environment.GetEnvironmentVariable("LOADOUT_REPLAY_ACCEPT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, record);
            return;
        }

        File.Exists(path).Should().BeTrue($"{scenario} needs a baseline; write one with LOADOUT_REPLAY_ACCEPT=1");

        var expected = (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n").Split('\n');

        var actual = record.Split('\n');

        // The first line that differs, said plainly: a whole record printed
        // twice tells nobody where to look.
        var first = Enumerable.Range(0, Math.Max(actual.Length, expected.Length))
            .FirstOrDefault(i => i >= actual.Length || i >= expected.Length || actual[i] != expected[i], -1);

        first.Should().Be(-1,
            $"line {first + 1} of {Path.GetFileName(path)} reads{Environment.NewLine}"
            + $"{(first >= 0 && first < expected.Length ? expected[first] : "(nothing)")}{Environment.NewLine}"
            + $"but the run wrote{Environment.NewLine}"
            + $"{(first >= 0 && first < actual.Length ? actual[first] : "(nothing)")}{Environment.NewLine}");
    }

    // ---------------------------------------------------------- scenarios

    /// <summary>S1. Copied from A_folded_decision_keeps_the_time_it_was_made's lead, without its side file.</summary>
    private async Task<string> OneRoundDoneAsync()
    {
        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadDone(), 0.04m));

        return await RecordAsync(await ReplayRunAsync());
    }

    /// <summary>
    /// S2. ScriptAGatedRun, with FakeGit, and the reviewer's task naming the
    /// implementer's branch as in
    /// A_reviewer_asked_about_a_branch_of_this_run_is_started_in_its_worktree,
    /// so the reviewer is started where the work is. The reviewer and the
    /// verifier are asked for in separate rounds rather than together: two
    /// nodes in one round run at once, and their lines interleave differently
    /// from run to run.
    /// </summary>
    private async Task<string> GatedMergeAsync()
    {
        var git = new FakeGit(Path.Combine(_root, "repo"));

        ScriptAGatedReplay(git);

        return await RecordAsync(await ReplayRunAsync(git: git));
    }

    /// <summary>The script S2 and S7 share: one implementer through the full merge gate, a node a round.</summary>
    private void ScriptAGatedReplay(FakeGit git)
    {
        string? branch = null;

        var accepted = new Report(
            "reviewer", ReportStatus.Done, "Fine.",
            [new ReportDeliverable(DeliverableKind.Decision, "accept", "a4f21c9")], [Passed], []);

        var verified = new Report(
            "verifier", ReportStatus.Done, "Ran it.",
            [new ReportDeliverable(DeliverableKind.Decision, "accept", "a4f21c9")], [Passed], []);

        _launcher.BeforeStart = directory =>
        {
            if (branch is not null)
            {
                return;
            }

            branch = $"teams-{Path.GetFileName(directory)}-implementer";
            git.Linked.Add(new GitWorktree(Path.Combine(_root, "trees", branch), branch, IsPrimary: false));

            _launcher.Script(
                "role.project-lead",
                Init("lead-1"),
                Result(LeadRequests(AskImplementer()), 0.05m),
                Result(LeadRequests(
                    new ReportRequest("reviewer", $"Review commit a4f21c9 on branch {branch}.", DeliverableKind.Decision)), 0.06m),
                Result(LeadRequests(new ReportRequest("verifier", "verify it", DeliverableKind.Decision)), 0.07m),
                Result(LeadDone(), 0.09m),
                Result(LeadDone(), 0.09m));
        };

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.03m));
        _launcher.Script("role.reviewer", Init("rev-1"), Result(accepted, 0.02m));
        _launcher.Script("role.verifier", Init("ver-1"), Result(verified, 0.02m));
    }

    /// <summary>
    /// S3. Copied from A_node_stopped_on_a_permission_question_is_answered_by_the_person,
    /// the says=true case. The question is dated at a fixed past time rather
    /// than now, so the record does not carry the wall clock.
    /// </summary>
    private async Task<string> PermissionAnsweredAsync()
    {
        var directory = string.Empty;

        _launcher.BeforeStart = where => directory = where;

        _console.Confirm = _ => true;

        _launcher.Hold("role.project-lead", async () =>
        {
            var asked = await NodePermissions
                .AskAsync(directory, new PendingAsk(
                    "lead-1", "lead", "role.project-lead", "Bash", "dotnet test",
                    new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero)),
                    TimeProvider.System)
                .WaitAsync(TimeSpan.FromSeconds(30));

            asked.Should().NotBeNull("the coordinator never answered the question");
            asked!.Allowed.Should().BeTrue();
        });

        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadDone(), 0.05m));

        return await RecordAsync(await ReplayRunAsync());
    }

    /// <summary>S4. Copied from Two_rounds_without_a_request_or_a_finish_stop_the_run.</summary>
    private async Task<string> QuietRoundsAsync()
    {
        var idle = new Report("lead", ReportStatus.NeedsDecision, "thinking", [], [], [],
            Questions: [new ReportQuestion("Continue?", ["yes", "no"], "yes")]);

        _launcher.Script("role.project-lead", Init("lead-1"), Result(idle, 0.01m), Result(idle, 0.02m), Result(idle, 0.03m));

        return await RecordAsync(await ReplayRunAsync(autonomy: "autonomous"));
    }

    /// <summary>S5. Copied from A_lead_that_requests_a_worker_gets_its_report_back_and_finishes: the gate reminder, no git.</summary>
    private async Task<string> GateRemindedAsync()
    {
        _launcher.Script(
            "role.project-lead",
            Init("lead-1"), Result(LeadRequests(AskImplementer()), 0.05m), Result(LeadDone(), 0.09m), Result(LeadDone(), 0.09m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.03m));

        return await RecordAsync(await ReplayRunAsync());
    }

    /// <summary>S6. Copied from Nothing_is_merged_until_every_node_the_gate_names_has_accepted.</summary>
    private async Task<string> GateRefusedAsync()
    {
        var returned = new Report(
            "reviewer", ReportStatus.Done, "Two findings.",
            [new ReportDeliverable(DeliverableKind.Decision, "return", "a4f21c9")], [Passed], []);

        _launcher.Script(
            "role.project-lead",
            Init("lead-1"),
            Result(LeadRequests(AskImplementer()), 0.05m),
            Result(LeadRequests(new ReportRequest("reviewer", "review it", DeliverableKind.Decision)), 0.06m),
            Result(LeadDone(), 0.09m), Result(LeadDone(), 0.09m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.03m));
        _launcher.Script("role.reviewer", Init("rev-1"), Result(returned, 0.02m));

        return await RecordAsync(await ReplayRunAsync());
    }

    /// <summary>
    /// S7. Copied from A_branch_that_conflicts_goes_back_to_the_node_that_wrote_it,
    /// over S2's script rather than ScriptAGatedRun for the reason S2 gives.
    /// </summary>
    private async Task<string> MergeConflictResolvedAsync()
    {
        var git = new FakeGit(Path.Combine(_root, "repo"));
        git.NextMerge(new GitMerge(Merged: false, FastForward: false, ["docs/commands.md"]));

        ScriptAGatedReplay(git);

        // The implementer is briefed a second time, to resolve.
        _launcher.Script("role.implementer", Init("impl-2"), Result(ImplementerDone(), 0.04m));

        return await RecordAsync(await ReplayRunAsync(git: git));
    }

    /// <summary>S8. Copied from A_question_is_put_to_the_person_and_the_answer_goes_back_to_the_lead.</summary>
    private async Task<string> DecisionQuestionAsync()
    {
        _console.Decide = q => q.Options[1];

        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadAsks(), 0.05m), Result(LeadDone(), 0.09m));

        return await RecordAsync(await ReplayRunAsync());
    }

    /// <summary>S9. Copied from A_watched_run_given_more_carries_on_and_says_so.</summary>
    private async Task<string> BudgetRaisedAsync()
    {
        var team = await IteratingProjectAsync();

        team.Rules.Budget.Usd = 0.10m;

        _console.Decide = question => question.Recommendation;

        _launcher.Script(
            "role.project-lead",
            Init("lead-1"),
            Result(LeadRequests(AskImplementer()), 0.05m),
            Result(LeadDone(), 0.20m),
            Result(LeadDone(), 0.30m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.03m));

        return await RecordAsync(await ReplayRunAsync(team));
    }

    /// <summary>
    /// S10. Copied from An_ended_run_is_picked_up_where_it_stopped_with_its_lead_s_own_session,
    /// with SpentAsync's first run inlined. Both runs write one journal.
    /// </summary>
    private async Task<string> ResumedAsync()
    {
        var team = await IteratingProjectAsync();

        team.Rules.Budget.Usd = 0.10m;
        _console.CanAsk = false;

        _launcher.Script(
            "role.project-lead",
            Init("lead-1"),
            Result(LeadRequests(AskImplementer()), 0.05m),
            Result(LeadDone(), 0.20m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.03m));

        var first = await ReplayRunAsync(team);

        await RunControl.SetBudgetAsync(first.Directory!, 5m);
        await RunControl.StopAsync(first.Directory!);

        _launcher.Script(
            "role.project-lead",
            Init("lead-1"),
            Result(LeadDone(), 0.02m),
            Result(LeadDone(), 0.03m));

        var picked = await ReplayRunAsync(team, resuming: first.RunId, message: "Finish it off.", rounds: 10);

        return await RecordAsync([first, picked]);
    }

    /// <summary>
    /// S11. The side files of What_a_node_asked_for_reaches_the_run_and_the_person
    /// and A_folded_decision_keeps_the_time_it_was_made, both in one, so the
    /// refused question and the explicit time are each pinned.
    /// </summary>
    private async Task<string> FoldedAsksAsync()
    {
        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadDone(), 0.04m));

        _launcher.BeforeStart = directory => File.AppendAllText(
            Path.Combine(directory, NodePermissions.AskedFileName("lead")),
            """{"at":"2026-09-16T10:00:00+00:00","node":"lead","tool":"WebFetch","target":"https://example.invalid","allowed":false,"rule":null,"reason":"nothing allows it"}"""
                + "\n"
                + """{"at":"2026-09-16T10:00:00+00:00","node":"lead","tool":"Bash","target":"dotnet test","allowed":true,"rule":null,"reason":"allowed for this call only"}"""
                + "\n");

        return await RecordAsync(await ReplayRunAsync());
    }

    /// <summary>S12. Copied from Before_any_worker_starts_the_lead_s_readings_go_to_the_person.</summary>
    private async Task<string> ReadingsAcceptedAsync()
    {
        _launcher.Script("role.project-lead", Init("lead-1"),
            Result(LeadReadsAndRequests(), 0.01m), Result(LeadDoneCovering(Mine), 0.02m), Result(LeadDoneCovering(Mine), 0.02m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.01m));

        return await RecordAsync(await ReplayRunAsync(criteria: [Mine]));
    }

    /// <summary>S13. Copied from A_brief_that_was_changed_says_so_and_says_what_it_was.</summary>
    private async Task<string> ManualBriefRevisedAsync()
    {
        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadRequests(AskImplementer()), 0.05m),
            Result(LeadDone(), 0.09m), Result(LeadDone(), 0.09m));
        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.03m));

        _console.Revise = _ => "Something else entirely.";

        return await RecordAsync(await ReplayRunAsync(autonomy: "manual"));
    }

    /// <summary>
    /// S7b. Copied from A_branch_that_conflicts_twice_is_left_alone_and_said_so,
    /// over S2's script, so the second merge.conflicted and its again=true are pinned.
    /// </summary>
    private async Task<string> MergeConflictedTwiceAsync()
    {
        var git = new FakeGit(Path.Combine(_root, "repo"));
        git.NextMerge(new GitMerge(Merged: false, FastForward: false, ["docs/commands.md"]));
        git.NextMerge(new GitMerge(Merged: false, FastForward: false, ["docs/commands.md"]));

        ScriptAGatedReplay(git);
        _launcher.Script("role.implementer", Init("impl-2"), Result(ImplementerDone(), 0.04m));

        return await RecordAsync(await ReplayRunAsync(git: git));
    }

    /// <summary>S15. Copied from With_the_project_s_criteria_and_none_of_its_own_the_lead_still_proposes: criteria.defaults, criteria.agreed.</summary>
    private async Task<string> CriteriaProposedAsync()
    {
        _launcher.Script(
            "role.project-lead",
            Init("lead-1"),
            Result(LeadProposes(AskImplementer()) with { Readings = Readings(Suite) }, 0.01m),
            Result(LeadDoneCovering([Suite, .. Proposal]), 0.02m),
            Result(LeadDoneCovering([Suite, .. Proposal]), 0.02m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.01m));

        return await RecordAsync(await ReplayRunAsync(standing: Standing(Suite)));
    }

    /// <summary>S16. Copied from A_done_that_ignores_the_project_s_criteria_is_not_a_done: goal.unmet.</summary>
    private async Task<string> GoalUnmetAsync()
    {
        _launcher.Script(
            "role.project-lead",
            Init("lead-1"),
            Result(LeadDoneCovering("the team criterion"), 0.01m),
            Result(LeadDoneCovering("the team criterion"), 0.02m),
            Result(LeadDoneCovering("the team criterion"), 0.02m));

        return await RecordAsync(await ReplayRunAsync(await JudgedTeamAsync("the team criterion"), standing: Standing(Suite)));
    }

    /// <summary>S17. Copied from A_node_says_what_it_is_doing_while_the_turn_is_still_running: node.doing, throttled by the stepping clock.</summary>
    private async Task<string> NodeDoingAsync()
    {
        _launcher.Script(
            "role.project-lead",
            Init("lead-1"),
            Using("Read", """{"file_path":"docs/commands.md"}"""),
            Using("Bash", """{"command":"dotnet test"}"""),
            Result(LeadDone(), 0.04m));

        return await RecordAsync(await ReplayRunAsync());
    }

    /// <summary>S18. Copied from A_lead_that_asks_for_a_node_it_may_not_is_refused_and_both_the_person_and_the_lead_are_told: request.refused.</summary>
    private async Task<string> RequestRefusedAsync()
    {
        var lead = LeadRequests(new ReportRequest("verifier", "verify", DeliverableKind.Decision)) with { Status = ReportStatus.Blocked };
        var team = await IteratingProjectAsync();
        team.Nodes["lead"].Delegates = ["implementer"];

        _launcher.Script("role.project-lead", Init("lead-1"), Result(lead, 0.05m), Result(LeadDone(), 0.09m));

        return await RecordAsync(await ReplayRunAsync(team));
    }

    /// <summary>S19. Copied from A_node_that_answers_in_prose_instead_of_a_report_has_what_it_said_passed_on: report.unreadable.</summary>
    private async Task<string> ReportUnreadableAsync()
    {
        const string Limit = "You've reached your Fable limit. Switch to another model to continue.";

        _launcher.Script(
            "role.project-lead",
            Init("lead-1"), Result(LeadRequests(AskImplementer()), 0.05m),
            Say(Limit), NoReport(0.05m), Say(Limit), NoReport(0.05m));

        _launcher.Script("role.implementer", Init("impl-1"), Say(Limit), NoReport(0m), Say(Limit), NoReport(0m));

        return await RecordAsync(await ReplayRunAsync());
    }

    /// <summary>S20. Copied from What_somebody_says_to_a_lead_reaches_it_before_its_next_round: run.told.</summary>
    private async Task<string> RunToldAsync()
    {
        _launcher.BeforeStart = where =>
            RunControl.SayAsync(where, "leave the tests alone").GetAwaiter().GetResult();

        _launcher.Script(
            "role.project-lead",
            Init("lead-1"),
            Result(LeadRequests(AskImplementer()), 0.01m),
            Result(LeadDone(), 0.01m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.01m));

        return await RecordAsync(await ReplayRunAsync(rounds: 3));
    }

    /// <summary>
    /// S21. Copied from Readings_sent_back_brief_nobody_and_the_lead_reads_again,
    /// without its count of the questions asked: reading.changed.
    /// </summary>
    private async Task<string> ReadingsSentBackAsync()
    {
        var first = true;
        var answer = _console.Decide;

        _console.Decide = question =>
        {
            if (first && question.Question.StartsWith("Before any worker starts", StringComparison.Ordinal))
            {
                first = false;

                return TeamRunner.ThinkAgain;
            }

            return answer(question);
        };

        _launcher.Script("role.project-lead", Init("lead-1"),
            Result(LeadReadsAndRequests("any test passes"), 0.01m),
            Result(LeadReadsAndRequests("only dates after --since are printed"), 0.02m),
            Result(LeadDoneCovering(Mine), 0.03m),
            Result(LeadDoneCovering(Mine), 0.03m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.01m));

        return await RecordAsync(await ReplayRunAsync(criteria: [Mine]));
    }

    /// <summary>
    /// S22. A_run_asked_to_stop_ends_after_the_round_it_is_in, held rather
    /// than stopped: run.paused, run.resumed. No test in TeamRunnerTests
    /// pauses a run, so this one is adapted rather than copied. The hold is
    /// lifted only once run.paused is in the journal, so the order is fixed.
    /// </summary>
    private async Task<string> RunPausedAsync()
    {
        var started = 0;
        Task lifted = Task.CompletedTask;

        _launcher.BeforeStart = where =>
        {
            if (Interlocked.Increment(ref started) != 2)
            {
                return;
            }

            RunControl.PauseAsync(where).GetAwaiter().GetResult();

            // Watching the console's note rather than the journal: the note
            // follows run.paused, and reading the journal while the run
            // appends to it makes the run's write fail, and it drops the line.
            lifted = Task.Run(async () =>
            {
                for (var waited = 0; waited < 600; waited++)
                {
                    lock (_console.Notes)
                    {
                        if (_console.Notes.Exists(n => n.StartsWith("Held.", StringComparison.Ordinal)))
                        {
                            break;
                        }
                    }

                    await Task.Delay(50);
                }

                RunControl.Resume(where);
            });
        };

        _launcher.Script(
            "role.project-lead",
            Init("lead-1"),
            Result(LeadRequests(AskImplementer()), 0.01m),
            Result(LeadDone(), 0.01m),
            Result(LeadDone(), 0.01m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.01m));

        var outcome = await ReplayRunAsync();

        await lifted;

        return await RecordAsync(outcome);
    }

    /// <summary>
    /// S23. S1's lead, with a message left for it and an account of its own
    /// progress already written, as the dashboard and loadout_progress leave
    /// them: node.told, node.said. No test in TeamRunnerTests covers either,
    /// so this is adapted from What_somebody_says_to_a_lead_reaches_it_before_its_next_round.
    /// Both are dated at a fixed past time, so the record does not carry the wall clock.
    /// A node's progress is folded only where it also left an asked file, so
    /// one allowed call from S11's side file is left beside it.
    /// </summary>
    private async Task<string> NodeToldAndSaidAsync()
    {
        var then = new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);

        _launcher.BeforeStart = where =>
        {
            File.AppendAllText(
                Path.Combine(where, NodePermissions.AskedFileName("lead")),
                """{"at":"2026-09-16T10:00:00+00:00","node":"lead","tool":"Bash","target":"dotnet test","allowed":true,"rule":null,"reason":"allowed for this call only"}"""
                    + "\n");
            NodeControl.SayAsync(where, "lead", "leave the tests alone", then).GetAwaiter().GetResult();
            NodeProgress.AppendAsync(where, new NodeSaid(then, "lead", "role.project-lead", 1, 2, "reading the brief"))
                .GetAwaiter().GetResult();
        };

        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadDone(), 0.04m));

        return await RecordAsync(await ReplayRunAsync());
    }

    /// <summary>
    /// S24. S5's script with no session for the implementer, so its launch
    /// fails: node.failed. No test in TeamRunnerTests fails a launch, so this
    /// is adapted from A_lead_that_requests_a_worker_gets_its_report_back_and_finishes.
    /// </summary>
    private async Task<string> NodeFailedAsync()
    {
        _launcher.Script(
            "role.project-lead",
            Init("lead-1"), Result(LeadRequests(AskImplementer()), 0.05m), Result(LeadDone(), 0.09m), Result(LeadDone(), 0.09m));

        return await RecordAsync(await ReplayRunAsync());
    }

    /// <summary>
    /// S25. S7 with a merge git refuses outright rather than one that
    /// conflicts: merge.failed. No test in TeamRunnerTests fails a merge, so
    /// this is adapted from A_branch_that_conflicts_goes_back_to_the_node_that_wrote_it.
    /// </summary>
    private async Task<string> MergeFailedAsync()
    {
        var git = new FakeGit(Path.Combine(_root, "repo"));
        git.NextMergeFails("fatal: not a git repository");

        ScriptAGatedReplay(git);

        return await RecordAsync(await ReplayRunAsync(git: git));
    }

    /// <summary>
    /// S14. Copied from Two_nodes_the_lead_asked_for_together_run_together:
    /// two implementers asked for in one round, each held until the other has
    /// arrived, so they are genuinely running at the same moment. Compared
    /// through <see cref="PerNode"/>, the one scenario that is not compared in
    /// exact order.
    /// </summary>
    private async Task<string> ParallelWorkersAsync()
    {
        var both = new TaskCompletionSource();
        var arrived = 0;

        _launcher.Hold("role.implementer", async () =>
        {
            if (Interlocked.Increment(ref arrived) == 2)
            {
                both.SetResult();
            }

            await both.Task.WaitAsync(TimeSpan.FromSeconds(30));
        });

        _launcher.Script(
            "role.project-lead",
            Init("lead-1"),
            Result(LeadRequests(
                AskImplementer() with { Node = "implementer/1" },
                AskImplementer() with { Node = "implementer/2" }), 0.05m),
            Result(LeadDone(), 0.09m),
            Result(LeadDone(), 0.09m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone() with { Node = "implementer/1" }, 0.03m));
        _launcher.Script("role.implementer", Init("impl-2"), Result(ImplementerDone() with { Node = "implementer/2" }, 0.03m));

        var outcome = await ReplayRunAsync();

        arrived.Should().Be(2, "both implementers were meant to be running at once");

        return await RecordAsync(outcome);
    }

    // ----------------------------------------------------------- the harness

    /// <summary>
    /// A record whose workers ran at the same time, put in an order that does
    /// not depend on how their lines happened to interleave.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the workers' own journal lines move, and only among themselves:
    /// they are gathered at the place the first of them was written, grouped
    /// by node in name order, each node's lines in the order that node wrote
    /// them. Every other line, the run's and the lead's included, stays where
    /// it was, so their order is still compared exactly.
    /// </para>
    /// <para>
    /// The workers' stepped times are blanked, and nothing else is: each
    /// worker's times depend on how many lines the other wrote first, so they
    /// differ from run to run whatever the order. A time that is not the
    /// stepping clock's, such as a folded question's, is kept.
    /// </para>
    /// </remarks>
    private static string PerNode(string record)
    {
        static string? Worker(string line) =>
            JsonDocument.Parse(line).RootElement is { ValueKind: JsonValueKind.Object } root
            && root.TryGetProperty("kind", out _)
            && root.TryGetProperty("node", out var node)
            && node.GetString() is { } name
            && name.Contains('/', StringComparison.Ordinal)
                ? name
                : null;

        var lines = record.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var workers = lines.Where(line => Worker(line) is not null).ToList();

        if (workers.Count == 0)
        {
            return record;
        }

        var block = workers
            .Select((line, index) => (Node: Worker(line)!, Index: index, Line: StepTime().Replace(line, "\"at\":\"<t>\"")))
            .OrderBy(one => one.Node, StringComparer.Ordinal)
            .ThenBy(one => one.Index)
            .Select(one => one.Line);

        var first = Array.FindIndex(lines, line => Worker(line) is not null);
        var others = lines.Where(line => Worker(line) is null).ToList();

        others.InsertRange(first, block);

        return string.Join('\n', others) + "\n";
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"""at"":""<t\+\d+>""")]
    private static partial System.Text.RegularExpressions.Regex StepTime();


    private async Task<TeamRunOutcome> ReplayRunAsync(
        TeamDefinition? team = null,
        string autonomy = "supervised",
        FakeGit? git = null,
        IReadOnlyList<string>? criteria = null,
        string? resuming = null,
        string? message = null,
        int rounds = 5,
        StandingCriteria? standing = null)
    {
        var result = await new TeamRunner(
            _launcher,
            _paths,
            _clock,
            git is null ? null : new FakeProjects("demo", Path.Combine(_root, "repo")),
            git is null ? null : RecordingGit.Over(git, _gitCalls),
            null,
            _replayTasks,
            timers: new SteppingClock()).RunAsync(
            new TeamRunRequest("demo", team ?? await IteratingProjectAsync(), await SpecialistsAsync(),
                "Add --since to loadout usage.", autonomy, MaxRounds: rounds, Offline: true,
                Criteria: criteria, Resuming: resuming, ResumeMessage: message, Standing: standing),
            _console);

        result.Succeeded.Should().BeTrue(result.Error);

        return result.Value!;
    }

    /// <summary>
    /// What one replay recorded, one JSON line each: the journal, the task
    /// declarations, the git calls, the run's directory, then each outcome.
    /// </summary>
    /// <param name="outcomes">Every run of the scenario, in order; they share one directory.</param>
    private async Task<string> RecordAsync(TeamRunOutcome[] outcomes)
    {
        var last = outcomes[^1];
        var directory = last.Directory!;
        var lines = new List<string>();

        lines.AddRange(await File.ReadAllLinesAsync(Path.Combine(directory, "journal.jsonl")));

        lines.AddRange(_replayTasks.Declared.Select(d => Json(new
        {
            task = new { slug = d.Slug, id = d.Id, state = d.State.ToString(), by = d.By, title = d.Title, note = d.Note },
        })));

        lines.AddRange(_gitCalls);

        var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(directory, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        lines.Add(Json(new { files }));

        foreach (var file in files.Where(f =>
            f.StartsWith("brief-", StringComparison.Ordinal)
            || f.StartsWith("report-", StringComparison.Ordinal)
            || f == "final-report.json"))
        {
            // Normalised before it is quoted, or a path the file escapes once
            // is escaped twice here and the root is no longer found.
            var content = ReplayNormaliser.Normalise(
                await File.ReadAllTextAsync(Path.Combine(directory, file)), last.RunId, _root);

            lines.Add(Json(new { file, content }));
        }

        lines.AddRange(outcomes.Select(o => Json(new
        {
            outcome = new
            {
                ended = o.Ended,
                rounds = o.Rounds,
                cost = o.CostUsd,
                merged = o.Merged,
                branches = o.Branches?.OrderBy(b => b.Key, StringComparer.Ordinal).Select(b => new[] { b.Key, b.Value }),
                warnings = o.Warnings.Distinct(StringComparer.Ordinal).OrderBy(w => w, StringComparer.Ordinal),
            },
        })));

        var record = new StringBuilder();

        foreach (var line in lines)
        {
            record.Append(ReplayNormaliser.Normalise(line, last.RunId, _root)).Append('\n');
        }

        return record.ToString();
    }

    private Task<string> RecordAsync(TeamRunOutcome outcome) => RecordAsync([outcome]);

    private static string Json(object value) => JsonSerializer.Serialize(value, Line);

    private static string ExpectedPath(string scenario, [CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Replay", scenario + ".expected.jsonl");
}

/// <summary>
/// FakeGit, with every call that changes something written down in the order
/// it was made.
/// </summary>
/// <remarks>
/// A proxy rather than a second fake, so what answers is still FakeGit and
/// only the record is added. Public because the proxy is generated outside
/// this assembly.
/// </remarks>
public class RecordingGit : DispatchProxy
{
    private static readonly HashSet<string> Changes = new(StringComparer.Ordinal)
    {
        nameof(IGitManager.MergeAsync),
        nameof(IGitManager.DeleteMergedBranchAsync),
        nameof(IGitManager.RemoveWorktreeAsync),
        nameof(IGitManager.AddWorktreeAsync),
    };

    private IGitManager _inner = null!;
    private List<string> _calls = null!;

    internal static IGitManager Over(IGitManager inner, List<string> calls)
    {
        var proxy = Create<IGitManager, RecordingGit>();
        var recording = (RecordingGit)(object)proxy;

        recording._inner = inner;
        recording._calls = calls;

        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is not null && Changes.Contains(targetMethod.Name))
        {
            var said = (args ?? []).Where(a => a is not CancellationToken).ToArray();

            lock (_calls)
            {
                _calls.Add(JsonSerializer.Serialize(new { git = targetMethod.Name, args = said }));
            }
        }

        try
        {
            return targetMethod!.Invoke(_inner, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
