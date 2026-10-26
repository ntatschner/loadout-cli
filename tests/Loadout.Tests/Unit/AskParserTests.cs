using FluentAssertions;
using Loadout.Core.Ask;
using Loadout.Core.Tasks;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Placing typed requests into the shapes Loadout answers itself.
/// </summary>
/// <remarks>
/// A phrase corpus: the requests that started this, in the words they were
/// first typed, then the near misses. The rewrites are held to the parser
/// itself, because a suggestion that does not place is worse than none — it
/// teaches a phrasing that fails.
/// </remarks>
public sealed class AskParserTests
{
    /// <summary>A Monday, so "last tuesday" is six days back.</summary>
    private static readonly DateOnly Today = new(2026, 10, 26);

    private static readonly AskVocabulary Names = new(
        [
            ("loadout-cli", ["Loadout", "ailauncher"]),
            ("talkwatch", ["TalkWatch"]),
            ("starstats", ["StarStats"]),
        ],
        ["software-feature", "bug-squad"]);

    private static AskRequest Placed(string query)
    {
        var placement = AskParser.Place(query, Names, Today);

        placement.Request.Should().NotBeNull($"'{query}' should place");

        return placement.Request!;
    }

    [Fact]
    public void The_spend_question_places_with_its_day_and_every_project()
    {
        var request = Placed("? how much did i spend last tuesday, across all registered projects");

        request.Shape.Should().Be(AskShape.Spend);
        request.Project.Should().BeNull();
        request.Period.Should().NotBeNull();
        request.Period!.From.Should().Be(new DateOnly(2026, 10, 20));
        request.Period.Until.Should().Be(new DateOnly(2026, 10, 20));
    }

    [Fact]
    public void The_team_request_places_with_its_count_its_pick_and_a_start()
    {
        var request = Placed("start a team and queue up 5 easy pending tasks and start it");

        request.Shape.Should().Be(AskShape.TeamQueue);
        request.Count.Should().Be(5);
        request.Pick.Should().Be(TaskPick.Easy);
        request.Start.Should().BeTrue();
        request.Team.Should().BeNull();
    }

    [Fact]
    public void The_terminals_request_places_with_its_count_and_needs_work_first()
    {
        var request = Placed("start 4 terminal sessions loaded with an open task from all my projects you think needs work first");

        request.Shape.Should().Be(AskShape.Terminals);
        request.Count.Should().Be(4);
        request.Pick.Should().Be(TaskPick.NeedsWorkFirst);
        request.Project.Should().BeNull();
    }

    [Theory]
    [InlineData("how much did talkwatch cost this week", "talkwatch")]
    [InlineData("what did I spend on loadout-cli yesterday", "loadout-cli")]
    [InlineData("spend for StarStats in the last 10 days", "starstats")]
    [InlineData("how much has ailauncher cost", "loadout-cli")]
    public void A_project_is_recognised_by_any_of_its_names(string query, string slug)
    {
        Placed(query).Project.Should().Be(slug);
    }

    [Fact]
    public void A_project_name_inside_another_is_not_mistaken_for_it()
    {
        var names = new AskVocabulary([("loadout-cli", []), ("loadout", [])], []);

        AskParser.Place("list open tasks for loadout-cli", names, Today).Request!.Project.Should().Be("loadout-cli");
        AskParser.Place("list open tasks for loadout", names, Today).Request!.Project.Should().Be("loadout");
    }

    [Fact]
    public void A_named_team_is_recognised_and_queueing_alone_does_not_start_it()
    {
        var request = Placed("queue three tasks for bug-squad");

        request.Shape.Should().Be(AskShape.TeamQueue);
        request.Team.Should().Be("bug-squad");
        request.Count.Should().Be(3);
        request.Start.Should().BeFalse();
    }

    [Theory]
    [InlineData("list 5 easy open tasks", AskShape.Tasks, 5, TaskPick.Easy)]
    [InlineData("which tasks need work first", AskShape.Tasks, null, TaskPick.NeedsWorkFirst)]
    [InlineData("show me the easiest tasks on talkwatch", AskShape.Tasks, null, TaskPick.Easy)]
    [InlineData("open two terminals with tasks", AskShape.Terminals, 2, TaskPick.Any)]
    public void Task_requests_place_with_their_slots(string query, AskShape shape, int? count, TaskPick pick)
    {
        var request = Placed(query);

        request.Shape.Should().Be(shape);
        request.Count.Should().Be(count);
        request.Pick.Should().Be(pick);
    }

    [Theory]
    [InlineData("list easy open tasks added in the last 10 days")]
    [InlineData("list easy open tasks added on 14 oct")]
    [InlineData("list easy open tasks added on october 14")]
    public void A_number_inside_a_period_is_not_a_count(string query)
    {
        Placed(query).Count.Should().BeNull();
    }

    [Fact]
    public void A_period_counted_in_days_runs_up_to_today()
    {
        var request = Placed("how much did I spend in the last 10 days");

        request.Period!.From.Should().Be(Today.AddDays(-9));
        request.Period.Until.Should().Be(Today);
    }

    [Fact]
    public void A_spend_question_about_a_team_is_still_a_spend_question()
    {
        Placed("how much did the team spend this week").Shape.Should().Be(AskShape.Spend);
    }

    [Theory]
    [InlineData("today", 2026, 10, 26, 2026, 10, 26)]
    [InlineData("yesterday", 2026, 10, 25, 2026, 10, 25)]
    [InlineData("last monday", 2026, 10, 19, 2026, 10, 19)]
    [InlineData("on sunday", 2026, 10, 25, 2026, 10, 25)]
    [InlineData("this week", 2026, 10, 26, 2026, 10, 26)]
    [InlineData("last week", 2026, 10, 19, 2026, 10, 25)]
    [InlineData("last month", 2026, 9, 1, 2026, 9, 30)]
    [InlineData("this month", 2026, 10, 1, 2026, 10, 26)]
    [InlineData("the last 2 weeks", 2026, 10, 13, 2026, 10, 26)]
    [InlineData("on 2026-10-14", 2026, 10, 14, 2026, 10, 14)]
    [InlineData("on 3rd oct", 2026, 10, 3, 2026, 10, 3)]
    [InlineData("on october 3", 2026, 10, 3, 2026, 10, 3)]
    [InlineData("on 20 dec", 2025, 12, 20, 2025, 12, 20)]
    public void Periods_are_read_as_people_say_them(string phrase, int fy, int fm, int fd, int uy, int um, int ud)
    {
        var span = AskDates.Find(phrase, Today);

        span.Should().NotBeNull();
        span!.From.Should().Be(new DateOnly(fy, fm, fd));
        span.Until.Should().Be(new DateOnly(uy, um, ud));
    }

    [Fact]
    public void No_period_named_is_none_rather_than_a_guess()
    {
        AskDates.Find("how much did i spend", Today).Should().BeNull();
    }

    [Theory]
    [InlineData("hello there")]
    [InlineData("translate this into french")]
    [InlineData("who broke the build")]
    public void An_unrelated_request_is_not_placed_and_every_shape_is_offered(string query)
    {
        var placement = AskParser.Place(query, Names, Today);

        placement.Request.Should().BeNull();
        placement.NearMisses.Select(near => near.Shape).Should().BeEquivalentTo(
            [AskShape.Spend, AskShape.Tasks, AskShape.TeamQueue, AskShape.Terminals]);
    }

    [Fact]
    public void A_near_miss_puts_the_closest_shape_first_and_keeps_what_was_found()
    {
        var placement = AskParser.Place("tokens burnt yesterday on talkwatch", Names, Today);

        placement.Request.Should().BeNull();
        placement.NearMisses[0].Shape.Should().Be(AskShape.Spend);
        placement.NearMisses[0].Rewrite.Should().Contain("talkwatch").And.Contain("2026-10-25");
    }

    [Theory]
    [InlineData(AskShape.Spend)]
    [InlineData(AskShape.Tasks)]
    [InlineData(AskShape.TeamQueue)]
    [InlineData(AskShape.Terminals)]
    public void Every_rewrite_places_as_the_shape_it_offers(AskShape shape)
    {
        foreach (var pick in Enum.GetValues<TaskPick>())
        {
            foreach (var project in new[] { null, "talkwatch" })
            {
                var rewrite = AskParser.Rewrite(shape, AskDates.Find("yesterday", Today), project, 4, pick);

                var placed = AskParser.Place(rewrite, Names, Today).Request;

                placed.Should().NotBeNull($"'{rewrite}' should place");
                placed!.Shape.Should().Be(shape, rewrite);
                placed.Project.Should().Be(project, rewrite);
            }
        }
    }

    [Fact]
    public void A_leading_question_mark_and_extra_spacing_are_ignored()
    {
        AskParser.Normalise("  ?  How   much  ").Should().Be("how much");
    }
}
