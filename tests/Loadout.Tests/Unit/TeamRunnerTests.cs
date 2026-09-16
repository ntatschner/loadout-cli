using FluentAssertions;
using Loadout.Agents;
using Loadout.Agents.Claude;
using Loadout.Agents.Teams;
using Loadout.Core.Diagnostics;
using Loadout.Core.Instructions;
using Loadout.Core.Teams;
using Loadout.Models.Agents;
using Loadout.Models.Platform;
using Loadout.Models.Results;
using Loadout.Models.Teams;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The coordinator, over scripted nodes.
/// </summary>
/// <remarks>
/// <para>
/// The launcher is a fake that answers each node with lines a real session
/// would write, so what is under test is the run: who gets briefed with
/// what, how a report is judged, what goes back to the lead, when a person
/// is asked, and when the run stops. Nothing here starts a process.
/// </para>
/// <para>
/// The built-in library and the built-in iterating-project team are used as
/// they ship, so a change to either that broke a run would show here.
/// </para>
/// </remarks>
public sealed class TeamRunnerTests : IDisposable
{
    private static SpecialistCatalogue? _specialists;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-run-" + Guid.NewGuid().ToString("N"));
    private readonly FakeLauncher _launcher = new();
    private readonly FakeConsole _console = new();
    private readonly IPlatformPaths _paths;

    public TeamRunnerTests()
    {
        var environment = new FakeEnvironmentProvider(
            Path.Combine(_root, "home"),
            new Dictionary<string, string>
            {
                ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
            });

        _paths = new LinuxPaths(
            environment,
            new NoOpFilePermissions(),
            new HostPlatform(HostOperatingSystem.Linux, System.Runtime.InteropServices.Architecture.X64, "test", "TEST"));

        _paths.EnsureDirectoriesExist();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private static async Task<SpecialistCatalogue> SpecialistsAsync() =>
        _specialists ??= await new SpecialistLibrary().LoadAsync(workspaceRoot: null);

    private static async Task<TeamDefinition> IteratingProjectAsync() =>
        (await new TeamCatalogue().LoadAsync(null, null, await SpecialistsAsync())).Find("iterating-project")!;

    private async Task<OperationResult<TeamRunOutcome>> RunAsync(
        TeamDefinition? team = null,
        string autonomy = "supervised",
        bool dryRun = false,
        IChildLifetime? lifetime = null)
    {
        return await new TeamRunner(_launcher, _paths, TimeProvider.System, lifetime: lifetime).RunAsync(
            new TeamRunRequest("demo", team ?? await IteratingProjectAsync(), await SpecialistsAsync(),
                "Add --since to loadout usage.", autonomy, dryRun, Offline: true),
            _console);
    }

    // ------------------------------------------------------------ scripts

    private static readonly ReportEvidence Passed = new(EvidenceKind.Test, "dotnet test", EvidenceResult.Pass, "1612 passed");

    private static Report LeadAsks(params ReportRequest[] requests) => new(
        "lead", ReportStatus.NeedsDecision, "Planned one piece.", [], [], [],
        Questions: [new ReportQuestion("Proceed with one implementer?", ["yes", "no"], "yes")],
        Requests: requests);

    private static Report LeadRequests(params ReportRequest[] requests) => new(
        "lead", ReportStatus.Blocked, "Round one.", [], [], [],
        Blocker: new ReportBlocker("waiting on my requests", "their reports"),
        Requests: requests);

    private static Report LeadDone() => new(
        "lead", ReportStatus.Done, "The option exists; the implementer's evidence is cited.",
        [new ReportDeliverable(DeliverableKind.Answer, "a4f21c9")], [Passed], []);

    private static Report ImplementerDone() => new(
        "implementer", ReportStatus.Done, "Added the option.",
        [new ReportDeliverable(DeliverableKind.Commit, "a4f21c9")], [Passed], []);

    private static string Init(string session) =>
        $$"""{"type":"system","subtype":"init","session_id":"{{session}}","model":"m","mcp_servers":[]}""";

    private static string Result(Report report, decimal cumulativeCost) =>
        $$"""{"type":"result","subtype":"success","is_error":false,"num_turns":2,"duration_ms":5,"total_cost_usd":{{cumulativeCost.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"usage":{},"structured_output":{{ReportReader.Write(report)}}}""";

    /// <summary>A node writing ordinary prose to the person.</summary>
    private static string Say(string text) =>
        $$"""{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"{{text}}"}]},"parent_tool_use_id":null}""";

    /// <summary>A turn that completed and produced no structured output at all.</summary>
    /// <remarks>Concatenated rather than interpolated: a literal <c>}}</c> inside a raw interpolated string is read as the interpolation's close.</remarks>
    private static string NoReport(decimal cumulativeCost) =>
        """{"type":"result","subtype":"success","is_error":false,"num_turns":1,"duration_ms":5,"usage":{},"total_cost_usd":"""
        + cumulativeCost.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";

    /// <summary>A node calling a tool, as the agent reports it while the turn runs.</summary>
    private static string Using(string tool, string inputJson) =>
        $$"""{"type":"assistant","message":{"role":"assistant","content":[{"type":"tool_use","id":"t1","name":"{{tool}}","input":{{inputJson}}}]},"parent_tool_use_id":null}""";

    private static ReportRequest AskImplementer() =>
        new("implementer", "Add --since to loadout usage; a test fails without it and passes with it.", DeliverableKind.Commit, ["plan:none"]);

    // ------------------------------------------------------------- tests

    [Fact]
    public async Task A_lead_that_requests_a_worker_gets_its_report_back_and_finishes()
    {
        // The lead's session is one pipe: it asks, and after the worker's
        // report it finishes. It says done twice because the team's merge
        // gate has not been consulted and the coordinator says so once; the
        // second is its answer. The worker is one pipe, one turn.
        _launcher.Script(
            "role.project-lead",
            Init("lead-1"), Result(LeadRequests(AskImplementer()), 0.05m), Result(LeadDone(), 0.09m), Result(LeadDone(), 0.09m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.03m));

        var result = await RunAsync();

        result.Succeeded.Should().BeTrue(result.Error);
        var outcome = result.Value!;

        outcome.Ended.Should().Be("done");
        outcome.Rounds.Should().Be(3);
        outcome.CostUsd.Should().Be(0.05m + 0.03m + 0.04m, "the lead's second turn is the step in its running total");
        outcome.FinalReport!.Status.Should().Be(ReportStatus.Done);

        _launcher.Requests.Should().HaveCount(2);

        var (leadRequest, leadOptions) = _launcher.Requests[0];
        leadRequest.Specialists.Should().Equal("role.project-lead");
        leadRequest.Mode.Should().Be("coordinate");
        leadRequest.Task.Should().Be("Add --since to loadout usage.");
        leadOptions.Permission.Should().Be(HeadlessPermission.DenyUnlessAllowed);
        leadOptions.DeniedTools.Should().Contain("Edit");
        leadOptions.OutputSchemaJson.Should().Be(ReportSchema.Version1);
        leadOptions.DisableHooks.Should().BeTrue();
        leadOptions.IsolateMcpServers.Should().BeTrue();
        leadOptions.MaxTurns.Should().Be(40, "the team's turns per node");

        var (workerRequest, workerOptions) = _launcher.Requests[1];
        workerRequest.Specialists.Should().Equal("role.implementer");
        workerRequest.Mode.Should().Be("implement");
        workerRequest.Task.Should().Contain("Add --since");
        workerOptions.Permission.Should().Be(HeadlessPermission.AcceptEdits);
        workerOptions.DeniedTools.Should().Contain("Bash(git push:*)");

        // The worker's report went back to the lead verbatim.
        _launcher.Written("role.project-lead").Should().HaveCount(3);
        _launcher.Written("role.project-lead")[1].Should().Contain("a4f21c9").And.Contain("Reports from your requests");

        // Both sessions were ended and both launches completed with an exit code.
        _launcher.Completed.Should().Equal(0, 0);

        // The record: a journal, the briefs, the reports, the final report.
        var directory = outcome.Directory!;
        var journal = await File.ReadAllLinesAsync(Path.Combine(directory, "journal.jsonl"));

        journal.Should().Contain(l => l.Contains("\"kind\":\"run.started\""));
        journal.Should().Contain(l => l.Contains("\"kind\":\"node.launched\"") && l.Contains("\"node\":\"implementer\""));
        journal.Should().Contain(l => l.Contains("\"kind\":\"report.checked\"") && l.Contains("\"outcome\":\"accepted\""));
        journal.Last().Should().Contain("\"kind\":\"run.finished\"").And.Contain("\"ended\":\"done\"");

        File.Exists(Path.Combine(directory, "brief-lead.json")).Should().BeTrue();
        File.Exists(Path.Combine(directory, "brief-implementer-1.json")).Should().BeTrue();
        File.Exists(Path.Combine(directory, "report-implementer-1.json")).Should().BeTrue();
        File.Exists(Path.Combine(directory, "final-report.json")).Should().BeTrue();
    }

    [Fact]
    public async Task The_brief_a_node_reads_carries_the_task_the_contract_and_the_json()
    {
        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadDone(), 0.05m));

        await RunAsync();

        var prompt = _launcher.Written("role.project-lead")[0];

        prompt.Should().Contain("# Brief for node lead (role.project-lead)");
        prompt.Should().Contain("You report to the person.");
        prompt.Should().Contain("Add --since to loadout usage.");
        prompt.Should().Contain("\"contract\":\"brief/1\"");
        prompt.Should().Contain("outward actions allowed: none");
        prompt.TrimEnd().Should().EndWith("Status done needs evidence.", "the contract is the last thing the node reads");
    }

    [Fact]
    public async Task In_manual_mode_refusing_the_first_gate_starts_nothing()
    {
        _console.Confirm = _ => false;

        var outcome = (await RunAsync(autonomy: "manual")).Value!;

        outcome.Ended.Should().Be("stopped before the lead was briefed");
        _launcher.Requests.Should().BeEmpty();
        File.ReadAllLines(Path.Combine(outcome.Directory!, "journal.jsonl")).Should().HaveCount(2);
    }

    [Fact]
    public async Task In_manual_mode_every_brief_and_every_report_is_a_gate()
    {
        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadRequests(AskImplementer()), 0.05m),
            Result(LeadDone(), 0.09m), Result(LeadDone(), 0.09m));
        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.03m));

        (await RunAsync(autonomy: "manual")).Value!.Ended.Should().Be("done");

        _console.Confirmations.Should().Equal(
            "Brief the lead (role.project-lead) with the goal",
            "Act on the lead's report (round 1, blocked)",
            "Brief implementer (role.implementer): Add --since to loadout usage; a test fails without it and passes with it.",
            "Hand implementer's report (done) to the lead");
    }

    [Fact]
    public async Task A_report_that_fails_the_check_goes_back_once_with_the_reasons()
    {
        var noEvidence = ImplementerDone() with { Evidence = [] };

        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadRequests(AskImplementer()), 0.05m),
            Result(LeadDone(), 0.09m), Result(LeadDone(), 0.09m));
        _launcher.Script("role.implementer", Init("impl-1"), Result(noEvidence, 0.02m), Result(ImplementerDone(), 0.03m));

        (await RunAsync()).Value!.Ended.Should().Be("done");

        var written = _launcher.Written("role.implementer");

        written.Should().HaveCount(2);
        written[1].Should().Contain("Your report was returned.").And.Contain("done needs at least one evidence entry whose result is pass");
    }

    [Fact]
    public async Task An_outward_action_a_brief_did_not_allow_halts_the_run()
    {
        var pushed = ImplementerDone() with { OutwardTaken = ["git push origin main"] };

        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadRequests(AskImplementer()), 0.05m), Result(LeadDone(), 0.09m));
        _launcher.Script("role.implementer", Init("impl-1"), Result(pushed, 0.02m));

        var outcome = (await RunAsync()).Value!;

        outcome.Ended.Should().StartWith("halted: implementer took an outward action");
        _launcher.Written("role.project-lead").Should().HaveCount(1, "the lead never got the floor back");
    }

    [Fact]
    public async Task A_question_is_put_to_the_person_and_the_answer_goes_back_to_the_lead()
    {
        _console.Decide = q => q.Options[1];

        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadAsks(), 0.05m), Result(LeadDone(), 0.09m));

        (await RunAsync()).Value!.Ended.Should().Be("done");

        _launcher.Written("role.project-lead")[1].Should().Contain("Proceed with one implementer?: **no**");
    }

    [Fact]
    public async Task In_autonomous_mode_the_leads_recommendation_is_the_answer()
    {
        _console.Decide = _ => throw new InvalidOperationException("nobody is watching");

        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadAsks(), 0.05m), Result(LeadDone(), 0.09m));

        (await RunAsync(autonomy: "autonomous")).Value!.Ended.Should().Be("done");

        _launcher.Written("role.project-lead")[1].Should().Contain("Proceed with one implementer?: **yes**");
    }

    [Fact]
    public async Task A_node_says_what_it_is_doing_while_the_turn_is_still_running()
    {
        // Before this, a turn that ran for minutes wrote nothing until it
        // was over, so anybody watching saw "working" and no more.
        _launcher.Script(
            "role.project-lead",
            Init("lead-1"),
            Using("Read", """{"file_path":"docs/commands.md"}"""),
            Using("Bash", """{"command":"dotnet test"}"""),
            Result(LeadDone(), 0.04m));

        var outcome = (await RunAsync()).Value!;

        var doing = (await File.ReadAllLinesAsync(Path.Combine(outcome.Directory!, "journal.jsonl")))
            .Where(line => line.Contains("\"node.doing\"", StringComparison.Ordinal))
            .ToList();

        doing.Should().ContainSingle(
            "an agent writes hundreds of events in a turn, and a line for each would bury the record")
            .Which.Should().Contain("Read docs/commands.md");
    }

    [Fact]
    public async Task What_a_node_is_doing_names_the_tool_and_never_the_call()
    {
        // A tool call carries file contents and command text. The record is
        // shared and kept, so it gets the name and the target and no more.
        _launcher.Script(
            "role.project-lead",
            Init("lead-1"),
            Using("Write", """{"file_path":"notes.md","content":"the whole file, which does not belong in the record"}"""),
            Result(LeadDone(), 0.04m));

        var outcome = (await RunAsync()).Value!;

        var journal = await File.ReadAllTextAsync(Path.Combine(outcome.Directory!, "journal.jsonl"));

        journal.Should().Contain("Write notes.md");
        journal.Should().NotContain("does not belong in the record");
    }

    [Fact]
    public async Task A_run_says_when_its_nodes_would_survive_a_killed_coordinator()
    {
        // A coordinator killed rather than stopped once left a lead and a
        // verifier running. Where the platform cannot prevent that, the run
        // says so rather than letting an unattended run imply otherwise.
        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadDone(), 0.04m));

        var outcome = (await RunAsync(lifetime: new UnenforcedLifetime())).Value!;

        outcome.Warnings.Should().ContainSingle(w => w.Contains("keep running and keep spending"))
            .Which.Should().Contain("this machine has no job object");
    }

    [Fact]
    public async Task A_run_whose_platform_enforces_the_promise_says_nothing_about_it()
    {
        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadDone(), 0.04m));

        var outcome = (await RunAsync(lifetime: new EnforcedLifetime())).Value!;

        outcome.Warnings.Should().NotContain(w => w.Contains("keep running and keep spending"));
    }

    private sealed class UnenforcedLifetime : IChildLifetime
    {
        public bool IsEnforced => false;

        public string Detail => "this machine has no job object";

        public void Adopt(int processId)
        {
        }
    }

    private sealed class EnforcedLifetime : IChildLifetime
    {
        public bool IsEnforced => true;

        public string Detail => "a job object the kernel closes with the launcher";

        public void Adopt(int processId)
        {
        }
    }

    [Fact]
    public async Task A_lead_that_asks_for_a_node_it_may_not_is_refused_and_both_the_person_and_the_lead_are_told()
    {
        var lead = LeadRequests(new ReportRequest("verifier", "verify", DeliverableKind.Decision)) with { Status = ReportStatus.Blocked };
        var team = await IteratingProjectAsync();
        team.Nodes["lead"].Delegates = ["implementer"];

        _launcher.Script("role.project-lead", Init("lead-1"), Result(lead, 0.05m), Result(LeadDone(), 0.09m));

        var outcome = (await RunAsync(team)).Value!;

        outcome.Warnings.Should().Contain(w => w.Contains("'verifier', which it may not request") && w.Contains("It may request: implementer"));
        _launcher.Requests.Should().HaveCount(1, "the refused node was never launched");

        // The first real run refused the same request twice and the lead,
        // never told, asked twice. Now the refusal is in its next turn.
        _launcher.Written("role.project-lead")[1].Should().Contain("## Requests refused").And.Contain("'verifier'");
    }

    [Fact]
    public async Task A_lead_is_told_which_nodes_it_may_request_and_may_ask_for_them_by_instance()
    {
        // "implementer/1" is how a role file's example and the real lead both
        // spelled it. The node is called "implementer"; the instance name is
        // what the worker is briefed as and reports as.
        var done = ImplementerDone() with { Node = "implementer/1" };

        _launcher.Script("role.project-lead", Init("lead-1"),
            Result(LeadRequests(AskImplementer() with { Node = "implementer/1" }), 0.05m),
            Result(LeadDone(), 0.09m), Result(LeadDone(), 0.09m));
        _launcher.Script("role.implementer", Init("impl-1"), Result(done, 0.03m));

        var outcome = (await RunAsync()).Value!;

        outcome.Ended.Should().Be("done");
        outcome.Warnings.Should().NotContain(w => w.Contains("may not request"));

        var leadBrief = _launcher.Written("role.project-lead")[0];
        leadBrief.Should().Contain("## You may request");
        leadBrief.Should().Contain("`implementer` (role.implementer, hands back commit), up to 3 at once");
        leadBrief.Should().Contain("`name/1`, `name/2`");

        var workerBrief = _launcher.Written("role.implementer")[0];
        workerBrief.Should().Contain("# Brief for node implementer/1 (role.implementer)");
        workerBrief.Should().NotContain("## You may request", "a worker requests nothing");
    }

    [Fact]
    public async Task Two_rounds_without_a_request_or_a_finish_stop_the_run()
    {
        var idle = new Report("lead", ReportStatus.NeedsDecision, "thinking", [], [], [],
            Questions: [new ReportQuestion("Continue?", ["yes", "no"], "yes")]);

        _launcher.Script("role.project-lead", Init("lead-1"), Result(idle, 0.01m), Result(idle, 0.02m), Result(idle, 0.03m));

        var outcome = (await RunAsync(autonomy: "autonomous")).Value!;

        outcome.Ended.Should().StartWith("no progress");
    }

    [Fact]
    public async Task A_lead_that_exits_without_a_report_has_what_it_wrote_to_stderr_passed_on()
    {
        // The first real run: exit code 1 in under a second, no events, and
        // the outcome said only "ended without a report". What the agent had
        // printed on its error stream was the whole explanation, and nobody
        // saw it.
        _launcher.Script("role.project-lead", Init("lead-1"));
        _launcher.Error("role.project-lead", "Error: unknown option '--frobnicate'\nrun claude --help");

        var outcome = (await RunAsync()).Value!;

        outcome.Ended.Should().Be("the lead ended without a report");
        outcome.Warnings.Should().ContainSingle(w => w.Contains("error stream"))
            .Which.Should().Contain("unknown option '--frobnicate'").And.Contain("exit code 0");

        var journal = await File.ReadAllLinesAsync(Path.Combine(outcome.Directory!, "journal.jsonl"));
        journal.Should().Contain(l => l.Contains("\"kind\":\"node.ended\"") && l.Contains("frobnicate"));
    }

    [Fact]
    public async Task A_node_that_answers_in_prose_instead_of_a_report_has_what_it_said_passed_on()
    {
        // The second real run stopped because the account reached its model
        // limit, and every node said so in one plain sentence. The run
        // reported "ended without a report" and threw the sentence away, so
        // the cause took a transcript to find.
        const string Limit = "You've reached your Fable limit. Switch to another model to continue.";

        _launcher.Script(
            "role.project-lead",
            Init("lead-1"), Result(LeadRequests(AskImplementer()), 0.05m),
            Say(Limit), NoReport(0.05m), Say(Limit), NoReport(0.05m));

        _launcher.Script("role.implementer", Init("impl-1"), Say(Limit), NoReport(0m), Say(Limit), NoReport(0m));

        var outcome = (await RunAsync()).Value!;

        outcome.Warnings.Should().Contain(w => w.Contains("implementer produced no report") && w.Contains("Fable limit"));
        outcome.Ended.Should().Be($"the lead ended without a report. It said: {Limit}");

        var journal = await File.ReadAllLinesAsync(Path.Combine(outcome.Directory!, "journal.jsonl"));
        journal.Should().Contain(l => l.Contains("\"kind\":\"report.unreadable\"") && l.Contains("Fable limit"));

        // The lead was told too, in the worker's failed report.
        _launcher.Written("role.project-lead")[1].Should().Contain("Fable limit");
    }

    [Fact]
    public async Task The_nearest_model_wins_over_the_team_and_the_project()
    {
        // Mode-keyed models in the manifest give five buckets for
        // twenty-two roles, project-wide. A node names its own, and a run
        // names one for everything.
        var team = await IteratingProjectAsync();
        team.Nodes["lead"].Model = "haiku";
        team.Nodes["implementer"].Model = "sonnet";

        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadRequests(AskImplementer()), 0.05m), Result(LeadDone(), 0.09m));
        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.03m));

        await RunAsync(team);

        _launcher.Requests[0].Request.Model.Should().Be("haiku");
        _launcher.Requests[1].Request.Model.Should().Be("sonnet");

        // A run that names one overrules every node; a run that names none
        // leaves each node, and then the project, to decide.
        var second = new FakeLauncher();
        second.Script("role.project-lead", Init("lead-1"), Result(LeadDone(), 0.05m));

        await new TeamRunner(second, _paths, TimeProvider.System).RunAsync(
            new TeamRunRequest("demo", team, await SpecialistsAsync(), "goal", "supervised", Model: "opus"),
            _console);

        second.Requests[0].Request.Model.Should().Be("opus");
    }

    [Fact]
    public async Task A_node_with_no_model_leaves_the_choice_to_the_project()
    {
        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadDone(), 0.05m));

        await RunAsync();

        _launcher.Requests[0].Request.Model.Should().BeNull(
            "the manifest's model_by_mode, read at launch, is what decides when nothing nearer does");
    }

    [Fact]
    public async Task A_node_whose_team_gives_it_a_worktree_is_launched_into_one_and_told_so()
    {
        // The first run to finish committed to main, because the team's
        // worktree: true went nowhere and the reviewer then read a change
        // already on the branch.
        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadRequests(AskImplementer()), 0.05m), Result(LeadDone(), 0.09m));
        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.03m));

        var outcome = (await RunAsync()).Value!;

        var (leadRequest, _) = _launcher.Requests[0];
        var (workerRequest, _) = _launcher.Requests[1];

        leadRequest.Worktree.Should().BeNull("a lead changes nothing, so it works where the project is");
        leadRequest.CreateWorktree.Should().BeFalse();

        workerRequest.Worktree.Should().Be($"teams/{outcome.RunId}/implementer");
        workerRequest.CreateWorktree.Should().BeTrue();

        // The node is told where it is, because a node that switches branch
        // or commits elsewhere undoes the point of the tree.
        _launcher.Written("role.implementer")[0].Should()
            .Contain($"worktree: you are in a git worktree of your own, on branch `teams/{outcome.RunId}/implementer`")
            .And.Contain("do not merge");

        outcome.Warnings.Should().ContainSingle(w => w.Contains("worked in a new worktree"))
            .Which.Should().Contain("cleared away with its tree");
    }

    [Fact]
    public async Task The_same_notice_from_every_node_is_said_once()
    {
        _launcher.Warn("Pre-commit protection: not installed in this clone.");
        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadRequests(AskImplementer()), 0.05m), Result(LeadDone(), 0.09m));
        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.03m));

        var outcome = (await RunAsync()).Value!;

        outcome.Warnings.Count(w => w.StartsWith("Pre-commit protection", StringComparison.Ordinal))
            .Should().Be(1, "two nodes launched and each carried the same notice");
    }

    [Fact]
    public async Task Nothing_is_merged_until_every_node_the_gate_names_has_accepted()
    {
        // The iterating project's gate is the reviewer and the verifier. A
        // reviewer that returned a change is not a reviewer that accepted
        // it, and the difference is whether the run lands it.
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

        var outcome = (await RunAsync()).Value!;

        outcome.Ended.Should().Be("done");
        outcome.Merged.Should().BeEmpty();
        outcome.Branches.Should().ContainKey("implementer");

        outcome.Warnings.Should().ContainSingle(w => w.StartsWith("Nothing was merged", StringComparison.Ordinal))
            .Which.Should().Contain("reviewer said 'return'").And.Contain("verifier decided nothing");

        var journal = await File.ReadAllLinesAsync(Path.Combine(outcome.Directory!, "journal.jsonl"));
        journal.Should().Contain(l => l.Contains("\"kind\":\"gate.refused\""));
    }

    [Fact]
    public async Task A_lead_that_finishes_without_the_gate_is_told_once_and_then_left_to_it()
    {
        // A real lead took the implementer's done report and declared the
        // goal met, having asked neither the reviewer nor the verifier its
        // role tells it to route every change through. The gate refused the
        // merge, which is right and late: the run had ended.
        _launcher.Script(
            "role.project-lead",
            Init("lead-1"),
            Result(LeadRequests(AskImplementer()), 0.05m),
            Result(LeadDone(), 0.06m),
            Result(LeadDone(), 0.07m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.03m));

        var outcome = (await RunAsync()).Value!;

        var told = _launcher.Written("role.project-lead").Last();

        told.Should().Contain("You reported done");
        told.Should().Contain("reviewer and verifier");
        told.Should().Contain("reviewer decided nothing");

        // Told once. A lead that says done again has answered, and the work
        // stays on its branch, which is what the message said would happen.
        outcome.Ended.Should().Be("done");
        outcome.Merged.Should().BeEmpty();

        var journal = await File.ReadAllLinesAsync(Path.Combine(outcome.Directory!, "journal.jsonl"));
        journal.Count(l => l.Contains("\"kind\":\"gate.reminded\"")).Should().Be(1);
    }

    [Fact]
    public async Task A_run_that_did_not_finish_merges_nothing_and_says_where_the_work_is()
    {
        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadRequests(AskImplementer()), 0.05m),
            Result(LeadRequests() with { Status = ReportStatus.Blocked, Blocker = new ReportBlocker("stuck", "help") }, 0.06m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.03m));

        var outcome = (await RunAsync()).Value!;

        outcome.Ended.Should().Be("blocked");
        outcome.Merged.Should().BeEmpty();
        outcome.Warnings.Should().Contain(w => w.Contains("Nothing was merged: the run ended blocked"));
    }

    [Fact]
    public async Task A_template_and_a_bad_autonomy_are_refused_before_anything_starts()
    {
        var company = (await new TeamCatalogue().LoadAsync(null, null, await SpecialistsAsync())).Find("product-company")!;

        (await RunAsync(company)).Error.Should().Contain("is a template");
        (await RunAsync(autonomy: "yolo")).Error.Should().Contain("not an autonomy");
        _launcher.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_dry_run_shows_the_leads_line_and_writes_nothing()
    {
        var outcome = (await RunAsync(dryRun: true)).Value!;

        outcome.Ended.Should().Be("dry run");
        outcome.Directory.Should().BeNull();
        outcome.LeadPlan!.Arguments.Should().Contain("-p");
        Directory.Exists(Path.Combine(_paths.Paths.State, "teams")).Should().BeFalse();
    }

    // ------------------------------------------------------------- fakes

    /// <summary>Answers each node with the lines it was scripted, keyed by role.</summary>
    private sealed class FakeLauncher : IAgentLauncher
    {
        private readonly Dictionary<string, Queue<string[]>> _scripts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<StubProcessLauncher.StubPipedProcess>> _pipes = new(StringComparer.Ordinal);

        public List<(LaunchRequest Request, HeadlessOptions Options)> Requests { get; } = [];

        public List<int?> Completed { get; } = [];

        public void Script(string role, params string[] lines)
        {
            if (!_scripts.TryGetValue(role, out var queue))
            {
                _scripts[role] = queue = new Queue<string[]>();
            }

            queue.Enqueue(lines);
        }

        private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);

        /// <summary>What the next session of a role writes to its error stream.</summary>
        public void Error(string role, string text) => _errors[role] = text;

        private readonly List<string> _warnings = [];

        /// <summary>A notice every launch carries, as preflight's do.</summary>
        public void Warn(string warning) => _warnings.Add(warning);

        /// <summary>Every message written to every session of a role, in order, as the text the node read.</summary>
        public IReadOnlyList<string> Written(string role) =>
            _pipes.TryGetValue(role, out var pipes)
                ? pipes
                    .SelectMany(p => p.Written.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    .Select(line => System.Text.Json.JsonDocument.Parse(line).RootElement
                        .GetProperty("message").GetProperty("content").GetString()!)
                    .ToList()
                : [];

        public Task<OperationResult<LaunchOutcome>> LaunchAsync(LaunchRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException("a team never launches interactively");

        public Task<OperationResult<HeadlessLaunch>> StartHeadlessAsync(
            LaunchRequest request, HeadlessOptions options, CancellationToken ct = default)
        {
            Requests.Add((request, options));

            var role = request.Specialists![0];
            var plan = new LaunchPlan("claude", ["-p", "--verbose"], "C:/work", [], [], null, 0, 0, null, null, request.Task, request.Mode);
            var preflight = new PreflightResult([], new Dictionary<string, string>());

            if (request.DryRun)
            {
                return Task.FromResult(OperationResult<HeadlessLaunch>.Ok(new HeadlessLaunch(
                    plan, ["Dry run: nothing was launched."], preflight, "Demo", "claude", null, null, (_, _) => Task.CompletedTask)));
            }

            if (!_scripts.TryGetValue(role, out var queue) || queue.Count == 0)
            {
                return Task.FromResult(OperationResult<HeadlessLaunch>.Fail($"no script for {role}"));
            }

            var pipe = new StubProcessLauncher.StubPipedProcess(
                string.Join("\n", queue.Dequeue()) + "\n", 0, _errors.TryGetValue(role, out var error) ? error : string.Empty);

            if (!_pipes.TryGetValue(role, out var pipes))
            {
                _pipes[role] = pipes = [];
            }

            pipes.Add(pipe);

            return Task.FromResult(OperationResult<HeadlessLaunch>.Ok(new HeadlessLaunch(
                plan, [.. _warnings], preflight, "Demo", "claude", $"L-{Requests.Count}",
                new HeadlessSession(pipe, ClaudeHeadlessProtocol.Instance),
                (exit, _) => { Completed.Add(exit); return Task.CompletedTask; })));
        }
    }

    private sealed class FakeConsole : ITeamConsole
    {
        public Func<string, bool> Confirm { get; set; } = _ => true;

        public Func<ReportQuestion, string?> Decide { get; set; } = q => q.Recommendation;

        public List<string> Confirmations { get; } = [];

        public List<string> Notes { get; } = [];

        public Task<bool> ConfirmAsync(string what, CancellationToken ct = default)
        {
            Confirmations.Add(what);

            return Task.FromResult(Confirm(what));
        }

        public Task<string?> DecideAsync(ReportQuestion question, CancellationToken ct = default) =>
            Task.FromResult(Decide(question));

        public void Note(string line) => Notes.Add(line);
    }
}
