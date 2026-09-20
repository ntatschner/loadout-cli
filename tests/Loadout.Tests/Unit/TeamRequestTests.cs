using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Instructions;
using Loadout.Core.Teams;
using Loadout.Models.Configuration;
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
            "demo", Team(), Specialists(), Settings(), "autonomous", ceiling, new MachineConfig());

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
            "demo", Team(), Specialists(), Settings(), "autonomous", TeamCeiling.Nothing, machine);

        request.TrustedRemedies.Should().ContainSingle()
            .Which.Remedy.Should().Be("remove-artifact-txt-files");

        request.Remediation.Should().ContainKey("cleanup").WhoseValue.Should().Be("trusted");
    }

    [Fact]
    public void A_machine_that_has_said_nothing_grants_nothing()
    {
        // The safe direction, and the reason the two above were invisible.
        var request = TeamRunCommand.Requesting(
            "demo", Team(), Specialists(), Settings(), "supervised", TeamCeiling.Nothing, machine: null);

        request.OutwardAllowed.Should().BeEmpty();
        request.TrustedRemedies.Should().BeNull();
        request.Remediation.Should().BeNull();
    }
}
