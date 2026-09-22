using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which runs a prune takes.
/// </summary>
/// <remarks>
/// <para>
/// Tested hardest of anything in this area, because a run's journal is the only
/// copy of what that run did and nothing puts it back. A rule that lives inside
/// a command can only be checked by pointing the command at real runs, which is
/// the one way of checking it nobody wants to be wrong about.
/// </para>
/// <para>
/// The two conditions are an intersection. Every case below that gives both
/// exists to hold that line: read as a union, each option quietly widens the
/// other, and <c>--keep 10 --older-than 30d</c> starts taking things from
/// inside the ten.
/// </para>
/// </remarks>
public sealed class RunRetentionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    /// <param name="days">How long ago it finished.</param>
    /// <param name="running">Whether it is still going: a run with no ending is.</param>
    private static RunSummary Run(
        string id,
        int days,
        bool running = false,
        IReadOnlyList<string>? branches = null,
        IReadOnlyList<string>? merged = null,
        string ended = "done") =>
        new(
            id,
            "C:/runs/" + id,
            "bug-hunt",
            "a goal",
            "supervised",
            Now.AddDays(-days),
            running ? null : Now.AddDays(-days).AddMinutes(4),
            running ? null : ended,
            0.1m,
            1,
            [],
            merged ?? [],
            branches ?? []);

    [Fact]
    public void The_newest_are_kept_whatever_else_is_true()
    {
        var chosen = RunRetention.Choose(
            [Run("a", 1), Run("b", 2), Run("c", 3), Run("d", 4)], keep: 2, olderThan: null, Now);

        chosen.Forgetting.Select(run => run.RunId).Should().Equal("c", "d");
        chosen.Keeping.Select(one => one.Run.RunId).Should().Equal("a", "b");
        chosen.Keeping.Should().OnlyContain(one => one.Because == "one of the newest 2");
    }

    [Fact]
    public void Only_the_endings_asked_for_are_taken()
    {
        var chosen = RunRetention.Choose(
            [
                Run("a", 1, ended: "failed"),
                Run("b", 2, ended: "done"),
                Run("c", 3, ended: "stopped by the person"),
                Run("d", 4, ended: "the lead ended without a report"),
            ],
            keep: 0,
            olderThan: null,
            Now,
            outcomes: [RunOutcome.Failed]);

        chosen.Forgetting.Select(run => run.RunId).Should().Equal("a", "d");
        chosen.Keeping.Select(one => one.Because).Should().Equal("ended done", "ended stopped");
    }

    [Fact]
    public void An_ending_nothing_can_file_is_never_taken_by_one()
    {
        // The whole point of the filter refusing to guess. A sentence a later
        // version writes must not be swept up by --failed on the strength of
        // nobody being able to rule it out.
        var chosen = RunRetention.Choose(
            [Run("a", 9, ended: "something a later version writes")],
            keep: 0,
            olderThan: null,
            Now,
            outcomes: [RunOutcome.Failed]);

        chosen.Forgetting.Should().BeEmpty();
        chosen.Keeping.Single().Because.Should()
            .Be("its ending is not one this knows how to file");
    }

    [Fact]
    public void An_ending_nothing_can_file_is_still_taken_when_no_ending_was_asked_for()
    {
        // The other half: asking by age has never cared how a run ended, and
        // adding the filter must not quietly narrow the commands that do not
        // use it.
        var chosen = RunRetention.Choose(
            [Run("a", 9, ended: "something a later version writes")],
            keep: 0,
            olderThan: null,
            Now);

        chosen.Forgetting.Select(run => run.RunId).Should().Equal("a");
    }

    [Fact]
    public void The_ending_narrows_the_age_rather_than_widening_it()
    {
        // Intersection, like the rest: --failed --older-than 5d is the failed
        // ones older than five days, not the failed ones and the old ones.
        var chosen = RunRetention.Choose(
            [
                Run("old-failure", 9, ended: "failed"),
                Run("new-failure", 1, ended: "failed"),
                Run("old-success", 9, ended: "done"),
            ],
            keep: null,
            olderThan: TimeSpan.FromDays(5),
            Now,
            outcomes: [RunOutcome.Failed]);

        chosen.Forgetting.Select(run => run.RunId).Should().Equal("old-failure");
    }

    [Fact]
    public void A_live_run_is_kept_even_when_its_ending_was_asked_for()
    {
        var chosen = RunRetention.Choose(
            [Run("going", 1, running: true)],
            keep: 0,
            olderThan: null,
            Now,
            outcomes: [RunOutcome.Running, RunOutcome.Failed]);

        chosen.Forgetting.Should().BeEmpty();
        chosen.Keeping.Single().Because.Should().Be("still going");
    }

    [Fact]
    public void Several_endings_can_be_asked_for_at_once()
    {
        var chosen = RunRetention.Choose(
            [
                Run("a", 1, ended: "failed"),
                Run("b", 2, ended: "stopped by request"),
                Run("c", 3, ended: "done"),
            ],
            keep: 0,
            olderThan: null,
            Now,
            outcomes: [RunOutcome.Failed, RunOutcome.Stopped]);

        chosen.Forgetting.Select(run => run.RunId).Should().Equal("a", "b");
    }

    [Fact]
    public void Keeping_none_takes_every_finished_run()
    {
        var chosen = RunRetention.Choose([Run("a", 1), Run("b", 2)], keep: 0, olderThan: null, Now);

        chosen.Forgetting.Should().HaveCount(2);
    }

    [Fact]
    public void A_run_younger_than_the_age_stays()
    {
        var chosen = RunRetention.Choose(
            [Run("old", 40), Run("new", 3)], keep: null, olderThan: TimeSpan.FromDays(30), Now);

        chosen.Forgetting.Select(run => run.RunId).Should().Equal("old");
        chosen.Keeping.Single().Because.Should().Be("newer than 30d");
    }

    [Fact]
    public void Both_together_never_go_below_the_newest_count()
    {
        // Every one of these is older than the age. Read as a union this takes
        // all five; read as the intersection it takes two, which is what
        // somebody typing both of them asked for.
        var chosen = RunRetention.Choose(
            [Run("a", 40), Run("b", 41), Run("c", 42), Run("d", 43), Run("e", 44)],
            keep: 3,
            olderThan: TimeSpan.FromDays(30),
            Now);

        chosen.Forgetting.Select(run => run.RunId).Should().Equal("d", "e");
    }

    [Fact]
    public void A_run_that_is_still_going_is_never_taken()
    {
        // Older than the age, outside the newest count, and still going. The
        // first two say take it and the third has to win: its directory is
        // where its nodes read the answers they are waiting on.
        var chosen = RunRetention.Choose(
            [Run("fresh", 0), Run("live", 90, running: true)],
            keep: 1,
            olderThan: TimeSpan.FromDays(30),
            Now);

        chosen.Forgetting.Should().BeEmpty();
        chosen.Keeping.Should().Contain(one => one.Run.RunId == "live" && one.Because == "still going");
    }

    [Fact]
    public void A_run_that_left_a_branch_nothing_merged_stays_unless_asked_for()
    {
        RunSummary[] runs =
        [
            Run("orphan", 90, branches: ["teams/orphan/implementer-1"], merged: []),
            Run("tidy", 90, branches: ["teams/tidy/implementer-1"], merged: ["teams/tidy/implementer-1"]),
        ];

        var careful = RunRetention.Choose(runs, keep: 0, olderThan: null, Now);

        careful.Forgetting.Select(run => run.RunId).Should().Equal("tidy");
        careful.Keeping.Single().Because.Should().Be("left a branch nothing merged");

        var asked = RunRetention.Choose(runs, keep: 0, olderThan: null, Now, includeUnmerged: true);

        asked.Forgetting.Should().HaveCount(2);
    }

    [Fact]
    public void A_run_that_never_wrote_an_ending_is_dated_from_when_it_began()
    {
        // Finished is null here because the run was killed rather than because
        // it is going: Running is Finished being null, so this is the one shape
        // where the two are indistinguishable from the summary alone. The rule
        // that matters is the one above — it is kept — and this pins that the
        // age is read off its start rather than off nothing.
        var killed = Run("killed", 90, running: true);

        killed.Running.Should().BeTrue();

        RunRetention.Choose([killed], keep: 0, olderThan: TimeSpan.FromDays(30), Now)
            .Forgetting.Should().BeEmpty();
    }

    [Fact]
    public void Unmerged_is_what_a_run_made_and_nothing_took()
    {
        RunRetention.Unmerged(Run(
            "mixed",
            1,
            branches: ["one", "two", "three"],
            merged: ["two"]))
            .Should().Equal("one", "three");
    }

    [Fact]
    public void Nothing_at_all_is_an_empty_pruning_rather_than_a_failure()
    {
        var chosen = RunRetention.Choose([], keep: 0, olderThan: null, Now);

        chosen.Forgetting.Should().BeEmpty();
        chosen.Keeping.Should().BeEmpty();
    }
}
