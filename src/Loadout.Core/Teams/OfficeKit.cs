using System.Text.Json;
using System.Text.Json.Serialization;

namespace Loadout.Core.Teams;

/// <summary>A Wang tileset: which tile to draw for each mix of two terrains at a cell's corners.</summary>
/// <param name="Picture">The tile atlas, or null to draw in the kit's colours.</param>
/// <param name="Lower">The terrain the tileset calls lower (carpet, say).</param>
/// <param name="Upper">The terrain it calls upper (wall, glass).</param>
/// <param name="Corners">
/// For each corner pattern, the atlas tile to draw. A pattern is four letters,
/// l or u, for the north-west, north-east, south-west and south-east corners:
/// "llll" is all lower, "uuul" all upper but the south-east. All sixteen are
/// needed, or some cell somewhere has nothing to draw.
/// </param>
public sealed record OfficeTileset(
    [property: JsonPropertyName("picture")] string? Picture,
    [property: JsonPropertyName("lower")] string Lower,
    [property: JsonPropertyName("upper")] string Upper,
    [property: JsonPropertyName("corners")] IReadOnlyDictionary<string, int> Corners);

/// <summary>A place to sit at a piece, relative to its footprint's top left tile.</summary>
public sealed record OfficeSeat(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("facing")] string Facing = "s");

/// <summary>Frames of a piece that moves: a screen, a coffee machine.</summary>
/// <param name="Frames">Each frame as x, y, width and height in the piece's picture.</param>
/// <param name="Fps">Frames a second.</param>
public sealed record OfficePieceAnimation(
    [property: JsonPropertyName("frames")] IReadOnlyList<IReadOnlyList<int>> Frames,
    [property: JsonPropertyName("fps")] double Fps = 4);

/// <summary>
/// Where a piece belongs and what it is for there, so a room is furnished from
/// what suits it rather than from whatever shares a tag.
/// </summary>
/// <param name="Rooms">The kinds of room it may stand in, or * for any.</param>
/// <param name="Levels">The levels it may be on (floor, lobby, roof, basement-1, basement-2), or * or null for any.</param>
/// <param name="Role">
/// What it is to the room: main (what makes the room that room - the meeting
/// table, the kitchen counter), extra (goes with the main piece where it
/// fits), divider (what a room may be marked off with instead of a wall),
/// decor (a plant, a picture) or clutter (on a desk or the floor, never in
/// anybody's way).
/// </param>
/// <param name="Against">What its back goes to: wall, window, or free for standing in the room.</param>
/// <param name="With">Pieces, by tag, it is grouped round where both are in a room: an armchair with the coffee table.</param>
public sealed record OfficeSuits(
    [property: JsonPropertyName("rooms")] IReadOnlyList<string>? Rooms = null,
    [property: JsonPropertyName("levels")] IReadOnlyList<string>? Levels = null,
    [property: JsonPropertyName("role")] string Role = "extra",
    [property: JsonPropertyName("against")] string? Against = null,
    [property: JsonPropertyName("with")] IReadOnlyList<string>? With = null)
{
    /// <summary>The parts a piece can play in a room.</summary>
    public static readonly IReadOnlyList<string> Roles = ["main", "extra", "divider", "decor", "clutter"];

    /// <summary>What a piece can stand with its back to.</summary>
    public static readonly IReadOnlyList<string> Backs = ["wall", "window", "free"];

    /// <summary>Whether it may go in a room of this kind on this level.</summary>
    public bool Fits(string room, string level) =>
        (Rooms is null || Rooms.Contains("*") || Rooms.Contains(room))
        && (Levels is null || Levels.Contains("*") || Levels.Contains(level));
}

/// <summary>One part the building is furnished from.</summary>
/// <param name="Picture">The picture it is cut from, or null to draw it in the kit's shapes.</param>
/// <param name="Source">Where in the picture: x, y, width, height.</param>
/// <param name="Footprint">How many tiles it stands on: width and depth.</param>
/// <param name="Tags">What rules may ask for it as: desk, exec-desk, meeting-6, sofa, lift...</param>
/// <param name="Blocks">Whether nobody can walk through it.</param>
/// <param name="Depth">floor (always under people) or sorted (in front of or behind them).</param>
/// <param name="Place">floor, wall-north, wall-any, desk-top, ceiling or facade.</param>
/// <param name="Seats">Where people sit at it, if they do.</param>
/// <param name="Animation">Its frames, if it moves.</param>
/// <param name="Suits">Where it belongs and what it is for there; null to be found by its tags alone.</param>
/// <param name="Sides">
/// Where in the picture it is seen with its front facing each way (n, e, s, w):
/// what a floor turned a quarter at a time needs. The south side is the one
/// <paramref name="Source"/> gives. A floor turns only once every picture on
/// it has all four.
/// </param>
public sealed record OfficePiece(
    [property: JsonPropertyName("picture")] string? Picture,
    [property: JsonPropertyName("source")] IReadOnlyList<int>? Source,
    [property: JsonPropertyName("footprint")] IReadOnlyList<int> Footprint,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("blocks")] bool Blocks = true,
    [property: JsonPropertyName("depth")] string Depth = "sorted",
    [property: JsonPropertyName("place")] string Place = "floor",
    [property: JsonPropertyName("seats")] IReadOnlyList<OfficeSeat>? Seats = null,
    [property: JsonPropertyName("animation")] OfficePieceAnimation? Animation = null,
    [property: JsonPropertyName("sides")] IReadOnlyDictionary<string, IReadOnlyList<int>>? Sides = null,
    [property: JsonPropertyName("suits")] OfficeSuits? Suits = null);

/// <summary>
/// A pixel-art image laid over the faces of the neighbourhood the page
/// generates round the tower: one tile of it per bay and storey of a building,
/// or per tile of ground, so it turns and zooms with the meshes it covers.
/// </summary>
/// <param name="Picture">The image, in the set, tileable in both directions.</param>
/// <param name="Size">How much of a face one tile covers, in art pixels: a bay's width and a storey's height for a facade.</param>
/// <param name="Night">The same tile after dark, lit windows and all; null draws the day tile dimmed.</param>
public sealed record OfficeMaterial(
    [property: JsonPropertyName("picture")] string Picture,
    [property: JsonPropertyName("size")] IReadOnlyList<int> Size,
    [property: JsonPropertyName("night")] string? Night = null);

/// <summary>The pieces the tower's outside is assembled from, by piece name.</summary>
/// <param name="Bays">One module of a storey; several give the facade some variety.</param>
/// <param name="Corner">The module at each end of a storey, mirrored for the right.</param>
/// <param name="Lobby">Modules of the two-storey lobby front.</param>
/// <param name="Entrance">The lobby's entrance module.</param>
/// <param name="Crown">The top of the tower.</param>
/// <param name="Basement">A module of the storeys below the street.</param>
/// <param name="Windows">Where the glass is in a bay module, x, y, width, height, for lit windows and silhouettes.</param>
public sealed record OfficeFacade(
    [property: JsonPropertyName("bays")] IReadOnlyList<string> Bays,
    [property: JsonPropertyName("corner")] string? Corner = null,
    [property: JsonPropertyName("lobby")] IReadOnlyList<string>? Lobby = null,
    [property: JsonPropertyName("entrance")] string? Entrance = null,
    [property: JsonPropertyName("crown")] string? Crown = null,
    [property: JsonPropertyName("basement")] string? Basement = null,
    [property: JsonPropertyName("windows")] IReadOnlyList<IReadOnlyList<int>>? Windows = null);

/// <summary>One module of the tower's outside as the page draws it: the picture and where in it.</summary>
/// <param name="Picture">The image, in the set.</param>
/// <param name="Source">Where the module is in it: x, y, width and height.</param>
public sealed record OfficeFacadeModule(
    [property: JsonPropertyName("picture")] string Picture,
    [property: JsonPropertyName("source")] IReadOnlyList<int> Source);

/// <summary>
/// A kit's facade with its pieces looked up, for the page: what to draw each
/// storey of the tower with, rather than the names of the pieces to find it in.
/// </summary>
/// <param name="Bays">The storeys' modules, at least one.</param>
/// <param name="Corner">The module at each end of a storey, or null for a bay there too.</param>
/// <param name="Lobby">The lobby's modules, or null for the page's own lobby front.</param>
/// <param name="Entrance">The lobby's entrance, or null.</param>
/// <param name="Crown">The top of the tower, or null.</param>
/// <param name="Basement">A storey below the street, or null.</param>
/// <param name="Windows">Where the glass is in a bay module, for lit windows and silhouettes.</param>
public sealed record OfficeFacadeArt(
    [property: JsonPropertyName("bays")] IReadOnlyList<OfficeFacadeModule> Bays,
    [property: JsonPropertyName("corner")] OfficeFacadeModule? Corner,
    [property: JsonPropertyName("lobby")] IReadOnlyList<OfficeFacadeModule>? Lobby,
    [property: JsonPropertyName("entrance")] OfficeFacadeModule? Entrance,
    [property: JsonPropertyName("crown")] OfficeFacadeModule? Crown,
    [property: JsonPropertyName("basement")] OfficeFacadeModule? Basement,
    [property: JsonPropertyName("windows")] IReadOnlyList<IReadOnlyList<int>>? Windows)
{
    /// <summary>
    /// The kit's facade with each piece's picture, or null where it has none, or
    /// where any piece it names has no picture to draw: half a facade drawn in
    /// art and half in shapes looks worse than either.
    /// </summary>
    public static OfficeFacadeArt? For(OfficeKit kit)
    {
        ArgumentNullException.ThrowIfNull(kit);

        if (kit.Facade is not { Bays.Count: > 0 } facade)
        {
            return null;
        }

        var missing = false;

        OfficeFacadeModule? Module(string? name)
        {
            if (name is null)
            {
                return null;
            }

            if (kit.Pieces.TryGetValue(name, out var piece) && piece.Picture is { } picture && piece.Source is { Count: 4 } source)
            {
                return new OfficeFacadeModule(picture, source);
            }

            missing = true;

            return null;
        }

        var art = new OfficeFacadeArt(
            [.. facade.Bays.Select(Module).OfType<OfficeFacadeModule>()],
            Module(facade.Corner),
            facade.Lobby is { } lobby ? [.. lobby.Select(Module).OfType<OfficeFacadeModule>()] : null,
            Module(facade.Entrance),
            Module(facade.Crown),
            Module(facade.Basement),
            facade.Windows);

        return missing ? null : art;
    }
}

/// <summary>
/// A box of parts the building is generated from.
/// </summary>
/// <param name="Schema">Always <see cref="OfficeKit.Version"/>.</param>
/// <param name="Tile">A tile's size in pixels.</param>
/// <param name="Tilesets">Floor and wall tilesets by name: carpet-wall, carpet-glass, wood, tiled, marble, decking, street, concrete.</param>
/// <param name="Pieces">Every part, by name.</param>
/// <param name="Facade">What the outside is made of, or null to draw it in the kit's shapes.</param>
/// <param name="Skins">For each role, the sheets people in it may be drawn with; "worker" for any other.</param>
/// <param name="Sheets">The sheets, by name, as a scene holds them.</param>
/// <param name="Materials">Images laid over the neighbourhood round the tower, by material name, or null to draw it in code.</param>
/// <remarks>
/// <para>
/// A scene is one room drawn by hand. A kit is the parts a building is made
/// from, and the floor planner lays the rooms out from it, the same way every
/// time for a given run. What the planner produces is a scene, so everything a
/// scene is checked for still holds.
/// </para>
/// <para>
/// A pack need not have a part for every tag. A tag the rules furnish from and
/// the pack lacks is drawn from the built-in kit's shapes instead, and
/// <c>team office check</c> lists which, so a pack can be built up a piece at
/// a time.
/// </para>
/// </remarks>
public sealed record OfficeKit(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("tile")] int Tile,
    [property: JsonPropertyName("tilesets")] IReadOnlyDictionary<string, OfficeTileset> Tilesets,
    [property: JsonPropertyName("pieces")] IReadOnlyDictionary<string, OfficePiece> Pieces,
    [property: JsonPropertyName("facade")] OfficeFacade? Facade = null,
    [property: JsonPropertyName("skins")] IReadOnlyDictionary<string, IReadOnlyList<string>>? Skins = null,
    [property: JsonPropertyName("sheets")] IReadOnlyDictionary<string, OfficeSheet>? Sheets = null,
    [property: JsonPropertyName("materials")] IReadOnlyDictionary<string, OfficeMaterial>? Materials = null)
{
    /// <summary>
    /// The surfaces the neighbourhood is made of, which a kit may give images
    /// for. Any it leaves out are drawn in code: flat colour with windows.
    /// </summary>
    public static readonly IReadOnlyList<string> MaterialNames =
        ["facade-glass", "facade-brick", "facade-concrete", "roof", "road", "pavement", "grass", "canopy", "trunk"];

    /// <summary>
    /// What a floor's material is called before its name: <c>floor-wood</c> is
    /// what a room the rules floor with <c>wood</c> is drawn with.
    /// </summary>
    public const string FloorMaterial = "floor-";

    /// <summary>Whether a kit may give a material this name: one of the neighbourhood's, or a floor.</summary>
    public static bool IsMaterial(string name) =>
        MaterialNames.Contains(name, StringComparer.Ordinal)
        || (name.StartsWith(FloorMaterial, StringComparison.Ordinal) && name.Length > FloorMaterial.Length
            && name[FloorMaterial.Length..].All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'));

    /// <summary>What <see cref="Schema"/> has to say.</summary>
    public const string Version = "loadout.kit/1";

    /// <summary>The file a set keeps its kit in.</summary>
    public const string FileName = "kit.json";

    /// <summary>Where a piece can go.</summary>
    public static readonly IReadOnlyList<string> Places = ["floor", "wall-north", "wall-any", "desk-top", "ceiling", "facade"];

    /// <summary>All sixteen corner patterns, north-west, north-east, south-west, south-east.</summary>
    public static readonly IReadOnlyList<string> CornerPatterns =
    [
        .. Enumerable.Range(0, 16).Select(bits => string.Concat(
            Enumerable.Range(0, 4).Select(corner => (bits >> (3 - corner) & 1) == 1 ? 'u' : 'l'))),
    ];

    /// <summary>The pieces that answer to a tag, by name.</summary>
    public IReadOnlyList<string> Tagged(string tag) =>
        [.. Pieces.Where(one => one.Value.Tags.Contains(tag)).Select(one => one.Key).OrderBy(name => name, StringComparer.Ordinal)];

    /// <summary>
    /// The kit drawn when no pack is installed, or a pack lacks a part: every
    /// tag the built-in rules use, each a shape in the kit's colours.
    /// </summary>
    /// <remarks>
    /// No pictures at all: what the building is drawn with where no set can be
    /// used, not even the built-in one. Footprints and seats are real, so what
    /// the planner lays out with these is what it lays out with any pack.
    /// </remarks>
    public static OfficeKit Kit()
    {
        static OfficePiece Shape(int w, int h, string[] tags, OfficeSeat[]? seats = null, string place = "floor", bool blocks = true, string depth = "sorted") =>
            new(null, null, [w, h], tags, blocks, depth, place, seats);

        // Tile n is corner pattern n, so the page, drawing these in the kit's
        // colours with no picture, can tell from a tile's number which corners
        // are wall and draw the wall's edge where it runs.
        static OfficeTileset Plain(string lower, string upper) =>
            new(null, lower, upper, CornerPatterns.Select((pattern, index) => (pattern, index)).ToDictionary(one => one.pattern, one => one.index, StringComparer.Ordinal));

        var pieces = new Dictionary<string, OfficePiece>(StringComparer.Ordinal)
        {
            ["desk"] = Shape(3, 1, ["desk"], [new(1, -1, "s")]),
            ["exec-desk"] = Shape(3, 1, ["exec-desk"], [new(1, -1, "s")]),
            ["visitor-chair"] = Shape(1, 1, ["visitor-chair"], [new(0, 0, "n")], blocks: false),
            ["meeting-4"] = Shape(2, 2, ["meeting-4"], [new(0, -1, "s"), new(1, -1, "s"), new(0, 2, "n"), new(1, 2, "n")]),
            ["meeting-6"] = Shape(3, 2, ["meeting-6"], [new(0, -1, "s"), new(1, -1, "s"), new(2, -1, "s"), new(0, 2, "n"), new(1, 2, "n"), new(2, 2, "n")]),
            ["meeting-10"] = Shape(5, 2, ["meeting-10"], [.. Enumerable.Range(0, 5).SelectMany(x => new OfficeSeat[] { new(x, -1, "s"), new(x, 2, "n") })]),
            ["status-board"] = Shape(3, 1, ["status-board"], place: "wall-north", blocks: false),
            ["kitchen"] = Shape(3, 1, ["kitchen"]),
            ["coffee"] = Shape(1, 1, ["coffee"]),
            ["sofa"] = Shape(2, 1, ["sofa"], [new(0, 0, "s"), new(1, 0, "s")], blocks: false),
            ["cupboard"] = Shape(1, 1, ["cupboard"]),
            ["storage"] = Shape(2, 1, ["storage"]),
            ["lift"] = Shape(2, 1, ["lift"], place: "wall-north"),
            ["stairs"] = Shape(2, 2, ["stairs"], blocks: false, depth: "floor"),
            ["toilet"] = Shape(1, 1, ["toilet"]),
            ["exit"] = Shape(1, 1, ["exit"], place: "wall-any", blocks: false),
            ["plant"] = Shape(1, 1, ["plant", "decor"]),
            ["screen"] = Shape(1, 1, ["partition-screen"]),
            ["planter-box"] = Shape(1, 1, ["partition-planter"]),
            ["reception"] = Shape(3, 1, ["reception"], [new(1, -1, "s")]),
            ["pergola"] = Shape(3, 2, ["pergola"], blocks: false),
            ["mail"] = Shape(3, 1, ["mail"]),
            ["bin"] = Shape(2, 1, ["bin"]),
            ["server"] = Shape(1, 1, ["server"]),
        };

        // Thirty-two pixel tiles, the resolution the building's art is made at:
        // people about seventy-two pixels tall.
        return new OfficeKit(
            Version,
            32,
            new Dictionary<string, OfficeTileset>(StringComparer.Ordinal)
            {
                ["carpet-wall"] = Plain("carpet", "wall"),
                ["carpet-glass"] = Plain("carpet", "glass"),
            },
            pieces);
    }
}

/// <summary>A kit, and what is wrong with it.</summary>
/// <param name="Kit">The kit as read, or null where it could not be read at all.</param>
/// <param name="Rules">The rules it is to be laid out by: the built-in ones with the set's rules.json over them.</param>
/// <param name="Problems">Each thing wrong, as a sentence; empty when it can be used.</param>
/// <param name="Missing">Tags the rules furnish from that the kit has no part for; drawn from the built-in kit instead.</param>
public sealed record OfficeKitCheck(
    OfficeKit? Kit,
    OfficeRules Rules,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Missing)
{
    /// <summary>Whether the planner may use it.</summary>
    public bool Fit => Kit is not null && Problems.Count == 0;
}

/// <summary>Reading a set's kit and saying whether the building can be made from it.</summary>
public static class OfficeKits
{
    /// <summary>Whether a set is a kit rather than a single scene or a painted room.</summary>
    public static bool Has(string root, string set) =>
        OfficeArt.Names(set) && File.Exists(Path.Combine(root, set, OfficeKit.FileName));

    /// <summary>Read one set's kit and rules, and check both.</summary>
    public static OfficeKitCheck Check(string root, string set)
    {
        var (rules, ruleProblems) = OfficeRuleBook.Read(root, set);

        if (!OfficeArt.Names(set))
        {
            return new(null, rules, [$"'{set}' is not a set name."], []);
        }

        OfficeKit? kit;

        try
        {
            kit = JsonSerializer.Deserialize<OfficeKit>(File.ReadAllText(Path.Combine(root, set, OfficeKit.FileName)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(null, rules, [$"{OfficeKit.FileName} could not be read: {ex.Message}"], []);
        }
        catch (JsonException ex)
        {
            return new(null, rules, [$"{OfficeKit.FileName} is not a kit: {ex.Message}"], []);
        }

        if (kit is null)
        {
            return new(null, rules, [$"{OfficeKit.FileName} is empty."], []);
        }

        var problems = new List<string>(ruleProblems);

        problems.AddRange(Problems(kit, piece => OfficeScenes.PictureSize(OfficeArt.FileOf(root, set, piece))));
        problems.AddRange(Unsuited(kit, rules));

        return new(kit, rules, problems, Missing(kit, rules));
    }

    /// <summary>
    /// The rooms and levels pieces say they suit that the rules have no room or
    /// level called: a misspelt room would otherwise leave the piece unused,
    /// with nothing said.
    /// </summary>
    public static IReadOnlyList<string> Unsuited(OfficeKit kit, OfficeRules rules)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(rules);

        var rooms = (rules.Rooms?.Keys ?? []).Concat(OfficeRules.Structural).Append("*").ToHashSet(StringComparer.Ordinal);
        var levels = OfficeRules.Levels.Append("*").ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var (name, piece) in kit.Pieces.OrderBy(one => one.Key, StringComparer.Ordinal))
        {
            foreach (var room in piece.Suits?.Rooms ?? [])
            {
                if (!rooms.Contains(room))
                {
                    problems.Add($"piece '{name}' suits room '{room}', which the rules have no room called.");
                }
            }

            foreach (var level in piece.Suits?.Levels ?? [])
            {
                if (!levels.Contains(level))
                {
                    problems.Add($"piece '{name}' suits level '{level}'; it has to be one of {string.Join(", ", OfficeRules.Levels)}, or *.");
                }
            }
        }

        return problems;
    }

    /// <summary>The tags the rules furnish from that a kit has no part for.</summary>
    public static IReadOnlyList<string> Missing(OfficeKit kit, OfficeRules rules)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(rules);

        var has = kit.Pieces.Values.SelectMany(piece => piece.Tags).ToHashSet(StringComparer.Ordinal);

        return [.. rules.Tags().Where(tag => !has.Contains(tag)).OrderBy(tag => tag, StringComparer.Ordinal)];
    }

    /// <summary>Everything wrong with a kit, given a way to learn how big a picture is.</summary>
    public static IReadOnlyList<string> Problems(OfficeKit kit, Func<string, (int Width, int Height)?> sizeOf)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(sizeOf);

        var problems = new List<string>();

        if (kit.Schema != OfficeKit.Version)
        {
            problems.Add($"schema is '{kit.Schema}'; this Loadout reads '{OfficeKit.Version}'.");
        }

        if (kit.Tile is < 4 or > 128)
        {
            problems.Add($"tile is {kit.Tile} pixels; it has to be between 4 and 128.");

            return problems;
        }

        foreach (var (name, tileset) in kit.Tilesets)
        {
            Tileset(kit, name, tileset, sizeOf, problems);
        }

        foreach (var (name, piece) in kit.Pieces)
        {
            Piece(name, piece, sizeOf, problems);
        }

        if (kit.Facade is { } facade)
        {
            Facade(kit, facade, problems);
        }

        foreach (var (name, material) in kit.Materials ?? new Dictionary<string, OfficeMaterial>())
        {
            Material(name, material, sizeOf, problems);
        }

        var sheets = kit.Sheets ?? new Dictionary<string, OfficeSheet>();

        foreach (var (role, names) in kit.Skins ?? new Dictionary<string, IReadOnlyList<string>>())
        {
            if (names.Count == 0)
            {
                problems.Add($"skins gives '{role}' no sheets.");
            }

            foreach (var sheet in names.Where(one => !sheets.ContainsKey(one)))
            {
                problems.Add($"skins draws '{role}' with sheet '{sheet}', and there is no such sheet.");
            }
        }

        if (kit.Sheets is { Count: > 0 } && kit.Skins?.ContainsKey("worker") != true)
        {
            problems.Add("skins has no 'worker', which draws every role the kit does not name.");
        }

        OfficeScenes.SheetProblems(sheets, sizeOf, problems);

        return problems;
    }

    private static void Tileset(
        OfficeKit kit,
        string name,
        OfficeTileset tileset,
        Func<string, (int Width, int Height)?> sizeOf,
        List<string> problems)
    {
        var called = $"tileset '{name}'";
        int? count = null;

        if (tileset.Picture is { } picture)
        {
            if (sizeOf(picture) is not { } size)
            {
                problems.Add($"{called} is '{picture}', and the set has no PNG by that name.");
            }
            else
            {
                count = size.Width / kit.Tile * (size.Height / kit.Tile);
            }
        }

        var absent = OfficeKit.CornerPatterns.Where(pattern => !tileset.Corners.ContainsKey(pattern)).ToList();

        if (absent.Count > 0)
        {
            problems.Add($"{called} has no tile for corner pattern{(absent.Count == 1 ? string.Empty : "s")} {string.Join(", ", absent)}.");
        }

        foreach (var (pattern, index) in tileset.Corners)
        {
            if (!OfficeKit.CornerPatterns.Contains(pattern))
            {
                problems.Add($"{called} has corner pattern '{pattern}'; a pattern is four letters, l or u.");
            }
            else if (index < 0 || (count is { } has && index >= has))
            {
                problems.Add(
                    $"{called} draws '{pattern}' with tile {index}"
                    + (count is { } total ? $"; '{tileset.Picture}' has tiles 0 to {total - 1}." : "; tiles start at 0."));
            }
        }
    }

    private static void Piece(
        string name,
        OfficePiece piece,
        Func<string, (int Width, int Height)?> sizeOf,
        List<string> problems)
    {
        var called = $"piece '{name}'";

        if (piece.Footprint is not [var w, var h] || w is < 1 or > 32 || h is < 1 or > 32)
        {
            problems.Add($"{called} needs a footprint of 1 to 32 tiles each way.");

            return;
        }

        if (piece.Tags.Count == 0)
        {
            problems.Add($"{called} has no tags, so no room can ask for it.");
        }

        if (piece.Depth is not ("floor" or "sorted"))
        {
            problems.Add($"{called} has depth '{piece.Depth}'; it has to be floor or sorted.");
        }

        if (!OfficeKit.Places.Contains(piece.Place))
        {
            problems.Add($"{called} is placed '{piece.Place}'; it has to be one of {string.Join(", ", OfficeKit.Places)}.");
        }

        if (piece.Suits is { } suits)
        {
            if (!OfficeSuits.Roles.Contains(suits.Role))
            {
                problems.Add($"{called} suits a room as '{suits.Role}'; it has to be one of {string.Join(", ", OfficeSuits.Roles)}.");
            }

            if (suits.Against is { } back && !OfficeSuits.Backs.Contains(back))
            {
                problems.Add($"{called} stands against '{back}'; it has to be one of {string.Join(", ", OfficeSuits.Backs)}.");
            }

            if (suits.Rooms is { Count: 0 })
            {
                problems.Add($"{called} suits no room; give it rooms, or * for any.");
            }
        }

        foreach (var seat in piece.Seats ?? [])
        {
            if (!OfficeScene.Facings.Contains(seat.Facing))
            {
                problems.Add($"{called} has a seat facing '{seat.Facing}'; it has to be n, e, s or w.");
            }

            // A seat is on the piece or on a tile touching it: at a desk, behind it.
            if (seat.X < -1 || seat.Y < -1 || seat.X > w || seat.Y > h)
            {
                problems.Add($"{called} has a seat at {seat.X},{seat.Y}, which is not on or beside its {w}x{h} footprint.");
            }
        }

        if (piece.Picture is null)
        {
            return;
        }

        if (sizeOf(piece.Picture) is not { } size)
        {
            problems.Add($"{called} is cut from '{piece.Picture}', and the set has no PNG by that name.");

            return;
        }

        if (!Inside(piece.Source, size))
        {
            problems.Add($"{called} needs a source of x, y, width and height inside '{piece.Picture}', which is {size.Width}x{size.Height}.");
        }

        foreach (var (side, rect) in piece.Sides ?? new Dictionary<string, IReadOnlyList<int>>())
        {
            if (!OfficeScene.Facings.Contains(side))
            {
                problems.Add($"{called} has a side '{side}'; sides are {string.Join(", ", OfficeScene.Facings)}, the way its front faces.");
            }
            else if (!Inside(rect, size))
            {
                problems.Add($"{called} side {side} is not inside '{piece.Picture}', which is {size.Width}x{size.Height}.");
            }
        }

        if (piece.Animation is { } animation)
        {
            if (animation.Frames.Count == 0)
            {
                problems.Add($"{called} has an animation with no frames.");
            }

            for (var i = 0; i < animation.Frames.Count; i++)
            {
                if (!Inside(animation.Frames[i], size))
                {
                    problems.Add($"{called} animation frame {i} is not inside '{piece.Picture}', which is {size.Width}x{size.Height}.");
                }
            }

            if (animation.Fps is <= 0 or > 60)
            {
                problems.Add($"{called} animation runs at {animation.Fps} frames a second; it has to be above 0 and at most 60.");
            }
        }
    }

    private static bool Inside(IReadOnlyList<int>? box, (int Width, int Height) size) =>
        box is [var x, var y, var bw, var bh]
        && x >= 0 && y >= 0 && bw >= 1 && bh >= 1
        && x + bw <= size.Width && y + bh <= size.Height;

    private static void Material(string name, OfficeMaterial material, Func<string, (int Width, int Height)?> sizeOf, List<string> problems)
    {
        if (!OfficeKit.IsMaterial(name))
        {
            problems.Add($"material '{name}' is not one the neighbourhood uses or a floor; they are {string.Join(", ", OfficeKit.MaterialNames)}, and {OfficeKit.FloorMaterial} with a floor's name.");
        }

        if (material.Size is not [>= 1 and <= 1024, >= 1 and <= 1024])
        {
            problems.Add($"material '{name}' needs a size of two numbers from 1 to 1024: the tile's width and height in art pixels.");
        }

        foreach (var picture in new[] { material.Picture, material.Night }.OfType<string>())
        {
            if (sizeOf(picture) is null)
            {
                problems.Add($"material '{name}' uses '{picture}', which is not in the set or is not a picture.");
            }
        }
    }

    private static void Facade(OfficeKit kit, OfficeFacade facade, List<string> problems)
    {
        var named = facade.Bays
            .Concat(facade.Lobby ?? [])
            .Concat(new[] { facade.Corner, facade.Entrance, facade.Crown, facade.Basement }.OfType<string>());

        if (facade.Bays.Count == 0)
        {
            problems.Add("facade has no bays, so there is nothing to build a storey from.");
        }

        foreach (var piece in named.Distinct(StringComparer.Ordinal))
        {
            if (!kit.Pieces.TryGetValue(piece, out var found))
            {
                problems.Add($"facade uses piece '{piece}', and there is no such piece.");
            }
            else if (found.Place != "facade")
            {
                problems.Add($"facade uses piece '{piece}', which is placed '{found.Place}' rather than facade.");
            }
        }

        foreach (var window in facade.Windows ?? [])
        {
            if (window is not [var x, var y, var ww, var wh] || x < 0 || y < 0 || ww < 1 || wh < 1)
            {
                problems.Add("facade windows need an x, y, width and height each.");
            }
        }
    }
}
