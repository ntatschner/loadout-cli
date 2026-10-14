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
    //
    // S14, two workers asked for together (Two_nodes_the_lead_asked_for_together_run_together),
    // is left out. Grouped by node it still differs from run to run: each
    // node's stepped times depend on how the other's lines interleaved, and
    // dropping the times to make it pass would loosen every comparison.

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

    // ----------------------------------------------------------- the harness

    private async Task<TeamRunOutcome> ReplayRunAsync(
        TeamDefinition? team = null,
        string autonomy = "supervised",
        FakeGit? git = null,
        IReadOnlyList<string>? criteria = null,
        string? resuming = null,
        string? message = null,
        int rounds = 5)
    {
        var result = await new TeamRunner(
            _launcher,
            _paths,
            _clock,
            git is null ? null : new FakeProjects("demo", Path.Combine(_root, "repo")),
            git is null ? null : RecordingGit.Over(git, _gitCalls),
            null,
            _replayTasks).RunAsync(
            new TeamRunRequest("demo", team ?? await IteratingProjectAsync(), await SpecialistsAsync(),
                "Add --since to loadout usage.", autonomy, MaxRounds: rounds, Offline: true,
                Criteria: criteria, Resuming: resuming, ResumeMessage: message),
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
