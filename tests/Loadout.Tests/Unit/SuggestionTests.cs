using FluentAssertions;
using Loadout.Core.Tasks;
using Loadout.Models.Tasks;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Short replies offered instead of composed, and which of them can be trusted.
/// </summary>
/// <remarks>
/// A composed reply cannot be wrong about the state it names, because it was
/// assembled out of that state. A drafted one can be confidently wrong about
/// exactly the same thing, in the same shape. Saying which is which is the
/// whole safety of the feature, so the two are never in one list.
/// </remarks>
public sealed class SuggestionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static TaskItem Task(string id, TaskState state, int minutesAgo = 0) =>
        new()
        {
            Id = id,
            Title = id,
            State = state,
            DeclaredBy = "someone",
            DeclaredUtc = Now.AddMinutes(-minutesAgo),
        };

    [Fact]
    public void Everything_composed_is_marked_composed()
    {
        var offered = Suggestions.Compose([Task("a", TaskState.Doing)]);

        offered.Should().OnlyContain(s => s.Source == SuggestionSource.Composed);
    }

    [Fact]
    public void What_is_underway_is_offered_before_what_has_not_started()
    {
        var offered = Suggestions.Compose(
            [
                Task("not-started", TaskState.Open),
                Task("stuck", TaskState.Blocked),
                Task("underway", TaskState.Doing),
            ]);

        // The order somebody would act in. A list that opened with the
        // untouched backlog would be answering a question nobody asked in the
        // middle of doing something else.
        offered.Select(s => s.Text).Should().Equal(
            "continue underway", "why is stuck blocked", "start not-started");
    }

    [Fact]
    public void What_the_record_does_not_back_up_is_offered_as_a_check()
    {
        var offered = Suggestions.Compose(
            [Task("a", TaskState.Done)],
            [new TaskDisagreement("a", "called done, nothing committed since")]);

        // "check", never "fix". The record not backing a claim up is not the
        // same as the claim being wrong, and a suggestion saying "fix" would
        // settle that question on nobody's authority.
        offered.Select(s => s.Text).Should().Contain("check a");
        offered.Should().NotContain(s => s.Text.Contains("fix", StringComparison.Ordinal));
    }

    [Fact]
    public void The_most_recently_declared_comes_first_within_a_state()
    {
        var offered = Suggestions.Compose(
            [Task("older", TaskState.Doing, minutesAgo: 90), Task("newer", TaskState.Doing, minutesAgo: 5)]);

        offered.Select(s => s.Text).Should().Equal("continue newer", "continue older");
    }

    [Fact]
    public void The_list_stays_short()
    {
        var many = Enumerable.Range(0, 30)
            .Select(i => Task($"task-{i:00}", TaskState.Doing, minutesAgo: i))
            .ToList();

        // Thirty suggestions is a backlog with a different name, and reading it
        // costs more than typing the reply would have.
        Suggestions.Compose(many).Should().HaveCount(Suggestions.Most);
    }

    [Fact]
    public void The_same_reply_is_never_offered_twice()
    {
        var offered = Suggestions.Compose(
            [Task("a", TaskState.Done)],
            [
                new TaskDisagreement("a", "one reason"),
                new TaskDisagreement("a", "another reason"),
            ]);

        offered.Select(s => s.Text).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Done_and_dropped_work_is_not_offered_as_something_to_continue()
    {
        var offered = Suggestions.Compose(
            [Task("finished", TaskState.Done), Task("abandoned", TaskState.Dropped)]);

        offered.Should().BeEmpty();
    }

    [Fact]
    public void Nothing_recorded_offers_nothing()
    {
        Suggestions.Compose([]).Should().BeEmpty();
    }

    [Fact]
    public void A_drafted_reply_is_marked_drafted_and_left_alone()
    {
        var drafted = Suggestions.Draft("  ask why the parser is slow  ");

        drafted.Source.Should().Be(SuggestionSource.Drafted);

        // Trimmed, and otherwise untouched. A method that improved the text
        // would be a place for a drafted reply to quietly become something
        // else while still carrying the label.
        drafted.Text.Should().Be("ask why the parser is slow");
    }

    [Fact]
    public void A_drafted_reply_can_never_arrive_as_a_composed_one()
    {
        // The property the whole feature rests on. There is no path from Draft
        // into Compose's output, and nothing composed can be relabelled by
        // passing it back through Draft.
        var composed = Suggestions.Compose([Task("a", TaskState.Doing)]).Single();
        var redrafted = Suggestions.Draft(composed.Text);

        redrafted.Source.Should().Be(SuggestionSource.Drafted);
        composed.Source.Should().Be(SuggestionSource.Composed);
    }
}
