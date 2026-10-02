namespace Loadout.Core.Teams;

/// <summary>
/// A run's floor, laid out from the rules rather than from a plan written in
/// code: which rooms the floor has comes from the rules' counts, where each goes
/// from its zone and the rooms it should be near or away from, and what is in
/// it from the pieces that say they suit it.
/// </summary>
/// <remarks>
/// <para>
/// The shape: the core in the middle of the north wall, the floor's shared
/// rooms packed into the band either side of it, each with a door onto the
/// corridor that runs the width of the floor below them; the team's area along
/// the south windows, as wide as the team needs, with the lead's office in its
/// window corner and the desks in pods facing each other; and anything left
/// over vacant, bare floor another team's area can take.
/// </para>
/// <para>
/// Which room goes where in the band is chosen by scoring a few hundred
/// seeded arrangements and keeping the best, so the same run gets the same
/// floor and the rules, not the code, decide what is beside what.
/// </para>
/// </remarks>
public static partial class FloorPlanner
{
    // How many arrangements of the band are scored before the best is kept.
    private const int Arrangements = 300;

    // The strongest partition first: the wall between two rooms is the
    // stronger of theirs, so a glass meeting room beside the kitchen has the
    // kitchen's solid wall between them.
    private static readonly string[] Strength = ["solid", "glass", "screen", "planters", "open"];

    /// <summary>A room of the floor's programme, before it has a place.</summary>
    private sealed record Wanted(string Name, string Kind, OfficeRoomRule Rule, int Min, int Max, int Priority);

    /// <summary>A room given a place in the band: its columns and the partition round it.</summary>
    private sealed record Placed(Wanted Room, int X, int W, string Walls);

    /// <summary>A team on a shared floor, with the bays it has and its own seeded choices.</summary>
    private sealed record Tenant(OfficeTenant Who, string Layout, Random Random);

    /// <summary>The columns of each bay of the field: equal, a column's walk between each.</summary>
    internal static List<(int From, int To)> BayColumns(int width, int count)
    {
        var inside = width - 2 - (count - 1);
        var bays = new List<(int From, int To)>();
        var x = 1;

        for (var i = 0; i < count; i++)
        {
            var w = inside / count + (i < inside % count ? 1 : 0);

            bays.Add((x, x + w - 1));
            x += w + 1;
        }

        return bays;
    }

    private static (OfficeScene Scene, int Capacity)? Build(Parts parts, OfficeRules rules, Random random, int team, bool plain, string layout, IReadOnlyList<Tenant>? tenants = null)
    {
        var (floor, depth, band, coreLeft, coreWidth) = Shell(rules);
        var width = floor.Width;
        var height = floor.Height;
        var corridorTop = band + 1;
        var fieldBottom = height - 2;

        // Plain, on a floor with no room below the corridor, the desks go
        // anywhere round the core rather than nowhere.
        var fieldTop = plain && fieldBottom - (band + 3) < 3 ? 1 : band + 3;

        if (fieldBottom - fieldTop < 4 && !plain)
        {
            return null;
        }

        if (!plain)
        {
            floor.Areas.Add(new OfficeArea("corridor", "corridor", 1, corridorTop, width - 2, 2, null));
        }
        Core(floor, parts, random, coreLeft, coreWidth, band);

        var board = StatusBoard(floor, parts, random, rules, band, coreLeft, coreWidth);

        if (!plain)
        {
            // The floor's shared rooms are for everybody on it.
            var programme = Programme(rules, tenants?.Sum(one => one.Who.People) ?? team);
            var arranged = Arrange(programme, rules, random, width, coreLeft, coreWidth);

            foreach (var room in arranged)
            {
                BandRoom(floor, parts, random, rules, room, depth, band, board);
            }

            // Between neighbours in the band, the stronger of their partitions.
            foreach (var (left, right) in arranged.OrderBy(one => one.X).Zip(arranged.OrderBy(one => one.X).Skip(1)))
            {
                if (left.X + left.W + 1 == right.X)
                {
                    var walls = Strength.First(kind => kind == left.Walls || kind == right.Walls);

                    for (var y = 1; y <= band; y++)
                    {
                        Edge(floor, parts, random, walls, left.X + left.W, y);
                    }
                }
            }

            // Band no room takes, four columns or more: an open nook to sit in,
            // furnished as a lounge is, walled off from the rooms beside it.
            var nooks = 0;

            foreach (var (from, to) in new[] { (1, coreLeft - 1), (coreLeft + coreWidth, width - 2) })
            {
                var free = Enumerable.Range(from, to - from + 1).Where(x => !arranged.Any(one => x >= one.X - 1 && x <= one.X + one.W)).ToList();

                foreach (var run in free.Select((x, i) => (x, key: x - i)).GroupBy(one => one.key).Select(group => group.Select(one => one.x).ToList()))
                {
                    if (run.Count >= 4)
                    {
                        var (nx, nw) = (run[0], run.Count);
                        var name = $"nook-{++nooks}";
                        var rule = rules.Rooms is { } all && all.TryGetValue("lounge", out var lounge) ? lounge.Function : "idle";

                        floor.Areas.Add(new OfficeArea(name, "lounge", nx, 1, nw, depth, rule));
                        floor.Reserve(nx, 1, nw, depth);
                        floor.Last.Add(() => Furnish(floor, parts, random, rules, "lounge", name, "floor", nx, 1, nw, depth, back: "n", entry: (nx + nw / 2, depth)));
                    }
                }
            }

            // The ends of each run of rooms: against the core, or open band.
            foreach (var room in arranged)
            {
                foreach (var x in new[] { room.X - 1, room.X + room.W })
                {
                    if (x >= 1 && x < width - 1 && floor.Cells[x, 1] == Cell.Carpet && !arranged.Any(other => other != room && x >= other.X - 1 && x <= other.X + other.W))
                    {
                        for (var y = 1; y <= band; y++)
                        {
                            Edge(floor, parts, random, room.Walls, x, y);
                        }
                    }
                }
            }
        }

        Exits(floor, parts, random, corridorTop + 1, corridorTop);

        var desks = new List<OfficeSpot>();
        List<OfficeTeamSeats>? teams = null;
        int capacity;

        if (tenants is null)
        {
            capacity = TeamArea(floor, parts, rules, random, team, fieldTop, fieldBottom, layout, plain, coreLeft + coreWidth / 2, 1, width - 2, "", null);

            if (floor.LeadSeat is { } lead)
            {
                desks.Add(lead);
            }

            // One each: a bench seats four, and a team of five at two benches
            // leaves three seats empty rather than giving anybody two.
            desks.AddRange(floor.Seats.Take(Math.Max(0, team - desks.Count)));
        }
        else
        {
            // Each team in its own bays, laid out by its own seed, so another
            // team arriving or leaving never moves its desks.
            var bays = BayColumns(width, rules.Bays ?? OfficeRules.BayCount);
            var used = new bool[bays.Count];

            teams = [];
            capacity = 0;

            foreach (var tenant in tenants.OrderBy(one => one.Who.Bay))
            {
                var (from, _) = bays[tenant.Who.Bay];
                var (_, to) = bays[tenant.Who.Bay + tenant.Who.Bays - 1];
                var before = floor.Seats.Count;

                floor.LeadSeat = null;

                var seats = TeamArea(floor, parts, rules, tenant.Random, tenant.Who.People, fieldTop, fieldBottom, tenant.Layout, plain, (from + to) / 2, from, to, $"{tenant.Who.Run}#{tenant.Who.Part}:", tenant.Who.Run);
                var mine = new List<OfficeSpot>();

                if (floor.LeadSeat is { } lead)
                {
                    mine.Add(lead);
                }

                mine.AddRange(floor.Seats.Skip(before).Take(Math.Max(0, tenant.Who.People - mine.Count)));
                teams.Add(new OfficeTeamSeats(tenant.Who.Run, tenant.Who.Part, mine));
                desks.AddRange(mine);
                capacity = Math.Max(capacity, seats);

                for (var bay = tenant.Who.Bay; bay < tenant.Who.Bay + tenant.Who.Bays; bay++)
                {
                    used[bay] = true;
                }
            }

            floor.LeadSeat = null;

            // A bay nobody has: bare floor, for the next team.
            for (var bay = 0; bay < bays.Count; bay++)
            {
                if (!used[bay])
                {
                    floor.Areas.Add(new OfficeArea($"bay-{bay + 1}", "vacant", bays[bay].From, fieldTop, bays[bay].To - bays[bay].From + 1, fieldBottom - fieldTop + 1, null));
                }
            }
        }

        if (!plain)
        {
            Corridor(floor, parts, random, corridorTop, board);
        }

        var scene = Scene(parts, rules, "@floor", floor, desks, coreLeft, band);

        return (teams is null ? scene : scene with { Teams = teams }, capacity);
    }

    /// <summary>
    /// The rooms the floor's band holds, from the rules: every room on this
    /// level that is not the core's, the team's own area or a place on a wall,
    /// as many of each as the rules give a team this size, the ones that
    /// matter most first so they are the last to be left out.
    /// </summary>
    private static List<Wanted> Programme(OfficeRules rules, int team)
    {
        string[] notBand = ["open-plan", "team-room", "status-board", "exit", "cupboard", "lead-office", "lift", "stairs", "toilets"];
        var wanted = new List<Wanted>();

        foreach (var (kind, rule) in (rules.Rooms ?? new Dictionary<string, OfficeRoomRule>()).OrderBy(one => one.Key, StringComparer.Ordinal))
        {
            if (rule.Level != "floor" || rule.Core || notBand.Contains(kind))
            {
                continue;
            }

            var count = rule.CountFor(team);
            var min = rule.Min is [var mw, _] ? Math.Max(2, mw) : 3;
            var max = rule.Max is [var xw, _] ? Math.Max(min, xw) : 9;

            for (var i = 0; i < count; i++)
            {
                // The kitchen and a first meeting room before anything else,
                // then the rest in the order they come, the second of a kind after
                // the first of every other.
                var priority = kind switch { "kitchen" => 0, "meeting" when i == 0 => 1, _ => 2 + i };

                wanted.Add(new Wanted(count > 1 || kind == "meeting" ? $"{kind}-{i + 1}" : kind, kind, rule, min, max, priority));
            }
        }

        return [.. wanted.OrderBy(one => one.Priority).ThenBy(one => one.Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Where each room goes in the band, either side of the core: the best of
    /// many seeded arrangements, scored on the rules. Rooms that cannot all
    /// fit are left out from the least important.
    /// </summary>
    private static List<Placed> Arrange(List<Wanted> programme, OfficeRules rules, Random random, int width, int coreLeft, int coreWidth)
    {
        var segments = new[] { (From: 1, To: coreLeft - 1), (From: coreLeft + coreWidth, To: width - 2) };
        var room = segments.Sum(one => one.To - one.From + 1);

        // Leave out from the end until what is left fits, walls and all.
        var fitting = programme.ToList();

        while (fitting.Count > 0 && fitting.Sum(one => one.Min + 1) > room)
        {
            fitting.RemoveAt(fitting.Count - 1);
        }

        List<Placed>? best = null;
        var bestScore = double.MinValue;

        for (var attempt = 0; attempt < Arrangements; attempt++)
        {
            var order = fitting.OrderBy(_ => random.Next()).ToList();
            var split = random.Next(order.Count + 1);
            var sides = new[] { order.Take(split).ToList(), order.Skip(split).ToList() };
            var placed = new List<Placed>();
            var fits = true;

            for (var side = 0; side < 2 && fits; side++)
            {
                var (from, to) = segments[side];
                var rooms = sides[side];
                var span = to - from + 1;
                var walls = Math.Max(0, rooms.Count - 1);

                if (rooms.Sum(one => one.Min) + walls > span)
                {
                    fits = false;

                    break;
                }

                // Each its least, then what is left shared out a column at a
                // time to those still under their most; what nobody can take is
                // open band, at the outer end or by the core.
                var widths = rooms.Select(one => one.Min).ToArray();
                var spare = span - walls - widths.Sum();

                while (spare > 0 && Enumerable.Range(0, rooms.Count).Any(i => widths[i] < rooms[i].Max))
                {
                    foreach (var i in Enumerable.Range(0, rooms.Count).Where(i => widths[i] < rooms[i].Max))
                    {
                        if (spare > 0)
                        {
                            widths[i]++;
                            spare--;
                        }
                    }
                }

                var x = from + (spare > 0 && random.Next(2) == 0 ? spare : 0);

                for (var i = 0; i < rooms.Count; i++)
                {
                    placed.Add(new Placed(rooms[i], x, widths[i], Partition(rules, rooms[i].Kind, random)));
                    x += widths[i] + 1;
                }
            }

            if (!fits)
            {
                continue;
            }

            var score = Score(placed, rules, width, coreLeft, coreWidth);

            if (score > bestScore)
            {
                (best, bestScore) = (placed, score);
            }
        }

        return best ?? [];
    }

    /// <summary>
    /// How well an arrangement keeps the rules: each room in its zone, and
    /// nearer the rooms it should be near than those it should be away from.
    /// </summary>
    private static double Score(List<Placed> placed, OfficeRules rules, int width, int coreLeft, int coreWidth)
    {
        var score = 0.0;
        var core = coreLeft + coreWidth / 2.0;

        double Centre(Placed one) => one.X + one.W / 2.0;

        double Closeness(double a, double b) => 1 - Math.Min(1, Math.Abs(a - b) / width);

        foreach (var room in placed)
        {
            var outer = room.X == 1 || room.X + room.W == width - 1;
            var byCore = room.X + room.W == coreLeft || room.X == coreLeft + coreWidth;

            score += room.Room.Rule.Zone switch
            {
                "corner" or "perimeter" => outer ? 3 : 0,
                "core" => byCore ? 3 : 0,
                "interior" => outer ? 0 : 1,
                _ => 0,
            };

            foreach (var (word, sign) in new[] { ("near", 1), ("away", -1) })
            {
                var weights = word == "near" ? room.Room.Rule.Near : room.Room.Rule.Away;

                foreach (var (other, weight) in weights ?? new Dictionary<string, int>())
                {
                    var targets = other == "core"
                        ? [core]
                        : placed.Where(one => one != room && one.Room.Kind == other).Select(Centre).ToList();

                    if (targets.Count > 0)
                    {
                        score += sign * weight * 2 * targets.Max(target => Closeness(Centre(room), target));
                    }
                }
            }
        }

        return score;
    }

    /// <summary>Which partition a room gets, chosen from those its rule allows; solid where it says nothing.</summary>
    private static string Partition(OfficeRules rules, string kind, Random random) => Walls(rules, kind, random);

    /// <summary>
    /// A room of the band: its area, its south wall onto the corridor with a
    /// door in it - never behind the status board - and what is in it.
    /// </summary>
    private static void BandRoom(Floor floor, Parts parts, Random random, OfficeRules rules, Placed room, int depth, int band, (int X, int W)? board)
    {
        var (x, w) = (room.X, room.W);
        var name = room.Room.Name;
        var door = x + w / 2;

        // Off the board's columns, to whichever side is nearer the middle.
        if (board is { } b && door >= b.X && door < b.X + b.W)
        {
            door = Enumerable.Range(x, w).Where(col => col < b.X || col >= b.X + b.W).OrderBy(col => Math.Abs(col - (x + w / 2))).DefaultIfEmpty(door).First();
        }

        floor.Areas.Add(new OfficeArea(name, room.Room.Kind, x, 1, w, depth, room.Room.Rule.Function));
        floor.Reserve(x, 1, w, depth);

        for (var xx = x; xx < x + w; xx++)
        {
            if (xx != door)
            {
                Edge(floor, parts, random, room.Walls, xx, band);
            }
        }

        // Keep the way in clear: the tile inside the door is nobody's.
        floor.Last.Add(() => Furnish(floor, parts, random, rules, room.Room.Kind, name, "floor", x, 1, w, depth, back: "n", entry: (door, depth)));
    }

    /// <summary>
    /// The team's area along the south windows: as wide as its desks need,
    /// centred under the core where it can be, with the lead's office in its
    /// window corner; anything either side vacant. Returns how many a floor
    /// laid out this way could seat, lead included.
    /// </summary>
    private static int TeamArea(
        Floor floor,
        Parts parts,
        OfficeRules rules,
        Random random,
        int team,
        int top,
        int bottom,
        string layout,
        bool plain,
        int middle,
        int left,
        int right,
        string prefix,
        string? run)
    {
        // Walls round the team only when there are desks in it: round a lead
        // alone they were a second wall a row outside the office's own.
        var walled = !plain && layout == "corridor" && team > 1;
        var desk = parts.Pick("desk", random, one => one.Footprint[1] == 1 && one.Seats is { Count: > 0 });
        var leadRule = rules.Rooms is { } rooms && rooms.TryGetValue("lead-office", out var found) ? found : new OfficeRoomRule(["exec-desk"]);
        var officeW = Math.Clamp(leadRule.Max is [var lw, _] ? lw : 6, 4, 8);
        var officeH = Math.Clamp(leadRule.Max is [_, var lh] ? lh : 4, 3, Math.Max(3, bottom - top - 1));
        var officeOnLeft = random.Next(2) == 0;

        // The smallest area against the windows whose desks seat the team:
        // at least the lead's office, its wall and a way past, and a row of
        // desks above it; of two the same size, the one nearer twice as wide
        // as it is deep. Centred under the core where the floor allows.
        var each = desk.Piece.Seats!.Count;
        var all = Slots(floor, desk.Piece, left, right, top + (walled ? 1 : 0), bottom, officeOnLeft, officeW, officeH, plain).Count * each;
        var (x0, x1, areaTop) = (left, right, top);
        var sizes =
            from w in Enumerable.Range(Math.Min(officeW + 2, right - left + 1), right - left + 1 - Math.Min(officeW + 2, right - left + 1) + 1)
            from h in Enumerable.Range(Math.Min(officeH + 3, bottom - top + 1), bottom - top + 1 - Math.Min(officeH + 3, bottom - top + 1) + 1)
            orderby w * h, Math.Abs(w - 2 * h)
            select (W: w, H: h);

        foreach (var (w, h) in plain ? [] : sizes)
        {
            var from = Math.Clamp(middle - w / 2, left, right - w + 1);
            var upTo = bottom - h + 1;

            if (Slots(floor, desk.Piece, from, from + w - 1, upTo + (walled ? 1 : 0), bottom, officeOnLeft, officeW, officeH, plain).Count * each >= team - 1)
            {
                (x0, x1, areaTop) = (from, from + w - 1, upTo);

                break;
            }
        }

        var areaKind = walled ? "team-room" : "open-plan";
        var function = rules.Rooms is { } named && named.TryGetValue(areaKind, out var areaRule) ? areaRule.Function : null;

        floor.Areas.Add(new OfficeArea(prefix + (walled ? "team-room-1" : "open-plan"), areaKind, x0, areaTop, x1 - x0 + 1, bottom - areaTop + 1, function, Run: run));

        // A team room has walls and a door. An open plan has nothing round
        // it: its carpet against the bare floor is the edge, and a divider
        // belongs between two teams, not between a team and nobody.
        if (walled)
        {
            var edge = Partition(rules, "team-room", random);
            var door = (x0 + x1) / 2;

            foreach (var x in new[] { x0 - 1, x1 + 1 })
            {
                if (x > left && x < right)
                {
                    for (var y = areaTop; y <= bottom; y++)
                    {
                        Edge(floor, parts, random, edge, x, y);
                    }
                }
            }

            for (var x = Math.Max(left, x0 - 1); x <= Math.Min(right, x1 + 1); x++)
            {
                if (x != door)
                {
                    Edge(floor, parts, random, edge, x, areaTop);
                }
            }
        }

        // The rest of the field: nobody's yet.
        var gap = walled ? 1 : 0;

        if (x0 - gap > left)
        {
            floor.Areas.Add(new OfficeArea(prefix + "vacant-west", "vacant", left, top, x0 - gap - left, bottom - top + 1, null));
        }

        if (x1 + gap < right)
        {
            floor.Areas.Add(new OfficeArea(prefix + "vacant-east", "vacant", x1 + gap + 1, top, right - x1 - gap, bottom - top + 1, null));
        }

        if (areaTop > top)
        {
            floor.Areas.Add(new OfficeArea(prefix + "vacant-north", "vacant", Math.Max(left, x0 - gap), top, Math.Min(right, x1 + gap) - Math.Max(left, x0 - gap) + 1, areaTop - top, null));
        }

        top = areaTop;

        var deskTop = top + (walled ? 1 : 0);

        // The lead's office in the window corner: walls on its two inner
        // sides, the door in the top, the desk turned to face it.
        var ox = officeOnLeft ? x0 : x1 - officeW + 1;
        var oy = bottom - officeH + 1;

        if (!plain && x1 - x0 + 1 >= officeW && oy - 1 > deskTop)
        {
            var walls = Partition(rules, "lead-office", random);
            var officeDoor = officeOnLeft ? ox + officeW - 2 : ox + 1;
            var side = officeOnLeft ? ox + officeW : ox - 1;

            floor.Areas.Add(new OfficeArea(prefix + "lead-office", "lead-office", ox, oy, officeW, officeH, leadRule.Function, Run: run));
            floor.Reserve(ox, oy, officeW, officeH);

            for (var x = Math.Min(ox, side); x <= Math.Max(ox + officeW - 1, side); x++)
            {
                if (x != officeDoor)
                {
                    Edge(floor, parts, random, walls, x, oy - 1);
                }
            }

            for (var y = oy; y <= bottom; y++)
            {
                Edge(floor, parts, random, walls, side, y);
            }

            var lead = Furnish(floor, parts, random, rules, "lead-office", prefix + "lead-office", "floor", ox, oy, officeW, officeH, back: "s", entry: (officeDoor, oy), seatsAs: "lead");

            if (!lead)
            {
                LeadDesk(floor, parts, random, x0, deskTop, prefix);
            }
        }
        else
        {
            LeadDesk(floor, parts, random, x0, deskTop, prefix);
        }

        var slots = Slots(floor, desk.Piece, x0, x1, deskTop, bottom, officeOnLeft, officeW, officeH, plain);
        var turned = Turned(desk.Piece);
        var placed = 0;

        // A pod at a time, the one nearest the windows first; in each, the
        // block nearest the middle of the area, both sides of it, so people
        // face each other rather than an empty desk.
        var pod = (desk.Piece.Sides?.ContainsKey("n") == true ? 4 : 2) + 1;

        foreach (var slot in slots
            .OrderByDescending(one => (one.Y - (one.North ? 1 : 0) - deskTop) / pod)
            .ThenBy(one => Math.Abs(one.X - (x0 + x1) / 2))
            .ThenBy(one => one.North))
        {
            if (placed >= team - 1)
            {
                break;
            }

            var piece = slot.North ? turned : desk.Piece;

            floor.Put($"{prefix}desk-{placed + 1}", "desk", piece, slot.X, slot.Y, "desk", slot.North ? "n" : "s");

            foreach (var seat in piece.Seats ?? [])
            {
                floor.Take(slot.X + seat.X, slot.Y + seat.Y, 1, 1);
                placed++;
            }
        }

        // A plant at each window corner of the area where nothing else is.
        if (parts.Has("plant", one => one.Footprint[0] == 1 && one.Footprint[1] == 1))
        {
            var plant = parts.Pick("plant", random, one => one.Footprint[0] == 1 && one.Footprint[1] == 1);
            var index = 0;

            var corners = new[] { (x0, bottom), (x1, bottom) }.Concat(walled ? [] : new[] { (x0, top), (x1, top) });

            foreach (var (x, y) in corners)
            {
                if (floor.CanAdd(x, y, 1, 1, true) && !floor.Spots.Values.Any(spot => spot.X == x && spot.Y == y))
                {
                    floor.Put($"{prefix}plant-{++index}", "plant", plant.Piece, x, y);
                }
            }
        }

        return 1 + all;
    }

    /// <summary>The lead at a desk of their own in the area's first row, where there is no room for an office.</summary>
    private static void LeadDesk(Floor floor, Parts parts, Random random, int x, int top, string prefix)
    {
        var desk = parts.Pick("exec-desk", random);

        floor.Put($"{prefix}lead-desk", "exec-desk", desk.Piece, x + 1, top + 1, "lead");
    }

    /// <summary>
    /// Where desks can go in an area: pods two rows deep, a row facing the
    /// windows and, where the kit's desk has a back to show, a row facing it
    /// across the monitors, a seat row outside each, an aisle between pods
    /// and between blocks; clear of the lead's office and the walk along the
    /// area's top.
    /// </summary>
    private static List<(int X, int Y, bool North)> Slots(
        Floor floor,
        OfficePiece desk,
        int x0,
        int x1,
        int top,
        int bottom,
        bool officeOnLeft,
        int officeW,
        int officeH,
        bool plain)
    {
        var w = desk.Footprint[0];
        var seat = desk.Seats![0];
        var pods = desk.Sides?.ContainsKey("n") == true;
        // The office, its wall, and the row above the wall, which is the way
        // to its door: a desk there boxed the door in.
        var office = plain ? (X0: -1, X1: -2, Y0: -1) : officeOnLeft
            ? (X0: x0, X1: x0 + officeW, Y0: bottom - officeH - 1)
            : (X0: x1 - officeW, X1: x1, Y0: bottom - officeH - 1);
        var slots = new List<(int X, int Y, bool North)>();

        bool Open(int x, int y) =>
            x >= x0 && x <= x1 && y > top && y <= bottom && floor.Free(x, y, 1, 1)
            && !(x >= office.X0 && x <= office.X1 && y >= office.Y0)
            && !floor.Spots.Values.Append(floor.LeadSeat).OfType<OfficeSpot>().Any(spot => spot.X == x && spot.Y == y);

        // A pod: seat row, desk row facing the windows, then the turned desk
        // row and its seats; one desk row and its seats where desks don't turn.
        var height = pods ? 4 : 2;

        for (var y = top + 1; y + height - 1 <= bottom; y += height + 1)
        {
            for (var x = x0 + 1; x + w - 1 <= x1 - 1; x += w + 1)
            {
                var south = (X: x, Y: y - seat.Y);

                if (Enumerable.Range(x, w).All(cx => Open(cx, south.Y)) && Open(x + seat.X, y))
                {
                    slots.Add((south.X, south.Y, false));
                }

                if (pods)
                {
                    var northY = south.Y + 1;
                    var northSeatY = northY + (0 - seat.Y);

                    if (Enumerable.Range(x, w).All(cx => Open(cx, northY)) && Open(x + w - 1 - seat.X, northSeatY))
                    {
                        slots.Add((x, northY, true));
                    }
                }
            }
        }

        return slots;
    }

    /// <summary>A piece turned half round: its front to the north, its seats on the other side, facing the other way.</summary>
    private static OfficePiece Turned(OfficePiece piece) =>
        piece with
        {
            Seats = piece.Seats?.Select(seat => new OfficeSeat(
                piece.Footprint[0] - 1 - seat.X,
                piece.Footprint[1] - 1 - seat.Y,
                seat.Facing switch { "s" => "n", "n" => "s", "e" => "w", "w" => "e", var other => other })).ToList(),
        };

    /*
        Furnishing a room from what suits it. Its main piece first - the one
        that makes it that room - against the back wall if it says so, in the
        middle otherwise, the largest that fits with its seats inside the room;
        then the extras, each beside or in front of the piece it goes with, or
        along a wall, or as near the main piece as it will go; then a plant in a
        back corner. Every piece passes the same test: on clear floor, off every
        seat and place to stand and the tile inside the door, and never cutting
        any of the floor off. Returns whether the main piece went in.
    */
    private static bool Furnish(
        Floor floor,
        Parts parts,
        Random random,
        OfficeRules rules,
        string kind,
        string name,
        string level,
        int x,
        int y,
        int w,
        int h,
        string back,
        (int X, int Y) entry,
        string? seatsAs = null)
    {
        var rule = rules.Rooms is { } rooms && rooms.TryGetValue(kind, out var found) ? found : null;
        var keep = new HashSet<(int, int)> { entry };
        var main = parts.Suiting(kind, level, "main", rule?.Tags)
            .Where(one => one.Piece.Footprint[0] <= w && one.Piece.Footprint[1] <= h)
            .OrderByDescending(one => one.Piece.Footprint[0] * one.Piece.Footprint[1])
            .ThenBy(_ => random.Next())
            .ToList();
        var seated = seatsAs ?? (kind is "meeting" or "lounge" or "waiting-room" or "break-area" ? name : null);
        OfficeProp? centre = null;

        foreach (var (pieceName, candidate) in main)
        {
            // Facing into the room: turned round when the room's back is its south.
            var turn = back == "s" && candidate.Sides?.ContainsKey("n") == true;
            var piece = turn ? Turned(candidate) : candidate;
            var (pw, ph) = (piece.Footprint[0], piece.Footprint[1]);
            var against = candidate.Suits?.Against ?? "free";
            var places = new List<(int X, int Y, int Rank)>();

            for (var py = y; py + ph <= y + h; py++)
            {
                for (var px = x; px + pw <= x + w; px++)
                {
                    var fromMiddle = Math.Abs(px + pw / 2.0 - (x + w / 2.0)) + Math.Abs(py + ph / 2.0 - (y + h / 2.0));
                    var onBack = back == "n" ? py == y : py + ph == y + h;

                    places.Add((px, py, against == "wall" ? (onBack ? 0 : 100) + (int)(fromMiddle * 2) : (int)(fromMiddle * 2)));
                }
            }

            foreach (var (px, py, _) in places.OrderBy(one => one.Rank))
            {
                var seats = (piece.Seats ?? []).Select(seat => (X: px + seat.X, Y: py + seat.Y)).ToList();

                if (seats.All(seat => seat.X >= x && seat.X < x + w && seat.Y >= y && seat.Y < y + h && !keep.Contains(seat) && floor.Clear(seat.X, seat.Y))
                    && !Overlaps(keep, px, py, pw, ph)
                    && floor.CanAdd(px, py, pw, ph, piece.Blocks))
                {
                    floor.Put($"{name}-{Tag(candidate, kind)}", Tag(candidate, kind), piece, px, py, seated, turn ? "n" : "s");
                    centre = floor.Props[^1];
                    keep.UnionWith(seats);

                    break;
                }
            }

            if (centre is not null)
            {
                break;
            }
        }

        if (centre is null)
        {
            return false;
        }

        // Somewhere to stand at a kitchen: the row in front of the counter.
        if (kind == "kitchen")
        {
            var row = back == "n" ? centre.Y + centre.H : centre.Y - 1;
            var index = 0;

            for (var xx = x; xx < x + w; xx++)
            {
                if (row >= y && row < y + h && floor.Clear(xx, row) && (xx, row) != entry)
                {
                    floor.Spots[$"{name}-{++index}"] = new OfficeSpot(xx, row, back == "n" ? "n" : "s");
                    keep.Add((xx, row));
                }
            }
        }

        // The extras: the rule's, in its order, then anything else that suits
        // the room as an extra; one of each, and only as many as the room has
        // floor for.
        var extras = parts.Suiting(kind, level, "extra", [.. rule?.Tags ?? [], .. rule?.Extras ?? []], own: true)
            .Where(one => one.Piece.Footprint[0] <= w && one.Piece.Footprint[1] <= h)
            .ToList();
        var limit = Math.Max(1, w * h / 6);
        var added = 0;
        var waiting = extras.ToList();

        // Those whose partner is in go first, so a coffee table is in front of
        // the sofa before the armchairs look for it.
        for (var pass = 0; pass < 3 && added < limit && waiting.Count > 0; pass++)
        {
            foreach (var (pieceName, extra) in waiting.ToList())
            {
                if (added >= limit)
                {
                    break;
                }

                var with = extra.Suits?.With;
                var partner = with is { Count: > 0 } ? floor.Props.LastOrDefault(prop => prop.Id.StartsWith(name + "-", StringComparison.Ordinal) && with.Contains(prop.Kind ?? "")) : null;

                if (with is { Count: > 0 } && partner is null && pass < 2)
                {
                    continue;
                }

                if (Place(floor, parts, extra, Tag(extra, kind), name, x, y, w, h, back, keep, partner, random))
                {
                    added++;
                }

                waiting.Remove((pieceName, extra));
            }
        }

        // A plant in a back corner, where the room is big enough to want one.
        if (w >= 3 && h >= 3)
        {
            var decor = parts.Suiting(kind, level, "decor", ["plant"], own: true)
                .Where(one => one.Piece.Footprint is [1, 1] && one.Piece.Place == "floor")
                .OrderBy(_ => random.Next())
                .FirstOrDefault();

            if (decor.Piece is { } plant)
            {
                var row = back == "n" ? y : y + h - 1;

                foreach (var col in new[] { x, x + w - 1 }.OrderBy(_ => random.Next()))
                {
                    if (!keep.Contains((col, row)) && floor.CanAdd(col, row, 1, 1, plant.Blocks))
                    {
                        floor.Put($"{name}-plant", "plant", plant, col, row);

                        break;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>
    /// One extra into a room: beside or in front of the piece it goes with -
    /// in front of one people sit at, so a visitor's chair faces the lead
    /// across the desk; beside one they don't, so the coffee machine stands at
    /// the counter's end - else along the back wall for one that stands against
    /// a wall, else as near the room's middle as it will go.
    /// </summary>
    private static bool Place(
        Floor floor,
        Parts parts,
        OfficePiece extra,
        string tag,
        string name,
        int x,
        int y,
        int w,
        int h,
        string back,
        HashSet<(int, int)> keep,
        OfficeProp? partner,
        Random random)
    {
        var (ew, eh) = (extra.Footprint[0], extra.Footprint[1]);
        var rise = extra.Source is [_, _, _, var tall] && parts.Tile > 0
            ? Math.Max(0, (tall - eh * parts.Tile + parts.Tile - 1) / parts.Tile)
            : 0;
        var places = new List<(int X, int Y, int Rank)>();

        for (var py = y; py + eh <= y + h; py++)
        {
            for (var px = x; px + ew <= x + w; px++)
            {
                int rank;

                if (partner is not null)
                {
                    // Somewhere people sit: seats behind or before it, or on it, as a sofa's are.
                    var seatedAt = floor.Seats.Concat(floor.Spots.Values).Append(floor.LeadSeat).OfType<OfficeSpot>()
                        .Any(spot => spot.X >= partner.X && spot.X < partner.X + partner.W && spot.Y >= partner.Y - 1 && spot.Y <= partner.Y + partner.H);
                    var front = partner.Facing == "n" ? partner.Y - eh : partner.Y + partner.H;
                    var beside = (px + ew == partner.X || px == partner.X + partner.W) && py + eh - 1 >= partner.Y && py <= partner.Y + partner.H - 1;
                    var before = py == front && px + ew > partner.X && px < partner.X + partner.W;

                    rank = seatedAt
                        ? before ? 0 : beside ? 10 : 100
                        : beside ? 0 : before ? 10 : 100;
                    rank += Math.Abs(px + ew / 2 - (partner.X + partner.W / 2));
                }
                else if (extra.Suits?.Against == "wall")
                {
                    var onBack = back == "n" ? py == y : py + eh == y + h;
                    var onFront = back == "n" ? py + eh == y + h : py == y;
                    var onSide = px == x || px + ew == x + w;

                    rank = onBack ? 0 : onSide ? 20 : onFront ? 30 : 200;
                }
                else
                {
                    rank = 50 + (int)(Math.Abs(px + ew / 2.0 - (x + w / 2.0)) + Math.Abs(py + eh / 2.0 - (y + h / 2.0)));
                }

                places.Add((px, py, rank * 1000 + random.Next(1000)));
            }
        }

        foreach (var (px, py, _) in places.OrderBy(one => one.Rank))
        {
            if (!Overlaps(keep, px, py, ew, eh) && floor.CanAdd(px, py, ew, eh, extra.Blocks) && !floor.Behind(px, py, ew, rise))
            {
                floor.Put($"{name}-{tag}", tag, extra, px, py);

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The corridor's furniture: what suits it - a copier, a water cooler -
    /// against the band's wall, clear of the doors and the status board.
    /// </summary>
    private static void Corridor(Floor floor, Parts parts, Random random, int row, (int X, int W)? board)
    {
        foreach (var (name, piece) in parts.Suiting("corridor", "floor", "extra", null, own: true).OrderBy(_ => random.Next()).Take(2))
        {
            var (pw, ph) = (piece.Footprint[0], piece.Footprint[1]);

            if (ph != 1)
            {
                continue;
            }

            foreach (var px in Enumerable.Range(2, floor.Width - 3 - pw).OrderBy(_ => random.Next()))
            {
                var underDoor = Enumerable.Range(px, pw).Any(cx => floor.Cells[cx, row - 1] == Cell.Carpet);
                var onBoard = board is { } b && px < b.X + b.W && px + pw > b.X;

                if (!underDoor && !onBoard && floor.CanAdd(px, row, pw, 1, piece.Blocks)
                    && !floor.Spots.Values.Any(spot => spot.Y == row + 1 && spot.X >= px && spot.X < px + pw))
                {
                    floor.Put($"corridor-{Tag(piece, "corridor")}", Tag(piece, "corridor"), piece, px, row);

                    break;
                }
            }
        }
    }

    private static bool Overlaps(HashSet<(int, int)> cells, int x, int y, int w, int h)
    {
        for (var yy = y; yy < y + h; yy++)
        {
            for (var xx = x; xx < x + w; xx++)
            {
                if (cells.Contains((xx, yy)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // What a piece is called on the floor: its first tag, or the room's kind.
    private static string Tag(OfficePiece piece, string fallback) => piece.Tags.Count > 0 ? piece.Tags[0] : fallback;
}
