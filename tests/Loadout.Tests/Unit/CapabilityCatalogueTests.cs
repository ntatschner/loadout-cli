using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Core.Teams;
using Loadout.Models.Instructions;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The machinery a declaration can lean on, as a file.
/// </summary>
/// <remarks>
/// A declaration is prose in a brief and enforces nothing by itself. The first
/// real one worked only because seven separate things were built to match it.
/// A capability is that bundle as a file, so the next one is not seven commits
/// of C# — with one thing held back on purpose, which most of this covers.
/// </remarks>
public sealed class CapabilityCatalogueTests
{
    private static DeclarationCapability? Parse(string yaml, List<RuleFinding> findings) =>
        CapabilityCatalogue.Parse(yaml, "made-up.yaml", findings);

    [Fact]
    public async Task The_one_that_ships_is_readable_and_names_a_gate_that_exists()
    {
        var catalogue = await new CapabilityCatalogue().LoadAsync(null, null);

        catalogue.Findings.Should().BeEmpty();

        var remedies = catalogue.Capabilities.Should().ContainKey("remedies").WhoseValue;

        remedies.Shelf.Should().Be("remedies");
        remedies.Roles.Should().Contain("role.remediator");
        remedies.Gate.Should().Be("remedy");

        // Verbatim, because a paraphrase of a record format is a different
        // record format. This is the text that tells a node what registering
        // something actually means.
        remedies.Brief.Should().Contain("remedies/<name>.yaml")
            .And.Contain("A record claiming to be trusted decides nothing.");
    }

    [Fact]
    public void A_file_cannot_invent_a_gate()
    {
        // The line the whole design rests on. A capability names a decision
        // that already exists; it never describes one. A file that could
        // define its own gate would be a shared file deciding what agents may
        // execute, which is what moving trust out of the team's directory
        // existed to prevent - after a node wrote itself a record claiming to
        // be trusted and ran unasked.
        var findings = new List<RuleFinding>();

        Parse("id: mine\ngate: anything-i-like\n", findings).Should().BeNull();

        var said = findings.Should().ContainSingle().Subject;

        said.Severity.Should().Be(RuleFindingSeverity.Error);
        said.Kind.Should().Be("capability-gate");
        said.Detail.Should().Contain("cannot be described in a file");
    }

    [Theory]
    [InlineData("remedy")]
    [InlineData("outward")]
    public void The_gates_that_exist_are_accepted(string gate)
    {
        var findings = new List<RuleFinding>();

        Parse($"id: mine\ngate: {gate}\n", findings).Should().NotBeNull();

        findings.Should().BeEmpty();
    }

    [Fact]
    public void A_capability_that_gates_nothing_is_allowed()
    {
        // Not everything a team keeps needs permission to act on. A shelf of
        // notes is a shelf of notes.
        var findings = new List<RuleFinding>();

        Parse("id: notes\nshelf: notes\n", findings).Should().NotBeNull();

        findings.Should().BeEmpty();
    }

    [Fact]
    public void One_with_no_id_is_refused_because_nothing_could_ask_for_it()
    {
        var findings = new List<RuleFinding>();

        Parse("shelf: notes\n", findings).Should().BeNull();

        findings.Should().ContainSingle().Which.Kind.Should().Be("capability-id");
    }

    [Fact]
    public void Text_that_is_not_a_capability_at_all_is_reported_rather_than_thrown()
    {
        var findings = new List<RuleFinding>();

        Parse("id: mine\n\tshelf: [unclosed\n", findings).Should().BeNull();

        findings.Should().ContainSingle().Which.Kind.Should().Be("capability-unreadable");
    }

    [Fact]
    public async Task A_team_asking_for_something_nothing_provides_is_told_so()
    {
        var team = new TeamDefinition { Name = "made-up", Lead = "lead" };

        team.Nodes["lead"] = new TeamNode { Role = "role.project-lead" };
        team.Capabilities.Add("schema-migrations");

        var findings = TeamCatalogue.Check(
            team,
            await new SpecialistLibrary().LoadAsync(workspaceRoot: null),
            (await new CapabilityCatalogue().LoadAsync(null, null)).Capabilities);

        findings.Should().ContainSingle(f => f.Kind == "team-capability-unknown")
            .Which.Detail.Should().Contain("loadout team capabilities");
    }

    [Fact]
    public async Task A_team_that_cannot_act_on_what_it_asked_for_is_warned()
    {
        // The whole point of naming the machinery: a team can now be checked
        // against it, rather than against a guess at what its prose meant.
        var team = new TeamDefinition { Name = "made-up", Lead = "lead" };

        team.Nodes["lead"] = new TeamNode { Role = "role.project-lead" };
        team.Capabilities.Add("remedies");

        var findings = TeamCatalogue.Check(
            team,
            await new SpecialistLibrary().LoadAsync(workspaceRoot: null),
            (await new CapabilityCatalogue().LoadAsync(null, null)).Capabilities);

        var said = findings.Should().ContainSingle(f => f.Kind == "team-capability-inert").Subject;

        said.Severity.Should().Be(RuleFindingSeverity.Warning);
        said.Detail.Should().Contain("role.remediator");
    }

    [Fact]
    public async Task Saying_what_it_relies_on_stands_the_keyword_guess_down()
    {
        // The fallback exists for a team that named nothing. Once a team says
        // what it relies on there is something better to check than its prose,
        // and two findings about one thing is one too many.
        var team = new TeamDefinition { Name = "made-up", Lead = "lead" };

        team.Nodes["lead"] = new TeamNode { Role = "role.project-lead" };
        team.Capabilities.Add("remedies");
        team.Declarations.Add("Register any remedy you write in the team's directory.");

        var findings = TeamCatalogue.Check(
            team,
            await new SpecialistLibrary().LoadAsync(workspaceRoot: null),
            (await new CapabilityCatalogue().LoadAsync(null, null)).Capabilities);

        findings.Should().NotContain(f => f.Kind == "team-declaration-inert");
        findings.Should().ContainSingle(f => f.Kind == "team-capability-inert");
    }

    [Fact]
    public async Task The_team_that_ships_with_one_asks_for_it_and_can_use_it()
    {
        var specialists = await new SpecialistLibrary().LoadAsync(workspaceRoot: null);
        var catalogue = await new TeamCatalogue().LoadAsync(null, null, specialists);

        catalogue.Teams["system-watch"].Capabilities.Should().Contain("remedies");

        catalogue.Findings.Should().NotContain(f =>
            f.Rule == "system-watch"
            && (f.Kind == "team-capability-inert" || f.Kind == "team-capability-unknown"));
    }
}
