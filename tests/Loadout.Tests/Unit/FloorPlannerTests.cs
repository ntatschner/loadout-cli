using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The floor planner: every floor it lays out must be one people can use, and
/// the same run must always get the same floor.
/// </summary>
/// <remarks>
/// Checked across thousands of seeds and every team size rather than on a few
/// hand-picked floors, because a layout rule that fails one seed in a hundred
/// fails somebody's run every week.
/// </remarks>
public sealed class FloorPlannerTests
{
    private static readonly OfficeKit Kit = OfficeKit.Kit();

    // The office Loadout ships, its pieces saying where they suit.
    private static readonly OfficeKit Tech = Unpacked();

    private static OfficeKit Unpacked()
    {
        var root = Path.Combine(Path.GetTempPath(), "loadout-tech-" + Guid.NewGuid().ToString("N"));

        OfficeArt.Unpack(root);

        return OfficeKits.Check(root, OfficeArt.BuiltIn).Kit!;
    }
    private static readonly OfficeRules Rules = OfficeRules.Default;

    private static OfficeFloorPlan Plan(string seed, int team) => FloorPlanner.Plan(Kit, Rules, seed, team);

    [Fact]
    public void Every_floor_for_every_team_size_passes_the_scene_check_first_time()
    {
        var failures = new List<string>();

        for (var team = 1; team <= 30; team++)
        {
            for (var seed = 0; seed < 100; seed++)
            {
                var plan = Plan($"run-{seed}", team);
                var problems = OfficeScenes.Problems(plan.Scene, _ => null);

                if (problems.Count > 0 || plan.Attempt != 0)
                {
                    var first = FloorPlanner.Faults(Kit, Rules, $"run-{seed}", team, 0);

                    failures.Add($"team {team} seed {seed}: attempt {plan.Attempt}; first try: {string.Join(" / ", first)}; returned: {string.Join(" / ", problems)}");
                }
            }
        }

        failures.Should().BeEmpty("the built-in kit should never need a retry or the fallback");
    }

    [Fact]
    public void No_piece_of_wall_stands_on_its_own()
    {
        // Every wall joins another: a scrap of wall left over where two rooms'
        // walls failed to meet is drawn as a stub on its own. Pieces of three
        // cells or fewer, joined only side by side, are looked for on every
        // team size.
        var stubs = new List<string>();

        for (var team = 1; team <= 30; team++)
        {
            for (var seed = 0; seed < 20; seed++)
            {
                var scene = Plan($"run-{seed}", team).Scene;
                var seen = new bool[scene.Width, scene.Height];

                bool Wall(int x, int y) => x >= 0 && y >= 0 && x < scene.Width && y < scene.Height && scene.Walls![y][x] >= 0;

                for (var sy = 0; sy < scene.Height; sy++)
                {
                    for (var sx = 0; sx < scene.Width; sx++)
                    {
                        if (seen[sx, sy] || !Wall(sx, sy))
                        {
                            continue;
                        }

                        var cells = new List<(int X, int Y)>();
                        var queue = new Queue<(int X, int Y)>([(sx, sy)]);

                        seen[sx, sy] = true;

                        while (queue.Count > 0)
                        {
                            var (cx, cy) = queue.Dequeue();

                            cells.Add((cx, cy));

                            foreach (var (nx, ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
                            {
                                if (Wall(nx, ny) && !seen[nx, ny])
                                {
                                    seen[nx, ny] = true;
                                    queue.Enqueue((nx, ny));
                                }
                            }
                        }

                        if (cells.Count <= 3)
                        {
                            stubs.Add($"team {team} seed {seed}: {string.Join(" ", cells.Select(cell => $"{cell.X},{cell.Y}"))}");
                        }
                    }
                }
            }
        }

        stubs.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(12)]
    [InlineData(40)]
    public void Everybody_up_to_the_floors_capacity_gets_a_desk_the_lead_first(int team)
    {
        var plan = Plan("run-a", team);
        var lead = plan.Scene.Areas!.Single(area => area.Kind == "lead-office");
        var first = plan.Scene.Desks[0];

        plan.Scene.Desks.Should().HaveCount(Math.Min(team, plan.Capacity));
        first.X.Should().BeInRange(lead.X, lead.X + lead.W - 1, "the lead's seat is desk 0, in the lead's office");
        first.Y.Should().BeInRange(lead.Y, lead.Y + lead.H - 1);
    }

    [Fact]
    public void A_floor_seats_at_least_a_dozen()
    {
        FloorPlanner.Capacity(Kit, Rules).Should().BeGreaterThanOrEqualTo(12);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 2)]
    [InlineData(10, 3)]
    public void Meeting_rooms_follow_the_table_by_team_size(int team, int meetings)
    {
        Plan("run-b", team).Scene.Areas!.Count(area => area.Kind == "meeting").Should().Be(meetings);
    }

    [Fact]
    public void Every_floor_has_the_lead_office_in_a_window_corner_of_the_team_area_a_kitchen_a_core_and_a_status_board()
    {
        foreach (var seed in Enumerable.Range(0, 50).Select(one => $"run-{one}"))
        {
            var scene = Plan(seed, 6).Scene;
            var lead = scene.Areas!.Single(area => area.Kind == "lead-office");
            var team = scene.Areas!.Single(area => area.Kind is "open-plan" or "team-room");

            // Against the south windows, at one end of the team's own area.
            (lead.Y + lead.H).Should().Be(scene.Height - 1, $"{seed}: the lead's office is against the windows");
            (lead.X == team.X || lead.X + lead.W == team.X + team.W).Should().BeTrue($"{seed}: the lead's office is in a corner of the team's area");
            scene.Areas.Should().Contain(area => area.Kind == "kitchen")
                .And.Contain(area => area.Kind == "core")
                .And.Contain(area => area.Kind == "status-board");
            scene.Props!.Should().Contain(prop => prop.Kind == "lift")
                .And.Contain(prop => prop.Kind == "exec-desk")
                .And.Contain(prop => prop.Kind == "coffee");
        }
    }

    [Fact]
    public void Every_floor_of_the_tech_office_passes_the_scene_check_first_time()
    {
        var failures = new List<string>();

        foreach (var team in new[] { 1, 2, 3, 5, 8, 12, 20, 30 })
        {
            for (var seed = 0; seed < 40; seed++)
            {
                var plan = FloorPlanner.Plan(Tech, Rules, $"run-{seed}", team);
                // Not measuring pictures, as the planner doesn't: the kit's check did.
                var problems = OfficeScenes.Problems(plan.Scene, sizeOf: null);

                if (problems.Count > 0 || plan.Attempt != 0)
                {
                    failures.Add($"team {team} seed {seed}: attempt {plan.Attempt}; first try: {string.Join(" / ", FloorPlanner.Faults(Tech, Rules, $"run-{seed}", team, 0))}; returned: {string.Join(" / ", problems)}");
                }

                // One desk each, the lead's first: a bench seats four, and nobody gets two.
                if (plan.Scene.Desks.Count != Math.Min(team, plan.Capacity))
                {
                    failures.Add($"team {team} seed {seed}: {plan.Scene.Desks.Count} desks");
                }
            }
        }

        failures.Should().BeEmpty("the office Loadout ships should never need a retry or the fallback");
    }

    // ---- floors teams share ----

    private static readonly OfficeTenant[][] Mixes =
    [
        [new("a", 0, 0, 1, 3)],
        [new("a", 0, 0, 1, 3), new("b", 0, 1, 1, 2), new("c", 0, 2, 1, 1)],
        [new("a", 0, 0, 2, 9), new("c", 0, 2, 1, 4)],
        [new("a", 0, 1, 1, 1)],
        [new("a", 1, 0, 3, 14)],
        [new("a", 0, 0, 1, 1), new("c", 0, 2, 1, 6)],
    ];

    [Fact]
    public void Every_shared_floor_passes_the_scene_check_first_time_and_seats_each_team_in_its_own_bays()
    {
        var failures = new List<string>();

        foreach (var kit in new[] { Kit, Tech })
        {
            var each = FloorPlanner.BayCapacity(kit, Rules);
            var bays = FloorPlanner.BayColumns(40, OfficeRules.BayCount);

            // And three full bays side by side, which only fit if each team
            // keeps to its own: a small team fits in its bay whatever happens.
            OfficeTenant[] full = [new("a", 0, 0, 1, each), new("b", 0, 1, 1, each), new("c", 0, 2, 1, each)];

            foreach (var number in Enumerable.Range(1, 12))
            {
                foreach (var mix in Mixes.Append(full))
                {
                    var plan = FloorPlanner.Shared(kit, Rules, number, mix);
                    var problems = OfficeScenes.Problems(plan.Scene, sizeOf: null);
                    var called = $"{(kit == Tech ? "tech" : "shapes")} floor {number} [{string.Join(", ", mix.Select(one => $"{one.Run}:{one.People}@{one.Bay}+{one.Bays}"))}]";

                    if (problems.Count > 0 || plan.Attempt != 0)
                    {
                        failures.Add($"{called}: attempt {plan.Attempt}: {string.Join(" / ", problems)}");
                    }

                    foreach (var tenant in mix)
                    {
                        var team = plan.Scene.Teams!.Single(one => one.Run == tenant.Run && one.Part == tenant.Part);
                        var (from, to) = (bays[tenant.Bay].From, bays[tenant.Bay + tenant.Bays - 1].To);

                        if (team.Desks.Count != Math.Min(tenant.People, each * tenant.Bays))
                        {
                            failures.Add($"{called}: {tenant.Run} has {team.Desks.Count} desks");
                        }

                        if (team.Desks.Any(desk => desk.X < from || desk.X > to))
                        {
                            failures.Add($"{called}: {tenant.Run} sits outside its bays");
                        }

                        if (plan.Scene.Areas!.Any(area => area.Run == tenant.Run && (area.X < from || area.X + area.W - 1 > to)))
                        {
                            failures.Add($"{called}: {tenant.Run}'s area reaches outside its bays");
                        }
                    }
                }
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void A_team_s_desks_are_where_they_were_whoever_else_comes_or_goes()
    {
        static IEnumerable<string> Own(OfficeScene scene) =>
            scene.Props!.Where(prop => prop.Id.StartsWith("a#0:", StringComparison.Ordinal)).Select(prop => $"{prop.Id}@{prop.X},{prop.Y}{prop.Facing}");

        foreach (var number in Enumerable.Range(1, 10))
        {
            var alone = FloorPlanner.Shared(Tech, Rules, number, [new("a", 0, 1, 1, 4)]).Scene;
            var crowded = FloorPlanner.Shared(Tech, Rules, number, [new("b", 0, 0, 1, 3), new("a", 0, 1, 1, 4), new("c", 0, 2, 1, 2)]).Scene;

            Own(crowded).Should().Equal(Own(alone), $"floor {number}: neighbours never move a team's furniture");
            crowded.Teams!.Single(one => one.Run == "a").Desks.Should().Equal(alone.Teams!.Single().Desks);
        }
    }

    [Fact]
    public void A_team_s_rooms_say_who_may_use_them_and_the_shared_rooms_say_nothing()
    {
        var scene = FloorPlanner.Shared(Tech, Rules, 4, [new("a", 0, 0, 1, 3), new("c", 0, 2, 1, 2)]).Scene;

        // The rules give the open plan and team rooms to the team, the office to
        // its lead; the page keeps everybody else out of them.
        scene.Areas!.Where(area => area.Run != null && area.Kind is "open-plan" or "team-room")
            .Should().NotBeEmpty().And.OnlyContain(area => area.Access == "team");
        scene.Areas!.Where(area => area.Kind == "lead-office")
            .Should().NotBeEmpty().And.OnlyContain(area => area.Access == "lead" && area.Run != null);

        // Rooms everybody may use name no access, so the scene only marks what is kept.
        scene.Areas!.Where(area => area.Kind is "kitchen" or "meeting" or "corridor" or "lounge")
            .Should().NotBeEmpty().And.OnlyContain(area => area.Access == null);
    }

    [Fact]
    public void Each_team_s_area_says_whose_it_is_and_every_bay_is_fitted_out()
    {
        var scene = FloorPlanner.Shared(Tech, Rules, 4, [new("a", 0, 0, 1, 3), new("c", 0, 2, 1, 2)]).Scene;
        var bays = FloorPlanner.BayColumns(scene.Width, OfficeRules.BayCount);

        // A team's areas carry its run; the rooms the floor shares carry none.
        scene.Areas!.Where(area => area.Kind is "team-room" or "lead-office" || area.Kind == "open-plan" && area.Run != null)
            .Select(area => area.Run).Distinct().Should().BeEquivalentTo(["a", "c"]);
        scene.Areas!.Where(area => area.Kind is "kitchen" or "meeting" or "corridor").Should().OnlyContain(area => area.Run == null, "shared rooms are nobody's own");

        // Every bay is fitted out: a team's with more desks than it has people,
        // the bay nobody has with desks nobody is given - a part-let office,
        // not bare concrete.
        foreach (var (from, to) in bays)
        {
            scene.Props!.Count(prop => prop.Kind == "desk" && prop.X >= from && prop.X <= to).Should().BeGreaterThan(2, $"the bay at {from}-{to} has desks");
        }

        scene.Areas!.Should().NotContain(area => area.Kind == "vacant" && area.W * area.H > 12, "no bay is left bare");

        var spare = bays[1];

        scene.Desks.Should().NotContain(seat => seat.X >= spare.From && seat.X <= spare.To, "nobody is given a desk in the bay nobody has");
        scene.Teams!.Single(team => team.Run == "a").Desks.Should().HaveCount(3);
        scene.Teams!.Single(team => team.Run == "c").Desks.Should().HaveCount(2);
        OfficeScenes.Problems(scene, sizeOf: null).Should().BeEmpty();
    }

    [Fact]
    public void The_building_has_its_facilities_each_with_the_piece_that_makes_it()
    {
        foreach (var kit in new[] { Kit, Tech })
        {
            var name = kit == Tech ? "tech" : "shapes";
            var lobby = FloorPlanner.Lobby(kit, Rules, 4).Scene;
            var roof = FloorPlanner.Roof(kit, Rules, 4).Scene;
            var basement = FloorPlanner.Basement(kit, Rules, 1).Scene;

            void Has(OfficeScene scene, string kind, string tag)
            {
                var area = scene.Areas!.Should().ContainSingle(one => one.Kind == kind, $"{name}: there is a {kind}").Subject;

                scene.Props!.Should().Contain(
                    prop => prop.Kind == tag && prop.X >= area.X && prop.X < area.X + area.W && prop.Y >= area.Y && prop.Y < area.Y + area.H,
                    $"{name}: the {kind} has its {tag}");
                OfficeScenes.Problems(scene, sizeOf: null).Should().BeEmpty($"{name}: the {kind}'s level still passes");
            }

            Has(lobby, "it-help", "help-desk");
            Has(roof, "gym", "treadmill");
            Has(basement, "bike-store", "bike-rack");
            Has(basement, "showers", "shower");

            FloorPlanner.Basement(kit, Rules, 2).Scene.Areas!.Should().NotContain(one => one.Kind == "bike-store", "bikes are on the first level down");
        }
    }

    [Fact]
    public void A_big_team_s_floor_has_facilities_furnished_and_meeting_rooms_come_first()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var seed in Enumerable.Range(0, 30).Select(one => $"run-{one}"))
        {
            var scene = FloorPlanner.Plan(Tech, Rules, seed, 12).Scene;
            var rooms = scene.Areas!.Where(area => area.Y == 1 && area.Kind is not ("core" or "corridor")).ToList();
            var meetings = rooms.Count(area => area.Kind == "meeting");

            // A team of twelve has three meeting rooms by the rules, before any facility.
            if (rooms.Any(area => area.Kind is "library" or "print-corner" or "phone-booth" or "wellness-room" or "training-room"))
            {
                meetings.Should().Be(3, $"{seed}: a facility never takes a meeting room's place");
            }

            string[] facilities = ["library", "print-corner", "phone-booth", "wellness-room", "training-room"];
            string[] makes = ["reading-chair", "stationery", "phone-booth", "yoga-mat", "training-desk"];

            foreach (var room in rooms.Where(area => facilities.Contains(area.Kind)))
            {
                var tag = makes[Array.IndexOf(facilities, room.Kind)];

                scene.Props!.Should().Contain(
                    prop => prop.Kind == tag && prop.X >= room.X && prop.X < room.X + room.W && prop.Y >= room.Y && prop.Y < room.Y + room.H,
                    $"{seed}: the {room.Kind} has its {tag}");
                seen.Add(room.Kind);
            }
        }

        seen.Should().NotBeEmpty("some floor of a team of twelve has room for a facility");
    }

    // ---- the building's own rooms ----

    // Three floors of three, laid out bottom to top the way the server does it:
    // each offered the building's rooms no floor below took.
    private static Dictionary<int, OfficeScene> Tower(int people, Func<int, OfficeBuildingRooms?, OfficeBuildingRooms?>? offer = null)
    {
        var floors = new Dictionary<int, OfficeScene>();
        int[] numbers = [1, 2, 3];

        foreach (var number in numbers)
        {
            var offered = FloorPlanner.BuildingRooms(Rules, number, numbers, people, below => floors.GetValueOrDefault(below));

            floors[number] = FloorPlanner.Shared(Tech, Rules, number, [new($"run-{number}", 0, 0, 1, 3)], offer is null ? offered : offer(number, offered)).Scene;
        }

        return floors;
    }

    [Fact]
    public void The_building_s_own_rooms_are_on_one_floor_each_the_lowest_with_room()
    {
        string[] own = ["library", "training-room", "wellness-room"];
        string[] makes = ["reading-chair", "training-desk", "yoga-mat"];
        var floors = Tower(people: 30);

        // Thirty in the building call for one of each; three on a floor would call for none.
        foreach (var kind in own)
        {
            var on = floors.Where(one => one.Value.Areas!.Any(area => area.Kind == kind)).Select(one => one.Key).ToList();

            on.Should().ContainSingle($"the building has one {kind}, not one a floor");

            // Every floor below it was offered it and had no room: one with room took it.
            var room = floors[on[0]].Areas!.Single(area => area.Kind == kind);
            var tag = makes[Array.IndexOf(own, kind)];

            floors[on[0]].Props!.Should().Contain(prop => prop.Kind == tag && prop.X >= room.X && prop.X < room.X + room.W && prop.Y >= room.Y && prop.Y < room.Y + room.H, $"the {kind} has its {tag}");
        }

        floors[1].Areas!.Should().Contain(area => own.Contains(area.Kind), "the first floor takes what fits before any floor above");

        foreach (var scene in floors.Values)
        {
            OfficeScenes.Problems(scene, sizeOf: null).Should().BeEmpty();
        }
    }

    [Fact]
    public void A_floor_offered_none_of_the_building_s_rooms_has_none_however_many_are_in()
    {
        var scene = FloorPlanner.Shared(Tech, Rules, 1, [new("a", 0, 0, 1, 9), new("b", 0, 1, 1, 9), new("c", 0, 2, 1, 9)]).Scene;

        scene.Areas!.Should().NotContain(area => area.Kind == "library" || area.Kind == "training-room" || area.Kind == "wellness-room", "they are the building's, offered by it");
        scene.Areas!.Should().Contain(area => area.Kind == "meeting", "the floor's own rooms are still counted from everybody on it");
    }

    [Fact]
    public void A_room_the_building_has_is_offered_only_to_floors_above_the_one_that_took_it()
    {
        // A first floor offered the library alone, which takes it.
        var floors = new Dictionary<int, OfficeScene>
        {
            [1] = FloorPlanner.Shared(Tech, Rules, 1, [new("a", 0, 0, 1, 3)], new OfficeBuildingRooms(["library"], 30)).Scene,
        };

        floors[1].Areas!.Should().Contain(area => area.Kind == "library");

        FloorPlanner.BuildingRooms(Rules, 1, [1, 2, 4], 30, floors.GetValueOrDefault)!.Kinds.Should().BeEquivalentTo(["library", "training-room", "wellness-room"], "nothing below the first floor");
        FloorPlanner.BuildingRooms(Rules, 2, [1, 2, 4], 30, floors.GetValueOrDefault)!.Kinds.Should().BeEquivalentTo(["training-room", "wellness-room"], "the first floor took the library");
        FloorPlanner.BuildingRooms(Rules, 4, [1, 2, 4], 30, floors.GetValueOrDefault)!.People.Should().Be(30, "counted from everybody in the building");
        FloorPlanner.BuildingRooms(Rules with { Rooms = new Dictionary<string, OfficeRoomRule>(StringComparer.Ordinal) }, 1, [1], 30, floors.GetValueOrDefault).Should().BeNull("rules with no building rooms offer nothing");
    }

    // ---- the outside, from the floor ----

    [Fact]
    public void A_storey_s_outside_is_solid_where_the_floor_has_a_wall_and_lit_where_a_team_is_in()
    {
        var wallsMet = 0;

        foreach (var number in Enumerable.Range(1, 10))
        {
            var scene = FloorPlanner.Shared(Tech, Rules, number, [new("a", 0, 0, 1, 3), new("c", 0, 2, 1, 2)]).Scene;
            var strips = OfficeFacadePlan.For(scene, run => run == "a");

            // One letter a bay: the long sides the floor's width, the short its depth.
            new[] { strips.S, strips.N, strips.LitS, strips.LitN }.Should().OnlyContain(one => one.Length == scene.Width);
            new[] { strips.E, strips.W, strips.LitE, strips.LitW }.Should().OnlyContain(one => one.Length == scene.Height);

            // The north wall is solid all along; the windows are glass between
            // the corners, and wherever a wall inside meets the side glass the
            // bay is solid too, so a room's wall is not glass from outside.
            strips.N.Should().MatchRegex("^s+$", $"floor {number}: the north wall is solid");
            strips.S[1..^1].Should().Contain("g");

            var first = scene.Atlases![0].Tiles;

            for (var y = 1; y < scene.Height - 1; y++)
            {
                var inside = scene.Walls![y][1];

                if (inside >= 0 && inside < first)
                {
                    strips.W[y].Should().Be('s', $"floor {number}: a wall meets the west glass at row {y}");
                    wallsMet++;
                }
            }

            // Lit behind team a's own area, never behind team c's, whose run is
            // not in, nor behind solid wall.
            var a = scene.Areas!.Where(area => area.Run == "a").ToList();
            var c = scene.Areas!.Where(area => area.Run == "c").ToList();

            for (var x = 1; x < scene.Width - 1; x++)
            {
                var lit = strips.LitS[x] == '1';

                if (c.Any(area => x >= area.X && x < area.X + area.W && area.Y + area.H == scene.Height - 1))
                {
                    lit.Should().BeFalse($"floor {number}: column {x} is behind a team that is not in");
                }

                if (strips.S[x] == 's')
                {
                    lit.Should().BeFalse($"floor {number}: column {x} is wall");
                }
            }

            strips.LitS.Should().Contain("1", $"floor {number}: team a is in");
            OfficeFacadePlan.For(scene, _ => false).LitS.Should().MatchRegex("^0+$", "nobody is in");
        }

        wallsMet.Should().BePositive("some floor has a wall meeting the west glass, or the check above proved nothing");
    }

    [Fact]
    public void Every_room_in_the_band_has_a_door_onto_the_corridor()
    {
        foreach (var team in new[] { 2, 8, 20 })
        {
            foreach (var seed in Enumerable.Range(0, 30).Select(one => $"run-{one}"))
            {
                var scene = FloorPlanner.Plan(Tech, Rules, seed, team).Scene;
                var corridor = scene.Areas!.Single(area => area.Kind == "corridor");
                var walls = scene.Blocked();

                foreach (var room in scene.Areas!.Where(area => area.Y == 1 && area.Kind is not ("core" or "corridor")))
                {
                    // An open cell in the row between the room and the corridor.
                    Enumerable.Range(room.X, room.W).Should().Contain(x => !walls[x, corridor.Y - 1], $"{seed}, team {team}: {room.Name} has a way in");
                }
            }
        }
    }

    [Fact]
    public void The_team_area_is_as_big_as_the_team_and_the_rest_of_the_floor_is_vacant()
    {
        foreach (var seed in Enumerable.Range(0, 20).Select(one => $"run-{one}"))
        {
            var small = FloorPlanner.Plan(Tech, Rules, seed, 2).Scene;
            var big = FloorPlanner.Plan(Tech, Rules, seed, 16).Scene;

            static OfficeArea Team(OfficeScene scene) => scene.Areas!.Single(area => area.Kind is "open-plan" or "team-room");

            Team(small).W.Should().BeLessThan(Team(big).W, $"{seed}: two people need less floor than sixteen");
            small.Areas!.Should().Contain(area => area.Kind == "vacant", $"{seed}: what two people don't need is left for another team");

            // Nothing stands on vacant floor, and nobody is sent there.
            foreach (var vacant in small.Areas!.Where(area => area.Kind == "vacant"))
            {
                bool Inside(int x, int y) => x >= vacant.X && x < vacant.X + vacant.W && y >= vacant.Y && y < vacant.Y + vacant.H;

                small.Props!.Where(prop => Inside(prop.X, prop.Y)).Should().BeEmpty($"{seed}: {vacant.Name} is bare");
                small.Spots!.Values.Where(spot => Inside(spot.X, spot.Y)).Should().BeEmpty($"{seed}: nobody goes to {vacant.Name}");
            }
        }
    }

    [Fact]
    public void Pieces_go_with_what_they_belong_with()
    {
        foreach (var seed in Enumerable.Range(0, 30).Select(one => $"run-{one}"))
        {
            var scene = FloorPlanner.Plan(Tech, Rules, seed, 6).Scene;
            var props = scene.Props!;

            static bool Touch(OfficeProp a, OfficeProp b) =>
                a.X <= b.X + b.W && b.X <= a.X + a.W && a.Y <= b.Y + b.H && b.Y <= a.Y + a.H;

            // The coffee machine at the counter.
            var counter = props.Single(prop => prop.Id == "kitchen-kitchen");

            props.Where(prop => prop.Kind == "coffee" && !Touch(prop, counter)).Should().BeEmpty($"{seed}: the coffee machine is at the counter");

            // The lead behind the desk, facing the office door, the visitor's
            // chair across it: the desk turned to face north, the lead's seat
            // below it, the chair above.
            var desk = props.Single(prop => prop.Kind == "exec-desk");
            var lead = scene.Desks[0];

            desk.Facing.Should().Be("n", $"{seed}: the lead faces the door");
            lead.Y.Should().Be(desk.Y + desk.H, $"{seed}: the lead sits behind the desk");
            lead.Facing.Should().Be("n");

            foreach (var chair in props.Where(prop => prop.Kind == "visitor-chair" && prop.Id.StartsWith("lead-office", StringComparison.Ordinal)))
            {
                (chair.Y + chair.H).Should().Be(desk.Y, $"{seed}: a visitor sits across the desk from the lead");
            }
        }
    }

    [Fact]
    public void Nothing_stands_in_the_corridor_but_what_belongs_there()
    {
        string[] belongs = ["toilet", "cupboard", "lift", "stairs", "exit", "status-board"];

        foreach (var seed in Enumerable.Range(0, 30).Select(one => $"run-{one}"))
        {
            var scene = FloorPlanner.Plan(Tech, Rules, seed, 8).Scene;
            var corridor = scene.Areas!.Single(area => area.Kind == "corridor");
            var suited = Tech.Pieces.Values.Where(piece => piece.Suits?.Fits("corridor", "floor") == true).SelectMany(piece => piece.Tags).ToHashSet();

            scene.Props!
                .Where(prop => prop.Y >= corridor.Y && prop.Y < corridor.Y + corridor.H && prop.X >= corridor.X && prop.X < corridor.X + corridor.W)
                .Should().OnlyContain(prop => belongs.Contains(prop.Kind) || suited.Contains(prop.Kind!), $"{seed}: the corridor is for walking along");
        }
    }

    [Fact]
    public void No_desk_is_ever_put_inside_another_room()
    {
        foreach (var team in new[] { 6, 10, 16, 30 })
        {
            foreach (var seed in Enumerable.Range(0, 40).Select(one => $"run-{one}"))
            {
                var scene = Plan(seed, team).Scene;
                // A team room off a corridor is where its desks belong.
                var rooms = scene.Areas!.Where(area => area.Kind is not ("open-plan" or "core" or "status-board" or "team-room")).ToList();

                foreach (var desk in scene.Props!.Where(prop => prop.Kind == "desk"))
                {
                    rooms.Should().NotContain(
                        room => desk.X < room.X + room.W && desk.X + desk.W > room.X && desk.Y < room.Y + room.H && desk.Y + desk.H > room.Y,
                        $"{seed}, team {team}: desk {desk.Id} at {desk.X},{desk.Y}");
                }
            }
        }
    }

    [Fact]
    public void Small_teams_get_corridor_floors_as_well_as_open_ones()
    {
        var kinds = Enumerable.Range(0, 40)
            .Select(one => Plan($"run-{one}", 3).Scene.Areas!)
            .Select(areas => areas.Any(area => area.Kind == "team-room") ? "corridor" : "open")
            .ToHashSet();

        kinds.Should().BeEquivalentTo(["corridor", "open"]);
    }

    [Fact]
    public void A_corridor_floor_seats_its_team_in_rooms_off_the_corridor()
    {
        var plan = Enumerable.Range(0, 40)
            .Select(one => Plan($"run-{one}", 4))
            .First(one => one.Scene.Areas!.Any(area => area.Kind == "team-room"));
        var rooms = plan.Scene.Areas!.Where(area => area.Kind == "team-room").ToList();

        rooms.Should().NotBeEmpty();
        plan.Scene.Desks.Should().HaveCount(4);

        // Every seat but the lead's is inside a team room.
        plan.Scene.Desks.Skip(1).Should().OnlyContain(seat =>
            rooms.Any(room => seat.X >= room.X && seat.X < room.X + room.W && seat.Y >= room.Y && seat.Y < room.Y + room.H));
    }

    [Fact]
    public void A_corridor_floor_only_ever_goes_to_a_team_it_seats()
    {
        // A team too big for one is given open plan, which seats more.
        foreach (var team in new[] { 3, 6, 10, 16, 24, 30, 40 })
        {
            foreach (var plan in Enumerable.Range(0, 40).Select(one => Plan($"run-{one}", team)))
            {
                if (plan.Scene.Areas!.Any(area => area.Kind == "team-room"))
                {
                    plan.Capacity.Should().BeGreaterThanOrEqualTo(team);
                }
            }
        }
    }

    [Fact]
    public void Rooms_can_be_partitioned_by_screens_and_planters_as_well_as_walls()
    {
        var kinds = Enumerable.Range(0, 60)
            .SelectMany(one => new[] { Plan($"run-{one}", 3), Plan($"run-{one}", 8) })
            .SelectMany(plan => plan.Scene.Props!)
            .Select(prop => prop.Kind)
            .ToHashSet();

        kinds.Should().Contain("partition-screen").And.Contain("partition-planter");
    }

    [Fact]
    public void Rules_refuse_a_layout_or_partition_the_planner_does_not_know()
    {
        var broken = Rules.With(new OfficeRules(
            OfficeRules.Version,
            Rooms: new Dictionary<string, OfficeRoomRule> { ["lounge"] = new(["sofa"], Walls: ["hedge"]) },
            Layouts: ["maze"]));

        OfficeRuleBook.Problems(broken).Should().BeEquivalentTo(
            "layouts names 'maze'; a floor is laid out as one of open, corridor.",
            "room 'lounge' has walls 'hedge'; they have to be one of solid, glass, screen, planters, open.");
    }

    [Fact]
    public void Rooms_carry_what_they_are_for_on_the_dashboard()
    {
        var areas = Plan("run-c", 10).Scene.Areas!;

        areas.Single(area => area.Kind == "lead-office").Function.Should().Be("controls");
        areas.Where(area => area.Kind == "meeting").Should().OnlyContain(area => area.Function == "questions");
        areas.Single(area => area.Kind == "status-board").Function.Should().Be("summary");
    }

    [Fact]
    public void People_arrive_by_the_lift()
    {
        var scene = Plan("run-d", 4).Scene;

        scene.Door.Should().Be(scene.Spots!["lift"] with { Facing = "s" });
    }

    [Fact]
    public void The_same_run_always_gets_the_same_floor_and_runs_differ()
    {
        string Json(string seed) => JsonSerializer.Serialize(Plan(seed, 8).Scene);

        Json("run-same").Should().Be(Json("run-same"));

        Enumerable.Range(0, 20).Select(one => Json($"run-{one}")).Distinct().Count()
            .Should().BeGreaterThan(1, "seeds choose sides, walls and tables");
    }

    [Fact]
    public void A_pack_with_narrower_desks_seats_more_and_still_passes()
    {
        var narrow = Kit with
        {
            Pieces = new Dictionary<string, OfficePiece>(Kit.Pieces)
            {
                ["desk"] = new(null, null, [2, 1], ["desk"], Seats: [new OfficeSeat(0, -1, "s")]),
            },
        };

        var plan = FloorPlanner.Plan(narrow, Rules, "run-e", 40);

        OfficeScenes.Problems(plan.Scene, _ => null).Should().BeEmpty();
        plan.Capacity.Should().BeGreaterThan(FloorPlanner.Capacity(Kit, Rules));
    }

    [Fact]
    public void A_floor_too_small_for_the_band_falls_back_to_plain_open_plan_that_still_works()
    {
        var tiny = Rules.With(new OfficeRules(OfficeRules.Version, Floor: [12, 8]));
        var plan = FloorPlanner.Plan(Kit, tiny, "run-f", 3);

        plan.Attempt.Should().Be(-1);
        OfficeScenes.Problems(plan.Scene, _ => null).Should().BeEmpty();
        plan.Scene.Desks.Should().NotBeEmpty();
    }

    [Fact]
    public void The_generated_floor_uses_the_kits_tilesets_one_atlas_per_wall_kind()
    {
        var scene = Plan("run-g", 4).Scene;

        scene.Atlases.Should().HaveCount(2);
        scene.Atlases![0].Upper.Should().Be("wall");
        scene.Atlases[1].Upper.Should().Be("glass");
        scene.Tile.Should().Be(32);
    }
}
