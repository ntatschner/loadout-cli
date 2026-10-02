using System.Security.Cryptography;
using System.Text;

namespace Loadout.Core.Teams;

/// <summary>A floor the planner laid out, and how many people it seats.</summary>
/// <param name="Scene">The floor, as a scene the page draws and the scene check has passed.</param>
/// <param name="Capacity">How many people a floor of this kit and these rules can seat, lead included.</param>
/// <param name="Attempt">Which of the seeded tries produced it; 0 is the first. -1 is the plain fallback.</param>
public sealed record OfficeFloorPlan(OfficeScene Scene, int Capacity, int Attempt);

/// <summary>
/// Lays out one run's floor of the building from a kit and the rules.
/// </summary>
/// <remarks>
/// <para>
/// The same run gets the same floor every time: every choice is drawn from a
/// sequence seeded by the run's identifier, so the floor a person learned
/// yesterday is the floor they see today, and two runs look different.
/// </para>
/// <para>
/// The shape of a run's floor - rooms in the band either side of the core,
/// placed by the rules; the team's area along the windows, as big as the team;
/// vacant floor round it - is laid out in <c>FloorPlanner.Layout.cs</c>. This
/// file has what every level shares: the shell, the core, the scene, and the
/// lobby, the roof and the basements.
/// </para>
/// <para>
/// What comes out is an ordinary scene, and it is put through the scene check
/// before it is returned. A layout that fails - which the tests say the built-in
/// kit never produces - is retried with the next seed, and after that the floor
/// falls back to plain open plan, which always passes.
/// </para>
/// </remarks>
public static partial class FloorPlanner
{
    private const int Tries = 20;

    private enum Cell
    {
        Carpet,
        Solid,
        Glass,
    }

    /// <summary>Lay out a floor for a run and the people on it.</summary>
    /// <param name="kit">The pack's kit, or the built-in one.</param>
    /// <param name="rules">The rules, with any pack changes already over them.</param>
    /// <param name="seed">The run's identifier.</param>
    /// <param name="team">How many people are on this floor, lead included.</param>
    public static OfficeFloorPlan Plan(OfficeKit kit, OfficeRules rules, string seed, int team)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(seed);

        var parts = new Parts(kit);

        for (var attempt = 0; attempt < Tries; attempt++)
        {
            var layout = Layout(rules, seed, attempt);
            var built = Build(parts, rules, new Random(Seed(seed, attempt)), Math.Max(1, team), plain: false, layout);

            // A corridor floor seats fewer; a team it cannot hold gets open plan.
            if (layout == "corridor" && built is { } corridor && corridor.Capacity < Math.Max(1, team))
            {
                built = Build(parts, rules, new Random(Seed(seed, attempt)), Math.Max(1, team), plain: false, "open");
            }

            // No pictures to measure here: the kit's check measured them, and
            // a check that can't find them refuses every try with art in it.
            if (built is not null && OfficeScenes.Problems(built.Value.Scene, sizeOf: null).Count == 0)
            {
                return new OfficeFloorPlan(built.Value.Scene, built.Value.Capacity, attempt);
            }
        }

        var fallback = Build(parts, rules, new Random(Seed(seed, -1)), Math.Max(1, team), plain: true, "open")!.Value;

        return new OfficeFloorPlan(fallback.Scene, fallback.Capacity, -1);
    }

    /// <summary>
    /// The ground floor, two storeys high: the entrance in the middle of the
    /// street side, reception, the waiting room with a seat for everything
    /// waiting, the lobby screen with what is scheduled next, and the core.
    /// </summary>
    /// <param name="kit">The pack's kit, or the built-in one.</param>
    /// <param name="rules">The rules, with any pack changes already over them.</param>
    /// <param name="waiting">How many should have a seat: each schedule and queued task is somebody on a sofa.</param>
    /// <remarks>
    /// The same every time for the same number of seats, so the lobby does not
    /// rearrange itself whenever something is queued; the page asks in steps.
    /// </remarks>
    public static OfficeFloorPlan Lobby(OfficeKit kit, OfficeRules rules, int waiting)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(rules);

        var parts = new Parts(kit);
        var random = new Random(Seed("lobby", waiting));
        var (floor, _, band, coreLeft, coreWidth) = Shell(rules);
        var width = floor.Width;
        var height = floor.Height;

        // The entrance: two cells of the street-side glass left open, and
        // where somebody coming in first stands.
        var door = width / 2 - 1;

        floor.Wall(door, height - 1, Cell.Carpet);
        floor.Wall(door + 1, height - 1, Cell.Carpet);
        floor.Areas.Add(new OfficeArea("entrance", "entrance", door, height - 2, 2, 1, null));

        Core(floor, parts, random, coreLeft, coreWidth, band);

        // The way from the entrance to the lift stays clear.
        floor.Reserve(coreLeft, band + 2, coreWidth, height - 2 - band - 1);
        floor.Reserve(door, band + 2, 2, height - 2 - band - 1);

        // The lobby screen on the north wall of the band, west of the core,
        // over the waiting room.
        var screen = parts.Pick("status-board", random);
        var screenX = Math.Max(1, (coreLeft - screen.Piece.Footprint[0]) / 2);

        floor.Put("lobby-screen", "lobby-screen", screen.Piece with { Blocks = false }, screenX, 1);
        floor.Areas.Add(new OfficeArea("lobby-screen", "lobby-screen", screenX, 1, screen.Piece.Footprint[0], 1, Function(rules, "lobby-screen") ?? "schedules"));

        // IT help in the band east of the core, open to the lobby.
        Facility(floor, parts, random, rules, "it-help", "lobby", coreLeft + coreWidth + 1, 7, band, "open");

        // Reception just inside the door, east of the way in, facing it, the
        // receptionist behind: the first thing somebody coming in walks up to.
        var desk = parts.Pick("reception", random, one => one.Seats is { Count: > 0 });
        var dw = desk.Piece.Footprint[0];
        var deskX = Math.Min(width - 2 - dw, door + 4);
        var deskY = height - 6;

        floor.Put("reception-desk", "reception", desk.Piece, deskX, deskY, "reception");
        floor.Areas.Add(new OfficeArea("reception", "reception", deskX - 1, deskY - 1, dw + 2, 3, Function(rules, "reception") ?? "arrivals"));
        floor.Reserve(deskX - 1, deskY - 1, dw + 2, 3);

        // The waiting lounge west of the way in: sofas in pairs facing each
        // other across a coffee table, a group for every four waiting and never
        // none - a lobby with nowhere to sit looks shut - from the door outwards,
        // on a rug.
        var sofa = parts.Pick("sofa", random, one => one.Footprint[1] == 1 && one.Seats is { Count: > 0 });
        // Turned to face the first: a picture only where the set has its back,
        // a shape always, having no picture to show the wrong side of.
        var turned = sofa.Piece.Picture is null || sofa.Piece.Sides?.ContainsKey("n") == true ? Turned(sofa.Piece) : sofa.Piece;
        var table = parts.Has("coffee-table", one => one.Footprint[1] == 1 && one.Footprint[0] <= sofa.Piece.Footprint[0])
            ? parts.Pick("coffee-table", random, one => one.Footprint[1] == 1 && one.Footprint[0] <= sofa.Piece.Footprint[0]).Piece
            : null;
        var sw = sofa.Piece.Footprint[0];
        var per = sofa.Piece.Seats!.Count * 2;
        var groups = Math.Max(1, (waiting + per - 1) / per);
        var seats = new List<OfficeSpot>();
        var placed = new List<(int X, int Y)>();

        for (int row = height - 6, made = 0; made < groups && row - 2 > band + 1; row -= 5)
        {
            for (var x = door - 3 - sw; made < groups && x >= 2; x -= sw + 3)
            {
                if (!floor.Free(x, row, sw, 3))
                {
                    continue;
                }

                var group = ++made;

                floor.Put($"sofa-{group}a", "sofa", sofa.Piece, x, row);
                floor.Put($"sofa-{group}b", "sofa", turned, x, row + 2, null, ReferenceEquals(turned, sofa.Piece) ? "s" : "n");

                if (table is not null)
                {
                    floor.Put($"coffee-table-{group}", "coffee-table", table, x + (sw - table.Footprint[0]) / 2, row + 1);
                }

                floor.Take(x, row, sw, 3);
                seats.AddRange(sofa.Piece.Seats.Select(seat => new OfficeSpot(x + seat.X, row + seat.Y, seat.Facing, Sit: true)));
                seats.AddRange((turned.Seats ?? []).Select(seat => new OfficeSpot(x + seat.X, row + 2 + seat.Y, seat.Facing, Sit: true)));
                placed.Add((x, row));
            }
        }

        if (placed.Count > 0)
        {
            var left = placed.Min(one => one.X) - 1;
            var top = placed.Min(one => one.Y) - 1;
            var right = placed.Max(one => one.X) + sw + 1;
            var bottom = placed.Max(one => one.Y) + 4;

            floor.Areas.Add(new OfficeArea("waiting-room", "waiting-room", left, top, right - left, bottom - top, Function(rules, "waiting-room") ?? "waiting"));
        }

        // Planters either side of the way in, in the band's corners and round
        // the atrium's walls: the big ones where the set has them.
        var big = parts.Has("planter-large", one => one.Footprint[0] == 1 && one.Footprint[1] == 1) ? "planter-large" : "plant";

        if (parts.Has(big, one => one.Footprint[0] == 1 && one.Footprint[1] == 1))
        {
            var plant = parts.Pick(big, random, one => one.Footprint[0] == 1 && one.Footprint[1] == 1);
            var index = 0;
            var places = new List<(int X, int Y)> { (door - 1, height - 2), (door + 2, height - 2), (1, 1), (width - 2, 1) };

            for (var y = band + 3; y < height - 3; y += 4)
            {
                places.Add((1, y));
                places.Add((width - 2, y));
            }

            foreach (var (x, y) in places)
            {
                if (floor.CanAdd(x, y, 1, 1, true) && !floor.Spots.Values.Any(spot => spot.X == x && spot.Y == y))
                {
                    floor.Put($"plant-{++index}", big, plant.Piece, x, y);
                }
            }
        }

        var scene = Scene(parts, rules, "@lobby", floor, seats, coreLeft, band) with { Door = new OfficeSpot(door, height - 2, "n") };

        return new OfficeFloorPlan(scene, seats.Count, 0);
    }

    /// <summary>
    /// The roof, where anybody free goes for a break: decking inside a glass
    /// balustrade, the lift and stairs coming up in the core, pergolas with
    /// benches under them, planters, and places at the edge to look out from.
    /// </summary>
    /// <param name="kit">The pack's kit, or the built-in one.</param>
    /// <param name="rules">The rules, with any pack changes already over them.</param>
    /// <param name="seats">How many should have a seat: everybody free, in the page's steps.</param>
    public static OfficeFloorPlan Roof(OfficeKit kit, OfficeRules rules, int seats)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(rules);

        var parts = new Parts(kit);
        var random = new Random(Seed("roof", seats));
        var (floor, _, band, coreLeft, coreWidth) = Shell(rules, roof: true);
        var width = floor.Width;
        var height = floor.Height;

        Core(floor, parts, random, coreLeft, coreWidth, band, doors: false);

        // Out of the lift and straight on to the south edge stays clear.
        floor.Reserve(coreLeft, band + 2, coreWidth, height - 2 - band - 1);

        // A gym in the band west of the core, behind glass.
        Facility(floor, parts, random, rules, "gym", "roof", coreLeft - 9, 8, band, "glass");

        // Pergolas in the band either side of the core, a bench under each.
        var pergola = parts.Pick("pergola", random);
        var bench = parts.Pick("sofa", random, one => one.Footprint[1] == 1 && one.Seats is { Count: > 0 });
        var per = bench.Piece.Seats!.Count;
        var spots = new List<OfficeSpot>();
        var placed = 0;
        var slots = new List<(int X, int Y)>();

        for (var y = 2; y <= height - 3; y += y < band ? band + 2 - y : 2)
        {
            for (var x = 2; x + bench.Piece.Footprint[0] <= width - 2; x += bench.Piece.Footprint[0] + 1)
            {
                if (floor.Free(x, y, bench.Piece.Footprint[0], 1))
                {
                    slots.Add((x, y));
                }
            }
        }

        var wanted = Math.Max(2, (seats + per - 1) / per);

        foreach (var (x, y) in slots.Take(wanted))
        {
            // A pergola over each of the first benches, where it fits clear of walls.
            if (placed < 4 && floor.Free(x, y - 1, pergola.Piece.Footprint[0], 1))
            {
                floor.Put($"pergola-{placed + 1}", "pergola", pergola.Piece with { Blocks = false }, x, y - 1);
            }

            floor.Put($"bench-{++placed}", "sofa", bench.Piece, x, y);
            floor.Take(x, y, bench.Piece.Footprint[0], 1);
            spots.AddRange(bench.Piece.Seats.Select(seat => new OfficeSpot(x + seat.X, y + seat.Y, seat.Facing, Sit: true)));
        }

        floor.Areas.Add(new OfficeArea("break-area", "break-area", 1, 1, width - 2, height - 2, Function(rules, "break-area") ?? "idle"));

        // Somewhere to stand at the balustrade, looking out.
        var edge = 0;

        for (var x = 2; x < width - 2; x += 4)
        {
            if (floor.Clear(x, height - 2))
            {
                floor.Spots[$"edge-{++edge}"] = new OfficeSpot(x, height - 2, "s");
            }
        }

        if (parts.Has("plant", one => one.Footprint[0] == 1 && one.Footprint[1] == 1))
        {
            var plant = parts.Pick("plant", random, one => one.Footprint[0] == 1 && one.Footprint[1] == 1);
            var index = 0;

            foreach (var (x, y) in new[] { (1, 1), (width - 2, 1), (1, height - 2), (width - 2, height - 2) })
            {
                if (floor.Clear(x, y) && !floor.Spots.Values.Any(spot => spot.X == x && spot.Y == y))
                {
                    floor.Put($"plant-{++index}", "plant", plant.Piece, x, y);
                }
            }
        }

        return new OfficeFloorPlan(Scene(parts, rules, "@roof", floor, spots, coreLeft, band), spots.Count, 0);
    }

    /// <summary>
    /// A basement: under the street, walled all round, the core coming down in
    /// the same place, a corridor along it, and two rooms off the corridor -
    /// the mail room and storage on the first level down, the garbage room and
    /// the server room on the second.
    /// </summary>
    /// <param name="kit">The pack's kit, or the built-in one.</param>
    /// <param name="rules">The rules, with any pack changes already over them.</param>
    /// <param name="level">1 or 2: how far down.</param>
    /// <remarks>
    /// Each room is filled with its kind of piece in rows - pigeonholes,
    /// shelves, bins, racks - each named for its kind and a number, so the page
    /// can put a thing it knows about in each: a run's post in a pigeonhole, a
    /// run's delivered files on a shelf, a removed run in a bin.
    /// </remarks>
    public static OfficeFloorPlan Basement(OfficeKit kit, OfficeRules rules, int level)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 2);

        var parts = new Parts(kit);
        var random = new Random(Seed("basement", level));
        var (floor, _, band, coreLeft, coreWidth) = Shell(rules);
        var width = floor.Width;
        var height = floor.Height;
        var rooms = rules.Rooms ?? new Dictionary<string, OfficeRoomRule>();

        // No glass below the street.
        for (var x = 0; x < width; x++)
        {
            floor.Wall(x, height - 1, Cell.Solid);
        }

        for (var y = 1; y < height - 1; y++)
        {
            floor.Wall(0, y, Cell.Solid);
            floor.Wall(width - 1, y, Cell.Solid);
        }

        Core(floor, parts, random, coreLeft, coreWidth, band);
        floor.Areas.Add(new OfficeArea("corridor", "corridor", 1, band + 1, width - 2, 2, null));

        var (west, east) = level == 1 ? ("mail-room", "storage") : ("garbage", "server-room");
        var (westTag, eastTag) = level == 1 ? ("mail", "storage") : ("bin", "server");
        var top = band + 4;
        var h = height - 1 - top;
        var middle = width / 2;

        foreach (var (name, tag, x, w) in new[] { (west, westTag, 1, middle - 1), (east, eastTag, middle + 1, width - 2 - middle) })
        {
            var door = x + w / 2;

            Room(floor, name, name, x, top, w, h, top - 1, "solid", rooms, parts, random, northDoor: door);
            Fill(floor, parts, random, tag, x, top, w, h);
        }

        for (var y = top - 1; y < height - 1; y++)
        {
            floor.Wall(middle, y, Cell.Solid);
        }

        // On the first level, a bike store and showers in the band east of the core.
        if (level == 1)
        {
            Facility(floor, parts, random, rules, "bike-store", "basement-1", coreLeft + coreWidth + 1, 8, band, "solid");
            Facility(floor, parts, random, rules, "showers", "basement-1", coreLeft + coreWidth + 10, Math.Min(7, width - 2 - (coreLeft + coreWidth + 10)), band, "solid");
        }

        // A desk in the band for whoever works down here: the post on the
        // first level, the machines on the second.
        var desk = parts.Pick("desk", random, one => one.Footprint[1] == 1 && one.Seats is { Count: > 0 });

        floor.Put("keeper-desk", "desk", desk.Piece, 2, 2, "desk");

        return new OfficeFloorPlan(Scene(parts, rules, "@basement", floor, [.. floor.Seats], coreLeft, band), 0, 0);
    }

    /// <summary>
    /// A facility in a level's band - the help desk, the gym, the bike store -
    /// as the rules have it: its area, its south wall with a door where its
    /// partition is a wall, and what suits it on that level. Nothing where the
    /// rules leave it out.
    /// </summary>
    private static void Facility(Floor floor, Parts parts, Random random, OfficeRules rules, string kind, string level, int x, int w, int band, string walls)
    {
        if (rules.Rooms is not { } rooms || !rooms.TryGetValue(kind, out var rule) || rule.CountFor(1) < 1 || w < 3)
        {
            return;
        }

        var depth = band - 1;
        var door = x + w / 2;

        floor.Areas.Add(new OfficeArea(kind, kind, x, 1, w, depth, rule.Function));
        floor.Reserve(x, 1, w, depth);

        if (walls != "open")
        {
            for (var xx = x - 1; xx <= x + w; xx++)
            {
                if (xx != door)
                {
                    Edge(floor, parts, random, walls, xx, band);
                }
            }

            foreach (var side in new[] { x - 1, x + w })
            {
                for (var y = 1; y < band; y++)
                {
                    Edge(floor, parts, random, walls, side, y);
                }
            }
        }

        floor.Last.Add(() => Furnish(floor, parts, random, rules, kind, kind, level, x, 1, w, depth, back: "n", entry: (door, depth)));
    }

    /// <summary>
    /// Rows of one kind of piece in a room, a row between each and one clear
    /// along the top, so every piece can be walked to from the door.
    /// </summary>
    private static void Fill(Floor floor, Parts parts, Random random, string tag, int x, int y, int w, int h)
    {
        var piece = parts.Pick(tag, random, one => one.Footprint[1] == 1 && one.Footprint[0] <= Math.Max(1, (w - 1) / 2));
        var pw = piece.Piece.Footprint[0];
        var index = 0;

        for (var row = y + 1; row <= y + h - 1; row += 2)
        {
            for (var at = x; at + pw <= x + w; at += pw + 1)
            {
                // The room reserved its own area, so free here means clear.
                if (Enumerable.Range(at, pw).All(cx => floor.Clear(cx, row)))
                {
                    floor.Put($"{tag}-{++index}", tag, piece.Piece, at, row);
                }
            }
        }
    }

    /// <summary>What is wrong with one seeded attempt, for tests saying why a floor needed a retry.</summary>
    internal static IReadOnlyList<string> Faults(OfficeKit kit, OfficeRules rules, string seed, int team, int attempt)
    {
        var built = Build(new Parts(kit), rules, new Random(Seed(seed, attempt)), Math.Max(1, team), plain: false, Layout(rules, seed, attempt));

        return built is null ? ["the band did not fit"] : OfficeScenes.Problems(built.Value.Scene, sizeOf: null);
    }

    /// <summary>
    /// A floor teams share: the band of rooms for everybody on it, and each
    /// team in its own bays, laid out by the team's own seed so its desks stay
    /// where they are whoever else comes and goes.
    /// </summary>
    /// <param name="kit">The pack's kit, or the built-in one.</param>
    /// <param name="rules">The rules, with any pack changes already over them.</param>
    /// <param name="number">The floor's number, which seeds the band.</param>
    /// <param name="tenants">The teams on it and their bays.</param>
    public static OfficeFloorPlan Shared(OfficeKit kit, OfficeRules rules, int number, IReadOnlyList<OfficeTenant> tenants)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(tenants);

        var parts = new Parts(kit);
        var people = Math.Max(1, tenants.Sum(one => one.People));

        for (var attempt = 0; attempt < Tries; attempt++)
        {
            List<Tenant> Seeded(bool open) =>
            [
                .. tenants.Select(one => new Tenant(
                    one with { People = Math.Max(1, one.People) },
                    open ? "open" : Layout(rules, $"{one.Run}#{one.Part}", attempt),
                    new Random(Seed($"{one.Run}#{one.Part}", attempt)))),
            ];

            var built = Build(parts, rules, new Random(Seed($"floor-{number}", attempt)), people, plain: false, "open", Seeded(open: false));

            // A team room seats fewer; a team it cannot hold has its bays open.
            if (built is { } walled && walled.Scene.Teams!.Any(team =>
                team.Desks.Count < Math.Min(Math.Max(1, tenants.First(one => one.Run == team.Run && one.Part == team.Part).People), walled.Capacity)))
            {
                built = Build(parts, rules, new Random(Seed($"floor-{number}", attempt)), people, plain: false, "open", Seeded(open: true));
            }

            if (built is not null && OfficeScenes.Problems(built.Value.Scene, sizeOf: null).Count == 0)
            {
                return new OfficeFloorPlan(built.Value.Scene, built.Value.Capacity, attempt);
            }
        }

        var plain = tenants.Select(one => new Tenant(one with { People = Math.Max(1, one.People) }, "open", new Random(Seed($"{one.Run}#{one.Part}", -1)))).ToList();
        var fallback = Build(parts, rules, new Random(Seed($"floor-{number}", -1)), people, plain: true, "open", plain)!.Value;

        return new OfficeFloorPlan(fallback.Scene, fallback.Capacity, -1);
    }

    /// <summary>
    /// How many one bay seats, lead included, however it falls: the fewest of
    /// a dozen layouts, every bay and both corners for the lead's office. One
    /// sample said nine where the office in the other corner left room for
    /// seven, and a team the building thought fitted had nowhere to sit.
    /// </summary>
    public static int BayCapacity(OfficeKit kit, OfficeRules rules)
    {
        var parts = new Parts(kit);
        var bays = rules.Bays ?? OfficeRules.BayCount;
        var fewest = int.MaxValue;

        for (var sample = 0; sample < 12; sample++)
        {
            var tenant = new Tenant(new OfficeTenant("capacity", 0, sample % bays, 1, 1000), "open", new Random(Seed("capacity", sample)));
            var built = Build(parts, rules, new Random(Seed("capacity", sample)), 1000, plain: false, "open", [tenant]);

            if (built is { } one)
            {
                fewest = Math.Min(fewest, one.Capacity);
            }
        }

        return fewest == int.MaxValue ? 1 : Math.Max(1, fewest);
    }

    /// <summary>One seeded attempt's floor as built, before any check: for tests to look at.</summary>
    internal static OfficeScene Attempt(OfficeKit kit, OfficeRules rules, string seed, int team, int attempt) =>
        Build(new Parts(kit), rules, new Random(Seed(seed, attempt)), Math.Max(1, team), plain: false, Layout(rules, seed, attempt))?.Scene
        ?? throw new InvalidOperationException("the floor did not fit");

    /// <summary>How many a floor seats, lead included, when the team is large.</summary>
    public static int Capacity(OfficeKit kit, OfficeRules rules) =>
        Build(new Parts(kit), rules, new Random(Seed("capacity", 0)), 1000, plain: false, "open")?.Capacity ?? 1;

    /// <summary>Which kind of floor a run's attempt gets, from the layouts the rules allow.</summary>
    private static string Layout(OfficeRules rules, string seed, int attempt)
    {
        var layouts = rules.Layouts is { Count: > 0 } allowed ? allowed : ["open"];

        return layouts[new Random(Seed(seed + "#layout", attempt)).Next(layouts.Count)];
    }

    private static int Seed(string seed, int attempt)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}#{attempt}"));

        return BitConverter.ToInt32(hash, 0);
    }

    /// <summary>The kit's pieces by tag, falling back to the built-in kit's for a tag the pack lacks.</summary>
    private sealed class Parts
    {
        private readonly OfficeKit _kit;
        private readonly OfficeKit _builtIn = OfficeKit.Kit();

        public Parts(OfficeKit kit) => _kit = kit;

        public int Tile => _kit.Tile;

        /// <summary>The kit's people: its sheets, and which of them draw each role.</summary>
        public (IReadOnlyDictionary<string, OfficeSheet>? Sheets, IReadOnlyDictionary<string, IReadOnlyList<string>>? Cast) People =>
            _kit.Sheets is { Count: > 0 } && _kit.Skins is { Count: > 0 } ? (_kit.Sheets, _kit.Skins) : (null, null);

        public (string Name, OfficePiece Piece) Pick(string tag, Random random, Func<OfficePiece, bool>? fits = null)
        {
            foreach (var kit in new[] { _kit, _builtIn })
            {
                var named = kit.Tagged(tag)
                    .Select(name => (Name: name, Piece: kit.Pieces[name]))
                    .Where(one => fits is null || fits(one.Piece))
                    .ToList();

                if (named.Count > 0)
                {
                    return named[random.Next(named.Count)];
                }
            }

            throw new InvalidOperationException($"No piece, not even the built-in kit's, is tagged '{tag}'.");
        }

        /// <summary>A piece the set's own kit has for a tag, or null: never one of the built-in shapes.</summary>
        public (string Name, OfficePiece Piece)? Own(string tag, Random random, Func<OfficePiece, bool> fits)
        {
            var named = _kit.Tagged(tag)
                .Select(name => (Name: name, Piece: _kit.Pieces[name]))
                .Where(one => fits(one.Piece))
                .ToList();

            return named.Count > 0 ? named[random.Next(named.Count)] : null;
        }

        /*
            The pieces that suit a kind of room on a level in a role: the set's
            own that say so, in the order the given tags name them and then the
            rest; where the set has none, those its pieces or, unless only its
            own are wanted, the built-in kit's have under those tags.
        */
        public List<(string Name, OfficePiece Piece)> Suiting(string kind, string level, string role, IReadOnlyList<string>? tags, bool own = false)
        {
            var order = tags ?? [];
            var suited = _kit.Pieces
                .Where(one => one.Value.Suits is { } suits && suits.Role == role && suits.Fits(kind, level))
                .Select(one => (Name: one.Key, Piece: one.Value))
                .OrderBy(one => order.Select((tag, i) => one.Piece.Tags.Contains(tag) ? i : int.MaxValue).DefaultIfEmpty(int.MaxValue).Min())
                .ThenBy(one => one.Name, StringComparer.Ordinal)
                .ToList();

            if (suited.Count > 0 || order.Count == 0)
            {
                return suited;
            }

            // By tag only for a kit whose pieces say nothing of where they suit:
            // one that does has said, and a meeting table is not a meeting
            // room's extra however it is tagged.
            foreach (var kit in (own ? new[] { _kit } : new[] { _kit, _builtIn }).Where(one => !one.Pieces.Values.Any(piece => piece.Suits is not null) || one == _builtIn && role == "main"))
            {
                var tagged = order.SelectMany(tag => kit.Tagged(tag).Select(name => (Name: name, Piece: kit.Pieces[name])))
                    .DistinctBy(one => one.Name)
                    .ToList();

                if (tagged.Count > 0)
                {
                    return tagged;
                }
            }

            return [];
        }

        public bool Has(string tag, Func<OfficePiece, bool> fits) =>
            _kit.Tagged(tag).Any(name => fits(_kit.Pieces[name]))
            || _builtIn.Tagged(tag).Any(name => fits(_builtIn.Pieces[name]));

        public OfficeTileset Tileset(params string[] names)
        {
            foreach (var name in names)
            {
                if (_kit.Tilesets.TryGetValue(name, out var found))
                {
                    return found;
                }
            }

            return _builtIn.Tilesets[names[^1] is "carpet-glass" or "curtain-wall" ? "carpet-glass" : "carpet-wall"];
        }
    }

    /// <summary>Everything being put together for one floor.</summary>
    private sealed class Floor
    {
        public Floor(int width, int height)
        {
            Width = width;
            Height = height;
            Cells = new Cell[width, height];
        }

        public int Width { get; }

        public int Height { get; }

        public Cell[,] Cells { get; }

        public List<OfficeProp> Props { get; } = [];

        public List<OfficeSpot> Seats { get; } = [];

        public OfficeSpot? LeadSeat { get; set; }

        public Dictionary<string, OfficeSpot> Spots { get; } = new(StringComparer.Ordinal);

        public List<OfficeArea> Areas { get; } = [];

        /// <summary>What goes in once the floor is otherwise finished: rooms' extras.</summary>
        public List<Action> Last { get; } = [];

        /// <summary>Tiles taken by something that blocks, or by a seat.</summary>
        public bool[,] Taken { get; set; } = null!;

        /// <summary>Tiles inside a walled room, which the open plan must not use.</summary>
        public bool[,] Reserved { get; set; } = null!;

        public void Reserve(int x, int y, int w, int h)
        {
            for (var yy = y; yy < y + h; yy++)
            {
                for (var xx = x; xx < x + w; xx++)
                {
                    if (xx >= 0 && yy >= 0 && xx < Width && yy < Height)
                    {
                        Reserved[xx, yy] = true;
                    }
                }
            }
        }

        /// <summary>Free of pieces and walls, whether or not a room is there.</summary>
        public bool Clear(int x, int y) =>
            x >= 1 && y >= 1 && x < Width - 1 && y < Height - 1 && Cells[x, y] == Cell.Carpet && !Taken[x, y];

        public void Wall(int x, int y, Cell kind)
        {
            if (x >= 0 && y >= 0 && x < Width && y < Height)
            {
                Cells[x, y] = kind;
            }
        }

        public bool Free(int x, int y, int w, int h)
        {
            for (var yy = y; yy < y + h; yy++)
            {
                for (var xx = x; xx < x + w; xx++)
                {
                    if (xx < 1 || yy < 1 || xx >= Width - 1 || yy >= Height - 1)
                    {
                        return false;
                    }

                    if (Cells[xx, yy] != Cell.Carpet || Taken[xx, yy] || Reserved[xx, yy])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Whether a piece can go here without landing on another, a seat or a
        /// place to stand, or - one in the way - cutting any of the floor off.
        /// </summary>
        public bool CanAdd(int x, int y, int w, int h, bool blocks)
        {
            var spots = Spots.Values.Concat(Seats).Append(LeadSeat).OfType<OfficeSpot>().ToList();

            for (var yy = y; yy < y + h; yy++)
            {
                for (var xx = x; xx < x + w; xx++)
                {
                    if (!Clear(xx, yy)
                        || spots.Any(spot => spot.X == xx && spot.Y == yy)
                        || Props.Any(prop => xx >= prop.X && xx < prop.X + prop.W && yy >= prop.Y && yy < prop.Y + prop.H))
                    {
                        return false;
                    }
                }
            }

            if (!blocks)
            {
                return true;
            }

            return Pockets(x, y, w, h) <= Pockets(0, 0, 0, 0);
        }

        // Whether any piece is in the rows just behind a place, which a picture
        // taller than its place would be drawn over.
        public bool Behind(int x, int y, int w, int rows) =>
            Props.Any(prop => prop.X < x + w && prop.X + prop.W > x && prop.Y < y && prop.Y + prop.H > y - rows);

        /*
            How many separate stretches of floor there are to walk on, with a
            piece in the given place as well. Walked as the scene check walks
            it - round whatever blocks, and through seats, which the planner
            counts as taken but a person stands in - so a seat walled in by
            what was added counts as cut off.
        */
        private int Pockets(int px, int py, int pw, int ph)
        {
            var blocked = new bool[Width, Height];

            foreach (var prop in Props.Where(prop => prop.Blocks).Append(new OfficeProp("added", null, null, px, py, pw, ph, true)))
            {
                for (var yy = prop.Y; yy < prop.Y + prop.H; yy++)
                {
                    for (var xx = prop.X; xx < prop.X + prop.W; xx++)
                    {
                        if (xx >= 0 && yy >= 0 && xx < Width && yy < Height)
                        {
                            blocked[xx, yy] = true;
                        }
                    }
                }
            }

            var seen = new bool[Width, Height];
            var count = 0;
            var queue = new Queue<(int X, int Y)>();

            for (var sy = 0; sy < Height; sy++)
            {
                for (var sx = 0; sx < Width; sx++)
                {
                    if (seen[sx, sy] || Cells[sx, sy] != Cell.Carpet || blocked[sx, sy])
                    {
                        continue;
                    }

                    count++;
                    seen[sx, sy] = true;
                    queue.Enqueue((sx, sy));

                    while (queue.Count > 0)
                    {
                        var (cx, cy) = queue.Dequeue();

                        foreach (var (nx, ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
                        {
                            if (nx >= 0 && ny >= 0 && nx < Width && ny < Height && !seen[nx, ny] && Cells[nx, ny] == Cell.Carpet && !blocked[nx, ny])
                            {
                                seen[nx, ny] = true;
                                queue.Enqueue((nx, ny));
                            }
                        }
                    }
                }
            }

            return count;
        }

        public void Take(int x, int y, int w, int h)
        {
            for (var yy = y; yy < y + h; yy++)
            {
                for (var xx = x; xx < x + w; xx++)
                {
                    if (xx >= 0 && yy >= 0 && xx < Width && yy < Height)
                    {
                        Taken[xx, yy] = true;
                    }
                }
            }
        }

        public void Put(string id, string tag, OfficePiece piece, int x, int y, string? seatsAs = null, string facing = "s")
        {
            // A second piece of a kind in one room is numbered: ids are unique.
            var called = id;

            for (var n = 2; Props.Any(prop => prop.Id == called); n++)
            {
                called = $"{id}-{n}";
            }

            id = called;

            Props.Add(new OfficeProp(
                id,
                piece.Picture,
                piece.Source,
                x,
                y,
                piece.Footprint[0],
                piece.Footprint[1],
                piece.Blocks,
                piece.Depth,
                tag,
                piece.Sides,
                facing));

            if (piece.Blocks)
            {
                Take(x, y, piece.Footprint[0], piece.Footprint[1]);
            }

            var index = 0;

            foreach (var seat in piece.Seats ?? [])
            {
                var spot = new OfficeSpot(x + seat.X, y + seat.Y, seat.Facing);

                if (seatsAs is "desk")
                {
                    Seats.Add(spot);
                }
                else if (seatsAs is "lead")
                {
                    LeadSeat = spot;
                }
                else if (seatsAs is { } prefix)
                {
                    // A meeting table's chairs, a sofa's places: somewhere to sit.
                    Spots[$"{prefix}-{++index}"] = spot with { Sit = true };
                }
            }
        }
    }

    /// <summary>
    /// What every level shares: the shell - a solid north wall and the curtain
    /// wall on the other three, or a glass balustrade all round on the roof -
    /// and the core's walls in the middle of the north band, in the same place
    /// on every level so the lift lines up through the tower.
    /// </summary>
    private static (Floor Floor, int Depth, int Band, int CoreLeft, int CoreWidth) Shell(OfficeRules rules, bool roof = false)
    {
        var width = rules.Floor is [var w, _] ? w : 24;
        var height = rules.Floor is [_, var h] ? h : 16;
        var floor = new Floor(width, height) { Taken = new bool[width, height], Reserved = new bool[width, height] };

        for (var x = 0; x < width; x++)
        {
            floor.Wall(x, 0, roof ? Cell.Glass : Cell.Solid);
            floor.Wall(x, height - 1, Cell.Glass);
        }

        for (var y = 1; y < height - 1; y++)
        {
            floor.Wall(0, y, Cell.Glass);
            floor.Wall(width - 1, y, Cell.Glass);
        }

        // The band of rooms along the north, and the core in its middle.
        var depth = height >= 14 ? 4 : 3;
        var band = depth + 1;
        var coreWidth = width >= 22 ? 6 : 4;
        var coreLeft = (width - coreWidth) / 2;

        for (var x = coreLeft; x < coreLeft + coreWidth; x++)
        {
            for (var y = 1; y <= band; y++)
            {
                floor.Wall(x, y, Cell.Solid);
            }
        }

        return (floor, depth, band, coreLeft, coreWidth);
    }

    /// <summary>Which partition a room gets, chosen from those its rule allows.</summary>
    private static string Walls(OfficeRules rules, string room, Random random)
    {
        var walls = rules.Rooms is { } rooms && rooms.TryGetValue(room, out var rule) && rule.Walls is { Count: > 0 } kinds
            ? kinds
            : ["solid"];

        return walls[random.Next(walls.Count)];
    }

    /// <summary>
    /// One tile of a room's edge: a wall cell for solid and glass, a piece for
    /// a low screen or a planter, and nothing at all for an open room, which
    /// is only its name on the floor.
    /// </summary>
    private static void Edge(Floor floor, Parts parts, Random random, string kind, int x, int y)
    {
        switch (kind)
        {
            case "solid":
                floor.Wall(x, y, Cell.Solid);
                break;
            case "glass":
                floor.Wall(x, y, Cell.Glass);
                break;
            case "screen" or "planters":
                if (floor.Clear(x, y))
                {
                    var tag = kind == "screen" ? "partition-screen" : "partition-planter";
                    var piece = parts.Pick(tag, random, one => one.Footprint[0] == 1 && one.Footprint[1] == 1);

                    floor.Put($"{tag}-{x}-{y}", tag, piece.Piece, x, y);
                }

                break;
        }
    }

    private static string? Function(OfficeRules rules, string room) =>
        rules.Rooms is { } rooms && rooms.TryGetValue(room, out var rule) ? rule.Function : null;

    /// <summary>A walled room: its area, its south wall (north wall when it sits below the open plan) and a door in it.</summary>
    private static void Room(
        Floor floor,
        string name,
        string kind,
        int x,
        int y,
        int w,
        int h,
        int wallRow,
        string walls,
        IReadOnlyDictionary<string, OfficeRoomRule> rules,
        Parts parts,
        Random random,
        bool above = false,
        int? northDoor = null)
    {
        var function = rules.TryGetValue(kind, out var rule) ? rule.Function : null;

        floor.Areas.Add(new OfficeArea(name, kind, x, y, w, h, function));
        floor.Reserve(x, y, w, h);

        if (northDoor is { } opening)
        {
            // A room off a corridor, to its south: the edge along its top,
            // the door where the room's own aisle runs down.
            for (var xx = x; xx < x + w; xx++)
            {
                if (xx != opening)
                {
                    Edge(floor, parts, random, walls, xx, y - 1);
                }
            }

            return;
        }

        if (!above)
        {
            // A band room: its south wall faces the corridor, door in the middle.
            var door = x + w / 2;

            for (var xx = x; xx < x + w; xx++)
            {
                if (xx != door)
                {
                    Edge(floor, parts, random, walls, xx, wallRow);
                }
            }

            return;
        }

        // A room in a corner of the open plan: a wall along its top and one
        // towards the floor, the door in the side, where an aisle runs past -
        // a door in the top would open onto the desks above it.
        var top = y - 1;
        var side = x == 1 ? x + w : x - 1;

        // In the room's bottom row, a seat row: the middle of the side can
        // open straight onto the end of the table.
        var sideDoor = y + h - 1;

        for (var xx = Math.Min(x, side); xx <= Math.Max(x + w - 1, side); xx++)
        {
            Edge(floor, parts, random, walls, xx, top);
        }

        for (var yy = top; yy < y + h; yy++)
        {
            if (yy != sideDoor)
            {
                Edge(floor, parts, random, walls, side, yy);
            }
        }
    }

    private static void Core(Floor floor, Parts parts, Random random, int left, int width, int band, bool doors = true)
    {
        // Along the core's south face, from the left: the toilets' door, the
        // broom cupboard's, the lift, the stairs. The roof has only the last two.
        var row = band + 1;
        var x = left;

        foreach (var tag in new[] { "toilet", "cupboard" })
        {
            // On the roof the doors are not there but their places are, so the
            // lift is where it is on every other level.
            if (doors)
            {
                var piece = parts.Pick(tag, random, one => one.Footprint[0] == 1);

                floor.Put($"core-{tag}", tag, piece.Piece, x, row);
                floor.Spots[tag] = new OfficeSpot(x, row + 1, "n");
            }

            x++;
        }

        // Whatever is left of the core's face after the two doors.
        var lift = parts.Pick("lift", random, one => one.Footprint[0] <= width - 2);

        floor.Put("core-lift", "lift", lift.Piece with { Blocks = false }, x, row);
        floor.Spots["lift"] = new OfficeSpot(x, row + 1, "n");
        x += lift.Piece.Footprint[0];

        if (x < left + width)
        {
            var stairs = parts.Pick("stairs", random);

            floor.Put("core-stairs", "stairs", stairs.Piece with { Blocks = false }, x, row);
        }

        floor.Areas.Add(new OfficeArea("core", "core", left, 1, width, band, null));
    }

    private static (int X, int W)? StatusBoard(Floor floor, Parts parts, Random random, OfficeRules rules, int band, int coreLeft, int coreWidth)
    {
        // On the band's south face, over the open plan, where everyone can see it.
        var board = parts.Pick("status-board", random);
        var w = board.Piece.Footprint[0];

        foreach (var x in new[] { coreLeft - w - 1, coreLeft + coreWidth + 1 })
        {
            if (x >= 1 && x + w < floor.Width - 1)
            {
                floor.Put("status-board", "status-board", board.Piece with { Blocks = false }, x, band + 1);
                floor.Areas.Add(new OfficeArea("status-board", "status-board", x, band + 1, w, 1, Function(rules, "status-board")));
                floor.Spots["status-board"] = new OfficeSpot(x + w / 2, band + 2, "n");

                return (x, w);
            }
        }

        return null;
    }

    private static void Exits(Floor floor, Parts parts, Random random, int row, int standing)
    {
        var exit = parts.Pick("exit", random, one => one.Footprint[0] == 1);

        floor.Put("exit-west", "exit", exit.Piece with { Blocks = false }, 1, row);
        floor.Put("exit-east", "exit", exit.Piece with { Blocks = false }, floor.Width - 2, row);
        // Beside each exit, inside the glass: the row after it on an open
        // floor, the exit's own row on a corridor floor, where the row after
        // is the rooms' edge.
        floor.Spots["window-west"] = new OfficeSpot(1, standing, "w");
        floor.Spots["window-east"] = new OfficeSpot(floor.Width - 2, standing, "e");
    }

    private static OfficeScene Scene(Parts parts, OfficeRules rules, string level, Floor floor, List<OfficeSpot> desks, int coreLeft, int band)
    {
        foreach (var later in floor.Last)
        {
            later();
        }

        floor.Last.Clear();

        var width = floor.Width;
        var height = floor.Height;

        // Three tilesets, numbered one after another: solid walls, glass
        // partitions, the curtain wall; each tile drawn from the corners' terrain.
        var solid = parts.Tileset("carpet-wall");
        var glass = parts.Tileset("carpet-glass");
        var atlases = new List<OfficeAtlas>
        {
            Atlas(solid),
            Atlas(glass),
        };
        var offsets = new[] { 0, atlases[0].Tiles };
        var sets = new[] { solid, glass };
        var kinds = new[] { Cell.Solid, Cell.Glass };

        bool Upper(Cell kind, int vx, int vy)
        {
            for (var y = vy - 1; y <= vy; y++)
            {
                for (var x = vx - 1; x <= vx; x++)
                {
                    if (x >= 0 && y >= 0 && x < width && y < height && floor.Cells[x, y] == kind)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        var floorGrid = new List<IReadOnlyList<int>>();
        var wallGrid = new List<IReadOnlyList<int>>();

        for (var y = 0; y < height; y++)
        {
            var floorRow = new int[width];
            var wallRow = new int[width];

            for (var x = 0; x < width; x++)
            {
                wallRow[x] = -1;
                floorRow[x] = offsets[0] + solid.Corners["llll"];

                var cell = floor.Cells[x, y];

                if (cell != Cell.Carpet)
                {
                    var which = cell == Cell.Solid ? 0 : 1;

                    wallRow[x] = offsets[which] + sets[which].Corners["uuuu"];

                    continue;
                }

                // A carpet cell beside a wall shows the wall's face: solid
                // first, where both meet at a corner.
                for (var which = 0; which < kinds.Length; which++)
                {
                    var pattern = string.Concat(
                        Upper(kinds[which], x, y) ? 'u' : 'l',
                        Upper(kinds[which], x + 1, y) ? 'u' : 'l',
                        Upper(kinds[which], x, y + 1) ? 'u' : 'l',
                        Upper(kinds[which], x + 1, y + 1) ? 'u' : 'l');

                    if (pattern != "llll")
                    {
                        floorRow[x] = offsets[which] + sets[which].Corners[pattern];

                        break;
                    }
                }
            }

            floorGrid.Add(floorRow);
            wallGrid.Add(wallRow);
        }

        var lift = floor.Spots.TryGetValue("lift", out var arrival) ? arrival : new OfficeSpot(coreLeft, band + 2, "n");
        var (sheets, cast) = parts.People;

        // Drawn on the dual grid when every tileset is a picture: a tile at
        // each point where four cells meet, chosen by those four, so a wall
        // one cell wide is drawn one cell wide. The built-in shapes keep the
        // grid above, drawn cell by cell in the kit's colours.
        List<IReadOnlyList<int>>? surface = null;

        if (sets.All(set => set.Picture is not null))
        {
            surface = [];

            Cell At(int x, int y) => floor.Cells[Math.Clamp(x, 0, width - 1), Math.Clamp(y, 0, height - 1)];

            for (var vy = 0; vy <= height; vy++)
            {
                var row = new int[width + 1];

                for (var vx = 0; vx <= width; vx++)
                {
                    row[vx] = offsets[0] + solid.Corners["llll"];

                    // Solid first, where a solid wall and glass meet at a point, and
                    // reaching over the glass there: with glass counted as floor,
                    // a solid wall ended in a cap short of the glass it joins and
                    // was drawn as a stub on its own.
                    for (var which = 0; which < kinds.Length; which++)
                    {
                        var corners = new[] { At(vx - 1, vy - 1), At(vx, vy - 1), At(vx - 1, vy), At(vx, vy) };

                        if (!corners.Contains(kinds[which]))
                        {
                            continue;
                        }

                        var pattern = string.Concat(corners.Select(cell => cell == kinds[which] || (which == 0 && cell != Cell.Carpet) ? 'u' : 'l'));

                        row[vx] = offsets[which] + sets[which].Corners[pattern];

                        break;
                    }
                }

                surface.Add(row);
            }
        }

        return new OfficeScene(
            OfficeScene.Version,
            parts.Tile,
            width,
            height,
            floorGrid,
            wallGrid,
            null,
            floor.Props,
            desks,
            lift with { Facing = "s" },
            floor.Spots,
            Sheets: sheets,
            Atlases: atlases,
            // Each room floored as the rules say for its kind, and anything in no
            // room as they say for the level.
            Areas: [.. floor.Areas.Select(area => area with { Floor = area.Floor ?? rules.FloorFor(area.Kind) })],
            Cast: cast,
            Surface: surface,
            Ground: rules.FloorFor(level));
    }

    // A picture carries its corner patterns so the page can turn it; the
    // built-in atlas's tile numbers are its patterns, so it needs none.
    private static OfficeAtlas Atlas(OfficeTileset tileset) =>
        new(tileset.Picture, AtlasTiles(tileset), tileset.Lower, tileset.Upper, tileset.Picture is null ? null : tileset.Corners);

    private static int AtlasTiles(OfficeTileset tileset) => tileset.Corners.Values.DefaultIfEmpty(0).Max() + 1;
}
