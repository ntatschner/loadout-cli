using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Instructions;
using Loadout.Core.Teams;
using Loadout.Agents.Teams;
using Loadout.Models.Configuration;
using Loadout.Models.Projects;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// That what this machine decided reaches the run that needs it.
/// </summary>
/// <remarks>
/// Three decisions are taken in the command and nowhere else: what a team may
/// do outwardly, what a remediator may do with each kind of task, and which
/// remedies have been agreed to. Two of them were computed and then dropped -
/// the ceiling was worked out, checked for being short, and never passed on;
/// and <c>team remedy trust</c> wrote to a file no run ever read.
///
/// Nothing caught it, because a missing optional argument is not a compile
/// error and the runner's safe direction when told nothing - grant nothing -
/// is indistinguishable from a machine that allows nothing. So it looked like
/// a working feature that nobody had configured.
/// </remarks>
public sealed class TeamRequestTests
{
    private static TeamRunCommand.Settings Settings() =>
        new() { Goal = "Fix the thing", Rounds = 3 };

    private static TeamDefinition Team() => new() { Name = "system-watch" };

    private static SpecialistCatalogue Specialists() => SpecialistCatalogue.Empty;

    [Fact]
    public void What_this_machine_let_the_team_keep_is_what_the_run_is_given()
    {
        // The ceiling is decided against the machine's own list and then has
        // to travel. A run that is told nothing allows nothing, so dropping it
        // reads as a machine that permits nothing rather than as a bug.
        // Everything the team asked for is allowed here, so the run goes
        // ahead: a ceiling with anything refused is failed up front by the
        // command and never gets this far.
        var ceiling = TeamCeiling.Decide(["git push"], ["git push", "gh pr create"]);

        ceiling.IsShort.Should().BeFalse();

        var request = TeamRunCommand.Requesting(
            "demo", Team(), Specialists(), Settings(), "autonomous", ceiling, new MachineConfig(), projectTeams: null);

        request.OutwardAllowed.Should().BeEquivalentTo(["git push"]);
    }

    [Fact]
    public void Remedies_this_machine_has_agreed_to_reach_the_run()
    {
        // Otherwise `team remedy trust` writes to a file nothing reads, and
        // every trusted remedy is still held for a person - which looks like
        // the gate working rather than trust never arriving.
        var machine = new MachineConfig();

        machine.Teams.TrustedRemedies.Add(new TrustedRemedy
        {
            Team = "system-watch",
            Remedy = "remove-artifact-txt-files",
            Fingerprint = "abc123",
        });

        machine.Teams.Remediation["cleanup"] = "trusted";

        var request = TeamRunCommand.Requesting(
            "demo", Team(), Specialists(), Settings(), "autonomous", TeamCeiling.Nothing, machine, projectTeams: null);

        request.TrustedRemedies.Should().ContainSingle()
            .Which.Remedy.Should().Be("remove-artifact-txt-files");

        request.Remediation.Should().ContainKey("cleanup").WhoseValue.Should().Be("trusted");
    }

    [Fact]
    public void A_machine_that_has_said_nothing_grants_nothing()
    {
        // The safe direction, and the reason the two above were invisible.
        var request = TeamRunCommand.Requesting(
            "demo", Team(), Specialists(), Settings(), "supervised", TeamCeiling.Nothing, machine: null, projectTeams: null);

        request.OutwardAllowed.Should().BeEmpty();
        request.TrustedRemedies.Should().BeNull();
        request.Remediation.Should().BeNull();
    }

    private static ProjectTeams ProjectWith(params (string Text, string[] Teams)[] entries)
    {
        var teams = new ProjectTeams();

        foreach (var (text, names) in entries)
        {
            teams.DoneWhen.Add(new ProjectDoneWhen { Text = text, Teams = [.. names] });
        }

        return teams;
    }

    [Fact]
    public void The_projects_standing_criteria_for_this_team_reach_the_run()
    {
        // Resolved here for every way a run starts, the dashboard and the
        // schedules included, because they all come through this command.
        var project = ProjectWith(
            ("the suite passes", ["all"]),
            ("docs are in the house voice", ["docs-crew"]),
            ("nothing new is logged at warning", []),
            ("the watch is quiet", ["System-Watch"]));

        var request = TeamRunCommand.Requesting(
            "demo", Team(), Specialists(), Settings(), "supervised", TeamCeiling.Nothing, null, project);

        request.Standing!.Applied.Should().Equal(
            "the suite passes", "nothing new is logged at warning", "the watch is quiet");
        request.Standing.Dropped.Should().BeEmpty();
        request.Standing.DroppedBy.Should().BeNull();
    }

    [Fact]
    public void A_criterion_unticked_for_this_run_is_left_out_and_said_to_be()
    {
        var project = ProjectWith(("the suite passes", ["all"]), ("the watch is quiet", ["all"]));

        var settings = new TeamRunCommand.Settings
        {
            Goal = "Fix the thing",
            Rounds = 3,
            DropProjectDoneWhen = ["  The Suite Passes "],
        };

        var request = TeamRunCommand.Requesting(
            "demo", Team(), Specialists(), settings, "supervised", TeamCeiling.Nothing, null, project);

        request.Standing!.Applied.Should().Equal("the watch is quiet");
        request.Standing.Dropped.Should().Equal("the suite passes");
        request.Standing.DroppedBy.Should().Be("person");
    }

    [Fact]
    public void The_flag_leaves_all_of_them_out()
    {
        var project = ProjectWith(("the suite passes", ["all"]), ("the watch is quiet", []));

        var settings = new TeamRunCommand.Settings { Goal = "Fix the thing", Rounds = 3, NoProjectDoneWhen = true };

        var request = TeamRunCommand.Requesting(
            "demo", Team(), Specialists(), settings, "supervised", TeamCeiling.Nothing, null, project);

        request.Standing!.Applied.Should().BeEmpty();
        request.Standing.Dropped.Should().Equal("the suite passes", "the watch is quiet");
        request.Standing.DroppedBy.Should().Be("flag");
    }

    [Fact]
    public void A_project_that_sets_none_holds_the_run_to_none()
    {
        var request = TeamRunCommand.Requesting(
            "demo", Team(), Specialists(), Settings(), "supervised", TeamCeiling.Nothing, null, new ProjectTeams());

        request.Standing.Should().Be(StandingCriteria.None);
    }
}
