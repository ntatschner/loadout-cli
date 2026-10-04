using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Loadout.Core.Teams;

/// <summary>What the building needs of one kind of room.</summary>
/// <param name="Tags">The kit pieces the room is furnished from; one of each group must exist, or the kit's shapes stand in.</param>
/// <param name="Min">The smallest the room may be, in tiles: width and depth.</param>
/// <param name="Max">The largest, or null for no limit.</param>
/// <param name="Count">A fixed number of these rooms, when the number does not depend on the team.</param>
/// <param name="ByTeam">
/// How many for a team of a given size: <c>{"1": 1, "5": 2, "10": 3}</c> is one
/// from a team of one, two from five, three from ten. Wins over Count.
/// </param>
/// <param name="Level">Where in the building: floor (every run's floor), lobby, roof, basement-1, basement-2.</param>
/// <param name="Where">A placement hint the planner honours: corner, ends, core, north-wall.</param>
/// <param name="Core">Whether it belongs to the building's core, in the same place on every floor.</param>
/// <param name="Walls">Wall kinds the planner may choose between: glass, solid.</param>
/// <param name="Function">What the room is for on the dashboard, for its popups: mail, storage, bin, server, questions, controls, summary, arrivals, waiting, schedules, idle.</param>
/// <param name="Extras">
/// Kinds of piece the room takes as well as its own, in order, when the set's
/// kit has them and they fit; the built-in kit has none. A room is furnished
/// from its tags one piece per kind, picked at random, so a fridge counted as a
/// kitchen would sometimes be the whole kitchen: it is an extra instead.
/// </param>
/// <param name="PerHead">One of these rooms for every so many people on the floor: 6 is a phone booth for every six. Wins over Count and ByTeam.</param>
/// <param name="Scope">Whose it is: team (each team its own), floor (one set shared by every team on the floor) or building (one for the whole tower, on the lowest floor with room for it, counted from everybody in the building). A room on the lobby, roof or a basement is the building's whatever it says.</param>
/// <param name="Zone">Where on the floor it belongs: core, perimeter (daylight), interior, corner, entrance or any.</param>
/// <param name="Near">Kinds of room it does well beside, with how much that matters: <c>{"open-plan": 3}</c>.</param>
/// <param name="Away">Kinds of room it does badly beside, the same way: a quiet room away from the kitchen.</param>
/// <param name="Access">Who may use it: everyone (the default), team (the team whose room it is) or lead (that team's lead). People are sent only to rooms they may use; how a room is reached is its walls' and doors' business.</param>
public sealed record OfficeRoomRule(
    [property: JsonPropertyName("tags")] IReadOnlyList<string>? Tags = null,
    [property: JsonPropertyName("min")] IReadOnlyList<int>? Min = null,
    [property: JsonPropertyName("max")] IReadOnlyList<int>? Max = null,
    [property: JsonPropertyName("count")] int? Count = null,
    [property: JsonPropertyName("by-team")] IReadOnlyDictionary<string, int>? ByTeam = null,
    [property: JsonPropertyName("level")] string Level = "floor",
    [property: JsonPropertyName("where")] string? Where = null,
    [property: JsonPropertyName("core")] bool Core = false,
    [property: JsonPropertyName("walls")] IReadOnlyList<string>? Walls = null,
    [property: JsonPropertyName("function")] string? Function = null,
    [property: JsonPropertyName("extras")] IReadOnlyList<string>? Extras = null,
    [property: JsonPropertyName("per-head")] int? PerHead = null,
    [property: JsonPropertyName("scope")] string? Scope = null,
    [property: JsonPropertyName("zone")] string? Zone = null,
    [property: JsonPropertyName("near")] IReadOnlyDictionary<string, int>? Near = null,
    [property: JsonPropertyName("away")] IReadOnlyDictionary<string, int>? Away = null,
    [property: JsonPropertyName("access")] string? Access = null)
{
    /// <summary>Whose a room is.</summary>
    public static readonly IReadOnlyList<string> Scopes = ["team", "floor", "building"];

    /// <summary>Where on a floor a room can belong.</summary>
    public static readonly IReadOnlyList<string> Zones = ["core", "perimeter", "interior", "corner", "entrance", "any"];

    /// <summary>Who may use a room.</summary>
    public static readonly IReadOnlyList<string> Accesses = ["everyone", "team", "lead"];

    /// <summary>How many of these rooms a team of this size gets.</summary>
    public int CountFor(int team)
    {
        if (PerHead is > 0 and var every)
        {
            return (Math.Max(1, team) + every - 1) / every;
        }

        if (ByTeam is { Count: > 0 } table)
        {
            var found = 0;
            var best = int.MinValue;

            foreach (var (from, count) in table)
            {
                if (int.TryParse(from, NumberStyles.None, CultureInfo.InvariantCulture, out var size)
                    && size <= team && size > best)
                {
                    best = size;
                    found = count;
                }
            }

            return found;
        }

        return Count ?? 1;
    }
}

/// <summary>How long the building waits before it moves a team between floors.</summary>
/// <param name="SpillUpSeconds">Too big for its floor this long: it spills onto the floor above.</param>
/// <param name="GiveBackSeconds">Fits on fewer floors this long: it gives one back.</param>
/// <param name="KeptMinutes">How long a finished run keeps its floor, dark.</param>
public sealed record OfficeMoves(
    [property: JsonPropertyName("spill-up-seconds")] int SpillUpSeconds = 20,
    [property: JsonPropertyName("give-back-seconds")] int GiveBackSeconds = 300,
    [property: JsonPropertyName("kept-minutes")] int KeptMinutes = 60);

/// <summary>
/// The design decisions the building is generated from.
/// </summary>
/// <param name="Schema">Always <see cref="OfficeRules.Version"/>.</param>
/// <param name="Floor">Every floor plate's size in tiles: width and depth.</param>
/// <param name="MinFloors">The fewest floors the tower is drawn with, whatever is running.</param>
/// <param name="Rooms">Each kind of room, by name.</param>
/// <param name="Use">Which room a person goes to in each state: working, lead-waiting, briefing, review, merge-gate, failed, free, done.</param>
/// <param name="Moves">How long the building waits before moving a team.</param>
/// <param name="Layouts">The kinds of floor a run may be given, one chosen by its seed: open, corridor.</param>
/// <param name="Storey">How high a storey is in tiles, or null for <see cref="StoreyTiles"/>.</param>
/// <param name="Bays">How many bays a floor's team side is split into for teams to share; null for <see cref="BayCount"/>.</param>
/// <param name="Floors">
/// What each kind of room is floored with, by the name of a kit material less
/// its <c>floor-</c>: <c>{"kitchen": "tile", "lounge": "wood"}</c>. The keys
/// <c>@floor</c>, <c>@lobby</c>, <c>@roof</c> and <c>@basement</c> floor
/// whatever on that level is in no room.
/// </param>
/// <remarks>
/// <para>
/// Built in, from the decisions of 30 Sep 2026 recorded in the specification,
/// and overridden in part by a pack's rules.json: a room named there replaces
/// the built-in room of that name, anything else it names replaces the
/// built-in value, and whatever it leaves out stays as built in.
/// </para>
/// <para>
/// Counts are tables by team size rather than formulas, so anybody can read and
/// edit them, and there is no little language to parse and document.
/// </para>
/// </remarks>
public sealed record OfficeRules(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("floor")] IReadOnlyList<int>? Floor = null,
    [property: JsonPropertyName("min-floors")] int? MinFloors = null,
    [property: JsonPropertyName("rooms")] IReadOnlyDictionary<string, OfficeRoomRule>? Rooms = null,
    [property: JsonPropertyName("use")] IReadOnlyDictionary<string, string>? Use = null,
    [property: JsonPropertyName("moves")] OfficeMoves? Moves = null,
    [property: JsonPropertyName("layouts")] IReadOnlyList<string>? Layouts = null,
    [property: JsonPropertyName("storey")] int? Storey = null,
    [property: JsonPropertyName("floors")] IReadOnlyDictionary<string, string>? Floors = null,
    [property: JsonPropertyName("bays")] int? Bays = null)
{
    /// <summary>
    /// How many bays teams share a floor in where the rules don't say: three,
    /// a dozen tiles each on the built-in floor, about nine metres.
    /// </summary>
    public const int BayCount = 3;

    /// <summary>
    /// What a room of this kind is floored with, or what a level is floored
    /// with outside its rooms when given <c>@</c> and the level; null where the
    /// rules don't say, and the room is floored like the rest of its level.
    /// </summary>
    public string? FloorFor(string kind) =>
        Floors is { } floors && floors.TryGetValue(kind, out var floor) && !string.IsNullOrWhiteSpace(floor) ? floor : null;

    /// <summary>
    /// How high a storey is, in tiles, where the rules don't say: five, so 3.75
    /// metres floor to floor at three quarters of a metre a tile, which is the
    /// scale the people are drawn to. Three was lower than a person can stand.
    /// </summary>
    public const int StoreyTiles = 5;

    /// <summary>What <see cref="Schema"/> has to say.</summary>
    public const string Version = "loadout.rules/1";

    /// <summary>The file a set keeps its rules in, when it has any.</summary>
    public const string FileName = "rules.json";

    /// <summary>
    /// The kinds of floor the planner can lay out: open (one open plan below
    /// the band of rooms) and corridor (a corridor with team rooms off it).
    /// </summary>
    public static readonly IReadOnlyList<string> FloorLayouts = ["open", "corridor"];

    /// <summary>
    /// What may stand between a room and the floor around it: walls (solid,
    /// glass), low screens, a row of planters, or nothing but the room's
    /// name (open).
    /// </summary>
    public static readonly IReadOnlyList<string> Partitions = ["solid", "glass", "screen", "planters", "open"];

    /// <summary>
    /// The parts of a floor that are not rooms the rules list but that pieces
    /// may still suit: the corridor, the core round the lift, the way in.
    /// </summary>
    public static readonly IReadOnlyList<string> Structural = ["corridor", "core", "entrance"];

    /// <summary>The levels a room can be on.</summary>
    public static readonly IReadOnlyList<string> Levels = ["floor", "lobby", "roof", "basement-1", "basement-2"];

    /// <summary>The placement hints the planner understands.</summary>
    /// <remarks>
    /// The last four are for the lobby, roof and basements: <c>band</c>, in the
    /// row of rooms along the north wall beside the core, arranged the way a
    /// floor's band is; <c>entrance</c>, by the front door; <c>south</c>, one of
    /// the rooms off a basement's corridor, which share the space between them;
    /// <c>open</c>, the open floor. A room on one of those levels that says none
    /// goes in the band.
    /// </remarks>
    public static readonly IReadOnlyList<string> Placements = ["corner", "ends", "core", "north-wall", "near-open-plan", "band", "entrance", "south", "open"];

    /// <summary>The states a person can be in, as <see cref="Use"/> names them.</summary>
    public static readonly IReadOnlyList<string> States = ["working", "lead-waiting", "briefing", "review", "merge-gate", "failed", "asks-you", "free", "done"];

    /// <summary>The rules as decided, before any pack changes them.</summary>
    public static OfficeRules Default { get; } = new(
        Version,
        [40, 24],
        10,
        new Dictionary<string, OfficeRoomRule>(StringComparer.Ordinal)
        {
            // Every run's floor.
            ["open-plan"] = new(["desk"], Min: [8, 5], Count: 1, Scope: "team", Zone: "perimeter", Access: "team"),
            ["status-board"] = new(["status-board"], Count: 1, Where: "north-wall", Function: "summary", Scope: "floor", Zone: "core"),
            ["lead-office"] = new(
                ["exec-desk"],
                Min: [4, 3],
                Max: [6, 4],
                Count: 1,
                Where: "corner",
                Walls: ["glass", "solid"],
                Function: "controls",
                Scope: "team",
                Zone: "corner",
                Near: new Dictionary<string, int>(StringComparer.Ordinal) { ["open-plan"] = 3 },
                Access: "lead"),
            ["meeting"] = new(
                ["meeting-4", "meeting-6", "meeting-10"],

                // Room for the smallest table and the way in beside it: at
                // three columns a chair always landed inside the door.
                Min: [4, 3],
                ByTeam: new Dictionary<string, int>(StringComparer.Ordinal) { ["1"] = 1, ["5"] = 2, ["10"] = 3 },
                Where: "near-open-plan",

                // No screens for walls, here or in any built-in room: the Tech
                // set's screen is one framed panel on legs, and a row of them
                // read as a row of seats. A pack whose screen joins up can
                // still name one.
                Walls: ["glass", "solid"],
                Function: "questions",
                Scope: "floor",
                Zone: "interior",
                Near: new Dictionary<string, int>(StringComparer.Ordinal) { ["open-plan"] = 3 },
                Away: new Dictionary<string, int>(StringComparer.Ordinal) { ["kitchen"] = 2 },
                Access: "everyone"),
            ["kitchen"] = new(
                ["kitchen", "coffee"],

                // Wide enough for the counter and what stands beside it: three
                // columns was a counter wall to wall, the coffee machine nowhere.
                Min: [5, 3],
                Count: 1,
                Function: "idle",
                Extras: ["fridge", "cooler", "kitchen-table"],
                Scope: "floor",
                Zone: "perimeter",
                Near: new Dictionary<string, int>(StringComparer.Ordinal) { ["core"] = 2, ["lounge"] = 3 },
                Access: "everyone"),
            ["lounge"] = new(
                ["sofa", "partition-planter"],
                Min: [3, 3],
                ByTeam: new Dictionary<string, int>(StringComparer.Ordinal) { ["1"] = 0, ["6"] = 1 },
                Walls: ["planters", "open"],
                Function: "idle",
                Extras: ["armchair", "coffee-table", "beanbag"],
                Scope: "floor",
                Zone: "perimeter",
                Near: new Dictionary<string, int>(StringComparer.Ordinal) { ["kitchen"] = 3 },
                Away: new Dictionary<string, int>(StringComparer.Ordinal) { ["meeting"] = 1 },
                Access: "everyone"),

            // A corridor floor's rooms off the corridor, each with its own desks.
            ["team-room"] = new(["desk", "partition-screen"], Min: [6, 5], Walls: ["glass", "solid", "planters", "open"], Function: "work", Scope: "team", Zone: "perimeter", Access: "team"),
            ["cupboard"] = new(["cupboard"], Min: [1, 1], Max: [2, 2], Count: 1, Scope: "floor", Zone: "interior"),
            ["storage-cupboard"] = new(["storage"], ByTeam: new Dictionary<string, int>(StringComparer.Ordinal) { ["1"] = 0, ["8"] = 1 }, Scope: "floor", Zone: "interior"),
            ["lift"] = new(["lift"], Count: 1, Core: true, Where: "core", Scope: "floor", Zone: "core"),
            ["stairs"] = new(["stairs"], Count: 1, Core: true, Where: "core", Scope: "floor", Zone: "core"),
            ["toilets"] = new(["toilet"], Count: 1, Core: true, Where: "core", Scope: "floor", Zone: "core"),
            ["exit"] = new(["exit"], Count: 2, Where: "ends"),

            // Facilities, sharing the band with the rooms above as space allows:
            // somewhere to take a call, to read, to print, to stretch, to train.
            ["phone-booth"] = new(["phone-booth"], Min: [2, 2], Max: [3, 4], PerHead: 8, Walls: ["open"], Function: "idle", Scope: "floor", Zone: "interior", Away: new Dictionary<string, int>(StringComparer.Ordinal) { ["kitchen"] = 2 }),
            ["library"] = new(["reading-chair"], Min: [4, 3], Max: [6, 4], ByTeam: new Dictionary<string, int>(StringComparer.Ordinal) { ["1"] = 0, ["6"] = 1 }, Walls: ["glass", "solid"], Function: "idle", Extras: ["storage", "plant"], Scope: "building", Zone: "interior", Away: new Dictionary<string, int>(StringComparer.Ordinal) { ["kitchen"] = 3, ["meeting"] = 1 }),
            ["print-corner"] = new(["stationery"], Min: [3, 3], Max: [4, 4], ByTeam: new Dictionary<string, int>(StringComparer.Ordinal) { ["1"] = 0, ["4"] = 1 }, Walls: ["open"], Extras: ["copier", "storage"], Scope: "floor", Zone: "core", Near: new Dictionary<string, int>(StringComparer.Ordinal) { ["core"] = 2 }),
            ["wellness-room"] = new(["yoga-mat"], Min: [4, 3], Max: [5, 4], ByTeam: new Dictionary<string, int>(StringComparer.Ordinal) { ["1"] = 0, ["10"] = 1 }, Walls: ["solid"], Function: "idle", Extras: ["floor-cushion", "plant"], Scope: "building", Zone: "perimeter", Away: new Dictionary<string, int>(StringComparer.Ordinal) { ["kitchen"] = 3, ["meeting"] = 2 }),
            ["training-room"] = new(["training-desk"], Min: [5, 3], Max: [8, 4], ByTeam: new Dictionary<string, int>(StringComparer.Ordinal) { ["1"] = 0, ["12"] = 1 }, Walls: ["glass", "solid"], Function: "questions", Extras: ["whiteboard", "decor"], Scope: "building", Zone: "interior", Near: new Dictionary<string, int>(StringComparer.Ordinal) { ["meeting"] = 2 }),

            // The rest of the building.
            ["reception"] = new(["reception"], Level: "lobby", Count: 1, Where: "entrance", Function: "arrivals"),
            ["waiting-room"] = new(["sofa"], Level: "lobby", Count: 1, Where: "entrance", Function: "waiting"),
            ["lobby-screen"] = new(["status-board"], Level: "lobby", Count: 1, Where: "north-wall", Function: "schedules"),
            ["break-area"] = new(["pergola", "sofa"], Level: "roof", Count: 1, Where: "open", Function: "idle"),
            ["mail-room"] = new(["mail"], Level: "basement-1", Count: 1, Where: "south", Function: "mail"),
            ["storage"] = new(["storage"], Level: "basement-1", Count: 1, Where: "south", Function: "storage"),
            ["garbage"] = new(["bin"], Level: "basement-2", Count: 1, Where: "south", Function: "bin"),
            ["server-room"] = new(["server"], Level: "basement-2", Count: 1, Where: "south", Function: "server"),

            // Below and above the floors: somewhere to leave a bike and shower,
            // a gym on the roof, IT help by the front door.
            ["bike-store"] = new(["bike-rack"], Min: [6, 3], Max: [8, 4], Level: "basement-1", Count: 1, Where: "band", Walls: ["solid"], Zone: "core"),
            ["showers"] = new(["shower"], Min: [5, 3], Max: [7, 4], Level: "basement-1", Count: 1, Where: "band", Walls: ["solid"], Extras: ["lockers"]),
            ["gym"] = new(["treadmill"], Min: [6, 3], Max: [8, 4], Level: "roof", Count: 1, Where: "band", Walls: ["glass"], Function: "idle", Extras: ["weights-bench"], Zone: "core"),
            ["it-help"] = new(["help-desk"], Min: [5, 3], Max: [7, 4], Level: "lobby", Count: 1, Where: "band", Walls: ["open"], Function: "arrivals", Extras: ["visitor-chair"], Zone: "core"),
        },
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["working"] = "open-plan",
            ["lead-waiting"] = "lead-office",
            ["briefing"] = "meeting|cupboard",
            ["review"] = "meeting|cupboard",
            ["merge-gate"] = "meeting|cupboard",
            ["failed"] = "open-plan",
            ["asks-you"] = "stay",
            ["free"] = "break-area|kitchen|lounge",
            ["done"] = "lift",
        },
        new OfficeMoves(),
        ["open", "corridor"],
        Floors: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Outside any room, by level.
            ["@floor"] = "carpet",
            ["@lobby"] = "stone",
            ["@roof"] = "decking",
            ["@basement"] = "concrete",

            // Where people work on carpet tiles; where they walk, the darker
            // walkway; somewhere to sit or eat, something harder or warmer.
            ["open-plan"] = "carpet",
            ["team-room"] = "carpet",
            ["corridor"] = "walkway",
            ["core"] = "stone",
            ["lead-office"] = "wood",
            ["meeting"] = "walkway",
            ["kitchen"] = "tile",
            ["lounge"] = "wood",
            ["reception"] = "wood",
            ["waiting-room"] = "walkway",
            ["seating"] = "wood",
            ["entrance"] = "mat",
            ["library"] = "wood",
            ["wellness-room"] = "wood",
            ["training-room"] = "walkway",
            ["showers"] = "tile",
            ["gym"] = "concrete",

            // Floor no team has taken yet: bare, so it reads as empty rather
            // than as a room nobody is in.
            ["vacant"] = "concrete",
        });

    /// <summary>These rules with a pack's changes laid over them.</summary>
    public OfficeRules With(OfficeRules? changes)
    {
        if (changes is null)
        {
            return this;
        }

        var rooms = new Dictionary<string, OfficeRoomRule>(Rooms ?? new Dictionary<string, OfficeRoomRule>(), StringComparer.Ordinal);

        foreach (var (name, room) in changes.Rooms ?? new Dictionary<string, OfficeRoomRule>())
        {
            rooms[name] = room;
        }

        var use = new Dictionary<string, string>(Use ?? new Dictionary<string, string>(), StringComparer.Ordinal);

        foreach (var (state, room) in changes.Use ?? new Dictionary<string, string>())
        {
            use[state] = room;
        }

        return this with
        {
            Floor = changes.Floor ?? Floor,
            MinFloors = changes.MinFloors ?? MinFloors,
            Rooms = rooms,
            Use = use,
            Moves = changes.Moves ?? Moves,
            Layouts = changes.Layouts ?? Layouts,
            Storey = changes.Storey ?? Storey,
            Floors = Merge(Floors, changes.Floors),
            Bays = changes.Bays ?? Bays,
        };
    }

    // A set's map laid over the built-in one, key by key, so a set naming one
    // room's floor keeps every other room's.
    private static Dictionary<string, string>? Merge(IReadOnlyDictionary<string, string>? under, IReadOnlyDictionary<string, string>? over)
    {
        if (under is null && over is null)
        {
            return null;
        }

        var merged = new Dictionary<string, string>(under ?? new Dictionary<string, string>(), StringComparer.Ordinal);

        foreach (var (key, value) in over ?? new Dictionary<string, string>())
        {
            merged[key] = value;
        }

        return merged;
    }

    /// <summary>Every tag some room is furnished from.</summary>
    public IReadOnlySet<string> Tags() =>
        (Rooms ?? new Dictionary<string, OfficeRoomRule>())
            .Values
            .SelectMany(room => room.Tags ?? [])
            .ToHashSet(StringComparer.Ordinal);
}

/// <summary>Reading a pack's rules and saying what is wrong with them.</summary>
public static class OfficeRuleBook
{
    /// <summary>The built-in rules with a set's rules.json over them, and what is wrong with the result.</summary>
    public static (OfficeRules Rules, IReadOnlyList<string> Problems) Read(string root, string set)
    {
        var path = Path.Combine(root, set, OfficeRules.FileName);

        if (!OfficeArt.Names(set) || !File.Exists(path))
        {
            return (OfficeRules.Default, []);
        }

        OfficeRules? changes;

        try
        {
            changes = JsonSerializer.Deserialize<OfficeRules>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return (OfficeRules.Default, [$"{OfficeRules.FileName} could not be read: {ex.Message}"]);
        }

        if (changes is null)
        {
            return (OfficeRules.Default, [$"{OfficeRules.FileName} is empty."]);
        }

        var problems = new List<string>();

        if (changes.Schema != OfficeRules.Version)
        {
            problems.Add($"{OfficeRules.FileName}: schema is '{changes.Schema}'; this Loadout reads '{OfficeRules.Version}'.");
        }

        var merged = OfficeRules.Default.With(changes);

        problems.AddRange(Problems(merged));

        return (merged, problems);
    }

    /// <summary>Everything wrong with a set of rules, as sentences naming the room.</summary>
    public static IReadOnlyList<string> Problems(OfficeRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var problems = new List<string>();

        if (rules.Floor is not [var width, var depth] || width is < 12 or > 64 || depth is < 8 or > 64)
        {
            problems.Add("floor has to be a width of 12 to 64 tiles and a depth of 8 to 64.");
        }

        if (rules.Bays is < 1 or > 6)
        {
            problems.Add($"bays is {rules.Bays}; a floor is shared in 1 to 6.");
        }

        if (rules.Storey is < 3 or > 10)
        {
            problems.Add($"storey is {rules.Storey} tiles; it has to be 3 to 10.");
        }

        if (rules.MinFloors is < 1 or > 200)
        {
            problems.Add($"min-floors is {rules.MinFloors}; it has to be 1 to 200.");
        }

        if (rules.Moves is { } moves
            && (moves.SpillUpSeconds < 0 || moves.GiveBackSeconds < 0 || moves.KeptMinutes < 0))
        {
            problems.Add("moves cannot be negative.");
        }

        if (rules.Layouts is { } layouts)
        {
            if (layouts.Count == 0)
            {
                problems.Add("layouts is empty; name at least one of open, corridor.");
            }

            foreach (var layout in layouts.Where(one => !OfficeRules.FloorLayouts.Contains(one)))
            {
                problems.Add($"layouts names '{layout}'; a floor is laid out as one of {string.Join(", ", OfficeRules.FloorLayouts)}.");
            }
        }

        var rooms = rules.Rooms ?? new Dictionary<string, OfficeRoomRule>();

        foreach (var (name, room) in rooms)
        {
            var called = $"room '{name}'";

            if (room.Tags is not { Count: > 0 })
            {
                problems.Add($"{called} names no tags, so there is nothing to furnish it with.");
            }

            if (!OfficeRules.Levels.Contains(room.Level))
            {
                problems.Add($"{called} is on level '{room.Level}'; it has to be one of {string.Join(", ", OfficeRules.Levels)}.");
            }

            if (room.Where is { } where && !OfficeRules.Placements.Contains(where))
            {
                problems.Add($"{called} is placed '{where}'; it has to be one of {string.Join(", ", OfficeRules.Placements)}.");
            }

            if (room.Min is { } min && (min is not [var mw, var md] || mw < 1 || md < 1))
            {
                problems.Add($"{called} min has to be a width and depth of at least 1.");
            }

            if (room.Max is { } max && (max is not [var xw, var xd] || xw < 1 || xd < 1))
            {
                problems.Add($"{called} max has to be a width and depth of at least 1.");
            }
            else if (room.Min is [var lw, var ld] && room.Max is [var hw, var hd] && (hw < lw || hd < ld))
            {
                problems.Add($"{called} max {hw}x{hd} is smaller than its min {lw}x{ld}.");
            }

            if (room.Count is < 0)
            {
                problems.Add($"{called} count is {room.Count}; it cannot be negative.");
            }

            foreach (var (from, count) in room.ByTeam ?? new Dictionary<string, int>())
            {
                if (!int.TryParse(from, NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size < 1)
                {
                    problems.Add($"{called} by-team has '{from}'; each key has to be a team size of 1 or more.");
                }

                if (count < 0)
                {
                    problems.Add($"{called} by-team gives {count} for {from}; it cannot be negative.");
                }
            }

            foreach (var wall in room.Walls ?? [])
            {
                if (!OfficeRules.Partitions.Contains(wall))
                {
                    problems.Add($"{called} has walls '{wall}'; they have to be one of {string.Join(", ", OfficeRules.Partitions)}.");
                }
            }

            if (room.PerHead is < 1)
            {
                problems.Add($"{called} per-head is {room.PerHead}; it has to be one room for every 1 or more people.");
            }

            if (room.Scope is { } scope && !OfficeRoomRule.Scopes.Contains(scope))
            {
                problems.Add($"{called} scope is '{scope}'; it has to be one of {string.Join(", ", OfficeRoomRule.Scopes)}.");
            }
            else if (room.Scope is "team" or "floor" && room.Level != "floor")
            {
                problems.Add($"{called} is on level '{room.Level}' with scope '{room.Scope}'; a room off the floors is the building's, so its scope can only be building.");
            }

            if (room.Zone is { } zone && !OfficeRoomRule.Zones.Contains(zone))
            {
                problems.Add($"{called} zone is '{zone}'; it has to be one of {string.Join(", ", OfficeRoomRule.Zones)}.");
            }

            if (room.Access is { } access && !OfficeRoomRule.Accesses.Contains(access))
            {
                problems.Add($"{called} access is '{access}'; it has to be one of {string.Join(", ", OfficeRoomRule.Accesses)}.");
            }

            foreach (var (word, weights) in new[] { ("near", room.Near), ("away", room.Away) })
            {
                foreach (var (other, weight) in weights ?? new Dictionary<string, int>())
                {
                    if (!rooms.ContainsKey(other) && !OfficeRules.Structural.Contains(other))
                    {
                        problems.Add($"{called} {word} names '{other}', which is no room.");
                    }

                    if (weight is < 1 or > 10)
                    {
                        problems.Add($"{called} {word} gives '{other}' {weight}; it has to be 1 to 10.");
                    }
                }
            }
        }

        foreach (var (state, where) in rules.Use ?? new Dictionary<string, string>())
        {
            if (!OfficeRules.States.Contains(state))
            {
                problems.Add($"use names state '{state}'; the states are {string.Join(", ", OfficeRules.States)}.");
            }

            foreach (var room in where.Split('|'))
            {
                if (room is not ("stay" or "lift") && !rooms.ContainsKey(room))
                {
                    problems.Add($"use sends '{state}' to '{room}', and there is no such room.");
                }
            }
        }

        return problems;
    }
}
