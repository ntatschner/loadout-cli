using System.Text.Json;

using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Git;
using Loadout.Core.Teams;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Where a finished run's work went, and whether it has left this machine.
/// </summary>
/// <remarks>
/// A run that merged showed each branch as "merged" and nothing else. Into
/// what, whether it had been pushed and what to do next were all left for the
/// person to work out from git, and the one line naming the target was in the
/// decisions at the bottom of several hundred.
/// </remarks>
public sealed class RunLandingTests
{
    private const string Repository = "D:/repo";
    private const string Commit = "0f900893aa11bb22cc33dd44ee55ff6677889900";

    private static RunEvent Event(string kind, string data) =>
        new(DateTimeOffset.Parse("2026-10-21T18:07:00+01:00", System.Globalization.CultureInfo.InvariantCulture),
            "implementer/1", kind, JsonDocument.Parse(data).RootElement);

    private static RunSummary Run(string? path = Repository) =>
        new("20261021-1619-9196", "d", "iterating-project", "goal", "autonomous",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "done", 0m, 1, [], [], [], Path: path);

    private static RunEvent Merged(string branch, string? commit = Commit) =>
        Event("merge.done", commit is null
            ? $$"""{"branch":"{{branch}}","target":"main","FastForward":false}"""
            : $$"""{"branch":"{{branch}}","target":"main","FastForward":false,"commit":"{{commit}}"}""");

    [Fact]
    public async Task A_run_that_merged_nothing_has_landed_nowhere()
    {
        var landing = await RunLanding.OfAsync(
            Run(), [Event("gate.refused", """{"gate":["reviewer"]}""")], new FakeGit(Repository));

        landing.Should().BeNull();
    }

    [Fact]
    public async Task It_names_the_target_every_branch_and_the_last_merge_commit()
    {
        var git = new FakeGit(Repository);
        git.Pushes[$"main@{Commit}"] = new GitPushStanding("origin/main", Pushed: false);

        var landing = await RunLanding.OfAsync(
            Run(), [Merged("teams-r-implementer-1", "1111111"), Merged("teams-r-implementer-2")], git);

        landing!.Target.Should().Be("main");
        landing.Branches.Should().Equal("teams-r-implementer-1", "teams-r-implementer-2");
        landing.Commit.Should().Be(Commit, "the last merge is what the target was left at");
        landing.Repository.Should().Be(Repository);
        landing.Standing.Should().Be(new GitPushStanding("origin/main", Pushed: false));
    }

    [Fact]
    public async Task An_older_journal_with_no_commit_is_not_asked_about()
    {
        var landing = await RunLanding.OfAsync(Run(), [Merged("teams-r-implementer-1", commit: null)], new FakeGit(Repository));

        landing!.Standing.Should().BeNull("there is nothing to look for upstream");
    }

    [Fact]
    public void Merged_and_pushed_says_where_and_asks_nothing_more()
    {
        var lines = TeamStatusCommand.Landed(new Landing(
            "main", ["a", "b", "c"], Commit, Repository, new GitPushStanding("origin/main", Pushed: true)));

        lines[0].Should().Contain("merged 3 branches into main").And.Contain(Repository);
        lines.Should().Contain(line => line.Contains("pushed") && line.Contains("0f90089 is on origin/main"));
        lines.Should().NotContain(line => line.Contains("next:"));
    }

    [Fact]
    public void Merged_and_not_pushed_says_so_and_what_to_do_next()
    {
        var lines = TeamStatusCommand.Landed(new Landing(
            "main", ["a"], Commit, Repository, new GitPushStanding("origin/main", Pushed: false)));

        lines[0].Should().Contain("merged 1 branch into main");
        lines.Should().Contain(line => line.Contains("not pushed") && line.Contains("0f90089 is not on origin/main"));
        lines.Should().Contain(line => line.Contains("next: push main"));
    }

    [Fact]
    public void A_branch_with_no_upstream_is_not_pushed_anywhere()
    {
        var lines = TeamStatusCommand.Landed(new Landing(
            "main", ["a"], Commit, Repository, new GitPushStanding(null, Pushed: false)));

        lines.Should().Contain(line => line.Contains("not pushed") && line.Contains("main has no upstream"));
        lines.Should().Contain(line => line.Contains("next: push main"));
    }

    [Fact]
    public void Without_a_standing_it_still_says_a_run_never_pushes()
    {
        var lines = TeamStatusCommand.Landed(new Landing("main", ["a"], null, Repository, null));

        lines.Should().Contain(line => line.Contains("a run never pushes"));
    }
}
