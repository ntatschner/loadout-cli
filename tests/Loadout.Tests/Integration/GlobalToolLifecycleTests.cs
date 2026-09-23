using System.Text;
using FluentAssertions;
using Loadout.Agents.Teams;
using Loadout.Core.Configuration;
using Loadout.Core.Instructions;
using Loadout.Core.Teams;
using Loadout.Core.Tools;
using Loadout.Core.Workspace;
using Loadout.Models.Configuration;
using Loadout.Models.Instructions;
using Loadout.Models.Platform;
using Loadout.Models.Results;
using Loadout.Models.Teams;
using Loadout.Models.Tools;
using Loadout.Platform.Abstractions;
using Loadout.Tests.Fakes;
using Loadout.Tests.Unit;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// A lesson from one team becoming a tool another team uses, end to end, over
/// the catalogue's own code.
/// </summary>
/// <remarks>
/// <para>
/// What is scripted, and what is real. Team A's two runs are written as the
/// runner writes them (a journal and a remediator's report), and team B's node
/// is a brief rendered by <see cref="TeamRunner.Render" /> and a permission
/// policy built by <see cref="ToolOffer.For" />, the two calls the runner makes
/// when it briefs a remediator. The Creator's turns are the calls its commands
/// make: <c>tools search</c>, <c>tools submit</c>, <c>tools verify</c>. Every
/// harness run goes through a stub launcher, so nothing here needs pwsh.
/// </para>
/// <para>
/// Real: the daemon's nomination pass, the genericity and overlap screens, the
/// verify hold and the regression gate, write-once promotion, the trust ruling,
/// and usage. A person's answers - agreeing to a harness run, trusting a
/// version - are written into the same records the CLI writes them to.
/// </para>
/// </remarks>
public sealed class GlobalToolLifecycleTests : IDisposable
{
    private const string TeamA = "system-watch";
    private const string ProjectA = "alpha";
    private const string TeamB = "beta-ops";
    private const string ProjectB = "beta";

    private static readonly string[] RunsA = ["20260921-0900-a001", "20260922-0900-a002"];

    /// <summary>Team A's fix, as it was kept on its shelf: the cache it clears is written into it.</summary>
    private const string HardCoded =
        "$CachePath = 'D:\\alpha\\build\\cache'\nGet-ChildItem $CachePath -Recurse | Remove-Item -Force\nWrite-Output 'freed'\n";

    /// <summary>The same fix with the path made an input.</summary>
    private const string Generic =
        "param([Parameter(Mandatory)][string]$CachePath)\nGet-ChildItem $CachePath -Recurse | Remove-Item -Force\nWrite-Output 'freed'\n";

    private readonly ToolStoreFixture _store = new(ProjectA, TeamA, ProjectB, TeamB);

    public void Dispose() => _store.Dispose();

    [Fact]
    public async Task A_lesson_from_one_team_becomes_a_tool_another_team_uses()
    {
        var (registry, _) = _store.Registry();

        // 1. Team A's remediator fixes a full disk with a remedy that names
        //    its own project's cache, and the same remedy passes again.
        Shelve(TeamA, "clear-alpha-cache", HardCoded, revision: 1);

        foreach (var run in RunsA)
        {
            FinishedRun(run, TeamA, "pwsh -NoProfile -File remedies/clear-alpha-cache.ps1");
        }

        // 2. The daemon's run-finished schedule fires on it, and the pass that
        //    precedes starting tool-works files the nomination.
        var schedule = new TeamSchedule
        {
            Id = "tools-on-finish",
            Team = ScheduleService.ToolWorksTeam,
            Project = ProjectB,
            Goal = "Look at what finished.",
            On = ScheduleService.RunFinishedEvent,
            Enabled = true,
            LastCommit = "2026-09-01T00:00:00.0000000+00:00",
        };

        var journal = new RunJournal(_store.Paths);
        ScheduleService.RunFinished(schedule, [.. journal.List(50).Select(one => journal.Summarise(one).Value!)], DateTimeOffset.UtcNow)
            .Fire.Should().BeTrue("team A's runs have finished and have not been seen");

        var nominated = await Pass(registry).BeforeAsync(schedule);

        nominated.Should().ContainSingle(one => one.Nomination.Rule == 2 && one.Filed!.Succeeded,
            "a revised remedy passed in two of its team's runs");
        var nomination = nominated.Single().Filed!.Value!.Id;

        // 3. The Creator searches first and finds nothing to extend.
        registry.Search("disk cache").Should().BeEmpty();

        // Its first submission still carries team A's cache, and is refused
        // for it, naming the path and the project.
        var first = registry.Submit(new ToolSubmission(
            "candidate", "Frees disk held by a build cache.", By: "creator", Script: HardCoded,
            Capabilities: ["disk", "cache", "cleanup"], Summary: "Frees disk held by the alpha build cache."));

        first.Failed.Should().BeTrue("the draft still names one project's path");
        first.Error.Should().Contain("absolute path").And.Contain("'alpha'");

        var second = registry.Submit(new ToolSubmission(
            "candidate", "Frees disk held by a build cache.", By: "creator", Script: Generic,
            Capabilities: ["disk", "cache", "cleanup"], Summary: "Frees disk held by a build cache directory."));

        second.Succeeded.Should().BeTrue(second.Error);

        // 4. Verify is held for a person, as this machine has no rule for
        //    tool-test; the person agrees to that draft, and it runs.
        var manifest = Manifest("free-disk-by-cache", "1.0");
        var cases = ToolStoreFixture.Cases();
        var draft = _store.Draft(manifest, Generic, cases);

        var held = await registry.VerifyAsync(draft, new ToolTestConsent(null, []));
        held.Value!.Ruling.Should().NotBe(RemedyRuling.Run, "nobody has agreed to this harness run");
        held.Value.Gate.Should().BeNull();

        var verified = await registry.VerifyAsync(draft, ToolStoreFixture.Agreed(manifest, Generic, cases));
        verified.Value!.Gate!.Passed.Should().BeTrue(verified.Value.Because);

        var promoted = registry.Promote(draft, new ToolPromotionRequest(
            "nomination", "inbox/" + nomination, Actor: "creator", Owner: ScheduleService.ToolWorksTeam, Kind: "disk",
            Summary: "Frees disk held by a build cache directory.", Capabilities: ["disk", "cache", "cleanup"]));

        promoted.Succeeded.Should().BeTrue(promoted.Error);

        var shown = registry.Show("free-disk-by-cache").Value!;
        shown.Record.Active.Should().Be("1.0");

        registry.Audit("free-disk-by-cache").Select(one => one.Action).Should().ContainInOrder("verify", "verify", "promote");
        registry.Audit().Select(one => one.Action).Should().ContainInOrder("nominate", "submit", "verify", "promote");

        // 5. Team B, on another project, is briefed; the brief points at the
        //    catalogue and names no tool.
        var brief = BriefFor(TeamB);
        var read = TeamRunner.Render(brief);

        read.Should().Contain(TeamRunner.ToolsPointer);
        read.Should().NotContain("free-disk-by-cache").And.NotContain(ProjectA).And.NotContain(TeamA);

        // Its remediator searches in its own words, and finds the tool.
        registry.Search("disk full").Select(one => one.Name).Should().Equal("free-disk-by-cache");
        registry.Show("free-disk-by-cache").Value!.Active!.Inputs.Should().Contain(one => one.Name == "CachePath");

        // Offered, but held, until a person trusts that version.
        var rules = new Dictionary<string, string> { ["disk"] = "trusted" };
        var file = "free-disk-by-cache.v1.0.ps1";

        ToolOffer.For(registry, ToolOffer.Remediator, rules, []).Should().ContainSingle()
            .Which.Ruling.Should().NotBe("run", "nobody has trusted it on this machine");

        // What `loadout tools trust free-disk-by-cache@1.0` writes.
        var trusted = new List<TrustedTool>
        {
            new()
            {
                Tool = "free-disk-by-cache",
                Version = "1.0",
                Fingerprint = RemedyCeiling.Fingerprint(registry.ScriptOf("free-disk-by-cache", "1.0")!),
                By = "person",
                At = DateTimeOffset.UtcNow,
            },
        };
        registry.RecordTrust("free-disk-by-cache", "1.0", revoked: false, "person");

        var offered = ToolOffer.For(registry, ToolOffer.Remediator, rules, trusted);
        offered.Should().ContainSingle().Which.Ruling.Should().Be("run");

        var policy = new NodePolicy("20260923-1400-b001", "remediator/1", ToolOffer.Remediator, ["Bash"], [], Ask: true, Remedies: offered);
        var decided = NodePermissions.Decide(policy, "Bash", Calling($"pwsh -NoProfile -File ./{file} -CachePath ./beta-cache"));

        decided.Allowed.Should().BeTrue();
        decided.Reason.Should().Contain("tool:free-disk-by-cache@1.0");

        registry.RecordUsage(new ToolUsage
        {
            Tool = "free-disk-by-cache",
            Version = "1.0",
            Outcome = ToolOutcome.Ok,
            Team = TeamB,
            Run = "20260923-1400-b001",
        }).Succeeded.Should().BeTrue();

        // 6. What crossed from team A to team B, and what did not.
        var head = registry.Show("free-disk-by-cache").Value!.Record;
        head.UsageSummary.Teams.Should().Be(1);
        head.UsageSummary.Ok.Should().Be(1);

        head.Lineage[0].Source.Should().Be("inbox/" + nomination);
        var source = Path.Combine(registry.Root(), head.Lineage[0].Source + ".yaml");
        File.Exists(source).Should().BeTrue("lineage[0] resolves to the nomination");
        File.ReadAllText(source).Should().Contain("by: nominator").And.Contain("clear-alpha-cache");

        foreach (var written in Directory.EnumerateFiles(Path.Combine(registry.Root(), "free-disk-by-cache", "versions"), "*", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(written);

            foreach (var forbidden in new[] { ProjectA, TeamA, "D:\\", "D:/" }.Concat(RunsA))
            {
                text.Should().NotContain(forbidden, $"{Path.GetFileName(written)} is shared by every team");
            }
        }

        // A second tool changes nothing in team B's brief.
        var before = Encoding.UTF8.GetBytes(TeamRunner.Render(BriefFor(TeamB)));
        await _store.PromoteAsync(registry, Manifest("prune-old-logs", "1.0"), "param([string]$LogPath)\nRemove-Item (Join-Path $LogPath '*.log')\n", ToolStoreFixture.Cases());
        registry.Search(string.Empty).Should().HaveCount(2);

        Encoding.UTF8.GetBytes(TeamRunner.Render(BriefFor(TeamB))).Should().Equal(before);
    }

    [Fact]
    public async Task A_failed_refinement_leaves_the_known_good_version_active()
    {
        // 1.1 keeps its own cases but breaks one 1.0 was trusted for.
        var launcher = new ExitByCase(request =>
            request.Arguments.Any(one => one.Contains(Path.DirectorySeparatorChar + "1.1-", StringComparison.Ordinal))
            && request.Arguments.Contains("7") ? 1 : 0);
        var registry = new ToolRegistry(_store.Paths, new ToolHarness(launcher), TimeProvider.System, () => _store.Known);

        var known = ToolStoreFixture.Cases();
        known.Add(new ToolCase
        {
            Name = "keeps-newer-files",
            Class = ToolCaseClass.All[0],
            Args = new Dictionary<string, string> { ["CachePath"] = "{tmp}/cache", ["OlderThanDays"] = "7" },
            Expect = new ToolCaseExpect { Exit = 0 },
        });

        await _store.PromoteAsync(registry, Manifest("free-disk-by-cache", "1.0"), Generic, known);

        var refined = Manifest("free-disk-by-cache", "1.1");
        var script = Generic + "# refined\n";
        var cases = ToolStoreFixture.Cases(prefix: "v11-");
        var draft = _store.Draft(refined, script, cases);

        var verified = await registry.VerifyAsync(draft, ToolStoreFixture.Agreed(refined, script, cases));

        verified.Value!.Gate!.Passed.Should().BeFalse("1.1 fails a case 1.0 passed");
        verified.Value.Gate.Regressions.Should().Equal("keeps-newer-files");

        registry.Promote(draft, new ToolPromotionRequest("idea", "inbox/test")).Failed.Should().BeTrue();

        var head = registry.Show("free-disk-by-cache").Value!.Record;
        head.Active.Should().Be("1.0");
        head.KnownGood.Should().Equal("1.0");
        Directory.Exists(Path.Combine(registry.Root(), "free-disk-by-cache", "versions", "1.1")).Should().BeFalse();
        registry.Audit("free-disk-by-cache").Should().Contain(one => one.Action == "reject" && one.Version == "1.1");
    }

    [Fact]
    public async Task Nothing_project_specific_reaches_the_registry()
    {
        var (registry, _) = _store.Registry();

        // Each way a project can ride along: in the script, the origin, a
        // case's argument, and an example.
        var drafts = new List<(ToolVersion Manifest, string Script, List<ToolCase> Cases)>
        {
            (Manifest("from-script", "1.0"), HardCoded, ToolStoreFixture.Cases()),
            (Manifest("from-origin", "1.0"),Generic, ToolStoreFixture.Cases()),
            (Manifest("from-case", "1.0"), Generic, ToolStoreFixture.Cases()),
            (Manifest("from-example", "1.0"), Generic, ToolStoreFixture.Cases()),
        };

        drafts[1].Manifest.Origin = "The system-watch team's cache kept filling the disk.";
        drafts[2].Cases[0].Args["CachePath"] = "/home/nigel/alpha/cache";
        drafts[3].Manifest.Examples[0].Command = "pwsh -File tool.ps1 -CachePath C:\\work\\beta\\cache";

        foreach (var (manifest, script, cases) in drafts)
        {
            var draft = _store.Draft(manifest, script, cases);
            var verified = await registry.VerifyAsync(draft, ToolStoreFixture.Agreed(manifest, script, cases));
            var promoted = registry.Promote(draft, new ToolPromotionRequest("lesson", "inbox/test"));

            if (manifest.Name == "from-case")
            {
                // The harness refuses a case's absolute path before it runs, so
                // the gate fails at verify and promote has nothing to take.
                verified.Value!.Gate!.Passed.Should().BeFalse("a case may only use {tmp}");
                verified.Value.Because.Should().Contain("'CachePath' is an absolute path");
                promoted.Failed.Should().BeTrue(manifest.Name + " did not pass verify");
            }
            else
            {
                verified.Value!.Gate!.Passed.Should().BeTrue(manifest.Name + " runs; what it carries is the question");
                promoted.Failed.Should().BeTrue(manifest.Name + " carries a project");
                promoted.Error.Should().Contain("carries a project");
            }

            Directory.Exists(Path.Combine(registry.Root(), manifest.Name)).Should().BeFalse(manifest.Name + " left nothing behind");
        }

        registry.Search(string.Empty, all: true).Should().BeEmpty();
    }

    // ------------------------------------------------------------ helpers

    private static ToolVersion Manifest(string name, string version)
    {
        var manifest = ToolStoreFixture.Manifest(name, version);
        manifest.Inputs = [new ToolInput { Name = "CachePath", Type = "path", Required = true, Describe = "The cache directory to clear." }];

        return manifest;
    }

    private static Brief BriefFor(string team) => new(
        "20260923-1400-b001",
        "remediator",
        "lead",
        ToolOffer.Remediator,
        "The build disk is full; free space without touching sources.",
        DeliverableKind.Answer,
        [],
        new BriefConstraints("implement", null, 40, null, []),
        ["the disk has room again"],
        TeamDirectory: "/teams/work/" + team);

    private static string Calling(string command) =>
        System.Text.Json.JsonSerializer.Serialize(new { command });

    private ToolNominationPass Pass(IToolRegistry registry) =>
        new(
            registry,
            new RunJournal(_store.Paths),
            new RemedyBook(_store.Paths),
            _store.Paths,
            new NoLessons(),
            new FakeProjects(ProjectB, _store.Paths.Paths.State),
            new WorkspaceManager(
                _store.Paths, new FakeGit(_store.Paths.Paths.State), new YamlStore(new NoOpFilePermissions()), TimeProvider.System));

    /// <summary>A remedy on a team's shelf, as a node registers one.</summary>
    private void Shelve(string team, string name, string script, int revision)
    {
        var shelf = Path.Combine(_store.Paths.Paths.State, "teams", "work", team, "remedies");
        Directory.CreateDirectory(shelf);

        File.WriteAllText(Path.Combine(shelf, name + ".ps1"), script);
        File.WriteAllText(
            Path.Combine(shelf, name + ".yaml"),
            $"name: {name}\nkind: disk\nwhat: Clears the build cache when the disk fills.\nassumes: pwsh\nproves: the disk has room\n"
            + $"script: {name}.ps1\nrevision: {revision}\n");
    }

    /// <summary>A finished run, as the runner leaves one: its journal and its remediator's report.</summary>
    private void FinishedRun(string runId, string team, string command)
    {
        var journal = new RunJournal(_store.Paths);
        var directory = journal.DirectoryOf(runId);
        Directory.CreateDirectory(directory);

        File.WriteAllLines(Path.Combine(directory, "journal.jsonl"),
        [
            """{"at":"2026-09-21T10:00:00+00:00","kind":"run.started","data":{"team":""" + "\"" + team + "\""
                + ""","goal":"keep the build disk clear","autonomy":"autonomous"}}""",
            """{"at":"2026-09-21T10:30:00+00:00","kind":"run.finished","data":{"ended":"done","outcome":"done"}}""",
        ]);

        var report = new Report("remediator", ReportStatus.Done, "Cleared the cache; the disk has room.", [],
            [new ReportEvidence(EvidenceKind.Command, command, EvidenceResult.Pass, "exit 0, freed")], []);
        File.WriteAllText(Path.Combine(directory, "report-remediator-1.json"), ReportReader.Write(report));
    }

    /// <summary>A launcher whose exit code depends on what it is asked to run.</summary>
    private sealed class ExitByCase(Func<ProcessRequest, int> exit) : IProcessLauncher
    {
        private readonly StubProcessLauncher _rest = new(string.Empty);

        public Task<OperationResult<ProcessOutcome>> RunAsync(
            ProcessRequest request, TimeSpan? timeout = null, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<ProcessOutcome>.Ok(new ProcessOutcome(exit(request), "freed", string.Empty)));

        public Task<OperationResult<int>> RunInteractiveAsync(ProcessRequest request, CancellationToken ct = default) =>
            _rest.RunInteractiveAsync(request, ct);

        public Task<OperationResult<IPipedProcess>> StartPipedAsync(ProcessRequest request, CancellationToken ct = default) =>
            _rest.StartPipedAsync(request, ct);

        public OperationResult StartDetached(ProcessRequest request) => _rest.StartDetached(request);
    }

    /// <summary>Memory with no lessons in it, so only runs and shelves are read.</summary>
    private sealed class NoLessons : IMemoryService
    {
        public Task<OperationResult<IReadOnlyList<MemoryTopic>>> ListAsync(
            string workspaceRoot, string slug, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<MemoryTopic>>.Ok([]));

        public Task<OperationResult<MemoryAudit>> AuditAsync(
            string workspaceRoot, string slug, int staleMonths = 6, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<MemoryTopic>> WriteAsync(
            string workspaceRoot, string slug, string name, string description, MemoryKind kind,
            IReadOnlyList<string> facts, bool acknowledgedSimilar = false,
            MemoryScope scope = MemoryScope.Project, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public OperationResult ValidateWrite(string name, string description, IReadOnlyList<string> facts) =>
            throw new NotSupportedException();

        public Task<OperationResult> RebuildIndexAsync(string workspaceRoot, string slug, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<MemoryCleanup>> CleanAsync(
            string workspaceRoot, string slug, bool apply, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public IReadOnlyList<string> CleanupPaths(string workspaceRoot, string slug) =>
            throw new NotSupportedException();

        public Task<OperationResult<string?>> ReadIndexAsync(string workspaceRoot, string slug, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
