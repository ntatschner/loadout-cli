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
