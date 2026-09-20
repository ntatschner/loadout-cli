using FluentAssertions;
using Loadout.Agents.Teams;
using Loadout.Core.Instructions;
using Loadout.Core.Teams;
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
    public void An_empty_declaration_is_a_finding_rather_than_a_bare_dash()
    {
        // It would go into every brief of every run as "- " with nothing after
        // it, which nothing else on the page would ever show anybody.
        var team = new TeamDefinition
        {
            Name = "system-watch",
            Lead = "lead",
            Declarations = ["Look in the directory first.", "   "],
            Nodes = { ["lead"] = new TeamNode { Role = "role.project-lead" } },
        };

        var found = TeamCatalogue.Check(team, Specialists());

        found.Should().Contain(one => one.Kind == "team-declaration")
            .Which.Detail.Should().Contain("position 2");
    }

    [Fact]
    public void A_goal_nobody_could_read_twice_is_a_finding()
    {
        // Every node reads it, every round. A team that put an essay here would
        // pay for it in every brief of every run, and the first anybody would
        // know is the bill.
        var team = new TeamDefinition
        {
            Name = "system-watch",
            Lead = "lead",
            Goal = new string('a', 900),
            Nodes = { ["lead"] = new TeamNode { Role = "role.project-lead" } },
        };

        TeamCatalogue.Check(team, Specialists())
            .Should().Contain(one => one.Kind == "team-goal");
    }

    [Fact]
    public void A_team_that_says_nothing_standing_has_nothing_to_complain_about()
    {
        var team = new TeamDefinition
        {
            Name = "quiet",
            Lead = "lead",
            Nodes = { ["lead"] = new TeamNode { Role = "role.project-lead" } },
        };

        TeamCatalogue.Check(team, Specialists())
            .Should().NotContain(one => one.Kind == "team-goal" || one.Kind == "team-declaration");
    }

    private static SpecialistCatalogue Specialists() =>
        new SpecialistLibrary().LoadAsync(workspaceRoot: null).GetAwaiter().GetResult();

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
