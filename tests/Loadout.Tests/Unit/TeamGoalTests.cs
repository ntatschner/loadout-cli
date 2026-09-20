using FluentAssertions;
using Loadout.Agents.Teams;
using Loadout.Models.Instructions;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a team says about itself, standing, and where that reaches.
/// </summary>
/// <remarks>
/// A run's goal is the thing somebody typed when they started it, true of that
/// run and no other. Everything standing about a team — what it exists for, and
/// the rules it always works to — had nowhere to live, so it was either
/// repeated in every run's goal or left to whatever the roles happened to say.
/// </remarks>
public sealed class TeamGoalTests
{
    private static Brief Briefed(
        string? goal = null,
        IReadOnlyList<string>? declarations = null,
        string? directory = null) =>
        new(
            "20260920-1200-abcd",
            "investigator",
            "lead",
            "role.investigator",
            "find why the disk filled",
            DeliverableKind.Answer,
            [],
            new BriefConstraints("investigate", null, 40, null, []),
            ["the cause is proven"],
            Goal: goal,
            Declarations: declarations,
            TeamDirectory: directory);

    [Fact]
    public void A_node_is_told_what_the_team_is_for_before_it_is_told_its_task()
    {
        // "Find why the disk filled" is a different job inside a team that
        // exists to keep a system up from inside one that exists to write a
        // report about it. A node reading its task without that is reading
        // half of it.
        var read = TeamRunner.Render(Briefed(goal: "Keep this system working."));

        read.Should().Contain("## What this team is for")
            .And.Contain("Keep this system working.");

        read.IndexOf("What this team is for", StringComparison.Ordinal)
            .Should().BeLessThan(read.IndexOf("## Task", StringComparison.Ordinal),
                "a standing goal frames the task, so it is read first");
    }

    [Fact]
    public void A_declaration_holds_for_this_run_and_says_so()
    {
        var read = TeamRunner.Render(Briefed(declarations:
        [
            "Register anything you write in the team's directory.",
            "Look there before working a fix out again.",
        ]));

        read.Should().Contain("## How this team works")
            .And.Contain("Register anything you write in the team's directory.")
            .And.Contain("Look there before working a fix out again.");

        // Said out loud, because a list of rules under a heading reads as
        // background unless something says it applies now.
        read.Should().Contain("every run of this team, including this one");
    }

    [Fact]
    public void A_team_with_nothing_standing_to_say_says_nothing()
    {
        // Most teams have a description and need no second one. An empty
        // heading is worse than no heading: it reads as something missing.
        var read = TeamRunner.Render(Briefed());

        read.Should().NotContain("## What this team is for")
            .And.NotContain("## How this team works")
            .And.NotContain("## The team's directory");
    }

    [Fact]
    public void The_team_directory_is_named_so_that_it_means_one_place()
    {
        // The whole point. Left to each node, "the team's directory" is a
        // phrase that resolves differently every time and the work is lost
        // between runs.
        var read = TeamRunner.Render(Briefed(directory: @"C:\state\teams\work\system-watch"));

        read.Should().Contain(@"C:\state\teams\work\system-watch")
            .And.Contain("every node of every run")
            .And.Contain("not in the repository",
                "writing there must not read as a change to whatever is being worked on");
    }

    [Fact]
    public void What_a_team_file_says_standing_reaches_every_node_it_briefs()
    {
        // The gap a surviving mutation found: every test here rendered a brief
        // that had been handed a goal by hand, so nothing checked that a team
        // file's goal reached one at all. Deleting the line that copies it left
        // all five passing.
        var team = new TeamDefinition
        {
            Name = "system-watch",
            Lead = "lead",
            Goal = "Keep this system working.",
            Declarations = ["Register what you write in the team's directory."],
            Nodes = { ["investigator"] = new TeamNode { Role = "role.investigator" } },
        };

        var role = new SpecialistDocument(
            "role.investigator",
            SpecialistKind.Role,
            "Investigator",
            "Proves a cause.",
            new SpecialistActivation(),
            string.Empty,
            0,
            Role: new RoleDefinition("investigate", "answer", "report/1", [], []));

        var brief = TeamRunner.MakeBrief(
            "20260920-1200-abcd",
            "investigator",
            "lead",
            team.Nodes["investigator"],
            role,
            "find why the disk filled",
            inputs: [],
            doneWhen: [],
            team,
            "supervised",
            allowed: [],
            specialistsOf: _ => null,
            teamDirectory: @"C:\state	eams\work\system-watch");

        brief.Goal.Should().Be("Keep this system working.");
        brief.Declarations.Should().ContainSingle()
            .Which.Should().Be("Register what you write in the team's directory.");
        brief.TeamDirectory.Should().Be(@"C:\state	eams\work\system-watch");
    }

    [Fact]
    public void The_team_that_ships_with_this_carries_both()
    {
        // The example is the documentation. A feature whose only demonstration
        // is in a document is one nobody finds.
        // Read out of the assembly rather than off the disk, because that is
        // what ships: a file left behind in the source tree and not embedded
        // would pass a test that looked at the tree and reach nobody.
        using var stream = typeof(Loadout.Core.Teams.TeamCatalogue).Assembly
            .GetManifestResourceStream("Loadout.Core.Teams.Catalogue.system-watch.yaml");

        stream.Should().NotBeNull("the team ships inside the binary");

        using var reader = new StreamReader(stream!);

        var found = new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build()
            .Deserialize<TeamDefinition>(reader);

        found.Should().NotBeNull();
        found!.Goal.Should().NotBeEmpty();
        found.Declarations.Should().NotBeEmpty();

        // The one the feature was asked for: a fix becomes a thing the team
        // keeps, rather than steps in a transcript nobody reads again.
        found.Declarations.Should().Contain(one =>
            one.Contains("script or a function", StringComparison.Ordinal));

        found.Declarations.Should().Contain(one =>
            one.Contains("team's directory", StringComparison.Ordinal));
    }
}
