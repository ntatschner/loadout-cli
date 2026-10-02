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
    public void Every_floor_has_the_lead_office_in_a_corner_a_kitchen_a_core_and_a_status_board()
    {
        foreach (var seed in Enumerable.Range(0, 50).Select(one => $"run-{one}"))
        {
            var scene = Plan(seed, 6).Scene;
            var lead = scene.Areas!.Single(area => area.Kind == "lead-office");

            (lead.X == 1 || lead.X + lead.W == scene.Width - 1).Should().BeTrue($"{seed}: the lead's office is in a corner");
            lead.Y.Should().Be(1);
            scene.Areas.Should().Contain(area => area.Kind == "kitchen")
                .And.Contain(area => area.Kind == "core")
                .And.Contain(area => area.Kind == "status-board");
            scene.Props!.Should().Contain(prop => prop.Kind == "lift")
                .And.Contain(prop => prop.Kind == "exec-desk")
                .And.Contain(prop => prop.Kind == "coffee");
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
            .Select(areas => areas.Any(area => area.Kind == "corridor") ? "corridor" : "open")
            .ToHashSet();

        kinds.Should().BeEquivalentTo(["corridor", "open"]);
    }

    [Fact]
    public void A_corridor_floor_seats_its_team_in_rooms_off_the_corridor()
    {
        var plan = Enumerable.Range(0, 40)
            .Select(one => Plan($"run-{one}", 4))
            .First(one => one.Scene.Areas!.Any(area => area.Kind == "corridor"));
        var rooms = plan.Scene.Areas!.Where(area => area.Kind == "team-room").ToList();

        rooms.Should().NotBeEmpty();
        plan.Scene.Desks.Should().HaveCount(4);

        // Every seat but the lead's is inside a team room.
        plan.Scene.Desks.Skip(1).Should().OnlyContain(seat =>
            rooms.Any(room => seat.X >= room.X && seat.X < room.X + room.W && seat.Y >= room.Y && seat.Y < room.Y + room.H));
    }

    [Fact]
    public void A_team_too_big_for_a_corridor_floor_gets_open_plan()
    {
        // Thirty is more than a corridor floor of the 40 x 24 plate seats.
        Enumerable.Range(0, 40)
            .Select(one => Plan($"run-{one}", 30).Scene.Areas!)
            .Should().OnlyContain(areas => !areas.Any(area => area.Kind == "corridor"));

        // And at every size, a corridor floor only ever goes to a team it seats.
        foreach (var team in new[] { 3, 6, 10, 16, 24, 30, 40 })
        {
            foreach (var plan in Enumerable.Range(0, 40).Select(one => Plan($"run-{one}", team)))
            {
                if (plan.Scene.Areas!.Any(area => area.Kind == "corridor"))
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
