using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The keyword rule that tags a criterion the lead left untagged, and the rule
/// that decides when a run hands over rather than takes another round.
/// </summary>
public sealed class CriterionTagsTests
{
    [Theory]
    [InlineData("a contract test passes on Windows and Unix CI", CriterionNeeds.Ci)]
    [InlineData("The Unix CI legs are green (CI).", CriterionNeeds.Ci)]
    [InlineData("continuous integration passes", CriterionNeeds.Ci)]
    [InlineData("the release workflow passes on GitHub Actions", CriterionNeeds.Ci)]
    [InlineData("End run is checked by hand in Studio", CriterionNeeds.Person)]
    [InlineData("a hand check of the installer shows no console", CriterionNeeds.Person)]
    [InlineData("the dialog was tested manually", CriterionNeeds.Person)]
    [InlineData("checked by hand once CI is green", CriterionNeeds.Person)]
    [InlineData("the branch is pushed", CriterionNeeds.Push)]
    [InlineData("a draft PR is open for review", CriterionNeeds.Push)]
    [InlineData("a pull request is open against main", CriterionNeeds.Push)]
    [InlineData("push the branch to origin", CriterionNeeds.Push)]
    public void A_criterion_s_wording_says_what_it_needs(string criterion, CriterionNeeds needs)
    {
        CriterionTags.Guess(criterion).Should().Be(needs);
    }

    [Theory]
    [InlineData("the suite passes")]
    [InlineData("Studio sends a push notification when a run ends")]
    [InlineData("`pr explain` names the right and the level it came from")]
    [InlineData("the decision is recorded in the journal")]
    [InlineData("handover lists each criterion")]
    [InlineData("a specific person is named")]
    public void Wording_that_only_looks_like_it_is_left_local(string criterion)
    {
        CriterionTags.Guess(criterion).Should().BeNull();
        CriterionTags.Of(criterion, null).Should().Be(new CriterionTag(CriterionNeeds.Local, "default"));
    }

    [Fact]
    public void The_lead_s_tag_wins_over_the_wording()
    {
        CriterionTags.Of("CI passes", CriterionNeeds.Local).Should().Be(new CriterionTag(CriterionNeeds.Local, "lead"));
        CriterionTags.Of("the suite passes", CriterionNeeds.Person, "  look at it  ")
            .Should().Be(new CriterionTag(CriterionNeeds.Person, "lead", "look at it"));
    }

    [Fact]
    public void Every_tag_reads_back_from_its_spelling()
    {
        foreach (var needs in Enum.GetValues<CriterionNeeds>())
        {
            CriterionTags.TryParse(CriterionTags.Spell(needs), out var read).Should().BeTrue();
            read.Should().Be(needs);
        }

        CriterionTags.TryParse("sometime", out _).Should().BeFalse();
    }

    private static Dictionary<string, CriterionTag> Tags(params (string Criterion, CriterionNeeds Needs)[] tagged) =>
        tagged.ToDictionary(one => one.Criterion, one => new CriterionTag(one.Needs, "lead"), StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Nothing_outstanding_is_done_not_a_handover()
    {
        RunHandovers.Decide([], Tags(("a", CriterionNeeds.Ci))).Should().BeNull();
    }

    [Fact]
    public void One_outstanding_criterion_the_run_can_meet_keeps_it_working()
    {
        RunHandovers.Decide(["a", "b"], Tags(("a", CriterionNeeds.Ci), ("b", CriterionNeeds.Local))).Should().BeNull();
    }

    [Fact]
    public void Only_criteria_the_run_cannot_meet_left_is_a_handover_in_the_run_s_order()
    {
        var handed = RunHandovers.Decide(
            ["b", "a"],
            Tags(("a", CriterionNeeds.Person), ("b", CriterionNeeds.Push)));

        handed.Should().NotBeNull();
        handed!.Select(one => one.Criterion).Should().Equal("b", "a");
        handed[0].ToDo.Should().Be("Push the run's work and open a pull request.");
        handed[1].ToDo.Should().Be("Check it by hand.");

        // Not in the tags at all: an untagged one is read by its wording, and
        // "c" says nothing, so it is the run's and this is no handover.
        RunHandovers.Decide(["c"], Tags()).Should().BeNull();
        RunHandovers.Decide(["CI is green"], Tags()).Should().ContainSingle().Which.By.Should().Be("keyword");
    }

    [Theory]
    [InlineData("needs-decision: handed over 2 criterion(s) the run cannot meet itself", true)]
    [InlineData("needs-decision", false)]
    [InlineData("blocked", false)]
    [InlineData(null, false)]
    public void A_handover_is_known_by_its_ending(string? ended, bool handover)
    {
        RunHandovers.IsHandover(ended).Should().Be(handover);

        if (handover)
        {
            RunOutcomes.From(ended).Should().Be(RunOutcome.NeedsDecision);
        }
    }

    private static RunSummary Handed(string ended = "needs-decision: handed over 2 criterion(s) the run cannot meet itself") =>
        new("20261026-1000-abcd", "dir", "team", "goal", "supervised", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, ended, 1m, 2, [], [], [])
        {
            Handover = new RunHandover(
            [
                new HandoverItem("End run works in Studio", CriterionNeeds.Person, "Check it by hand.", "lead"),
                new HandoverItem("CI is green", CriterionNeeds.Ci, "Push it.", "keyword") { CheckedBy = "sam" },
            ], ["teams-x-implementer-1"]),
        };

    [Theory]
    [InlineData("1", "End run works in Studio")]
    [InlineData("end run works in studio", "End run works in Studio")]
    public void A_check_is_picked_by_number_or_by_its_words(string picked, string criterion)
    {
        RunHandoverChecks.Pick(Handed(), [picked]).Value.Should().ContainSingle().Which.Criterion.Should().Be(criterion);
    }

    [Theory]
    [InlineData("3", "is not one it handed over")]
    [InlineData("the suite passes", "is not one it handed over")]
    [InlineData("2", "already checked by sam")]
    public void A_check_of_something_not_open_is_refused(string picked, string why)
    {
        var pick = RunHandoverChecks.Pick(Handed(), [picked]);

        pick.Failed.Should().BeTrue();
        pick.Error.Should().Contain(why);
    }

    [Fact]
    public void A_run_that_handed_nothing_over_has_nothing_to_check()
    {
        RunHandoverChecks.Pick(Handed("blocked"), ["1"]).Error.Should().Contain("did not hand anything over");
        RunHandovers.Settled(Handed()).Should().BeNull("one is still open");
    }
}
