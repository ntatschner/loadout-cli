using FluentAssertions;
using Loadout.Agents.Teams;
using Loadout.Core.Capabilities;
using Loadout.Core.Teams;
using Loadout.Models.Capabilities;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>A capability that goes while a run is going is described in its report, and nothing more.</summary>
public sealed partial class TeamRunnerTests
{
    /// <summary>A repository whose CI is there at the first look and gone at the second.</summary>
    private sealed class CiGoes : ICapabilityProfiles
    {
        public int Looks { get; private set; }

        public Task<CapabilityProfile?> CachedAsync(string slug, CancellationToken ct = default) => Task.FromResult<CapabilityProfile?>(null);

        public Task<CapabilityProfile> BuildAsync(ProjectResolution project, bool askGitHub, CancellationToken ct = default) =>
            Task.FromResult(new CapabilityProfile
            {
                Project = project.Entry.Slug,
                Checks =
                [
                    Looks++ == 0
                        ? new CapabilityResult { Id = CapabilityChecks.Ci, State = CapabilityState.Present, Evidence = "ci.yml runs." }
                        : new CapabilityResult { Id = CapabilityChecks.Ci, State = CapabilityState.Gap, Evidence = "No workflow runs." },
                ],
            });

        public Task<OperationResult> KeepAsync(CapabilityProfile profile, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
    }

    [Fact]
    public async Task A_capability_gone_by_the_end_of_a_run_is_noted_in_its_report_and_its_timeline_and_the_run_still_finishes()
    {
        using var space = new TemporaryWorkspace();
        var team = await IteratingProjectAsync();
        var profiles = new CiGoes();
        var offers = new CapabilityOffers(
            profiles, new FakeProjects("demo", space.Scratch("repository")), space.Tasks, new RunJournal(_paths), space.Workspace, space.Yaml, TimeProvider.System);

        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadDone(), 0.50m), Result(LeadDone(), 0.60m));

        var outcome = await new TeamRunner(_launcher, _paths, TimeProvider.System, offers: offers).RunAsync(
            new TeamRunRequest("demo", team, await SpecialistsAsync(), "Add --since to loadout usage.", "supervised", MaxRounds: 10, Offline: true),
            _console);

        outcome.Succeeded.Should().BeTrue(outcome.Error);
        profiles.Looks.Should().Be(2, "once as it starts and once as it ends");
        outcome.Value!.Ended.Should().Be("done", "a note never stops a run");
        outcome.Value.Warnings.Should().ContainSingle(line => line.Contains("ci stopped being there") && line.Contains("No workflow runs."));

        var entries = new RunJournal(_paths).Read(outcome.Value.RunId).Value!;

        entries.Should().ContainSingle(entry => entry.Kind == "run.note");
        RunJournal.Wording(entries.Single(entry => entry.Kind == "run.note")).Should().Contain("ci stopped being there");
    }
}
