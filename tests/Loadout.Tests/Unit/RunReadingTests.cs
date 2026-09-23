using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a run's record says about how its goal and criteria were read, what
/// became of them, and who settled its questions.
/// </summary>
public sealed class RunReadingTests
{
    private static RunEvent Event(string kind, object data, string? node = null) =>
        new(DateTimeOffset.UtcNow, node, kind, JsonSerializer.SerializeToElement(data));

    private static RunSummary Fold(params RunEvent[] events) => RunJournal.Fold("r", "d", events);

    [Fact]
    public void The_lead_s_reading_of_the_goal_and_each_criterion_is_kept_with_its_verdict()
    {
        var summary = Fold(
            Event("run.started", new { team = "t", goal = "make the tests pass", autonomy = "supervised" }),
            Event("report.checked", new
            {
                status = "done",
                outcome = "accepted",
                goalUnderstood = "the whole suite green, not only the new test",
                coverage = new[]
                {
                    new { criterion = "the tests pass", verdict = "met", because = "suite: 1612 passed", understood = "every test, not only the new one" },
                },
            }, "lead"));

        summary.GoalUnderstood.Should().Be("the whole suite green, not only the new test");
        summary.Coverage.Should().ContainSingle().Which.Understood.Should().Be("every test, not only the new one");
    }

    /// <summary>
    /// A run written before readings were asked for still reads, with none.
    /// </summary>
    [Fact]
    public void A_run_from_before_readings_has_none_and_still_reads()
    {
        var summary = Fold(
            Event("run.started", new { team = "t", goal = "g", autonomy = "supervised" }),
            Event("report.checked", new
            {
                status = "done",
                coverage = new[] { new { criterion = "c", verdict = "met", because = "b" } },
            }, "lead"));

        summary.GoalUnderstood.Should().BeNull();
        summary.Coverage.Should().ContainSingle().Which.Understood.Should().BeNull();
    }

    /// <summary>
    /// Every run so far stored "notattempted", and a page that printed it
    /// showed a reader NOTATTEMPTED.
    /// </summary>
    [Theory]
    [InlineData("notattempted", "not attempted")]
    [InlineData("not-attempted", "not attempted")]
    [InlineData("met", "met")]
    [InlineData("unmet", "unmet")]
    public void A_verdict_is_put_into_words(string stored, string said)
    {
        new RunCovered("c", stored, null).InWords.Should().Be(said);
    }

    [Fact]
    public void A_recommendation_taken_for_nobody_says_so_and_after_how_long()
    {
        RunJournal.Wording(Event("decision", new
        {
            question = "Proceed?",
            answer = "yes",
            by = "timed default",
            after = "30m",
        })).Should().Be("took the lead's recommendation, nobody having answered in 30m: Proceed? -> yes");
    }

    [Fact]
    public void A_question_sent_back_says_it_was_sent_back()
    {
        RunJournal.Wording(Event("decision", new { question = "Proceed?", answer = "think again", by = "person" }))
            .Should().Be("sent back to the lead to think again: Proceed?");
    }

    [Fact]
    public void Criteria_from_the_team_say_they_were_the_team_s()
    {
        RunJournal.Wording(Event("criteria.agreed", new { criteria = new[] { "the suite passes" }, by = "team" }))
            .Should().Be("held to the team's own done-when: the suite passes");
    }
}
