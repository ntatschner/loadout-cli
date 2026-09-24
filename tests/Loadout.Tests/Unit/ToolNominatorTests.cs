using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Core.Tools;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The four ways finished work is recognised as worth a tool, read from runs
/// and shelves written the way a real run writes them.
/// </summary>
public sealed class ToolNominatorTests : IDisposable
{
    private const string Cache = "param([string]$CachePath)\nGet-ChildItem $CachePath -Recurse | Remove-Item -Force\nWrite-Output 'freed'\n";

    private readonly ToolStoreFixture _store = new();

    public void Dispose() => _store.Dispose();

    [Fact]
    public void Rule_1_the_same_remedy_on_two_team_shelves_is_nominated()
    {
        Shelve("alpha", "clear-cache", Cache);
        Shelve("beta", "free-disk", Cache);

        var scanned = Nominator().Scan([]);

        scanned.Should().ContainSingle(one => one.Nomination.Rule == 1);
        scanned.Single().Filed!.Succeeded.Should().BeTrue();
        Directory.EnumerateFiles(Path.Combine(_store.Paths.Paths.State, "tools", "inbox")).Should().ContainSingle();
    }

    [Fact]
    public void Rule_2_a_revised_remedy_that_passed_in_two_runs_is_nominated()
    {
        Shelve("alpha", "clear-cache", Cache, revision: 1);
        Run("20260901-1000-a001", "alpha", Command("pwsh -NoProfile -File remedies/clear-cache.ps1"));
        Run("20260902-1000-a002", "alpha", Command("pwsh -NoProfile -File remedies/clear-cache.ps1"));

        Nominator().Find([]).Should().ContainSingle(one => one.Rule == 2);
    }

    [Fact]
    public void Rule_3_one_command_shape_passing_for_two_teams_is_nominated()
    {
        Run("20260901-1000-a001", "alpha", Command("dotnet test --filter Alpha.Tests"));
        Run("20260902-1000-b001", "beta", Command("dotnet test --filter Beta.Tests"));

        Nominator().Find([]).Should().ContainSingle(one => one.Rule == 3 && one.Key == "3 dotnet test --filter <arg>");
    }

    [Fact]
    public void Rule_4_a_lesson_naming_a_command_that_passed_is_nominated()
    {
        Run("20260901-1000-a001", "alpha", Command("docker system prune --force"));

        Nominator().Find(["When the disk fills, docker system prune clears the build layers."])
            .Should().ContainSingle(one => one.Rule == 4);
    }

    [Fact]
    public async Task Covered_by_an_active_tool_is_not_nominated()
    {
        var (registry, _) = _store.Registry();
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Cache, ToolStoreFixture.Cases());

        Shelve("alpha", "clear-cache", Cache);
        Shelve("beta", "free-disk", Cache);

        Nominator().Find([]).Should().BeEmpty();
    }

    [Fact]
    public async Task A_covered_hit_becomes_a_refiner_hint()
    {
        var (registry, _) = _store.Registry();
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Cache, ToolStoreFixture.Cases());

        Shelve("alpha", "clear-cache", Cache);
        Shelve("beta", "free-disk", Cache);

        Nominator().Scan([]).Should().ContainSingle(one => one.Filed != null && one.Filed.Succeeded);

        var filed = File.ReadAllText(Directory.EnumerateFiles(Path.Combine(_store.Paths.Paths.State, "tools", "inbox")).Single());
        filed.Should().Contain("kind: idea").And.Contain("tool: free-cache").And.Contain("by: nominator");
    }

    [Fact]
    public async Task A_command_an_active_tool_wraps_is_not_nominated()
    {
        var (registry, _) = _store.Registry();
        await _store.PromoteAsync(
            registry, ToolStoreFixture.Manifest("prune-layers", "1.0"), "docker system prune --force\n", ToolStoreFixture.Cases());

        Run("20260901-1000-a001", "alpha", Command("docker system prune --force"));
        Run("20260902-1000-b001", "beta", Command("docker system prune --force"));

        Nominator().Find(["When the disk fills, docker system prune clears the build layers."]).Should().BeEmpty();
    }

    [Fact]
    public void Another_teams_same_named_script_does_not_count()
    {
        Shelve("alpha", "clear-cache", Cache, revision: 1);
        Run("20260901-1000-b001", "beta", Command("pwsh -NoProfile -File remedies/clear-cache.ps1"));
        Run("20260902-1000-b002", "beta", Command("pwsh -NoProfile -File remedies/clear-cache.ps1"));

        Nominator().Find([]).Should().NotContain(one => one.Rule == 2);
    }

    [Fact]
    public void fix_ps1_does_not_match_prefix_ps1()
    {
        Shelve("alpha", "fix", Cache, revision: 1);
        Run("20260901-1000-a001", "alpha", Command("pwsh -NoProfile -File remedies/prefix.ps1"));
        Run("20260902-1000-a002", "alpha", Command("pwsh -NoProfile -File remedies/prefix.ps1"));

        Nominator().Find([]).Should().NotContain(one => one.Rule == 2);
    }

    [Fact]
    public void A_third_team_joining_a_cluster_is_not_filed_again()
    {
        Shelve("alpha", "clear-cache", Cache);
        Shelve("beta", "free-disk", Cache);
        Nominator().Scan([]).Should().ContainSingle(one => one.Filed != null && one.Filed.Succeeded);

        // A near copy that sorts first, so the cluster's key moves to it and
        // only the keys it was filed under before can recognise it.
        var lowest = NearCopies(8).First(one =>
            string.CompareOrdinal(RemedyCeiling.Fingerprint(one), RemedyCeiling.Fingerprint(Cache)) < 0);
        Shelve("gamma", "tidy-cache", lowest);

        var again = Nominator().Scan([]);

        again.Should().ContainSingle(one => one.Nomination.Rule == 1, "gamma's near copy joins the same cluster");
        again.Single().Nomination.Key.Should().Be("1 " + RemedyCeiling.Fingerprint(lowest));
        again.Should().OnlyContain(one => one.Filed == null, "the cluster was filed before gamma joined it");
        Directory.EnumerateFiles(Path.Combine(_store.Paths.Paths.State, "tools", "inbox")).Should().ContainSingle();
    }

    [Fact]
    public void A_cluster_that_loses_its_filed_key_is_not_filed_again()
    {
        var copies = NearCopies(3)
            .OrderBy(RemedyCeiling.Fingerprint, StringComparer.Ordinal)
            .ToList();

        Shelve("alpha", "clear-cache", copies[0]);
        Shelve("beta", "free-disk", copies[1]);
        Nominator().Scan([]).Should().ContainSingle(one => one.Filed != null && one.Filed.Succeeded);

        // The script the cluster was keyed by goes, and another joins.
        Directory.Delete(Path.Combine(_store.Paths.Paths.State, "teams", "work", "alpha"), recursive: true);
        Shelve("gamma", "tidy-cache", copies[2]);

        var again = Nominator().Scan([]);

        again.Should().ContainSingle(one => one.Nomination.Rule == 1, "beta and gamma still keep the same remedy");
        again.Should().OnlyContain(one => one.Filed == null, "beta's script was one of the cluster filed before");
        Directory.EnumerateFiles(Path.Combine(_store.Paths.Paths.State, "tools", "inbox")).Should().ContainSingle();
    }

    [Fact]
    public async Task A_one_word_command_is_not_covered_by_a_script_mentioning_it_in_prose()
    {
        var (registry, _) = _store.Registry();
        await _store.PromoteAsync(
            registry,
            ToolStoreFixture.Manifest("free-cache", "1.0"),
            "# Make sure the cache path exists before clearing it.\n" + Cache,
            ToolStoreFixture.Cases());

        Run("20260901-1000-a001", "alpha", Command("make"));
        Run("20260902-1000-b001", "beta", Command("make"));

        Nominator().Find([]).Should().ContainSingle(one => one.Rule == 3 && one.Key == "3 make",
            "the tool's script says 'make sure', which is not running make");
    }

    /// <summary>Copies of the cache script differing only in what they print, so each has its own fingerprint.</summary>
    private static List<string> NearCopies(int count) =>
        [.. Enumerable.Range(0, count).Select(one => Cache + $"Write-Output 'pass {one}'\n")];

    [Fact]
    public void A_nomination_never_carries_a_secret_value()
    {
        const string Key = "AKIAABCDEFGHIJKLMNOP";
        var script = Cache + "$key = '" + Key + "'\n";

        Shelve("alpha", "clear-cache", script);
        Shelve("beta", "free-disk", script);

        var scanned = Nominator().Scan([]);

        scanned.Should().ContainSingle(one => one.Filed != null && one.Filed.Failed);

        var catalogue = Path.Combine(_store.Paths.Paths.State, "tools");
        var written = Directory.Exists(catalogue)
            ? Directory.EnumerateFiles(catalogue, "*", SearchOption.AllDirectories)
            : [];

        foreach (var file in written)
        {
            File.ReadAllText(file).Should().NotContain(Key, $"{file} was written by the nominator");
        }
    }

    /// <summary>An instruction paragraph of well over thirty words, as a lead might hand every reviewer.</summary>
    private const string Checklist =
        "Before you report, read every changed file from top to bottom, run the tests that cover it, "
        + "and write down each command you ran with the exact result it printed. Name anything you could "
        + "not verify and say what would verify it, rather than leaving it out of the report altogether.";

    [Fact]
    public void Rule_5_a_repeated_workflow_across_teams_is_nominated()
    {
        Run("20260901-1000-a001", "alpha",
            Command("git fetch origin"), Command("dotnet build Alpha.slnx"), Command("dotnet test Alpha.slnx --no-build"));
        Run("20260902-1000-b001", "beta",
            Command("git fetch origin"), Command("dotnet build Beta.slnx"), Command("dotnet test Beta.slnx --no-build"));

        Nominator().Find([]).Should().ContainSingle(one => one.Rule == 5
            && one.Key == "5 git fetch origin | dotnet build <arg> | dotnet test <arg> --no-build");
    }

    [Fact]
    public void Rule_6_a_repeated_prompt_across_teams_is_nominated()
    {
        Run("20260901-1000-a001", "alpha", Brief("Review the parser change.\n\n" + Checklist));
        Run("20260902-1000-b001", "beta", Brief("Check the new export screen.\n\n  " + Checklist.ToUpperInvariant().Replace(" ", "  \n ", StringComparison.Ordinal)));

        var scanned = Nominator().Scan([]);

        scanned.Should().ContainSingle(one => one.Nomination.Rule == 6);

        var filed = File.ReadAllText(Directory.EnumerateFiles(Path.Combine(_store.Paths.Paths.State, "tools", "inbox")).Single());
        filed.Should().Contain("kind: candidate").And.Contain("- prompt");
        filed.Should().NotContain("parser").And.NotContain("export screen", "only the shared paragraph is carried, not either project's");
    }

    [Fact]
    public void Rule_7_a_shared_integration_is_nominated()
    {
        // Every node talks to Loadout's own server, so that says nothing.
        Run("20260901-1000-a001", "alpha", Stream(
            new NodeStep(DateTimeOffset.UnixEpoch, "tool", "mcp__github__create_issue", "alpha/repo"),
            new NodeStep(DateTimeOffset.UnixEpoch, "tool", "mcp__loadout__loadout_progress")));
        Run("20260902-1000-b001", "beta",
            new ReportEvidence(EvidenceKind.Command, "mcp__github__list_pulls beta/repo", EvidenceResult.Pass, "3 open"),
            new ReportEvidence(EvidenceKind.Command, "mcp__loadout__loadout_recall build", EvidenceResult.Pass, "2 topics"));
        Run("20260903-1000-a002", "alpha", Stream(new NodeStep(DateTimeOffset.UnixEpoch, "tool", "WebFetch", "https://status.example.com/api/v2")));
        Run("20260904-1000-b002", "beta", Command("curl -s https://status.example.com/api/v2/summary.json"));

        var found = Nominator().Find([]);

        found.Should().Contain(one => one.Rule == 7 && one.Key == "7 mcp github");
        found.Should().Contain(one => one.Rule == 7 && one.Key == "7 http status.example.com");
        found.Should().NotContain(one => one.Rule == 7 && one.Key.Contains("loadout", StringComparison.Ordinal));
    }

    [Fact]
    public void Back_to_back_repeated_steps_collapse_into_one()
    {
        // Alpha built twice and ran the tests twice; it is still the same
        // three steps beta ran once each.
        Run("20260901-1000-a001", "alpha",
            Command("git fetch origin"), Command("dotnet build Alpha.slnx"), Command("dotnet build Alpha.slnx"),
            Command("dotnet test Alpha.slnx --no-build"), Command("dotnet test Alpha.slnx --no-build"));
        Run("20260902-1000-b001", "beta",
            Command("git fetch origin"), Command("dotnet build Beta.slnx"), Command("dotnet test Beta.slnx --no-build"));

        Nominator().Find([]).Should().ContainSingle(one => one.Rule == 5
            && one.Key == "5 git fetch origin | dotnet build <arg> | dotnet test <arg> --no-build");
    }

    /// <summary>
    /// What the runner writes into every lead brief that has criteria, as it
    /// stood in runs 20260922-2154-699c and 20260923-1216-be60. Copied rather
    /// than referenced, because what is tested is that text the runner writes
    /// is never read, whatever it says.
    /// </summary>
    private static readonly string[] LeadDoneWhen =
    [
        "every criterion below is met, with the evidence cited from your nodes' reports",
        "your final report carries one coverage entry per criterion, each saying in 'understood' "
            + "what you took the criterion to mean, with a verdict of met, unmet or not-attempted, and "
            + "every met saying in 'because' which node, report and evidence shows it",
        "every report you make says in 'goal_understood', in a sentence, what you take the goal to "
            + "mean, so the person who wrote it can see you are working to what they asked",
    ];

    [Fact]
    public void Runner_boilerplate_in_two_teams_lead_briefs_is_not_a_prompt()
    {
        LeadBrief("20260922-2154-699c", "marketing-studio",
            "Your goal is to reimagine the main readme and all its documents.",
            ["the user needs to see what Loadout will do for them"]);
        LeadBrief("20260923-1216-be60", "iterating-project",
            "global tool creator, for all teams, that sits in the background building reusable tools",
            ["Tools are versioned and changes can be traced back to the lesson that caused them."]);

        Nominator().Find([]).Should().NotContain(one => one.Rule == 6, "nobody wrote the done_when of a lead brief but the runner");
    }

    [Fact]
    public void A_shared_paragraph_naming_a_project_path_does_not_reach_the_inbox()
    {
        const string Where = @"C:\git\alpha-app\docs\review-notes.md";
        var paragraph = Checklist + " Keep your notes in " + Where + " as you go.";

        Run("20260901-1000-a001", "alpha", Brief(paragraph));
        Run("20260902-1000-b001", "beta", Brief(paragraph));

        Nominator().Scan([]);

        var inbox = Path.Combine(_store.Paths.Paths.State, "tools", "inbox");
        var filed = Directory.Exists(inbox) ? Directory.EnumerateFiles(inbox).Select(File.ReadAllText).ToList() : [];

        filed.Should().NotContain(one => one.Contains(Where, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_second_pass_does_not_read_a_finished_runs_streams_again()
    {
        Run("20260901-1000-a001", "alpha", Stream(new NodeStep(DateTimeOffset.UnixEpoch, "tool", "mcp__github__create_issue", "alpha/repo")));
        Run("20260902-1000-b001", "beta", Stream(new NodeStep(DateTimeOffset.UnixEpoch, "tool", "mcp__github__list_pulls", "beta/repo")));

        var nominator = Nominator();
        nominator.Find([]).Should().Contain(one => one.Key == "7 mcp github");

        // Both streams now name another server. A pass that read them again
        // would find it; a fresh nominator does, which shows the change is real.
        foreach (var run in new[] { "20260901-1000-a001", "20260902-1000-b001" })
        {
            File.WriteAllText(
                Path.Combine(new RunJournal(_store.Paths).DirectoryOf(run), NodeStream.FileFor("worker/1")),
                NodeStream.Line(new NodeStep(DateTimeOffset.UnixEpoch, "tool", "mcp__jira__search", "x")) + "\n");
        }

        Nominator().Find([]).Should().Contain(one => one.Key == "7 mcp jira", "a fresh nominator reads the rewritten streams");
        nominator.Find([]).Should().NotContain(one => one.Key == "7 mcp jira", "a finished run's streams were read once already");
    }

    [Fact]
    public async Task An_unrelated_script_mentioning_github_does_not_cover_an_mcp_github_nomination()
    {
        var (registry, _) = _store.Registry();
        await _store.PromoteAsync(
            registry,
            ToolStoreFixture.Manifest("free-cache", "1.0"),
            "# Clears the cache a github runner leaves behind.\n" + Cache,
            ToolStoreFixture.Cases());

        Run("20260901-1000-a001", "alpha", Stream(new NodeStep(DateTimeOffset.UnixEpoch, "tool", "mcp__github__create_issue", "alpha/repo")));
        Run("20260902-1000-b001", "beta", Stream(new NodeStep(DateTimeOffset.UnixEpoch, "tool", "mcp__github__list_pulls", "beta/repo")));

        Nominator().Find([]).Should().Contain(one => one.Key == "7 mcp github", "clearing a cache is not talking to the github server");
    }

    [Fact]
    public void One_team_repeating_itself_is_not_nominated_for_5_6_7()
    {
        foreach (var run in new[] { "20260901-1000-a001", "20260902-1000-a002", "20260903-1000-a003" })
        {
            Run(run, "alpha",
                Command("git fetch origin"), Command("dotnet build Alpha.slnx"), Command("dotnet test Alpha.slnx --no-build"),
                Command("curl -s https://status.example.com/api/v2/summary.json"),
                new ReportEvidence(EvidenceKind.Command, "mcp__github__list_pulls alpha/repo", EvidenceResult.Pass, "3 open"));
            File.WriteAllText(Path.Combine(new RunJournal(_store.Paths).DirectoryOf(run), "brief-reviewer-1.json"), Brief(Checklist).Json);
        }

        Nominator().Find([]).Should().NotContain(one => one.Rule >= 5);
    }

    [Fact]
    public void A_prompt_nomination_carries_no_secret_value()
    {
        const string Key = "AKIAABCDEFGHIJKLMNOP";
        var paragraph = Checklist + " Sign in with the access key " + Key + " when the tests need the bucket.";

        Run("20260901-1000-a001", "alpha", Brief(paragraph));
        Run("20260902-1000-b001", "beta", Brief(paragraph));

        var scanned = Nominator().Scan([]);

        scanned.Should().ContainSingle(one => one.Nomination.Rule == 6 && one.Filed != null && one.Filed.Failed);

        var catalogue = Path.Combine(_store.Paths.Paths.State, "tools");
        var written = Directory.Exists(catalogue)
            ? Directory.EnumerateFiles(catalogue, "*", SearchOption.AllDirectories)
            : [];

        foreach (var file in written)
        {
            File.ReadAllText(file).Should().NotContainEquivalentOf(Key, $"{file} was written by the nominator");
        }
    }

    private ToolNominator Nominator() =>
        new(_store.Registry().Registry, new RunJournal(_store.Paths), new RemedyBook(_store.Paths), _store.Paths);

    private static ReportEvidence Command(string command) =>
        new(EvidenceKind.Command, command, EvidenceResult.Pass, "exit 0");

    /// <summary>A brief for a reviewer, to put beside a run's report.</summary>
    private static Extra Brief(string task) =>
        new("brief-reviewer-1.json", ReportReader.Write(new Brief(
            "run", "reviewer/1", "lead", "role.reviewer", task, DeliverableKind.Answer, [], new BriefConstraints("review"), [])));

    /// <summary>A finished run of a team whose lead brief carries the runner's done_when and these criteria.</summary>
    private void LeadBrief(string runId, string team, string goal, string[] criteria) =>
        Run(runId, team, new Extra("brief-lead.json", ReportReader.Write(new Brief(
            runId, "lead", null, "role.project-lead", goal, DeliverableKind.Answer, [],
            new BriefConstraints("coordinate", MaxTurns: 30), LeadDoneWhen,
            TeamDirectory: @"C:\Users\someone\AppData\Local\Loadout\teams\work\" + team,
            Criteria: criteria))));

    /// <summary>A node's stream, as the runner records one.</summary>
    private static Extra Stream(params NodeStep[] steps) =>
        new(NodeStream.FileFor("worker/1"), string.Join('\n', steps.Select(NodeStream.Line)) + "\n");

    private sealed record Extra(string Name, string Json);

    /// <summary>A finished run with no evidence of its own, only this file beside it.</summary>
    private void Run(string runId, string team, Extra extra)
    {
        Run(runId, team);
        File.WriteAllText(Path.Combine(new RunJournal(_store.Paths).DirectoryOf(runId), extra.Name), extra.Json);
    }

    /// <summary>A remedy on a team's shelf, as a node registers one.</summary>
    private void Shelve(string team, string name, string script, int revision = 0)
    {
        var shelf = Path.Combine(_store.Paths.Paths.State, "teams", "work", team, "remedies");
        Directory.CreateDirectory(shelf);

        File.WriteAllText(Path.Combine(shelf, name + ".ps1"), script);
        File.WriteAllText(
            Path.Combine(shelf, name + ".yaml"),
            $"name: {name}\nkind: disk\nwhat: Clears a cache that fills the disk.\nassumes: pwsh\nproves: the disk has room\n"
            + $"script: {name}.ps1\nrevision: {revision}\n");
    }

    /// <summary>A finished run of a team, with one report carrying this evidence.</summary>
    private void Run(string runId, string team, params ReportEvidence[] evidence)
    {
        var journal = new RunJournal(_store.Paths);
        var directory = journal.DirectoryOf(runId);
        Directory.CreateDirectory(directory);

        File.WriteAllLines(Path.Combine(directory, "journal.jsonl"),
        [
            """{"at":"2026-09-01T10:00:00+00:00","kind":"run.started","data":{"team":""" + "\"" + team + "\""
                + ""","goal":"keep it up","autonomy":"autonomous"}}""",
            """{"at":"2026-09-01T10:30:00+00:00","kind":"run.finished","data":{"ended":"done","outcome":"done"}}""",
        ]);

        var report = new Report("remediator", ReportStatus.Done, "Fixed it.", [], evidence, []);
        File.WriteAllText(Path.Combine(directory, "report-remediator-1.json"), ReportReader.Write(report));

        journal.Summarise(runId).Value!.Finished.Should().NotBeNull("the fixture is a finished run");
    }
}
