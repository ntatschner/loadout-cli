using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The parts the building is generated from, and the rules it is laid out by.
/// </summary>
/// <remarks>
/// A kit is a pack's box of parts and the rules are the design decisions as
/// data. Both come from somebody's own directory, so both are checked before the
/// planner is allowed near them, and each check is pinned by something that
/// breaks it, with the message naming the part.
/// </remarks>
public sealed class OfficeKitTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "loadout-kit-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private static readonly Func<string, (int Width, int Height)?> NoPictures = _ => null;

    private static OfficeKit Small(params (string Name, OfficePiece Piece)[] pieces) =>
        OfficeKit.Kit() with
        {
            Pieces = pieces.ToDictionary(one => one.Name, one => one.Piece, StringComparer.Ordinal),
        };

    // ---- where pieces suit ----

    [Fact]
    public void A_piece_suits_the_rooms_and_levels_it_names_and_any_where_it_names_none()
    {
        var sofa = new OfficeSuits(["lounge", "waiting-room"], ["floor", "lobby"], "main");

        sofa.Fits("lounge", "floor").Should().BeTrue();
        sofa.Fits("waiting-room", "lobby").Should().BeTrue();
        sofa.Fits("kitchen", "floor").Should().BeFalse("it names the rooms it suits");
        sofa.Fits("lounge", "roof").Should().BeFalse("it names the levels it suits");

        new OfficeSuits(["*"]).Fits("kitchen", "basement-2").Should().BeTrue("* is any room, and no levels any level");
        new OfficeSuits(["kitchen"], ["*"]).Fits("kitchen", "roof").Should().BeTrue();
    }

    [Fact]
    public void Where_a_piece_suits_is_checked_by_name()
    {
        static OfficePiece Suiting(OfficeSuits suits) => new(null, null, [1, 1], ["thing"], Suits: suits);

        var kit = Small(
            ("good", Suiting(new OfficeSuits(["kitchen", "corridor", "*"], ["floor", "*"], "extra", "wall", ["kitchen"]))),
            ("odd-role", Suiting(new OfficeSuits(["kitchen"], Role: "hero"))),
            ("odd-back", Suiting(new OfficeSuits(["kitchen"], Against: "ceiling"))),
            ("no-rooms", Suiting(new OfficeSuits([]))),
            ("misspelt", Suiting(new OfficeSuits(["kitchenn"], ["attic"]))));

        OfficeKits.Problems(kit, NoPictures).Should().BeEquivalentTo(
            "piece 'odd-role' suits a room as 'hero'; it has to be one of main, extra, divider, decor, clutter.",
            "piece 'odd-back' stands against 'ceiling'; it has to be one of wall, window, free.",
            "piece 'no-rooms' suits no room; give it rooms, or * for any.");

        // Room and level names are the rules', so they are checked against them.
        OfficeKits.Unsuited(kit, OfficeRules.Default).Should().Equal(
            "piece 'misspelt' suits room 'kitchenn', which the rules have no room called.",
            "piece 'misspelt' suits level 'attic'; it has to be one of floor, lobby, roof, basement-1, basement-2, or *.");

        // A room a set's own rules add is one a piece may suit.
        var added = OfficeRules.Default.With(new OfficeRules(OfficeRules.Version, Rooms: new Dictionary<string, OfficeRoomRule>(StringComparer.Ordinal)
        {
            ["kitchenn"] = new(["thing"]),
        }));

        OfficeKits.Unsuited(kit, added).Should().Equal(
            "piece 'misspelt' suits level 'attic'; it has to be one of floor, lobby, roof, basement-1, basement-2, or *.");
    }

    // ---- rules ----

    [Fact]
    public void A_rooms_place_on_the_floor_is_checked_by_name()
    {
        var rules = OfficeRules.Default.With(new OfficeRules(OfficeRules.Version, Rooms: new Dictionary<string, OfficeRoomRule>(StringComparer.Ordinal)
        {
            ["booth"] = new(
                ["booth"],
                PerHead: 0,
                Scope: "everyone",
                Zone: "basement",
                Near: new Dictionary<string, int>(StringComparer.Ordinal) { ["open-plan"] = 3, ["atrium"] = 2, ["corridor"] = 11 },
                Away: new Dictionary<string, int>(StringComparer.Ordinal) { ["kitchen"] = 0 },
                Access: "window"),
            ["quiet"] = new(
                ["booth"],
                PerHead: 6,
                Scope: "floor",
                Zone: "interior",
                Near: new Dictionary<string, int>(StringComparer.Ordinal) { ["core"] = 1 },
                Away: new Dictionary<string, int>(StringComparer.Ordinal) { ["kitchen"] = 10 },
                Access: "through"),
        }));

        OfficeRuleBook.Problems(rules).Should().BeEquivalentTo(
            "room 'booth' per-head is 0; it has to be one room for every 1 or more people.",
            "room 'booth' scope is 'everyone'; it has to be one of floor, team.",
            "room 'booth' zone is 'basement'; it has to be one of core, perimeter, interior, corner, entrance, any.",
            "room 'booth' access is 'window'; it has to be one of corridor, open, through.",
            "room 'booth' near names 'atrium', which is no room.",
            "room 'booth' near gives 'corridor' 11; it has to be 1 to 10.",
            "room 'booth' away gives 'kitchen' 0; it has to be 1 to 10.");
    }

    [Fact]
    public void The_built_in_rules_are_sound()
    {
        OfficeRuleBook.Problems(OfficeRules.Default).Should().BeEmpty();
    }

    [Fact]
    public void A_storey_is_five_tiles_unless_a_set_says_otherwise_and_never_one_nobody_could_stand_in()
    {
        // Three quarters of a metre a tile, so five is 3.75 metres floor to floor.
        OfficeRules.Default.Storey.Should().BeNull();
        OfficeRules.StoreyTiles.Should().Be(5);

        // A set's own height is kept when its rules are laid over the built-in ones.
        OfficeRules.Default.With(OfficeRules.Default with { Storey = 6 }).Storey.Should().Be(6);
        OfficeRules.Default.With(new OfficeRules(OfficeRules.Version)).Storey.Should().BeNull();

        OfficeRuleBook.Problems(OfficeRules.Default with { Storey = 2 }).Should().Contain("storey is 2 tiles; it has to be 3 to 10.");
        OfficeRuleBook.Problems(OfficeRules.Default with { Storey = 7 }).Should().BeEmpty();
    }

    [Fact]
    public void The_built_in_rules_are_the_decisions_made()
    {
        var rules = OfficeRules.Default;

        rules.Floor.Should().Equal(40, 24);
        rules.MinFloors.Should().Be(10);
        rules.Moves.Should().Be(new OfficeMoves(20, 300, 60));
        rules.Rooms!["mail-room"].Level.Should().Be("basement-1");
        rules.Rooms["garbage"].Function.Should().Be("bin");
        rules.Rooms["lead-office"].Where.Should().Be("corner");
        rules.Use!["asks-you"].Should().Be("stay", "somebody who needs something keeps their bubble where they are");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(9, 2)]
    [InlineData(10, 3)]
    [InlineData(40, 3)]
    public void Meeting_rooms_come_from_the_table_by_team_size(int team, int rooms)
    {
        OfficeRules.Default.Rooms!["meeting"].CountFor(team).Should().Be(rooms);
    }

    [Fact]
    public void A_room_without_a_table_uses_its_count()
    {
        new OfficeRoomRule(["exit"], Count: 2).CountFor(7).Should().Be(2);
        new OfficeRoomRule(["lift"]).CountFor(7).Should().Be(1);
    }

    [Fact]
    public void A_pack_replaces_the_rooms_it_names_and_keeps_the_rest()
    {
        var changes = new OfficeRules(
            OfficeRules.Version,
            Rooms: new Dictionary<string, OfficeRoomRule> { ["cupboard"] = new(["cupboard"], Count: 0) },
            Use: new Dictionary<string, string> { ["free"] = "kitchen" });

        var merged = OfficeRules.Default.With(changes);

        merged.Rooms!["cupboard"].CountFor(3).Should().Be(0);
        merged.Rooms["meeting"].Should().Be(OfficeRules.Default.Rooms!["meeting"]);
        merged.Use!["free"].Should().Be("kitchen");
        merged.Use["done"].Should().Be("lift");
        merged.Floor.Should().Equal(40, 24);
    }

    [Fact]
    public void Broken_rules_are_named_room_by_room()
    {
        var broken = OfficeRules.Default.With(new OfficeRules(
            OfficeRules.Version,
            Floor: [4, 4],
            Rooms: new Dictionary<string, OfficeRoomRule>
            {
                ["meeting"] = new(["meeting-6"], Min: [5, 5], Max: [3, 3], ByTeam: new Dictionary<string, int> { ["zero"] = 1 }),
                ["attic"] = new(["hay"], Level: "loft"),
            },
            Use: new Dictionary<string, string> { ["free"] = "pub" }));

        OfficeRuleBook.Problems(broken).Should().BeEquivalentTo(
            "floor has to be a width of 12 to 64 tiles and a depth of 8 to 64.",
            "room 'meeting' max 3x3 is smaller than its min 5x5.",
            "room 'meeting' by-team has 'zero'; each key has to be a team size of 1 or more.",
            "room 'attic' is on level 'loft'; it has to be one of floor, lobby, roof, basement-1, basement-2.",
            "use sends 'free' to 'pub', and there is no such room.");
    }

    [Fact]
    public void A_sets_rules_json_is_laid_over_the_built_in_ones()
    {
        var set = Path.Combine(_root, "tower");

        Directory.CreateDirectory(set);
        File.WriteAllText(Path.Combine(set, OfficeRules.FileName), """{"schema":"loadout.rules/1","min-floors":12}""");

        var (rules, problems) = OfficeRuleBook.Read(_root, "tower");

        problems.Should().BeEmpty();
        rules.MinFloors.Should().Be(12);
        rules.Rooms.Should().ContainKey("mail-room");
    }

    // ---- kit ----

    [Fact]
    public void The_built_in_kit_is_sound_and_has_a_part_for_every_tag_the_rules_use()
    {
        OfficeKits.Problems(OfficeKit.Kit(), NoPictures).Should().BeEmpty();
        OfficeKits.Missing(OfficeKit.Kit(), OfficeRules.Default).Should().BeEmpty();
    }

    [Fact]
    public void There_are_sixteen_corner_patterns_and_all_differ()
    {
        OfficeKit.CornerPatterns.Should().HaveCount(16).And.OnlyHaveUniqueItems();
        OfficeKit.CornerPatterns.Should().Contain("llll").And.Contain("uuuu").And.Contain("lllu");
    }

    [Fact]
    public void A_pack_missing_parts_lists_what_the_built_in_kit_will_draw()
    {
        var kit = Small(("desk-a", new OfficePiece(null, null, [3, 1], ["desk"])));

        OfficeKits.Problems(kit, NoPictures).Should().BeEmpty();
        OfficeKits.Missing(kit, OfficeRules.Default).Should().Contain("exec-desk").And.NotContain("desk");
    }

    [Fact]
    public void A_piece_with_its_sides_is_checked_and_carried_onto_the_floor()
    {
        // Sides are what lets a floor of pictures turn: each has to be a real
        // place in the picture, named by the way the piece's front faces.
        var sides = new Dictionary<string, IReadOnlyList<int>>
        {
            ["n"] = [0, 0, 96, 64], ["e"] = [96, 0, 32, 64], ["s"] = [128, 0, 96, 64], ["w"] = [224, 0, 32, 64],
        };
        var good = new OfficePiece("desk.png", sides["s"], [3, 1], ["desk"], Seats: [new(1, -1, "s")], Sides: sides);
        var kit = Small(("desk-a", good));
        Func<string, (int Width, int Height)?> pictures = piece => piece == "desk.png" ? (256, 64) : null;

        OfficeKits.Problems(kit, pictures).Should().BeEmpty();
        FloorPlanner.Plan(kit, OfficeRules.Default, "run-a", 3).Scene.Props!
            .Where(prop => prop.Kind == "desk").Should().OnlyContain(prop => prop.Sides != null && prop.Sides.Count == 4 && prop.Facing == "s");

        var odd = new Dictionary<string, IReadOnlyList<int>>(sides) { ["up"] = [0, 0, 8, 8], ["w"] = [240, 0, 32, 64] };

        OfficeKits.Problems(Small(("desk-a", good with { Sides = odd })), pictures).Should().BeEquivalentTo(
        [
            "piece 'desk-a' has a side 'up'; sides are n, e, s, w, the way its front faces.",
            "piece 'desk-a' side w is not inside 'desk.png', which is 256x64.",
        ]);
    }

    [Fact]
    public void A_tileset_short_of_a_corner_pattern_says_which()
    {
        var corners = OfficeKit.CornerPatterns.Where(one => one != "ullu").ToDictionary(one => one, _ => 0);
        var kit = Small() with
        {
            Tilesets = new Dictionary<string, OfficeTileset> { ["carpet-wall"] = new(null, "carpet", "wall", corners) },
        };

        OfficeKits.Problems(kit, NoPictures).Should().ContainSingle()
            .Which.Should().Be("tileset 'carpet-wall' has no tile for corner pattern ullu.");
    }

    [Fact]
    public void Seats_on_sofas_benches_and_at_tables_are_for_sitting_and_desks_and_standing_places_are_not()
    {
        // The page sits somebody with nothing to do in a seat that says so,
        // rather than standing them in front of a sofa. A desk's seat leaves it
        // to what its sitter is doing; a place to stand stays a place to stand.
        var kit = OfficeKit.Kit();
        var rules = OfficeRules.Default;
        var lobby = FloorPlanner.Lobby(kit, rules, 6).Scene;
        var roof = FloorPlanner.Roof(kit, rules, 6).Scene;
        var floor = FloorPlanner.Plan(kit, rules, "run-a", 8).Scene;

        lobby.Desks.Should().NotBeEmpty().And.OnlyContain(seat => seat.Sit, "the lobby's seats are its sofas");
        roof.Desks.Should().NotBeEmpty().And.OnlyContain(seat => seat.Sit, "the roof's seats are its benches");
        floor.Desks.Should().NotBeEmpty().And.OnlyContain(seat => !seat.Sit);
        floor.Spots!.Where(spot => spot.Key.StartsWith("meeting-", StringComparison.Ordinal)).Should().NotBeEmpty()
            .And.OnlyContain(spot => spot.Value.Sit, "a meeting table's places are chairs");
        floor.Spots!.Where(spot => spot.Key.StartsWith("kitchen-", StringComparison.Ordinal)).Should().NotBeEmpty()
            .And.OnlyContain(spot => !spot.Value.Sit, "the kitchen's places are for standing with a cup");
    }

    [Fact]
    public void An_extra_drawn_taller_than_its_place_stands_clear_of_the_pieces_behind_it()
    {
        // A cafe table 58 pixels tall on a 32 pixel row is drawn up into the
        // row behind it; put in front of the kitchen counter, it stood in the
        // counter. The row its picture reaches has to have no piece in it.
        var table = new OfficePiece("table.png", [0, 0, 64, 58], [2, 1], ["kitchen-table"]);
        var kit = Small(("kitchen-table", table));
        var placed = 0;

        foreach (var team in new[] { 3, 5, 8, 12 })
        {
            foreach (var seed in new[] { "run-a", "run-b", "run-c", "run-d", "run-e" })
            {
                var plan = FloorPlanner.Plan(kit, OfficeRules.Default, seed, team);
                var props = plan.Scene.Props!;

                plan.Attempt.Should().BeGreaterThanOrEqualTo(0, $"team {team}, {seed} came out plain");

                foreach (var one in props.Where(prop => prop.Kind == "kitchen-table"))
                {
                    placed++;
                    props.Where(other => other != one && other.X < one.X + one.W && other.X + other.W > one.X && other.Y < one.Y && other.Y + other.H > one.Y - 1)
                        .Select(other => other.Id).Should().BeEmpty($"team {team}, {seed}: the table at {one.X},{one.Y} is drawn over them");
                }
            }
        }

        placed.Should().BePositive("a kitchen has room for a table somewhere clear of its counter");
    }

    [Fact]
    public void A_room_takes_its_extras_from_the_sets_kit_where_they_fit_and_never_in_anybodys_way()
    {
        // The kitchen's fridge and cooler and the lounge's armchair are extras:
        // the room's own piece first, then each extra the set has, inside the
        // room, off every seat and place to stand. The scene check walks the
        // floor from the door, so a floor planned from a seeded try (not the
        // fallback) has every place still reachable.
        var kit = Small(
            ("fridge", new OfficePiece(null, null, [1, 1], ["fridge"])),
            ("cooler", new OfficePiece(null, null, [1, 1], ["cooler"])),
            ("armchair", new OfficePiece(null, null, [1, 1], ["armchair"])));
        var lounges = 0;

        foreach (var team in new[] { 3, 5, 8, 12 })
        {
            foreach (var seed in new[] { "run-a", "run-b", "run-c", "run-d" })
            {
                var scene = FloorPlanner.Plan(kit, OfficeRules.Default, seed, team) is var plan && plan.Attempt >= 0
                    ? plan.Scene
                    : throw new InvalidOperationException($"team {team}, {seed} came out plain");
                var kitchen = scene.Areas!.Single(area => area.Kind == "kitchen");
                var places = scene.Spots!.Values.Concat(scene.Desks).ToList();

                bool In(OfficeProp prop, OfficeArea area) =>
                    prop.X >= area.X && prop.Y >= area.Y && prop.X + prop.W <= area.X + area.W && prop.Y + prop.H <= area.Y + area.H;

                var extras = scene.Props!.Where(prop => prop.Kind == "fridge" || prop.Kind == "cooler" || prop.Kind == "armchair").ToList();

                extras.Count(prop => prop.Kind != "armchair").Should().Be(2, $"team {team}, {seed}");
                extras.Where(prop => prop.Kind != "armchair").All(prop => In(prop, kitchen)).Should().BeTrue($"team {team}, {seed}: in the kitchen");
                extras.Any(prop => places.Any(place => place.X >= prop.X && place.X < prop.X + prop.W && place.Y >= prop.Y && place.Y < prop.Y + prop.H))
                    .Should().BeFalse($"team {team}, {seed}: on somebody's place");

                foreach (var lounge in scene.Areas!.Where(area => area.Kind == "lounge"))
                {
                    lounges++;
                    extras.Any(prop => prop.Kind == "armchair" && In(prop, lounge)).Should().BeTrue($"team {team}, {seed}: an armchair in {lounge.Name}");
                }
            }
        }

        lounges.Should().BePositive("some of these floors have a lounge to put an armchair in");

        // A kitchen asked for more fridges than it has room for takes what fits
        // and no more: none where somebody stands, and the floor still planned
        // from a seeded try, so every place in it is still reachable from the door.
        var crowded = OfficeRules.Default with
        {
            Rooms = new Dictionary<string, OfficeRoomRule>(OfficeRules.Default.Rooms!)
            {
                ["kitchen"] = OfficeRules.Default.Rooms!["kitchen"] with { Extras = [.. Enumerable.Repeat("fridge", 20)] },

                // Its seats are round the table, away from the door: packed in
                // without a care, the fridges would wall them off.
                ["meeting"] = OfficeRules.Default.Rooms!["meeting"] with { Extras = [.. Enumerable.Repeat("fridge", 20)] },
            },
        };

        foreach (var seed in new[] { "run-a", "run-b", "run-c", "run-d" })
        {
            var plan = FloorPlanner.Plan(kit, crowded, seed, 5);
            var places = plan.Scene.Spots!.Values.Concat(plan.Scene.Desks).ToList();
            var fridges = plan.Scene.Props!.Where(prop => prop.Kind == "fridge").ToList();

            plan.Attempt.Should().BeGreaterThanOrEqualTo(0, $"{seed} came out plain");
            fridges.Count.Should().BeGreaterThan(2, $"{seed}: a crowded kitchen still takes what fits");
            fridges.Any(prop => places.Any(place => place.X == prop.X && place.Y == prop.Y)).Should().BeFalse($"{seed}: a fridge where somebody stands");
        }

        // The built-in kit has no extras: its floors are as they were.
        FloorPlanner.Plan(OfficeKit.Kit(), OfficeRules.Default, "run-a", 8).Scene.Props!
            .Select(prop => prop.Kind).Should().NotIntersectWith(["fridge", "cooler", "kitchen-table", "armchair", "coffee-table", "beanbag"]);
    }

    [Fact]
    public void A_kit_of_pictures_is_planned_as_the_built_in_one_is_rather_than_falling_back_to_open_plan()
    {
        // The planner has no pictures to measure; the kit check measured them.
        // Checking each try's floor for pictures it cannot see refused every
        // try with art in it, and every floor of an art kit came out plain:
        // no meeting rooms and no kitchen.
        var desk = new OfficePiece("desk.png", [0, 0, 96, 64], [3, 1], ["desk"], Seats: [new(1, -1, "s")]);
        var kit = Small(("desk-a", desk));

        foreach (var team in new[] { 1, 3, 5, 8 })
        {
            foreach (var seed in new[] { "run-a", "run-b", "run-c" })
            {
                var plan = FloorPlanner.Plan(kit, OfficeRules.Default, seed, team);

                plan.Attempt.Should().BeGreaterThanOrEqualTo(0, $"team {team}, {seed} came out plain");
                plan.Scene.Areas!.Select(area => area.Kind).Should().Contain(["meeting", "kitchen"], $"team {team}, {seed}");
                if (team > 1)
                {
                    plan.Scene.Props!.Should().Contain(prop => prop.Piece == "desk.png", $"team {team}, {seed}");
                }
            }
        }
    }

    [Fact]
    public void A_tile_picture_carries_its_corners_onto_the_floor_and_they_are_checked()
    {
        // A picture's tiles are wherever its kit put them, so the page can
        // only turn the floor with the kit's corners in hand. The built-in
        // atlas's tile numbers are its patterns, and it carries none.
        var shuffled = OfficeKit.CornerPatterns.Select((pattern, bits) => (pattern, bits)).ToDictionary(one => one.pattern, one => (one.bits * 5 + 3) % 16);
        var small = Small();
        var kit = small with
        {
            Tilesets = new Dictionary<string, OfficeTileset>(small.Tilesets) { ["carpet-wall"] = new("walls.png", "carpet", "wall", shuffled) },
        };

        var scene = FloorPlanner.Plan(kit, OfficeRules.Default, "run-a", 3).Scene;

        scene.Atlases![0].Corners.Should().BeEquivalentTo(shuffled);
        scene.Atlases[1].Corners.Should().BeNull();

        var wrong = new Dictionary<string, int>(shuffled) { ["uuuu"] = 16, ["sideways"] = 0 };

        wrong.Remove("llll");
        OfficeScenes.Problems(scene with { Atlases = [scene.Atlases[0] with { Corners = wrong }, scene.Atlases[1]] }, _ => (128, 128))
            .Should().BeEquivalentTo(
            [
                "atlas 'walls.png' gives no tile for the corners llll.",
                "atlas 'walls.png' names corners 'sideways'; a pattern is four of l and u.",
                "atlas 'walls.png' puts corners 'uuuu' at tile 16, outside its 16.",
            ]);
    }

    [Fact]
    public void A_floor_of_tile_pictures_is_drawn_on_the_dual_grid_chosen_by_the_four_cells_round_each_point()
    {
        // Each tile of the dual grid sits where four cells meet and shows
        // their corners, so a wall one cell wide is drawn one cell wide. The
        // expected tiles are worked out here from the scene's own walls.
        var shuffled = OfficeKit.CornerPatterns.Select((pattern, bits) => (pattern, bits)).ToDictionary(one => one.pattern, one => (one.bits * 5 + 3) % 16);
        var small = Small();
        var kit = small with
        {
            Tilesets = new Dictionary<string, OfficeTileset>(small.Tilesets)
            {
                ["carpet-wall"] = new("walls.png", "carpet", "wall", shuffled),
                ["carpet-glass"] = new("glass.png", "carpet", "glass", shuffled),
            },
        };

        foreach (var (seed, team) in new[] { ("run-a", 3), ("run-b", 8), ("run-c", 14) })
        {
            var scene = FloorPlanner.Plan(kit, OfficeRules.Default, seed, team).Scene;
            var solidTiles = scene.Atlases![0].Tiles;

            // 0 carpet, 1 solid, 2 glass, as the walls say, the edge carried on past it.
            int Kind(int x, int y)
            {
                var tile = scene.Walls![Math.Clamp(y, 0, scene.Height - 1)][Math.Clamp(x, 0, scene.Width - 1)];

                return tile < 0 ? 0 : tile < solidTiles ? 1 : 2;
            }

            scene.Surface.Should().NotBeNull().And.HaveCount(scene.Height + 1);
            scene.Surface!.Should().OnlyContain(row => row.Count == scene.Width + 1);

            var wrong = new List<string>();

            for (var y = 0; y <= scene.Height; y++)
            {
                for (var x = 0; x <= scene.Width; x++)
                {
                    var want = shuffled["llll"];

                    // Solid where any of the four is, glass counting as wall
                    // with it; glass only where there is no solid.
                    var four = new[] { (x - 1, y - 1), (x, y - 1), (x - 1, y), (x, y) }.Select(at => Kind(at.Item1, at.Item2)).ToList();

                    if (four.Contains(1))
                    {
                        want = shuffled[string.Concat(four.Select(kind => kind != 0 ? 'u' : 'l'))];
                    }
                    else if (four.Contains(2))
                    {
                        want = solidTiles + shuffled[string.Concat(four.Select(kind => kind == 2 ? 'u' : 'l'))];
                    }

                    if (scene.Surface[y][x] != want)
                    {
                        wrong.Add($"{x},{y}");
                    }
                }
            }

            wrong.Should().BeEmpty($"{seed}, team {team}");
            OfficeScenes.Problems(scene, sizeOf: null).Should().BeEmpty();
            OfficeScenes.Problems(scene with { Surface = scene.Surface.Skip(1).ToList() }, sizeOf: null).Should()
                .Contain($"surface has {scene.Height} rows; it is one more than the room, {scene.Height + 1}.");
        }

        // The built-in shapes are drawn cell by cell, as they always were.
        FloorPlanner.Plan(OfficeKit.Kit(), OfficeRules.Default, "run-a", 8).Scene.Surface.Should().BeNull();
    }

    [Fact]
    public void A_tile_past_the_end_of_the_atlas_is_named()
    {
        var corners = OfficeKit.CornerPatterns.ToDictionary(one => one, one => one == "uuuu" ? 16 : 0);
        var kit = Small() with
        {
            Tilesets = new Dictionary<string, OfficeTileset> { ["carpet-wall"] = new("atlas", "carpet", "wall", corners) },
        };

        // A hundred and twenty-eight square holds sixteen of the kit's
        // thirty-two pixel tiles.
        OfficeKits.Problems(kit, piece => piece == "atlas" ? (128, 128) : null).Should().ContainSingle()
            .Which.Should().Be("tileset 'carpet-wall' draws 'uuuu' with tile 16; 'atlas' has tiles 0 to 15.");
    }

    [Fact]
    public void Pieces_are_checked_for_footprint_seats_place_and_picture()
    {
        var kit = Small(
            ("flat", new OfficePiece(null, null, [0, 1], ["desk"])),
            ("chair", new OfficePiece(null, null, [1, 1], ["desk"], Seats: [new OfficeSeat(3, 0, "up")])),
            ("shelf", new OfficePiece(null, null, [1, 1], ["storage"], Place: "loft")),
            ("sofa", new OfficePiece("props", [40, 0, 16, 16], [1, 1], ["sofa"])));

        OfficeKits.Problems(kit, piece => piece == "props" ? (48, 48) : null).Should().BeEquivalentTo(
            "piece 'flat' needs a footprint of 1 to 32 tiles each way.",
            "piece 'chair' has a seat facing 'up'; it has to be n, e, s or w.",
            "piece 'chair' has a seat at 3,0, which is not on or beside its 1x1 footprint.",
            "piece 'shelf' is placed 'loft'; it has to be one of floor, wall-north, wall-any, desk-top, ceiling, facade.",
            "piece 'sofa' needs a source of x, y, width and height inside 'props', which is 48x48.");
    }

    [Fact]
    public void Materials_are_laid_over_the_neighbourhood_by_name_with_a_size_and_their_pictures()
    {
        (int Width, int Height)? Pictures(string name) => name is "brick.png" or "brick-night.png" ? (64, 96) : null;

        var good = OfficeKit.Kit() with
        {
            Materials = new Dictionary<string, OfficeMaterial>(StringComparer.Ordinal)
            {
                ["facade-brick"] = new("brick.png", [64, 96], "brick-night.png"),

                // A floor, named for what the rules call it.
                ["floor-wood"] = new("brick.png", [64, 96]),
            },
        };

        OfficeKits.Problems(good, Pictures).Should().BeEmpty();

        var bad = OfficeKit.Kit() with
        {
            Materials = new Dictionary<string, OfficeMaterial>(StringComparer.Ordinal)
            {
                ["facade-marble"] = new("brick.png", [64, 96]),
                ["floor-"] = new("brick.png", [64, 96]),
                ["floor-Oak Boards"] = new("brick.png", [64, 96]),
                ["roof"] = new("brick.png", [0, 96]),
                ["road"] = new("tarmac.png", [32, 32], "brick-night.png"),
            },
        };

        OfficeKits.Problems(bad, Pictures).Should().BeEquivalentTo(
            "material 'facade-marble' is not one the neighbourhood uses or a floor; they are " + string.Join(", ", OfficeKit.MaterialNames) + ", and floor- with a floor's name.",
            "material 'floor-' is not one the neighbourhood uses or a floor; they are " + string.Join(", ", OfficeKit.MaterialNames) + ", and floor- with a floor's name.",
            "material 'floor-Oak Boards' is not one the neighbourhood uses or a floor; they are " + string.Join(", ", OfficeKit.MaterialNames) + ", and floor- with a floor's name.",
            "material 'roof' needs a size of two numbers from 1 to 1024: the tile's width and height in art pixels.",
            "material 'road' uses 'tarmac.png', which is not in the set or is not a picture.");
    }

    [Fact]
    public void A_facade_must_be_made_of_facade_pieces_that_exist()
    {
        var kit = Small(("bay", new OfficePiece(null, null, [1, 1], ["facade-bay"], Place: "facade")),
                        ("desk", new OfficePiece(null, null, [3, 1], ["desk"]))) with
        {
            Facade = new OfficeFacade(["bay", "ghost"], Corner: "desk"),
        };

        OfficeKits.Problems(kit, NoPictures).Should().BeEquivalentTo(
            "facade uses piece 'ghost', and there is no such piece.",
            "facade uses piece 'desk', which is placed 'floor' rather than facade.");
    }

    [Fact]
    public void Skins_need_a_worker_and_every_sheet_they_name()
    {
        var kit = Small() with
        {
            Skins = new Dictionary<string, IReadOnlyList<string>> { ["project-lead"] = ["boss"] },
            Sheets = new Dictionary<string, OfficeSheet> { ["clerk"] = new("clerk", [16, 32], [8, 30], new Dictionary<string, OfficeAnimation>()) },
        };

        OfficeKits.Problems(kit, NoPictures).Should()
            .Contain("skins draws 'project-lead' with sheet 'boss', and there is no such sheet.")
            .And.Contain("skins has no 'worker', which draws every role the kit does not name.");
    }

    [Fact]
    public void A_kit_on_disk_is_read_with_its_rules_and_checked()
    {
        var set = Path.Combine(_root, "tower");

        Directory.CreateDirectory(set);
        File.WriteAllText(Path.Combine(set, OfficeKit.FileName), JsonSerializer.Serialize(OfficeKit.Kit()));
        File.WriteAllText(Path.Combine(set, OfficeRules.FileName), """{"schema":"loadout.rules/1","rooms":{"meeting":{"tags":["meeting-6"],"level":"attic"}}}""");

        var check = OfficeKits.Check(_root, "tower");

        OfficeKits.Has(_root, "tower").Should().BeTrue();
        check.Kit.Should().NotBeNull();
        check.Fit.Should().BeFalse();
        check.Problems.Should().ContainSingle().Which.Should().StartWith("room 'meeting' is on level 'attic'");
    }

    [Fact]
    public void A_kit_that_will_not_parse_is_a_problem_rather_than_an_exception()
    {
        var set = Path.Combine(_root, "broken");

        Directory.CreateDirectory(set);
        File.WriteAllText(Path.Combine(set, OfficeKit.FileName), "{ nope");

        OfficeKits.Check(_root, "broken").Problems.Should().ContainSingle()
            .Which.Should().StartWith("kit.json is not a kit");
    }
}
