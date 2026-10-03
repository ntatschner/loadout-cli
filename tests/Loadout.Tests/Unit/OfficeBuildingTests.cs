using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Who has which bays on which floor of the building, and when they move.
/// </summary>
/// <remarks>
/// A bay seats three here and a floor has three bays, so a run of four or more
/// needs a second bay and one of ten or more a second floor. The waits are the
/// built-in ones: twenty seconds before taking another bay, five minutes before
/// giving one back, an hour before a finished run's dark bays are freed.
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

    // Where a run is, as "floor:first bay+how many": "1:0+2" is the two west bays of floor 1.
    private static IReadOnlyList<string> PlacesOf(BuildingView view, string run) =>
        [.. view.Occupied.Where(floor => floor.Run == run).Select(floor => $"{floor.Number}:{floor.Bay}+{floor.Bays}")];

    [Fact]
    public void Runs_seen_first_get_their_bays_on_the_lowest_floor_with_room_in_start_order()
    {
        var view = new OfficeBuilding().Update([Run("b", 2, startedMinute: 5), Run("a", 5), Run("c", 5, startedMinute: 9)], Seats, Rules, Start);

        PlacesOf(view, "a").Should().Equal("1:0+2");
        PlacesOf(view, "b").Should().Equal(["1:2+1"], "a small team shares the floor rather than taking one of its own");
        PlacesOf(view, "c").Should().Equal(["2:0+2"], "two bays side by side are not free on floor 1");
        view.Floors.Should().Be(10, "the tower is never shorter than ten floors");
    }

    [Fact]
    public void People_fill_the_first_floor_s_bays_before_the_floor_above()
    {
        var view = new OfficeBuilding().Update([Run("a", 11)], Seats, Rules, Start);

        PlacesOf(view, "a").Should().Equal("1:0+3", "2:0+1");
        view.Occupied.Select(floor => floor.People).Should().Equal(9, 2);
        view.Occupied.Select(floor => floor.Part).Should().Equal(0, 1);
        view.Occupied[1].Nodes.Should().Equal("n9", "n10");
    }

    [Fact]
    public void A_team_that_outgrows_its_bay_takes_the_one_beside_it_only_after_twenty_seconds()
    {
        var building = new OfficeBuilding();

        building.Update([Run("a", 2)], Seats, Rules, Start);

        PlacesOf(building.Update([Run("a", 4)], Seats, Rules, Start.AddSeconds(1)), "a").Should().Equal(["1:0+1"], "not yet");
        PlacesOf(building.Update([Run("a", 4)], Seats, Rules, Start.AddSeconds(20)), "a").Should().Equal(["1:0+1"], "nineteen seconds over");
        PlacesOf(building.Update([Run("a", 4)], Seats, Rules, Start.AddSeconds(21)), "a").Should().Equal("1:0+2");
    }

    [Fact]
    public void Shrinking_back_before_the_wait_is_up_starts_the_wait_again()
    {
        var building = new OfficeBuilding();

        building.Update([Run("a", 2)], Seats, Rules, Start);
        building.Update([Run("a", 4)], Seats, Rules, Start.AddSeconds(1));
        building.Update([Run("a", 2)], Seats, Rules, Start.AddSeconds(10));
        building.Update([Run("a", 4)], Seats, Rules, Start.AddSeconds(15));

        PlacesOf(building.Update([Run("a", 4)], Seats, Rules, Start.AddSeconds(30)), "a").Should().Equal(["1:0+1"], "over again only since second 15");
        PlacesOf(building.Update([Run("a", 4)], Seats, Rules, Start.AddSeconds(36)), "a").Should().Equal("1:0+2");
    }

    [Fact]
    public void A_team_whose_neighbour_has_the_bay_beside_it_grows_onto_the_floor_above()
    {
        var building = new OfficeBuilding();

        building.Update([Run("a", 2), Run("b", 2, startedMinute: 1)], Seats, Rules, Start);
        building.Update([Run("a", 5), Run("b", 2, startedMinute: 1)], Seats, Rules, Start.AddSeconds(1));

        var view = building.Update([Run("a", 5), Run("b", 2, startedMinute: 1)], Seats, Rules, Start.AddSeconds(26));

        PlacesOf(view, "b").Should().Equal(["1:1+1"], "a neighbour is never moved");
        PlacesOf(view, "a").Should().Equal("1:0+1", "2:0+1");
    }

    [Fact]
    public void A_team_fills_its_own_floor_then_goes_up_even_with_a_lower_floor_free()
    {
        var building = new OfficeBuilding();

        building.Update([Run("x", 9), Run("y", 9, startedMinute: 1), Run("a", 2, startedMinute: 2)], Seats, Rules, Start);

        // x goes, freeing floor 1; a, alone on floor 3, then needs four bays.
        building.Update([Run("y", 9, startedMinute: 1), Run("a", 12, startedMinute: 2)], Seats, Rules, Start.AddSeconds(1));

        var view = building.Update([Run("y", 9, startedMinute: 1), Run("a", 12, startedMinute: 2)], Seats, Rules, Start.AddSeconds(30));

        PlacesOf(view, "a").Should().Equal(["3:0+3", "4:0+1"], "together, rather than on the free floor two storeys down");
    }

    [Fact]
    public void Dipping_below_its_bays_restarts_the_wait_to_take_another()
    {
        var building = new OfficeBuilding();

        building.Update([Run("a", 5)], Seats, Rules, Start);

        // Two bays, then over them, then under them, then over again.
        building.Update([Run("a", 7)], Seats, Rules, Start.AddSeconds(1));
        building.Update([Run("a", 5)], Seats, Rules, Start.AddSeconds(10));

        PlacesOf(building.Update([Run("a", 7)], Seats, Rules, Start.AddSeconds(25)), "a")
            .Should().Equal(["1:0+2"], "over again only since second 25");
    }

    [Fact]
    public void A_bay_is_given_back_only_after_five_minutes_of_fitting_in_fewer()
    {
        var building = new OfficeBuilding();

        building.Update([Run("a", 5)], Seats, Rules, Start);
        building.Update([Run("a", 2)], Seats, Rules, Start.AddMinutes(1));

        PlacesOf(building.Update([Run("a", 2)], Seats, Rules, Start.AddMinutes(5)), "a").Should().Equal(["1:0+2"], "four minutes under");
        PlacesOf(building.Update([Run("a", 2)], Seats, Rules, Start.AddMinutes(6)), "a").Should().Equal("1:0+1");
    }

    [Fact]
    public void A_finished_run_keeps_its_bays_dark_for_an_hour_then_frees_them()
    {
        var building = new OfficeBuilding();
        var ended = Start.AddMinutes(10);

        building.Update([Run("a", 11)], Seats, Rules, Start);

        var kept = building.Update([Run("a", 11, finished: ended, state: "done")], Seats, Rules, ended.AddMinutes(59));

        PlacesOf(kept, "a").Should().Equal("1:0+3", "2:0+1");
        kept.Occupied.Should().OnlyContain(floor => floor.Dark && floor.State == "done");

        building.Update([Run("a", 11, finished: ended, state: "done")], Seats, Rules, ended.AddMinutes(61))
            .Occupied.Should().BeEmpty();
    }

    [Fact]
    public void A_freed_bay_is_the_next_run_s_first()
    {
        var building = new OfficeBuilding();

        building.Update([Run("a", 2), Run("b", 2, startedMinute: 1)], Seats, Rules, Start);

        var view = building.Update([Run("b", 2, startedMinute: 1), Run("c", 2, startedMinute: 2)], Seats, Rules, Start.AddMinutes(1));

        PlacesOf(view, "b").Should().Equal("1:1+1");
        PlacesOf(view, "c").Should().Equal("1:0+1");
    }

    [Fact]
    public void Nobody_moves_when_a_neighbour_grows_shrinks_or_goes()
    {
        var building = new OfficeBuilding();

        building.Update([Run("a", 2), Run("b", 2, startedMinute: 1), Run("c", 2, startedMinute: 2)], Seats, Rules, Start);

        // b grows past its bay, shrinks back, and then goes.
        building.Update([Run("a", 2), Run("b", 6, startedMinute: 1), Run("c", 2, startedMinute: 2)], Seats, Rules, Start.AddSeconds(1));
        building.Update([Run("a", 2), Run("b", 6, startedMinute: 1), Run("c", 2, startedMinute: 2)], Seats, Rules, Start.AddSeconds(30));
        building.Update([Run("a", 2), Run("b", 2, startedMinute: 1), Run("c", 2, startedMinute: 2)], Seats, Rules, Start.AddMinutes(1));
        building.Update([Run("a", 2), Run("b", 2, startedMinute: 1), Run("c", 2, startedMinute: 2)], Seats, Rules, Start.AddMinutes(7));

        var view = building.Update([Run("a", 2), Run("c", 2, startedMinute: 2)], Seats, Rules, Start.AddMinutes(8));

        PlacesOf(view, "a").Should().Equal("1:0+1");
        PlacesOf(view, "c").Should().Equal("1:2+1");
    }

    [Fact]
    public void A_floor_has_as_many_bays_as_the_rules_say()
    {
        var two = Rules.With(new OfficeRules(OfficeRules.Version, Bays: 2));
        var view = new OfficeBuilding().Update([Run("a", 2), Run("b", 2, startedMinute: 1), Run("c", 2, startedMinute: 2)], Seats, two, Start);

        PlacesOf(view, "c").Should().Equal(["2:0+1"], "two bays to a floor, both taken on floor 1");
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
        // Twelve teams of nine, a floor each.
        var runs = Enumerable.Range(0, 12).Select(i => Run($"r{i}", 9, startedMinute: i)).ToList();

        new OfficeBuilding().Update(runs, Seats, Rules, Start).Floors.Should().Be(12);
    }
}
