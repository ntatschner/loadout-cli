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

    private ToolNominator Nominator() =>
        new(_store.Registry().Registry, new RunJournal(_store.Paths), new RemedyBook(_store.Paths), _store.Paths);

    private static ReportEvidence Command(string command) =>
        new(EvidenceKind.Command, command, EvidenceResult.Pass, "exit 0");

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
