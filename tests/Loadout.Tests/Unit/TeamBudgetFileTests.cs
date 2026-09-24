using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Core.Teams;
using Loadout.Models.Instructions;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// A team file's budget: a figure, <c>none</c> on purpose, or nothing said.
/// </summary>
/// <remarks>
/// <c>usd: none</c> used to make the whole file unreadable, because the figure
/// is a decimal and YamlDotNet cannot make one of a word. The file was dropped
/// and a lower layer's team of the same name ran instead.
/// </remarks>
public sealed class TeamBudgetFileTests
{
    [Theory]
    [InlineData("usd: 25", "25")]
    [InlineData("usd: 12.50", "12.5")]
    [InlineData("usd: none", "none")]
    [InlineData("usd: None", "none")]
    [InlineData("turns_per_node: 40", "")]
    public void A_team_file_budget_reads_as_a_figure_none_or_nothing(string line, string cap)
    {
        var team = Read($"{{ {line}, wall_clock: 2h }}");

        team.Rules.Budget.Cap.ToString().Should().Be(cap);
        team.Rules.Budget.WallClock.Should().Be("2h", "the rest of the budget is still read");
    }

    [Fact]
    public void The_rest_of_the_budget_survives_a_none()
    {
        var team = Read("{ usd: none, turns_per_node: 30, wall_clock: 90m }");

        team.Rules.Budget.Uncapped.Should().BeTrue();
        team.Rules.Budget.Usd.Should().BeNull();
        team.Rules.Budget.TurnsPerNode.Should().Be(30);
        team.Rules.Budget.WallClock.Should().Be("90m");
    }

    [Fact]
    public void A_word_that_is_not_none_is_a_finding_not_a_quiet_default()
    {
        var findings = new List<RuleFinding>();

        TeamCatalogue.Parse(Yaml("{ usd: plenty }"), "team.yaml", findings).Should().BeNull();

        findings.Should().ContainSingle().Which.Detail.Should().Contain("plenty").And.Contain("none");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task A_figure_of_zero_or_less_is_named_as_a_mistake(string usd)
    {
        var findings = TeamCatalogue.Check(
            Read($"{{ usd: {usd} }}"),
            await new SpecialistLibrary().LoadAsync(workspaceRoot: null));

        findings.Should().Contain(f => f.Kind == "team-budget")
            .Which.Detail.Should().Contain("none");
    }

    [Fact]
    public async Task A_team_with_no_cap_on_purpose_is_not_a_mistake()
    {
        var findings = TeamCatalogue.Check(
            Read("{ usd: none }"),
            await new SpecialistLibrary().LoadAsync(workspaceRoot: null));

        findings.Should().NotContain(f => f.Kind == "team-budget");
    }

    private static TeamDefinition Read(string budget)
    {
        var findings = new List<RuleFinding>();
        var team = TeamCatalogue.Parse(Yaml(budget), "team.yaml", findings);

        findings.Should().BeEmpty();

        return team!;
    }

    private static string Yaml(string budget) =>
        $$"""
        name: mine
        description: Mine.
        lead: boss
        nodes:
          boss: { role: role.project-lead }
        rules:
          autonomy: supervised
          budget: {{budget}}
          gates: { outward: ask }
        """;
}
