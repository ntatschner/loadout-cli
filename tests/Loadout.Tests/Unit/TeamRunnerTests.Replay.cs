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
    public async Task TeamRunnerReplay(string scenario)
    {
        var record = scenario switch
        {
            "one-round-done" => await OneRoundDoneAsync(),
            "gated-merge" => await GatedMergeAsync(),
            "permission-answered" => await PermissionAnsweredAsync(),
            "quiet-rounds" => await QuietRoundsAsync(),
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

        return await RecordAsync(await ReplayRunAsync(git: git));
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
    /// <param name="byNode">
    /// The journal grouped by node, keeping each node's own order, for a
    /// scenario whose nodes run at the same time.
    /// </param>
    private async Task<string> RecordAsync(TeamRunOutcome[] outcomes, bool byNode = false)
    {
        var last = outcomes[^1];
        var directory = last.Directory!;
        var lines = new List<string>();

        IEnumerable<string> journal = await File.ReadAllLinesAsync(Path.Combine(directory, "journal.jsonl"));

        if (byNode)
        {
            journal = journal.OrderBy(l => RunJournal.Parse(l)?.Node ?? string.Empty, StringComparer.Ordinal).ToList();
        }

        lines.AddRange(journal);

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
