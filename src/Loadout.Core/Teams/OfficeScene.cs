using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Loadout.Core.Teams;

/// <summary>A tile in a scene, and which way somebody there faces.</summary>
/// <param name="X">Tiles across from the left.</param>
/// <param name="Y">Tiles down from the top.</param>
/// <param name="Facing">n, e, s or w.</param>
public sealed record OfficeSpot(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("facing")] string Facing = "s");

/// <summary>Something standing in a scene: a desk, a plant, a cabinet.</summary>
/// <param name="Id">Its name, unique in the scene.</param>
/// <param name="Piece">The picture it is cut from, or null to draw it in the kit's colours.</param>
/// <param name="Source">Where in that picture, in pixels: x, y, width, height.</param>
/// <param name="X">The left tile of what it stands on.</param>
/// <param name="Y">The top tile of what it stands on.</param>
/// <param name="W">How many tiles across it stands on.</param>
/// <param name="H">How many tiles down it stands on.</param>
/// <param name="Blocks">Whether nobody can walk through it.</param>
/// <param name="Depth">
/// "floor" for something always under people, drawn once; "sorted" for
/// something people can be in front of or behind, drawn in order of how far
/// down the room it stands. Sorted is what a painted room needed a second
/// front picture for.
/// </param>
public sealed record OfficeProp(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("piece")] string? Piece,
    [property: JsonPropertyName("source")] IReadOnlyList<int>? Source,
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("w")] int W = 1,
    [property: JsonPropertyName("h")] int H = 1,
    [property: JsonPropertyName("blocks")] bool Blocks = true,
    [property: JsonPropertyName("depth")] string Depth = "sorted");

/// <summary>One animation in a sprite sheet.</summary>
/// <param name="Frames">Frame numbers, left to right and top to bottom across the sheet.</param>
/// <param name="Fps">Frames a second.</param>
public sealed record OfficeAnimation(
    [property: JsonPropertyName("frames")] IReadOnlyList<int> Frames,
    [property: JsonPropertyName("fps")] double Fps = 6);

/// <summary>A sprite sheet for one kind of person.</summary>
/// <param name="Piece">The sheet's picture.</param>
/// <param name="Frame">One frame's width and height in pixels.</param>
/// <param name="Anchor">Where in a frame the feet are, in pixels.</param>
/// <param name="Animations">Each animation by name: idle_n, walk_e, type_s and the rest.</param>
public sealed record OfficeSheet(
    [property: JsonPropertyName("piece")] string Piece,
    [property: JsonPropertyName("frame")] IReadOnlyList<int> Frame,
    [property: JsonPropertyName("anchor")] IReadOnlyList<int> Anchor,
    [property: JsonPropertyName("animations")] IReadOnlyDictionary<string, OfficeAnimation> Animations);

/// <summary>
/// A room drawn from tiles, with people who walk about in it.
/// </summary>
/// <param name="Schema">Always <see cref="OfficeScene.Version"/>.</param>
/// <param name="Tile">A tile's size in pixels.</param>
/// <param name="Width">The room's width in tiles.</param>
/// <param name="Height">The room's height in tiles.</param>
/// <param name="Floor">A row of tile numbers for each row of the room; -1 is nothing.</param>
/// <param name="Walls">The same, drawn over the floor; anything but -1 is a wall.</param>
/// <param name="Tiles">The picture the tile numbers come from, or null to draw in the kit's colours.</param>
/// <param name="Props">What stands in the room.</param>
/// <param name="Desks">Where each person sits, the lead's first.</param>
/// <param name="Door">Where people come in and leave.</param>
/// <param name="Spots">Named places somebody with nothing to do may go.</param>
/// <param name="Skins">Which sheet draws each role, with "worker" for any other.</param>
/// <param name="Sheets">The sheets, by name.</param>
/// <remarks>
/// <para>
/// The second kind of set, beside the painted rooms of <see cref="OfficeRoom"/>.
/// A painted room is one picture, so a person can only be placed on it; a
/// room made of tiles says where the floor is, so a person can walk across it.
/// </para>
/// <para>
/// Everything a set needs is in this one document, pictures apart. Pieces are
/// served by flat name and only as pictures, so a sheet's description could not
/// be a file of its own without widening what the server hands a browser.
/// </para>
/// </remarks>
public sealed record OfficeScene(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("tile")] int Tile,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("floor")] IReadOnlyList<IReadOnlyList<int>> Floor,
    [property: JsonPropertyName("walls")] IReadOnlyList<IReadOnlyList<int>>? Walls,
    [property: JsonPropertyName("tiles")] string? Tiles,
    [property: JsonPropertyName("props")] IReadOnlyList<OfficeProp>? Props,
    [property: JsonPropertyName("desks")] IReadOnlyList<OfficeSpot> Desks,
    [property: JsonPropertyName("door")] OfficeSpot Door,
    [property: JsonPropertyName("spots")] IReadOnlyDictionary<string, OfficeSpot>? Spots = null,
    [property: JsonPropertyName("skins")] IReadOnlyDictionary<string, string>? Skins = null,
    [property: JsonPropertyName("sheets")] IReadOnlyDictionary<string, OfficeSheet>? Sheets = null)
{
    /// <summary>What <see cref="Schema"/> has to say.</summary>
    public const string Version = "loadout.office/2";

    /// <summary>The file a set keeps its scene in.</summary>
    public const string FileName = "scene.json";

    /// <summary>The directions a sheet has to draw every animation in.</summary>
    public static readonly IReadOnlyList<string> Facings = ["n", "e", "s", "w"];

    /// <summary>The animations a sheet has to have, each in every direction.</summary>
    public static readonly IReadOnlyList<string> Animations = ["idle", "walk", "sit", "type"];

    /// <summary>
    /// Whether nobody can stand on a tile: off the room, no floor, a wall,
    /// or under something that blocks.
    /// </summary>
    public bool[,] Blocked()
    {
        var blocked = new bool[Width, Height];

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var floor = y < Floor.Count && x < Floor[y].Count ? Floor[y][x] : -1;
                var wall = Walls is not null && y < Walls.Count && x < Walls[y].Count ? Walls[y][x] : -1;

                blocked[x, y] = floor < 0 || wall >= 0;
            }
        }

        foreach (var prop in Props ?? [])
        {
            if (!prop.Blocks)
            {
                continue;
            }

            for (var y = prop.Y; y < prop.Y + prop.H; y++)
            {
                for (var x = prop.X; x < prop.X + prop.W; x++)
                {
                    if (x >= 0 && y >= 0 && x < Width && y < Height)
                    {
                        blocked[x, y] = true;
                    }
                }
            }
        }

        return blocked;
    }

    /// <summary>Every tile somebody can walk to from the door.</summary>
    public bool[,] Reachable()
    {
        var blocked = Blocked();
        var reached = new bool[Width, Height];

        if (!Inside(Door.X, Door.Y) || blocked[Door.X, Door.Y])
        {
            return reached;
        }

        var queue = new Queue<(int X, int Y)>();

        queue.Enqueue((Door.X, Door.Y));
        reached[Door.X, Door.Y] = true;

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();

            foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (Inside(nx, ny) && !blocked[nx, ny] && !reached[nx, ny])
                {
                    reached[nx, ny] = true;
                    queue.Enqueue((nx, ny));
                }
            }
        }

        return reached;
    }

    /// <summary>Whether a tile is in the room at all.</summary>
    public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>
    /// The room drawn when no set is installed, in the kit's own colours.
    /// </summary>
    /// <remarks>
    /// Loadout ships no art, for the reason <see cref="OfficeArt"/> gives. This
    /// is not art: it names no picture, so the page draws every tile, desk and
    /// person as the kit's shapes. It exists so the office works, and can be
    /// tested and shown, on a machine where nobody has installed anything.
    /// Twelve desks in two rows, which seats every team shipped so far; a larger
    /// team stands its extra people in the spare row the page already has.
    /// </remarks>
    public static OfficeScene Kit()
    {
        const int width = 20;
        const int height = 14;
        const int doorX = 10;

        var floor = new List<IReadOnlyList<int>>();
        var walls = new List<IReadOnlyList<int>>();

        for (var y = 0; y < height; y++)
        {
            var floorRow = new int[width];
            var wallRow = new int[width];

            for (var x = 0; x < width; x++)
            {
                // Two floor tiles in a checker, so the kit can tell them apart.
                floorRow[x] = (x + y) % 2;

                var edge = x == 0 || y == 0 || x == width - 1 || y == height - 1;

                wallRow[x] = edge && !(y == height - 1 && x == doorX) ? 0 : -1;
            }

            floor.Add(floorRow);
            walls.Add(wallRow);
        }

        var desks = new List<OfficeSpot>();
        var props = new List<OfficeProp>();

        foreach (var row in new[] { 4, 9 })
        {
            foreach (var column in new[] { 3, 5, 7, 12, 14, 16 })
            {
                desks.Add(new OfficeSpot(column, row, "n"));
                props.Add(new OfficeProp($"desk-{column}-{row}", null, null, column, row - 1));
            }
        }

        return new OfficeScene(
            Version,
            16,
            width,
            height,
            floor,
            walls,
            null,
            props,
            desks,
            new OfficeSpot(doorX, height - 1, "n"),
            new Dictionary<string, OfficeSpot>(StringComparer.Ordinal)
            {
                ["whiteboard"] = new(10, 2, "n"),
                ["coffee"] = new(17, 11, "e"),
                ["window"] = new(2, 11, "w"),
            });
    }
}

/// <summary>A scene, and what is wrong with it.</summary>
/// <param name="Scene">The scene as read, or null where it could not be read at all.</param>
/// <param name="Problems">Each thing wrong with it, as a sentence; empty when it is fit to draw.</param>
public sealed record OfficeSceneCheck(OfficeScene? Scene, IReadOnlyList<string> Problems)
{
    /// <summary>Whether the page may draw it.</summary>
    public bool Fit => Scene is not null && Problems.Count == 0;
}

/// <summary>
/// Reading a set's scene and saying whether it can be drawn.
/// </summary>
/// <remarks>
/// A set is somebody's own directory, edited by hand or exported from a tool,
/// and a scene that is wrong in one place draws wrong everywhere: a desk nobody
/// can walk to leaves a person stuck in the doorway for the whole run. So a
/// scene is served only once it passes, and what is wrong is said in words
/// that name the place, for <c>loadout team office check</c> to print.
/// </remarks>
public static class OfficeScenes
{
    /// <summary>
    /// When anything in a set last changed, as a word for a picture's address.
    /// </summary>
    /// <remarks>
    /// Pictures are cached for an hour, which suited painted rooms. A tile
    /// scene says exactly where each frame sits in its sheet, so a scene read
    /// fresh beside a sheet the browser kept from before the set was rebuilt
    /// draws pieces of the wrong frames. Putting this in the address makes an
    /// edited set a different address, and an unchanged one still cached.
    /// </remarks>
    public static string Stamp(string root, string set)
    {
        if (!OfficeArt.Names(set))
        {
            return string.Empty;
        }

        try
        {
            var directory = new DirectoryInfo(Path.Combine(root, set));

            return directory.Exists
                ? directory.EnumerateFiles()
                    .Select(file => file.LastWriteTimeUtc.Ticks)
                    .DefaultIfEmpty(0)
                    .Max()
                    .ToString("x", System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>Whether a set is a tile scene rather than a painted room.</summary>
    public static bool Has(string root, string set) =>
        OfficeArt.Names(set) && File.Exists(Path.Combine(root, set, OfficeScene.FileName));

    /// <summary>Read one set's scene and check it.</summary>
    public static OfficeSceneCheck Check(string root, string set)
    {
        if (!OfficeArt.Names(set))
        {
            return new(null, [$"'{set}' is not a set name."]);
        }

        var path = Path.Combine(root, set, OfficeScene.FileName);

        OfficeScene? scene;

        try
        {
            scene = JsonSerializer.Deserialize<OfficeScene>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(null, [$"{OfficeScene.FileName} could not be read: {ex.Message}"]);
        }
        catch (JsonException ex)
        {
            return new(null, [$"{OfficeScene.FileName} is not a scene: {ex.Message}"]);
        }

        if (scene is null)
        {
            return new(null, [$"{OfficeScene.FileName} is empty."]);
        }

        return new(scene, Problems(scene, piece => PictureSize(OfficeArt.FileOf(root, set, piece))));
    }

    /// <summary>
    /// Everything wrong with a scene, given a way to learn how big a picture is.
    /// </summary>
    /// <param name="scene">The scene.</param>
    /// <param name="sizeOf">
    /// The width and height of a piece, or null where the set has no such
    /// picture or it is not a PNG. Passed in so the rules can be tested without
    /// a directory of pictures.
    /// </param>
    public static IReadOnlyList<string> Problems(OfficeScene scene, Func<string, (int Width, int Height)?> sizeOf)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(sizeOf);

        var problems = new List<string>();

        if (scene.Schema != OfficeScene.Version)
        {
            problems.Add($"schema is '{scene.Schema}'; this Loadout reads '{OfficeScene.Version}'.");
        }

        if (scene.Tile is < 4 or > 128)
        {
            problems.Add($"tile is {scene.Tile} pixels; it has to be between 4 and 128.");
        }

        if (scene.Width is < 1 or > 256 || scene.Height is < 1 or > 256)
        {
            problems.Add($"the room is {scene.Width} by {scene.Height} tiles; each side has to be 1 to 256.");

            // Nothing below means anything in a room that has no size.
            return problems;
        }

        var tileCount = Atlas(scene, sizeOf, problems);

        Grid(scene, "floor", scene.Floor, tileCount, problems);

        if (scene.Walls is not null)
        {
            Grid(scene, "walls", scene.Walls, tileCount, problems);
        }

        Props(scene, sizeOf, problems);

        // Only once the grids are the right shape: a room whose rows are the
        // wrong length has no meaningful answer to where somebody can walk.
        if (problems.Count == 0)
        {
            Places(scene, problems);
        }

        Sheets(scene, sizeOf, problems);

        return problems;
    }

    private static int? Atlas(OfficeScene scene, Func<string, (int Width, int Height)?> sizeOf, List<string> problems)
    {
        if (scene.Tiles is null)
        {
            return null;
        }

        if (sizeOf(scene.Tiles) is not { } size)
        {
            problems.Add($"tiles names '{scene.Tiles}', and the set has no PNG by that name.");

            return null;
        }

        if (scene.Tile is < 4 or > 128)
        {
            return null;
        }

        var count = size.Width / scene.Tile * (size.Height / scene.Tile);

        if (count == 0)
        {
            problems.Add($"'{scene.Tiles}' is {size.Width}x{size.Height}, smaller than one {scene.Tile}-pixel tile.");
        }

        return count;
    }

    private static void Grid(
        OfficeScene scene,
        string name,
        IReadOnlyList<IReadOnlyList<int>> rows,
        int? tileCount,
        List<string> problems)
    {
        if (rows.Count != scene.Height)
        {
            problems.Add($"{name} has {rows.Count} rows; the room is {scene.Height} tall.");

            return;
        }

        for (var y = 0; y < rows.Count; y++)
        {
            if (rows[y].Count != scene.Width)
            {
                problems.Add($"{name} row {y} has {rows[y].Count} tiles; the room is {scene.Width} wide.");

                continue;
            }

            for (var x = 0; x < rows[y].Count; x++)
            {
                var tile = rows[y][x];

                if (tile < -1 || (tileCount is { } count && tile >= count))
                {
                    problems.Add(
                        $"{name} at {x},{y} is tile {tile}"
                        + (tileCount is { } has ? $"; '{scene.Tiles}' has tiles 0 to {has - 1}." : "; tiles start at 0, and -1 is none."));
                }
            }
        }
    }

    private static void Props(OfficeScene scene, Func<string, (int Width, int Height)?> sizeOf, List<string> problems)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var prop in scene.Props ?? [])
        {
            var called = $"prop '{prop.Id}'";

            if (!seen.Add(prop.Id))
            {
                problems.Add($"{called} appears twice; each prop needs its own id.");
            }

            if (prop.W < 1 || prop.H < 1
                || !scene.Inside(prop.X, prop.Y) || !scene.Inside(prop.X + prop.W - 1, prop.Y + prop.H - 1))
            {
                problems.Add($"{called} stands on {prop.W}x{prop.H} tiles at {prop.X},{prop.Y}, outside the room.");
            }

            if (prop.Depth is not ("floor" or "sorted"))
            {
                problems.Add($"{called} has depth '{prop.Depth}'; it has to be floor or sorted.");
            }

            if (prop.Piece is null)
            {
                continue;
            }

            if (sizeOf(prop.Piece) is not { } size)
            {
                problems.Add($"{called} is cut from '{prop.Piece}', and the set has no PNG by that name.");
            }
            else if (prop.Source is not [var sx, var sy, var sw, var sh]
                || sx < 0 || sy < 0 || sw < 1 || sh < 1 || sx + sw > size.Width || sy + sh > size.Height)
            {
                problems.Add(
                    $"{called} needs a source of x, y, width and height inside '{prop.Piece}', "
                    + $"which is {size.Width}x{size.Height}.");
            }
        }
    }

    private static void Places(OfficeScene scene, List<string> problems)
    {
        var blocked = scene.Blocked();
        var reached = scene.Reachable();

        if (!scene.Inside(scene.Door.X, scene.Door.Y) || blocked[scene.Door.X, scene.Door.Y])
        {
            problems.Add($"the door at {scene.Door.X},{scene.Door.Y} is not somewhere a person can stand.");

            return;
        }

        if (scene.Desks.Count == 0)
        {
            problems.Add("there are no desks, so nobody has anywhere to sit.");
        }

        var taken = new HashSet<(int, int)>();

        for (var i = 0; i < scene.Desks.Count; i++)
        {
            var desk = scene.Desks[i];

            Place($"desk {i}", desk, scene, blocked, reached, problems);

            if (!taken.Add((desk.X, desk.Y)))
            {
                problems.Add($"desk {i} at {desk.X},{desk.Y} is a tile another desk already has.");
            }
        }

        foreach (var (name, spot) in scene.Spots ?? new Dictionary<string, OfficeSpot>())
        {
            Place($"spot '{name}'", spot, scene, blocked, reached, problems);
        }
    }

    private static void Place(
        string called,
        OfficeSpot spot,
        OfficeScene scene,
        bool[,] blocked,
        bool[,] reached,
        List<string> problems)
    {
        if (!OfficeScene.Facings.Contains(spot.Facing))
        {
            problems.Add($"{called} faces '{spot.Facing}'; it has to be n, e, s or w.");
        }

        if (!scene.Inside(spot.X, spot.Y) || blocked[spot.X, spot.Y])
        {
            problems.Add($"{called} at {spot.X},{spot.Y} is not somewhere a person can stand.");
        }
        else if (!reached[spot.X, spot.Y])
        {
            problems.Add($"{called} at {spot.X},{spot.Y} cannot be walked to from the door.");
        }
    }

    private static void Sheets(OfficeScene scene, Func<string, (int Width, int Height)?> sizeOf, List<string> problems)
    {
        if (scene.Skins is null && scene.Sheets is null)
        {
            return;
        }

        var sheets = scene.Sheets ?? new Dictionary<string, OfficeSheet>();
        var skins = scene.Skins ?? new Dictionary<string, string>();

        if (!skins.ContainsKey("worker"))
        {
            problems.Add("skins has no 'worker', which draws every role the set does not name.");
        }

        foreach (var (role, skin) in skins)
        {
            if (!sheets.ContainsKey(skin))
            {
                problems.Add($"skins draws '{role}' with sheet '{skin}', and there is no such sheet.");
            }
        }

        foreach (var (name, sheet) in sheets)
        {
            var called = $"sheet '{name}'";

            if (sheet.Frame is not [var fw, var fh] || fw < 1 || fh < 1)
            {
                problems.Add($"{called} needs a frame of width and height.");

                continue;
            }

            if (sheet.Anchor is not [var ax, var ay] || ax < 0 || ay < 0 || ax > fw || ay > fh)
            {
                problems.Add($"{called} needs an anchor of x and y inside its {fw}x{fh} frame.");
            }

            int? frames = null;

            if (sizeOf(sheet.Piece) is not { } size)
            {
                problems.Add($"{called} is '{sheet.Piece}', and the set has no PNG by that name.");
            }
            else
            {
                frames = size.Width / fw * (size.Height / fh);

                if (frames == 0)
                {
                    problems.Add($"{called}: '{sheet.Piece}' is {size.Width}x{size.Height}, smaller than one {fw}x{fh} frame.");
                }
            }

            foreach (var animation in OfficeScene.Animations)
            {
                foreach (var facing in OfficeScene.Facings)
                {
                    if (!sheet.Animations.ContainsKey($"{animation}_{facing}"))
                    {
                        problems.Add($"{called} has no {animation}_{facing}.");
                    }
                }
            }

            foreach (var (animationName, animation) in sheet.Animations)
            {
                var outside = animation.Frames
                    .Where(one => one < 0 || (frames is { } has && one >= has))
                    .ToList();

                if (animation.Frames.Count == 0)
                {
                    problems.Add($"{called} {animationName} has no frames.");
                }
                else if (outside.Count > 0)
                {
                    problems.Add(
                        $"{called} {animationName} uses frame {outside[0]}"
                        + (frames is { } has ? $"; '{sheet.Piece}' has frames 0 to {has - 1}." : "."));
                }

                if (animation.Fps is <= 0 or > 60)
                {
                    problems.Add($"{called} {animationName} runs at {animation.Fps} frames a second; it has to be above 0 and at most 60.");
                }
            }
        }
    }

    /// <summary>
    /// A PNG's width and height from its header, or null for anything else.
    /// </summary>
    /// <remarks>
    /// PNG only, for tiles and sheets. Knowing a picture's size is what lets a
    /// tile number be checked against the atlas before the page draws a hole,
    /// and a PNG says it in the first twenty-four bytes. The painted rooms can
    /// go on being webp or gif; nothing checks their sizes.
    /// </remarks>
    public static (int Width, int Height)? PictureSize(string? path)
    {
        if (path is null || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[24];

            if (stream.ReadAtLeast(header, 24, throwOnEndOfStream: false) < 24)
            {
                return null;
            }

            ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

            if (!header[..8].SequenceEqual(signature) || !header[12..16].SequenceEqual("IHDR"u8))
            {
                return null;
            }

            var width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
            var height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);

            return width > 0 && height > 0 ? (width, height) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
