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
/// The shape of a floor: glass on three sides and a solid north wall; a band of
/// rooms along the north - the lead's office in a corner, meeting rooms, the
/// kitchen - either side of the core, which holds the lift, stairs, toilets and
/// the broom cupboard in the same place on every floor, so the lift lines up
/// through the tower; open-plan desks below, back to back in rows with aisles
/// between; the status board on the band's south face; exits at both ends. A
/// lounge and a third meeting room take corners of the open plan when the team
/// is big enough to have them.
/// </para>
/// <para>
/// What comes out is an ordinary scene, and it is put through the scene check
/// before it is returned. A layout that fails - which the tests say the built-in
/// kit never produces - is retried with the next seed, and after that the floor
/// falls back to plain open plan, which always passes.
/// </para>
/// </remarks>
public static class FloorPlanner
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

        // Reception, east of the way in, facing the door, the receptionist behind.
        var desk = parts.Pick("reception", random, one => one.Seats is { Count: > 0 });
        var deskX = coreLeft + coreWidth + 2;
        var deskY = band + 3;

        floor.Put("reception-desk", "reception", desk.Piece, deskX, deskY, "reception");
        floor.Areas.Add(new OfficeArea("reception", "reception", deskX - 1, band + 1, desk.Piece.Footprint[0] + 2, 4, Function(rules, "reception") ?? "arrivals"));
        floor.Reserve(deskX - 1, band + 1, desk.Piece.Footprint[0] + 2, 4);

        // Sofas, west side first, two rows apart, one per two waiting and never
        // fewer than two: a lobby with nowhere to sit looks shut.
        var sofa = parts.Pick("sofa", random, one => one.Footprint[1] == 1 && one.Seats is { Count: > 0 });
        var sw = sofa.Piece.Footprint[0];
        var per = sofa.Piece.Seats!.Count;
        var slots = new List<(int X, int Y)>();

        foreach (var west in new[] { true, false })
        {
            for (var y = band + 3; y <= height - 3; y += 2)
            {
                for (var x = 2; x + sw <= width - 2; x += sw + 1)
                {
                    if ((x < width / 2) == west && floor.Free(x, y, sw, 1))
                    {
                        slots.Add((x, y));
                    }
                }
            }
        }

        var wanted = Math.Max(2, (waiting + per - 1) / per);
        var seats = new List<OfficeSpot>();
        var placed = 0;

        foreach (var (x, y) in slots.Take(wanted))
        {
            floor.Put($"sofa-{++placed}", "sofa", sofa.Piece, x, y);
            floor.Take(x, y, sw, 1);
            seats.AddRange(sofa.Piece.Seats.Select(seat => new OfficeSpot(x + seat.X, y + seat.Y, seat.Facing, Sit: true)));
        }

        if (placed > 0)
        {
            var left = slots.Take(placed).Min(one => one.X);
            var top = slots.Take(placed).Min(one => one.Y);
            var right = slots.Take(placed).Max(one => one.X) + sw;
            var bottom = slots.Take(placed).Max(one => one.Y) + 1;

            floor.Areas.Add(new OfficeArea("waiting-room", "waiting-room", left, top, right - left, bottom - top, Function(rules, "waiting-room") ?? "waiting"));
        }

        // Planters either side of the way in, and in the band's corners.
        if (parts.Has("plant", one => one.Footprint[0] == 1 && one.Footprint[1] == 1))
        {
            var plant = parts.Pick("plant", random, one => one.Footprint[0] == 1 && one.Footprint[1] == 1);
            var index = 0;

            foreach (var (x, y) in new[] { (door - 1, height - 2), (door + 2, height - 2), (1, 1), (width - 2, 1) })
            {
                if (floor.Clear(x, y))
                {
                    floor.Put($"plant-{++index}", "plant", plant.Piece, x, y);
                }
            }
        }

        var scene = Scene(parts, floor, seats, coreLeft, band) with { Door = new OfficeSpot(door, height - 2, "n") };

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

        return new OfficeFloorPlan(Scene(parts, floor, spots, coreLeft, band), spots.Count, 0);
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

        // A desk in the band for whoever works down here: the post on the
        // first level, the machines on the second.
        var desk = parts.Pick("desk", random, one => one.Footprint[1] == 1 && one.Seats is { Count: > 0 });

        floor.Put("keeper-desk", "desk", desk.Piece, 2, 2, "desk");

        return new OfficeFloorPlan(Scene(parts, floor, [.. floor.Seats], coreLeft, band), 0, 0);
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

        public void Put(string id, string tag, OfficePiece piece, int x, int y, string? seatsAs = null)
        {
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
                piece.Sides));

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

    private static (OfficeScene Scene, int Capacity)? Build(Parts parts, OfficeRules rules, Random random, int team, bool plain, string layout)
    {
        var (floor, depth, band, coreLeft, coreWidth) = Shell(rules);
        var width = floor.Width;
        var height = floor.Height;
        var rooms = rules.Rooms ?? new Dictionary<string, OfficeRoomRule>();

        var meetings = rooms.TryGetValue("meeting", out var meetingRule) ? meetingRule.CountFor(team) : 1;
        var lounges = rooms.TryGetValue("lounge", out var loungeRule) ? loungeRule.CountFor(team) : 0;
        var leadOnLeft = random.Next(2) == 0;
        var placedMeetings = 0;
        var placedLounge = false;

        if (!plain)
        {
            var leftWidth = coreLeft - 1;
            var rightLeft = coreLeft + coreWidth;
            var rightWidth = width - 1 - rightLeft;

            // Each side: a corner room and one beside the core, a wall between.
            var cornerWidth = Math.Clamp((leftWidth - 1) / 2 + 1, 3, 6);
            var innerWidth = leftWidth - 1 - cornerWidth;

            if (innerWidth < 2)
            {
                return null;
            }

            var leadWalls = Walls(rules, "lead-office", random);
            var meetingWalls = Walls(rules, "meeting", random);

            // Lead's side: the lead's office in the corner, a meeting room by the core.
            var (leadX, leadMeetX) = leadOnLeft
                ? (1, 1 + cornerWidth + 1)
                : (rightLeft + innerWidth + 1, rightLeft);

            // Other side: the kitchen in the corner, the second meeting room (or,
            // for a team with one, a quiet room with a sofa) by the core.
            var (kitchenX, otherX) = leadOnLeft
                ? (rightLeft + innerWidth + 1, rightLeft)
                : (1, 1 + cornerWidth + 1);

            Room(floor, "lead-office", "lead-office", leadX, 1, cornerWidth, depth, band, leadWalls, rooms, parts, random);
            LeadOffice(floor, parts, random, leadX, cornerWidth, depth);

            Room(floor, "meeting-1", "meeting", leadMeetX, 1, innerWidth, depth, band, meetingWalls, rooms, parts, random);
            placedMeetings += Meeting(floor, parts, random, rules, "meeting-1", leadMeetX, 1, innerWidth, depth) ? 1 : 0;

            Room(floor, "kitchen", "kitchen", kitchenX, 1, cornerWidth, depth, band, "solid", rooms, parts, random);
            Kitchen(floor, parts, random, rules, kitchenX, cornerWidth, depth);

            if (meetings >= 2)
            {
                Room(floor, "meeting-2", "meeting", otherX, 1, innerWidth, depth, band, meetingWalls, rooms, parts, random);
                placedMeetings += Meeting(floor, parts, random, rules, "meeting-2", otherX, 1, innerWidth, depth) ? 1 : 0;
            }
            else
            {
                Room(floor, "quiet-room", "lounge", otherX, 1, innerWidth, depth, band, Walls(rules, "lounge", random), rooms, parts, random);
                Lounge(floor, parts, random, rules, "quiet-room", otherX, 1, innerWidth, depth);
                placedLounge = true;
            }

            // The wall between each pair of band rooms: the lead's office's kind
            // on the lead's side, solid on the kitchen's. Down to the rooms'
            // south wall and joining it: stopping short of it left each
            // room's south wall a piece on its own, drawn as a stub.
            foreach (var between in new[] { 1 + cornerWidth, rightLeft + innerWidth })
            {
                var onLeadSide = (between == 1 + cornerWidth) == leadOnLeft;

                for (var y = 1; y <= band; y++)
                {
                    Edge(floor, parts, random, onLeadSide ? leadWalls : "solid", between, y);
                }
            }
        }
        else
        {
            // Plain: the band is open floor, with the lead's desk in a corner.
            var desk = parts.Pick("exec-desk", random);

            floor.Put("lead-desk", "exec-desk", desk.Piece, 2, 2, "lead");
            floor.Areas.Add(new OfficeArea("lead-office", "lead-office", 1, 1, 5, depth, Function(rules, "lead-office")));
        }

        Core(floor, parts, random, coreLeft, coreWidth, band);

        var aisleTop = band + 2;

        // A lounge and a third meeting room take corners of the open plan when
        // the team is big enough for them.
        var corridor = !plain && layout == "corridor" && height - 2 - aisleTop >= 6;

        if (!plain && !corridor && meetings >= 3 && height - 2 - aisleTop >= 5)
        {
            var roomTop = height - 2 - 3;
            var x = leadOnLeft ? 1 : width - 1 - 4;

            Room(floor, "meeting-3", "meeting", x, roomTop, 4, 4, roomTop + 4, Walls(rules, "meeting", random), rooms, parts, random, above: true);
            placedMeetings += Meeting(floor, parts, random, rules, "meeting-3", x, roomTop, 4, 4) ? 1 : 0;
        }

        if (!plain && !corridor && !placedLounge && lounges > 0 && height - 2 - aisleTop >= 4)
        {
            var x = leadOnLeft ? width - 1 - 4 : 1;
            var y = height - 2 - 1;

            if (floor.Free(x, y, 4, 2))
            {
                floor.Areas.Add(new OfficeArea("lounge", "lounge", x, y, 4, 2, Function(rules, "lounge")));
                floor.Reserve(x, y, 4, 2);
                Lounge(floor, parts, random, rules, "lounge", x, y, 4, 2);
            }
        }

        StatusBoard(floor, parts, random, rules, band, coreLeft, coreWidth);
        Exits(floor, parts, random, aisleTop, corridor ? aisleTop : Math.Min(aisleTop + 1, height - 2));

        var capacity = 1 + (corridor
            ? CorridorRooms(floor, parts, random, rules, band, aisleTop, team - 1, meetings >= 3, !placedLounge && lounges > 0)
            : OpenPlan(floor, parts, random, rules, aisleTop, team - 1));

        Decorate(floor, parts, random, aisleTop);

        var desks = new List<OfficeSpot>();

        if (floor.LeadSeat is { } lead)
        {
            desks.Add(lead);
        }

        desks.AddRange(floor.Seats);

        return (Scene(parts, floor, desks, coreLeft, band), capacity);
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

    private static void LeadOffice(Floor floor, Parts parts, Random random, int x, int w, int h)
    {
        var desk = parts.Pick("exec-desk", random, one => one.Footprint[0] <= w && one.Footprint[1] <= h - 2);
        var deskX = x + Math.Max(0, (w - desk.Piece.Footprint[0]) / 2);

        // The desk faces the door, with the lead behind it: in the room's
        // second row, so the chair has the first.
        floor.Put("lead-desk", "exec-desk", desk.Piece, deskX, 2, "lead");

        if (parts.Has("plant", one => one.Footprint[0] == 1 && one.Footprint[1] == 1))
        {
            var plant = parts.Pick("plant", random, one => one.Footprint[0] == 1 && one.Footprint[1] == 1);

            // The front corner on the building's outside: not the back, where it
            // would close the way round the desk to the chair, and not the
            // door's column.
            var corner = x == 1 ? x : x + w - 1;

            if (corner != x + w / 2 && floor.Clear(corner, h))
            {
                floor.Put("lead-plant", "plant", plant.Piece, corner, h);
            }
        }
    }

    private static bool Meeting(Floor floor, Parts parts, Random random, OfficeRules rules, string name, int x, int y, int w, int h)
    {
        // The largest table that fits with a seat row either side and an aisle
        // down one end: a table as wide as its room walls its far row off.
        foreach (var size in new[] { "meeting-10", "meeting-6", "meeting-4" })
        {
            bool Fits(OfficePiece one) => one.Footprint[0] <= w - 1 && one.Footprint[1] + 2 <= h;

            if (!parts.Has(size, Fits))
            {
                continue;
            }

            var table = parts.Pick(size, random, Fits);
            var tableX = x + (w - table.Piece.Footprint[0]) / 2;
            var tableY = y + (h - table.Piece.Footprint[1]) / 2;

            floor.Put($"{name}-table", size, table.Piece, tableX, tableY, name);
            Extras(floor, parts, random, rules, "meeting", name, x, y, w, h);

            return true;
        }

        return false;
    }

    private static void Kitchen(Floor floor, Parts parts, Random random, OfficeRules rules, int x, int w, int h)
    {
        var counter = parts.Pick("kitchen", random, one => one.Footprint[0] <= w - 1 && one.Footprint[1] == 1);

        floor.Put("kitchen-counter", "kitchen", counter.Piece, x, 1);

        var coffee = parts.Pick("coffee", random, one => one.Footprint[0] == 1 && one.Footprint[1] == 1);
        var coffeeX = x + counter.Piece.Footprint[0];

        if (coffeeX < x + w)
        {
            floor.Put("kitchen-coffee", "coffee", coffee.Piece, coffeeX, 1);
        }

        // Somewhere to stand with a cup: the room's back row, clear of the door.
        for (var xx = x; xx < x + w; xx++)
        {
            floor.Spots[$"kitchen-{xx - x + 1}"] = new OfficeSpot(xx, h - 1, "n");
        }

        // The whole room: the front row too, where a table can stand clear of
        // the counter. What keeps the way in clear is the check every extra
        // passes, that the room can still be walked into.
        Extras(floor, parts, random, rules, "kitchen", "kitchen", x, 1, w, h);
    }

    private static void Lounge(Floor floor, Parts parts, Random random, OfficeRules rules, string name, int x, int y, int w, int h)
    {
        var sofa = parts.Pick("sofa", random, one => one.Footprint[0] <= w && one.Footprint[1] <= h);

        floor.Put($"{name}-sofa", "sofa", sofa.Piece, x + (w - sofa.Piece.Footprint[0]) / 2, y, name);
        Extras(floor, parts, random, rules, "lounge", name, x, y, w, h);
    }

    /*
        What a room takes as well as its own piece, from the rule's extras: a
        fridge and a cafe table in the kitchen, an armchair in the lounge. Only
        from the set's kit - the built-in one has none, so a building drawn in
        its shapes is planned as before - and only where one fits: inside the
        room, off every seat and place to stand, against the back wall first,
        and, for one in the way, without cutting anywhere off from anywhere
        else. One that fits nowhere is left out.

        Placed once the floor is otherwise finished: a wall built after a room
        is furnished - the lead's office beside a meeting room, say - can close
        the way round an extra that was open when it went in.
    */
    private static void Extras(Floor floor, Parts parts, Random random, OfficeRules rules, string kind, string name, int x, int y, int w, int h)
    {
        if (rules.Rooms is not { } rooms || !rooms.TryGetValue(kind, out var rule) || rule.Extras is not { Count: > 0 } extras)
        {
            return;
        }

        floor.Last.Add(() => Extras(floor, parts, random, extras, name, x, y, w, h));
    }

    private static void Extras(Floor floor, Parts parts, Random random, IReadOnlyList<string> extras, string name, int x, int y, int w, int h)
    {
        var index = 0;

        foreach (var tag in extras)
        {
            if (parts.Own(tag, random, one => one.Footprint[0] <= w && one.Footprint[1] <= h) is not { } extra)
            {
                continue;
            }

            var (ew, eh) = (extra.Piece.Footprint[0], extra.Piece.Footprint[1]);
            var places = new List<(int X, int Y, int Rank, int Shuffle)>();

            // A picture taller than its place reaches up into the rows behind
            // it, so those have to be clear of other pieces too, or a cafe
            // table stands in the kitchen counter it is in front of.
            var rise = extra.Piece.Source is [_, _, _, var tall] && parts.Tile > 0
                ? Math.Max(0, (tall - eh * parts.Tile + parts.Tile - 1) / parts.Tile)
                : 0;

            for (var py = y; py + eh <= y + h; py++)
            {
                for (var px = x; px + ew <= x + w; px++)
                {
                    places.Add((px, py, (py == y ? 0 : 2) + (px == x || px + ew == x + w ? 0 : 1), random.Next()));
                }
            }

            foreach (var (px, py, _, _) in places.OrderBy(one => one.Rank).ThenBy(one => one.Shuffle))
            {
                if (floor.CanAdd(px, py, ew, eh, extra.Piece.Blocks) && !floor.Behind(px, py, ew, rise))
                {
                    floor.Put($"{name}-{tag}-{++index}", tag, extra.Piece, px, py);

                    break;
                }
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

    private static void StatusBoard(Floor floor, Parts parts, Random random, OfficeRules rules, int band, int coreLeft, int coreWidth)
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

                return;
            }
        }
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

    /// <summary>
    /// A corridor floor's south half: rooms off the corridor, each with its
    /// own desks, a meeting room and a lounge among them when the team has
    /// them; returns how many it could seat.
    /// </summary>
    private static int CorridorRooms(
        Floor floor,
        Parts parts,
        Random random,
        OfficeRules rules,
        int band,
        int corridorRow,
        int wanted,
        bool meeting,
        bool lounge)
    {
        var rooms = rules.Rooms ?? new Dictionary<string, OfficeRoomRule>();
        var roomTop = corridorRow + 2;
        var h = floor.Height - 1 - roomTop;
        var kinds = new List<string> { "team-room", "team-room" };

        if (meeting)
        {
            kinds.Add("meeting");
        }

        if (lounge)
        {
            kinds.Add("lounge");
        }

        var min = rooms.TryGetValue("team-room", out var rule) && rule.Min is [var mw, _] ? mw : 6;
        var fits = Math.Max(1, (floor.Width - 1) / (min + 1));

        kinds = [.. kinds.Take(fits)];

        var inside = floor.Width - 2 - (kinds.Count - 1);
        var x = 1;
        var slots = 0;
        var placed = 0;
        var teamRooms = 0;

        floor.Areas.Add(new OfficeArea("corridor", "corridor", 1, band + 1, floor.Width - 2, corridorRow - band, null));

        for (var i = 0; i < kinds.Count; i++)
        {
            var w = inside / kinds.Count + (i < inside % kinds.Count ? 1 : 0);
            var kind = kinds[i];
            var name = kind == "team-room" ? $"team-room-{++teamRooms}" : kind == "meeting" ? "meeting-3" : "lounge";
            var walls = Walls(rules, kind, random);

            // The door at the room's left column, which stays clear as its aisle.
            Room(floor, name, kind, x, roomTop, w, h, roomTop - 1, walls, rooms, parts, random, northDoor: x);

            if (i < kinds.Count - 1)
            {
                for (var yy = roomTop - 1; yy < roomTop + h; yy++)
                {
                    Edge(floor, parts, random, walls, x + w, yy);
                }
            }

            switch (kind)
            {
                case "team-room":
                    var (seats, sat) = TeamRoomDesks(floor, parts, random, x, roomTop, w, h, wanted - placed);

                    slots += seats;
                    placed += sat;
                    break;
                case "meeting":
                    Meeting(floor, parts, random, rules, name, x + 1, roomTop, w - 1, Math.Min(h, 4));
                    break;
                default:
                    Lounge(floor, parts, random, rules, name, x + 1, roomTop + 1, w - 1, 2);
                    break;
            }

            x += w + 1;
        }

        return slots;
    }

    /// <summary>Desks in a team room, back to back, the room's left column kept as its aisle.</summary>
    private static (int Slots, int Placed) TeamRoomDesks(Floor floor, Parts parts, Random random, int x, int y, int w, int h, int wanted)
    {
        var desk = parts.Pick("desk", random, one => one.Footprint[1] == 1 && one.Seats is { Count: > 0 });
        var dw = desk.Piece.Footprint[0];
        var seat = desk.Piece.Seats![0];
        var slots = new List<(int X, int Y)>();

        // First row after the door row is the first seat row; the room's last
        // row stays free to walk along.
        for (var seatRow = y + 1; seatRow + 1 <= y + h - 2; seatRow += 2)
        {
            for (var dx = x + 1; dx + dw <= x + w; dx += dw + 1)
            {
                var deskY = seatRow - seat.Y;

                if (floor.Clear(dx, deskY) && floor.Clear(dx + seat.X, seatRow))
                {
                    slots.Add((dx, deskY));
                }
            }
        }

        var placed = 0;

        foreach (var (dx, dy) in slots.Take(Math.Max(0, wanted)))
        {
            floor.Put($"desk-{floor.Seats.Count + 1}", "desk", desk.Piece, dx, dy, "desk");
            floor.Take(dx + seat.X, dy + seat.Y, 1, 1);
            placed++;
        }

        return (slots.Count, placed);
    }

    /// <summary>Rows of desks, back to back, aisles between blocks; returns how many it could seat.</summary>
    private static int OpenPlan(Floor floor, Parts parts, Random random, OfficeRules rules, int top, int wanted)
    {
        var desk = parts.Pick("desk", random, one => one.Footprint[1] == 1 && one.Seats is { Count: > 0 });
        var w = desk.Piece.Footprint[0];
        var seatDy = desk.Piece.Seats![0].Y;

        // A seat row then a desk row, repeated, leaving the last interior row
        // free as an aisle; blocks of desks with a one-tile aisle between.
        var slots = new List<(int X, int Y)>();

        for (var seatRow = top + 1; seatRow + 1 <= floor.Height - 3; seatRow += 2)
        {
            for (var x = 2; x + w <= floor.Width - 2; x += w + 1)
            {
                var y = seatRow - seatDy;

                if (floor.Free(x, y, w, 1) && floor.Free(x + desk.Piece.Seats[0].X, seatRow, 1, 1))
                {
                    slots.Add((x, y));
                }
            }
        }

        var placed = 0;

        foreach (var (x, y) in slots)
        {
            if (placed >= wanted)
            {
                break;
            }

            floor.Put($"desk-{placed + 1}", "desk", desk.Piece, x, y, "desk");
            floor.Take(x + desk.Piece.Seats[0].X, y + seatDy, 1, 1);
            placed++;
        }

        floor.Areas.Add(new OfficeArea("open-plan", "open-plan", 1, top, floor.Width - 2, floor.Height - 1 - top, Function(rules, "open-plan")));

        return slots.Count;
    }

    private static void Decorate(Floor floor, Parts parts, Random random, int top)
    {
        if (!parts.Has("plant", one => one.Footprint[0] == 1 && one.Footprint[1] == 1))
        {
            return;
        }

        var plant = parts.Pick("plant", random, one => one.Footprint[0] == 1 && one.Footprint[1] == 1);
        var corners = new[] { (1, floor.Height - 2), (floor.Width - 2, floor.Height - 2) };
        var index = 0;

        foreach (var (x, y) in corners)
        {
            // Never on a spot somebody is sent to stand at.
            if (floor.Free(x, y, 1, 1) && !floor.Spots.Values.Any(spot => spot.X == x && spot.Y == y))
            {
                floor.Put($"plant-{++index}", "plant", plant.Piece, x, y);
            }
        }
    }

    private static OfficeScene Scene(Parts parts, Floor floor, List<OfficeSpot> desks, int coreLeft, int band)
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
            Areas: floor.Areas,
            Cast: cast,
            Surface: surface);
    }

    // A picture carries its corner patterns so the page can turn it; the
    // built-in atlas's tile numbers are its patterns, so it needs none.
    private static OfficeAtlas Atlas(OfficeTileset tileset) =>
        new(tileset.Picture, AtlasTiles(tileset), tileset.Lower, tileset.Upper, tileset.Picture is null ? null : tileset.Corners);

    private static int AtlasTiles(OfficeTileset tileset) => tileset.Corners.Values.DefaultIfEmpty(0).Max() + 1;
}
