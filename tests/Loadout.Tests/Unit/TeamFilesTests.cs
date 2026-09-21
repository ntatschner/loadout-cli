using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Teams of your own, as files.
/// </summary>
/// <remarks>
/// The copy is the part with something to get wrong. It is a text edit rather
/// than a reserialisation, which is what keeps the comments, the key order and
/// the inline maps of the file somebody chose to copy — and which means the two
/// lines it changes have to be matched at the top level and nowhere else.
/// </remarks>
public sealed class TeamFilesTests
{
    [Theory]
    [InlineData("bug-hunt", true)]
    [InlineData("a", true)]
    [InlineData("crew2", true)]
    [InlineData("Bug-Hunt", false)]
    [InlineData("bug hunt", false)]
    [InlineData("-leading", false)]
    [InlineData("trailing-", false)]
    [InlineData("", false)]
    public void What_can_be_a_teams_name(string name, bool allowed) =>
        TeamFiles.Names(name).Should().Be(allowed);

    /// <remarks>
    /// The name becomes a file name, so these are the cases that would write a
    /// team outside the teams directory. The same reasoning as a run
    /// identifier, and the same reason it is checked where the joining happens
    /// rather than trusted from the caller.
    /// </remarks>
    [Theory]
    [InlineData("../elsewhere")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    public void A_name_that_would_escape_the_directory_is_refused(string name) =>
        TeamFiles.Names(name).Should().BeFalse();

    [Fact]
    public void A_copy_takes_the_new_name_and_leaves_everything_else()
    {
        const string source = """
            name: product-company
            description: A shape to copy.
            lead: exec
            nodes:
              exec:
                role: role.exec-lead
                parameters: { department: engineering }
            """;

        var copy = TeamFiles.Renamed(source, "mine");

        copy.Should().Contain("name: mine");
        copy.Should().NotContain("name: product-company");

        // Everything that is not the name, byte for byte. A copy that came back
        // alphabetised with the inline map expanded would be a worse starting
        // point than the file somebody was reading when they chose to copy it.
        copy.Should().Contain("description: A shape to copy.");
        copy.Should().Contain("parameters: { department: engineering }");
        copy.Should().Contain("role: role.exec-lead");
    }

    [Fact]
    public void A_copy_of_a_template_is_not_a_template()
    {
        // The whole point of copying one. 'team run' refuses a template, so a
        // copy that kept the flag would be a team somebody had just made and
        // could not run.
        var copy = TeamFiles.Renamed(
            "name: product-company\ntemplate: true\nlead: exec\n", "mine");

        copy.Should().NotContain("template:");
        copy.Should().Contain("name: mine");
    }

    [Fact]
    public void A_node_called_name_is_left_alone()
    {
        // 'name:' under a node is that node's business. Matched at the start of
        // a line with no indentation for exactly this reason.
        var copy = TeamFiles.Renamed(
            "name: was\nnodes:\n  lead:\n    name: not-the-teams-name\n", "now");

        copy.Should().Contain("name: now");
        copy.Should().Contain("    name: not-the-teams-name");
    }

    [Fact]
    public void A_file_that_never_named_itself_gets_a_name()
    {
        TeamFiles.Renamed("lead: exec\n", "mine").Should().StartWith("name: mine");
    }

    [Fact]
    public void A_team_that_ships_and_a_teams_from_a_pack_are_refused_with_the_way_round_it()
    {
        // Refusals with a way forward rather than dead ends. Editing a file
        // inside a pack checkout is what the next 'pack update' overwrites, and
        // saying so without naming the copy is how somebody does it anyway.
        TeamFiles.Whyever("docs-crew", SpecialistOrigin.BuiltIn, "resource")
            .Should().Contain("team new docs-crew-mine --from docs-crew");

        TeamFiles.Whyever("house-style", SpecialistOrigin.Pack, "C:/packs/x/teams/house-style.yaml")
            .Should().Contain("pack update").And.Contain("--from house-style");
    }

    [Fact]
    public void A_team_of_yours_is_not_refused()
    {
        TeamFiles.Whyever("mine", SpecialistOrigin.Workspace, "C:/ws/global/teams/mine.yaml")
            .Should().BeNull();

        TeamFiles.Whyever("mine", SpecialistOrigin.Project, "C:/ws/projects/p/teams/mine.yaml")
            .Should().BeNull();
    }

    [Fact]
    public void A_team_whose_source_was_never_recorded_has_no_file_to_change() =>
        TeamFiles.Whyever("mine", SpecialistOrigin.Workspace, null)
            .Should().Contain("no file to change");

    [Fact]
    public void Where_a_team_of_yours_goes()
    {
        TeamFiles.DirectoryFor("C:/ws", null)
            .Should().Be(Path.Combine("C:/ws", "global", "teams"));

        TeamFiles.DirectoryFor("C:/ws", "loadout-cli")
            .Should().Be(Path.Combine("C:/ws", "projects", "loadout-cli", "teams"));
    }

    /// <remarks>
    /// The scaffold is only worth writing if what it writes is a team. Parsed
    /// with the real parser rather than eyeballed, because a file that does not
    /// load is worse than no command at all: somebody opens it, changes one
    /// line and cannot tell which of the two was wrong.
    /// </remarks>
    [Fact]
    public void The_scaffold_is_a_team_that_loads()
    {
        var findings = new List<RuleFinding>();

        var team = TeamCatalogue.Parse(TeamFiles.Scaffold("mine"), "mine.yaml", findings);

        team.Should().NotBeNull();
        findings.Should().BeEmpty();
        team!.Name.Should().Be("mine");
        team.Lead.Should().Be("lead");
        team.Nodes.Should().ContainKey("lead");
        team.Template.Should().BeFalse();
    }
}
