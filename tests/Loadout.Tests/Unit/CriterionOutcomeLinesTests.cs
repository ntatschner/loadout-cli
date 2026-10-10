using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The lines <c>team run</c> ends with and <c>team status</c> shows: one block
/// per criterion, with its reading, its verdict and what was delivered.
/// </summary>
public sealed class CriterionOutcomeLinesTests
{
    private static string Plain(IEnumerable<string> lines) =>
        Spectre.Console.Markup.Remove(string.Join("\n", lines));

    [Fact]
    public void Each_criterion_says_how_it_was_read_what_shows_it_and_what_was_delivered()
    {
        var text = Plain(TeamStyle.Outcomes(
        [
            new CriterionOutcome(
                "the suite passes", "the whole suite on a fresh clone", "met", "verifier/1: 4071 passed", "project",
                [new Delivered("implementer/1", "commit", "a4f21c9", null)],
                ["made-up-ref"]),
            new CriterionOutcome("docs say so", "the guide mentions --since", null, null, null, [], []),
        ]));

        text.Should().Contain("Done when 1 of 2 met");
        text.Should().Contain("the suite passes  (project)");
        text.Should().Contain("taken to mean: the whole suite on a fresh clone");
        text.Should().Contain("verifier/1: 4071 passed");
        text.Should().Contain("delivered: commit a4f21c9 by implementer/1");
        text.Should().Contain("cited but no worker reported it: made-up-ref");

        // Read and not yet judged, said as such rather than as unmet.
        text.Should().Contain("no verdict").And.Contain("taken to mean: the guide mentions --since");
    }

    [Fact]
    public void A_run_with_readings_and_no_verdicts_says_so()
    {
        Plain(TeamStyle.Outcomes([new CriterionOutcome("docs say so", "the guide", null, null, null, [], [])]))
            .Should().Contain("Done when no verdicts yet");
    }

    [Fact]
    public void A_run_with_no_criteria_prints_nothing()
    {
        TeamStyle.Outcomes([]).Should().BeEmpty();
    }
}
