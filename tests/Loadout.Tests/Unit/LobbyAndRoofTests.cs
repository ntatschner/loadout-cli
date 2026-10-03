using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The lobby and the roof: laid out like the floors, from the same kit and
/// rules, with seats for whatever is waiting and whoever is free.
/// </summary>
public sealed class LobbyAndRoofTests
{
    private static readonly OfficeKit Kit = OfficeKit.Kit();
    private static readonly OfficeRules Rules = OfficeRules.Default;

    // The office Loadout ships, with the armchairs and tables the shapes lack.
    private static readonly OfficeKit Tech = Unpacked();

    private static OfficeKit Unpacked()
    {
        var root = Path.Combine(Path.GetTempPath(), "loadout-tech-" + Guid.NewGuid().ToString("N"));

        OfficeArt.Unpack(root);

        return OfficeKits.Check(root, OfficeArt.BuiltIn).Kit!;
    }

    [Fact]
    public void Every_lobby_and_roof_passes_the_scene_check()
    {
        var failures = new List<string>();

        for (var seats = 0; seats <= 40; seats++)
        {
            foreach (var (name, plan) in new[] { ("lobby", FloorPlanner.Lobby(Kit, Rules, seats)), ("roof", FloorPlanner.Roof(Kit, Rules, seats)) })
            {
                var problems = OfficeScenes.Problems(plan.Scene, _ => null);

                if (problems.Count > 0)
                {
                    failures.Add($"{name} for {seats}: {string.Join(" / ", problems)}");
                }
            }
        }

        failures.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(3, 4)]
    [InlineData(9, 12)]
    [InlineData(16, 16)]
    public void The_waiting_room_seats_everything_waiting_and_never_looks_shut(int waiting, int seats)
    {
        var scene = FloorPlanner.Lobby(Kit, Rules, waiting).Scene;

        // Sofas in pairs facing each other, two seats each, and always one pair.
        scene.Desks.Should().HaveCount(seats, "a pair of sofas seats four, and there is always one pair");
        scene.Areas!.Should().Contain(area => area.Kind == "waiting-room" && area.Function == "waiting");

        // Half the seats face north across the table at the other half.
        scene.Desks.Count(seat => seat.Facing == "n").Should().Be(seats / 2);
    }

    [Fact]
    public void A_lobby_with_more_waiting_than_fits_seats_as_many_as_it_can()
    {
        var plan = FloorPlanner.Lobby(Kit, Rules, 500);

        plan.Capacity.Should().BeGreaterThan(16).And.BeLessThan(500);
        OfficeScenes.Problems(plan.Scene, _ => null).Should().BeEmpty();
    }

    [Fact]
    public void People_come_in_from_the_street_and_every_seat_can_be_walked_to()
    {
        var scene = FloorPlanner.Lobby(Kit, Rules, 12).Scene;
        var reached = scene.Reachable();

        scene.Door.Y.Should().Be(scene.Height - 2, "the entrance is in the street-side glass");
        scene.Walls![scene.Height - 1][scene.Door.X].Should().Be(-1, "the glass is open where the door is");
        reached[scene.Spots!["lift"].X, scene.Spots["lift"].Y].Should().BeTrue("the way in leads to the lift");
        reached[scene.Spots["reception-1"].X, scene.Spots["reception-1"].Y].Should().BeTrue("the receptionist gets to the desk");
        scene.Desks.Should().OnlyContain(seat => reached[seat.X, seat.Y]);
    }

    [Fact]
    public void The_lobby_has_reception_and_the_screen_with_what_they_are_for()
    {
        var scene = FloorPlanner.Lobby(Kit, Rules, 4).Scene;

        scene.Areas!.Should().Contain(area => area.Kind == "reception" && area.Function == "arrivals");
        scene.Areas!.Should().Contain(area => area.Kind == "lobby-screen" && area.Function == "schedules");
        scene.Props!.Should().Contain(prop => prop.Kind == "lobby-screen");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(16)]
    public void The_open_floor_between_the_lift_and_the_door_is_furnished_either_side_of_the_walk(int waiting)
    {
        var plan = FloorPlanner.Lobby(Tech, Rules, waiting);
        var scene = plan.Scene;
        var islands = scene.Areas!.Where(area => area.Kind == "seating").ToList();
        var walk = scene.Door.X;

        // Somewhere to sit west of the way in and east of it, not one empty hall.
        islands.Should().Contain(area => area.X + area.W <= walk);
        islands.Should().Contain(area => area.X > walk + 1);

        // In each, two armchairs facing each other across the table.
        foreach (var island in islands)
        {
            var chairs = scene.Props!.Where(prop => prop.Kind == "armchair" && prop.X >= island.X && prop.X < island.X + island.W && prop.Y >= island.Y && prop.Y < island.Y + island.H).ToList();

            chairs.Select(chair => chair.Facing).Should().BeEquivalentTo(["e", "w"], $"{island.Name} has a chair either side of its table");
            chairs.Single(chair => chair.Facing == "e").X.Should().BeLessThan(chairs.Single(chair => chair.Facing == "w").X, "they face each other, not away");
            scene.Props!.Count(prop => prop.Kind == "plant" && prop.X >= island.X && prop.X < island.X + island.W && prop.Y >= island.Y && prop.Y < island.Y + island.H)
                .Should().Be(2, $"{island.Name} has a plant at two corners");
        }

        // And the way from the street to the lift is still open.
        OfficeScenes.Problems(scene, sizeOf: null).Should().BeEmpty();
        scene.Reachable()[scene.Spots!["lift"].X, scene.Spots["lift"].Y].Should().BeTrue();
    }

    [Fact]
    public void The_lift_is_in_the_same_place_on_every_level()
    {
        var floor = FloorPlanner.Plan(Kit, Rules, "run-a", 6).Scene.Spots!["lift"];

        FloorPlanner.Lobby(Kit, Rules, 6).Scene.Spots!["lift"].Should().Be(floor);
        FloorPlanner.Roof(Kit, Rules, 6).Scene.Spots!["lift"].Should().Be(floor);
    }

    [Fact]
    public void The_roof_is_reached_by_the_lift_and_has_benches_and_the_edge()
    {
        var scene = FloorPlanner.Roof(Kit, Rules, 9).Scene;
        var reached = scene.Reachable();

        scene.Door.Should().Be(scene.Spots!["lift"] with { Facing = "s" }, "free people come up in the lift");
        scene.Desks.Should().HaveCount(10);
        scene.Desks.Should().OnlyContain(seat => reached[seat.X, seat.Y]);
        scene.Spots.Keys.Should().Contain(name => name.StartsWith("edge-", StringComparison.Ordinal));
        scene.Spots.Where(one => one.Key.StartsWith("edge-", StringComparison.Ordinal)).Should().OnlyContain(one => reached[one.Value.X, one.Value.Y]);
        scene.Props!.Should().Contain(prop => prop.Kind == "pergola");
        scene.Props!.Should().NotContain(prop => prop.Kind == "toilet" || prop.Kind == "cupboard", "the roof has only the lift and stairs");
        scene.Areas!.Should().Contain(area => area.Kind == "break-area" && area.Function == "idle");
    }

    [Fact]
    public void The_roof_deck_is_a_terrace_and_still_seats_exactly_who_is_free()
    {
        var failures = new List<string>();

        for (var seats = 0; seats <= 48; seats += 4)
        {
            var scene = FloorPlanner.Roof(Tech, Rules, seats).Scene;
            var reached = scene.Reachable();
            var middle = scene.Width / 2;
            var loungers = scene.Props!.Where(prop => prop.Kind == "lounger").ToList();
            var beds = scene.Props!.Where(prop => prop.Kind == "terrace-planter").ToList();

            // Loungers west of the way from the lift, beds east of it.
            if (loungers.Count < 4 || loungers.Any(one => one.X >= middle))
            {
                failures.Add($"{seats}: {loungers.Count} loungers, {loungers.Count(one => one.X >= middle)} east of the middle");
            }

            if (beds.Count < 4 || beds.Any(one => one.X < middle))
            {
                failures.Add($"{seats}: {beds.Count} beds, {beds.Count(one => one.X < middle)} west of the middle");
            }

            // The decor is no seat: a bench for every two free, as before.
            if (scene.Desks.Count != Math.Max(2, (seats + 1) / 2) * 2)
            {
                failures.Add($"{seats}: {scene.Desks.Count} seats");
            }

            var stranded = scene.Desks.Concat(scene.Spots!.Values).Where(spot => !reached[spot.X, spot.Y]).ToList();

            if (stranded.Count > 0)
            {
                failures.Add($"{seats}: {stranded.Count} places cannot be walked to, the first at {stranded[0].X},{stranded[0].Y}");
            }

            failures.AddRange(OfficeScenes.Problems(scene, sizeOf: null).Select(problem => $"{seats}: {problem}"));
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void The_same_number_always_gets_the_same_lobby()
    {
        string Json(int seats) => JsonSerializer.Serialize(FloorPlanner.Lobby(Kit, Rules, seats).Scene);

        Json(8).Should().Be(Json(8));
    }
}
