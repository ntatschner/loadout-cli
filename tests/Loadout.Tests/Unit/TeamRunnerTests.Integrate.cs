using FluentAssertions;
using Loadout.Agents.Teams;
using Loadout.Agents.Teams.Steps;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// A run that hands over with work on more than one node's branch: combined
/// into one branch in a tree of its own, and named as that one branch.
/// </summary>
public sealed partial class TeamRunnerTests
{
    private const string One = "teams-RUN-implementer-1";
    private const string Two = "teams-RUN-implementer-2";

    /// <summary>The stuck run's shape, with the work split over two implementers.</summary>
    private async Task<(TeamRunOutcome Outcome, FakeGit Git)> HandsOverFromTwoAsync(Action<FakeGit>? arrange = null)
    {
        var git = new FakeGit(Path.Combine(_root, "repo"));
        arrange?.Invoke(git);

        _launcher.Script("role.project-lead", Init("lead-1"),
            Result(LeadRequests(
                AskImplementer() with { Node = "implementer/1" },
                AskImplementer() with { Node = "implementer/2" }) with { Readings = TaggedReadings(Stuck) }, 0.50m),
            Result(LeadStuck(Stuck, OwnerLock, CloseRefuses, OldRuns), 1.00m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone() with { Node = "implementer/1" }, 0.20m));
        _launcher.Script("role.implementer", Init("impl-2"), Result(ImplementerDone() with { Node = "implementer/2" }, 0.20m));

        var outcome = (await RunAsync(criteria: Stuck, git: git)).Value!;

        outcome.Ended.Should().StartWith(RunHandovers.EndedOpening);

        return (outcome, git);
    }

    private static string Named(string branch, string runId) => branch.Replace("RUN", runId, StringComparison.Ordinal);

    [Fact]
    public async Task A_handover_from_two_branches_names_the_one_branch_they_were_combined_into()
    {
        var (outcome, git) = await HandsOverFromTwoAsync();
        var run = outcome.RunId;
        var combined = IntegrateStep.BranchFor(run);

        git.Detached.Should().ContainSingle().Which.Should().EndWith($"@{Named(One, run)}", "it starts at the first node's work");

        var tree = git.Detached[0].Split('@')[0];

        tree.Should().NotBe(git.State.Root, "the repository somebody works in is not where they are combined");
        git.Merges.Should().Equal(Named(Two, run));
        git.MergedIn.Should().Equal(tree);
        git.Created.Should().Equal($"{tree}:{combined}");
        git.Removed.Should().Contain(tree);

        new RunJournal(_paths).Summarise(run).Value!.Handover!.Branches.Should().Equal(combined);

        (await JournalAsync(outcome)).Should().ContainSingle(line => line.Contains("\"kind\":\"run.integrated\""))
            .Which.Should().Contain(Named(One, run)).And.Contain(Named(Two, run));
    }

    [Fact]
    public async Task Branches_that_conflict_are_handed_over_as_they_are_and_no_branch_is_made()
    {
        var (outcome, git) = await HandsOverFromTwoAsync(
            git => git.NextMerge(new Loadout.Core.Git.GitMerge(Merged: false, FastForward: false, ["src/Run.cs"])));

        var run = outcome.RunId;

        git.Created.Should().BeEmpty("a branch is made only once everything merged");
        git.Removed.Should().Contain(git.Detached[0].Split('@')[0]);

        new RunJournal(_paths).Summarise(run).Value!.Handover!.Branches
            .Should().BeEquivalentTo([Named(One, run), Named(Two, run)]);

        outcome.Warnings.Should().Contain(one => one.Contains("conflicts with the others in src/Run.cs"));
    }

    [Fact]
    public async Task A_handover_from_one_branch_combines_nothing()
    {
        var git = new FakeGit(Path.Combine(_root, "repo"));

        _launcher.Script("role.project-lead", Init("lead-1"),
            Result(LeadPlans(Stuck), 0.50m),
            Result(LeadStuck(Stuck, OwnerLock, CloseRefuses, OldRuns), 1.00m));

        _launcher.Script("role.implementer", Init("impl-1"), Result(ImplementerDone(), 0.20m));

        var outcome = (await RunAsync(criteria: Stuck, git: git)).Value!;

        outcome.Ended.Should().StartWith(RunHandovers.EndedOpening);
        git.Detached.Should().BeEmpty();
        new RunJournal(_paths).Summarise(outcome.RunId).Value!.Handover!.Branches
            .Should().Equal(Named("teams-RUN-implementer", outcome.RunId));
    }

    [Theory]
    [InlineData("same", false)]
    [InlineData("other", true)]
    public async Task A_name_taken_by_an_earlier_handover_is_reused_only_for_the_same_commit(string earlier, bool numbered)
    {
        // Picked up and handed over again. The branch from the first time may
        // have been pushed with a pull request open on it, so it is never moved.
        var (outcome, git) = await HandsOverFromTwoAsync(git =>
        {
            git.Commits["HEAD"] = "same";

            _launcher.BeforeStart = directory => git.Commits[IntegrateStep.BranchFor(Path.GetFileName(directory))] = earlier;
        });

        var combined = IntegrateStep.BranchFor(outcome.RunId) + (numbered ? "-2" : string.Empty);

        if (numbered)
        {
            git.Created.Should().ContainSingle().Which.Should().EndWith($":{combined}");
        }
        else
        {
            git.Created.Should().BeEmpty("the branch is already at that commit");
        }
        new RunJournal(_paths).Summarise(outcome.RunId).Value!.Handover!.Branches.Should().Equal(combined);
    }
}
