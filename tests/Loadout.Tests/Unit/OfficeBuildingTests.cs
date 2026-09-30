using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Who is on which floor of the building, and when they move.
/// </summary>
/// <remarks>
/// A floor seats three here, so a run of four or more has to spill. The waits
/// are the built-in ones: twenty seconds before spilling up, five minutes before
/// giving a floor back, an hour before a finished run's dark floor is freed.
/// </remarks>
public sealed class OfficeBuildingTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly OfficeRules Rules = OfficeRules.Default;
    private const int Seats = 3;

    private static RunSummary Run(string id, int people, int startedMinute = 0, DateTimeOffset? finished = null, string state = "working") =>
        new(id, "d", "team", "goal", "supervised", Start.AddMinutes(startedMinute), finished, null, 0m, 1,
            [.. Enumerable.Range(0, people).Select(i => new RunNode($"n{i}", i == 0 ? "role.project-lead" : "role.worker", state, 1, 0m, Start))],
            [], []);

    private static IReadOnlyList<int> FloorsOf(BuildingView view, string run) =>
        [.. view.Occupied.Where(floor => floor.Run == run).Select(floor => floor.Number)];

    [Fact]
    public void Runs_seen_first_get_the_floors_they_need_lowest_first_in_start_order()
    {
        var building = new OfficeBuilding();

        var view = building.Update([Run("b", 2, startedMinute: 5), Run("a", 5)], Seats, Rules, Start);

        FloorsOf(view, "a").Should().Equal(1, 2);
        FloorsOf(view, "b").Should().Equal(3);
        view.Floors.Should().Be(10, "the tower is never shorter than ten floors");
    }

    [Fact]
    public void People_fill_the_first_floor_before_the_one_above()
    {
        var view = new OfficeBuilding().Update([Run("a", 5)], Seats, Rules, Start);

        view.Occupied.Select(floor => floor.People).Should().Equal(3, 2);
        view.Occupied.Select(floor => floor.Part).Should().Equal(0, 1);
        view.Occupied[0].Nodes.Should().Equal("n0", "n1", "n2");
        view.Occupied[1].Nodes.Should().Equal("n3", "n4");
    }

    [Fact]
    public void A_team_that_outgrows_its_floor_spills_up_only_after_twenty_seconds()
    {
        var building = new OfficeBuilding();

        building.Update([Run("a", 2)], Seats, Rules, Start);

        FloorsOf(building.Update([Run("a", 4)], Seats, Rules, Start.AddSeconds(1)), "a").Should().Equal([1], "not yet");
        FloorsOf(building.Update([Run("a", 4)], Seats, Rules, Start.AddSeconds(20)), "a").Should().Equal([1], "nineteen seconds over");
        FloorsOf(building.Update([Run("a", 4)], Seats, Rules, Start.AddSeconds(21)), "a").Should().Equal(1, 2);
    }

    [Fact]
    public void Shrinking_back_before_the_wait_is_up_starts_the_wait_again()
    {
        var building = new OfficeBuilding();

        building.Update([Run("a", 2)], Seats, Rules, Start);
        building.Update([Run("a", 4)], Seats, Rules, Start.AddSeconds(1));
        building.Update([Run("a", 2)], Seats, Rules, Start.AddSeconds(10));
        building.Update([Run("a", 4)], Seats, Rules, Start.AddSeconds(15));

        FloorsOf(building.Update([Run("a", 4)], Seats, Rules, Start.AddSeconds(30)), "a").Should().Equal([1], "over again only since second 15");
        FloorsOf(building.Update([Run("a", 4)], Seats, Rules, Start.AddSeconds(36)), "a").Should().Equal(1, 2);
    }

    [Fact]
    public void A_spill_goes_to_the_floor_above_or_the_lowest_free_one_when_that_is_taken()
    {
        var building = new OfficeBuilding();

        building.Update([Run("a", 2), Run("b", 2, startedMinute: 1)], Seats, Rules, Start);

        // Seen over first, and still over twenty-five seconds on.
        building.Update([Run("a", 5), Run("b", 2, startedMinute: 1)], Seats, Rules, Start.AddSeconds(1));

        var view = building.Update([Run("a", 5), Run("b", 2, startedMinute: 1)], Seats, Rules, Start.AddSeconds(26));

        FloorsOf(view, "b").Should().Equal(2);
        FloorsOf(view, "a").Should().Equal(1, 3);
    }

    [Fact]
    public void A_spill_prefers_the_floor_above_even_with_a_lower_one_free()
    {
        var building = new OfficeBuilding();

        building.Update([Run("x", 2), Run("y", 2, startedMinute: 1), Run("a", 2, startedMinute: 2)], Seats, Rules, Start);

        // x goes, freeing floor 1; a, on floor 3, then outgrows it.
        building.Update([Run("y", 2, startedMinute: 1), Run("a", 5, startedMinute: 2)], Seats, Rules, Start.AddSeconds(1));

        var view = building.Update([Run("y", 2, startedMinute: 1), Run("a", 5, startedMinute: 2)], Seats, Rules, Start.AddSeconds(30));

        FloorsOf(view, "a").Should().Equal([3, 4], "together, rather than on the free floor two storeys down");
    }

    [Fact]
    public void Dipping_below_its_floors_restarts_the_wait_to_spill()
    {
        var building = new OfficeBuilding();

        building.Update([Run("a", 5)], Seats, Rules, Start);

        // Two floors, then over them, then under them, then over again.
        building.Update([Run("a", 7)], Seats, Rules, Start.AddSeconds(1));
        building.Update([Run("a", 2)], Seats, Rules, Start.AddSeconds(10));

        FloorsOf(building.Update([Run("a", 7)], Seats, Rules, Start.AddSeconds(25)), "a")
            .Should().Equal([1, 2], "over again only since second 25");
    }

    [Fact]
    public void A_floor_is_given_back_only_after_five_minutes_of_fitting_on_fewer()
    {
        var building = new OfficeBuilding();

        building.Update([Run("a", 5)], Seats, Rules, Start);
        building.Update([Run("a", 2)], Seats, Rules, Start.AddMinutes(1));

        FloorsOf(building.Update([Run("a", 2)], Seats, Rules, Start.AddMinutes(5)), "a").Should().Equal([1, 2], "four minutes under");
        FloorsOf(building.Update([Run("a", 2)], Seats, Rules, Start.AddMinutes(6)), "a").Should().Equal(1);
    }

    [Fact]
    public void A_finished_run_keeps_its_floors_dark_for_an_hour_then_frees_them()
    {
        var building = new OfficeBuilding();
        var ended = Start.AddMinutes(10);

        building.Update([Run("a", 5)], Seats, Rules, Start);

        var kept = building.Update([Run("a", 5, finished: ended, state: "done")], Seats, Rules, ended.AddMinutes(59));

        FloorsOf(kept, "a").Should().Equal(1, 2);
        kept.Occupied.Should().OnlyContain(floor => floor.Dark && floor.State == "done");

        building.Update([Run("a", 5, finished: ended, state: "done")], Seats, Rules, ended.AddMinutes(61))
            .Occupied.Should().BeEmpty();
    }

    [Fact]
    public void A_freed_floor_is_the_next_run_s_first()
    {
        var building = new OfficeBuilding();

        building.Update([Run("a", 2), Run("b", 2, startedMinute: 1)], Seats, Rules, Start);

        var view = building.Update([Run("b", 2, startedMinute: 1), Run("c", 2, startedMinute: 2)], Seats, Rules, Start.AddMinutes(1));

        FloorsOf(view, "b").Should().Equal(2);
        FloorsOf(view, "c").Should().Equal(1);
    }

    [Theory]
    [InlineData("waiting")]
    [InlineData("failed")]
    [InlineData("working")]
    public void A_floor_s_strip_shows_what_most_needs_seeing(string expected)
    {
        var run = Run("a", 3) with
        {
            Nodes =
            [
                new RunNode("lead", "role.project-lead", "blocked", 1, 0m, Start),
                new RunNode("w1", "role.worker", expected == "failed" ? "failed" : "working", 1, 0m, Start),
                new RunNode("w2", "role.worker", "working", 1, 0m, Start),
            ],
            Gates = expected == "waiting" ? [new PendingAsk("q", "w2", "role.worker", "Bash", "ls", Start)] : null,
        };

        new OfficeBuilding().Update([run], Seats, Rules, Start).Occupied[0].State.Should().Be(expected);
    }

    [Fact]
    public void The_tower_grows_past_ten_floors_when_it_has_to()
    {
        var runs = Enumerable.Range(0, 6).Select(i => Run($"r{i}", 5, startedMinute: i)).ToList();

        new OfficeBuilding().Update(runs, Seats, Rules, Start).Floors.Should().Be(12);
    }
}
