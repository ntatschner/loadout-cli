using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Core.Teams;
using Loadout.Models.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The teams that ship, the ones a person writes, and what is wrong with
/// either.
/// </summary>
/// <remarks>
/// A team file is content, like a specialist, and fails the same way: not
/// by refusing to compile but by naming a role that is not there, a lead
/// that is not a node, or a gate nobody can decide, none of which anything
/// notices until a run is half way through. So every built-in is checked
/// here against the library it ships with, and each rule has a case that
/// breaks it.
/// </remarks>
public sealed class TeamCatalogueTests : IDisposable
{
    private static SpecialistCatalogue? _specialists;

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "loadout-teams-" + Guid.NewGuid().ToString("N"));

    private static async Task<SpecialistCatalogue> SpecialistsAsync() =>
        _specialists ??= await new SpecialistLibrary().LoadAsync(workspaceRoot: null);

    private async Task<TeamCatalogueResult> LoadAsync(string? slug = null) =>
        await new TeamCatalogue().LoadAsync(Directory.Exists(_workspace) ? _workspace : null, slug, await SpecialistsAsync());

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workspace))
            {
                Directory.Delete(_workspace, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task The_built_in_teams_load_and_every_one_of_them_checks_out()
    {
        var catalogue = await LoadAsync();

        catalogue.Teams.Keys.Should().BeEquivalentTo(
            ["iterating-project", "bug-hunt", "release-crew", "docs-crew", "dependency-sweep", "marketing-studio", "product-company"]);

        catalogue.Findings.Should().BeEmpty(
            "a shipped team naming a role that does not ship is a shipped defect");
    }

    [Fact]
    public async Task The_iterating_project_is_wired_as_the_design_says()
    {
        var team = (await LoadAsync()).Find("iterating-project")!;

        team.Lead.Should().Be("lead");
        team.Nodes["lead"].Role.Should().Be("role.project-lead");
        team.Nodes["lead"].Delegates.Should().Equal("planner", "implementer", "reviewer", "verifier");
        team.Nodes["implementer"].Worktree.Should().BeTrue();
        team.Nodes["implementer"].Parallel.Should().Be(3);
        team.Rules.Autonomy.Should().Be("supervised");
        team.Rules.Budget.Usd.Should().Be(25m);
        team.Rules.Budget.TurnsPerNode.Should().Be(40);
        team.Rules.Budget.WallClock.Should().Be("4h");
        team.Rules.Gates.Merge.Should().Equal("reviewer", "verifier");
        team.Rules.StopWhen.Should().Equal("goal_met", "budget_spent", "no_progress_2_rounds");
        team.Template.Should().BeFalse();
    }

    [Fact]
    public async Task The_company_is_a_template_and_the_release_crew_may_push_a_tag_only_when_autonomous()
    {
        var catalogue = await LoadAsync();

        catalogue.Find("product-company")!.Template.Should().BeTrue();
        catalogue.Find("product-company")!.Nodes["marketing"].Parameters.Should().ContainKey("department").WhoseValue.Should().Be("marketing");

        catalogue.Find("release-crew")!.Rules.Gates.OutwardAllowedWhenAutonomous.Should().Equal("git push --tags");
        catalogue.Find("marketing-studio")!.Rules.Gates.OutwardAllowedWhenAutonomous.Should().BeEmpty();
    }

    [Fact]
    public async Task A_workspace_team_of_the_same_name_replaces_the_built_in_whole()
    {
        WriteTeam("global", """
            name: iterating-project
            description: Mine.
            lead: boss
            nodes:
              boss: { role: role.project-lead, delegates: [worker] }
              worker: { role: role.implementer }
            rules: { autonomy: manual, gates: { outward: ask } }
            """);

        var team = (await LoadAsync()).Find("iterating-project")!;

        team.Description.Should().Be("Mine.");
        team.Lead.Should().Be("boss");
        team.Nodes.Keys.Should().BeEquivalentTo(["boss", "worker"], "replacing, not merging");
        team.Rules.Budget.Usd.Should().BeNull("nothing of the built-in survives");
    }

    [Fact]
    public async Task A_project_team_wins_over_a_workspace_one()
    {
        WriteTeam("global", "name: mine\ndescription: global\nlead: a\nnodes: { a: { role: role.project-lead } }\n");
        WriteTeam("project", "name: mine\ndescription: project\nlead: a\nnodes: { a: { role: role.project-lead } }\n");

        (await LoadAsync("demo")).Find("mine")!.Description.Should().Be("project");
        (await LoadAsync()).Find("mine")!.Description.Should().Be("global", "without a project only the workspace layer applies");
    }

    [Theory]
    [InlineData("lead: nobody\nnodes: { a: { role: role.project-lead } }", "team-lead", "'nobody' as its lead")]
    [InlineData("lead: a\nnodes: { a: { role: role.wizard } }", "team-role", "'role.wizard', which is not a role")]
    [InlineData("lead: a\nnodes: { a: { role: language.csharp } }", "team-role", "not a role in the library")]
    [InlineData("lead: a\nnodes: { a: { role: role.project-lead, delegates: [ghost] } }", "team-delegate", "'ghost'")]
    [InlineData("lead: a\nnodes: { a: { role: role.project-lead, parallel: 0 } }", "team-parallel", "at least 1")]
    [InlineData("lead: a\nnodes: { a: { role: role.project-lead } }\nrules: { autonomy: yolo }", "team-autonomy", "manual, supervised or autonomous")]
    [InlineData("lead: a\nnodes: { a: { role: role.project-lead } }\nrules: { gates: { outward: allow } }", "team-outward", "may only ask")]
    [InlineData("lead: a\nnodes: { a: { role: role.project-lead } }\nrules: { gates: { merge: [nobody] } }", "team-merge-gate", "'nobody'")]
    public async Task A_team_that_would_not_make_sense_to_run_is_a_finding_that_says_what_to_change(
        string body, string rule, string detail)
    {
        WriteTeam("global", "name: broken\n" + body + "\n");

        // A finding's Rule is its subject, the team, and its Kind is the rule
        // it broke, as the specialist findings have it.
        var findings = (await LoadAsync()).Findings.Where(f => f.Rule == "broken").ToList();

        findings.Should().ContainSingle(f => f.Kind == rule).Which.Detail.Should().Contain(detail);
    }

    [Fact]
    public async Task A_file_that_is_not_yaml_or_names_no_team_is_reported_and_the_rest_still_load()
    {
        WriteTeam("global", "name: [unclosed", "bad.yaml");
        WriteTeam("global", "description: nameless\n", "nameless.yaml");

        var catalogue = await LoadAsync();

        catalogue.Findings.Should().Contain(f => f.Kind == "team-yaml" && f.Detail.Contains("bad.yaml"));
        catalogue.Findings.Should().Contain(f => f.Kind == "team-name" && f.Detail.Contains("nameless.yaml"));
        catalogue.Find("iterating-project").Should().NotBeNull();
    }

    private void WriteTeam(string layer, string yaml, string? file = null)
    {
        var directory = layer == "global"
            ? Path.Combine(_workspace, "global", "teams")
            : Path.Combine(_workspace, "projects", "demo", "teams");

        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, file ?? "team.yaml"), yaml);
    }
}
