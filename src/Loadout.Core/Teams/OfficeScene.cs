using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Loadout.Core.Teams;

/// <summary>A tile in a scene, and which way somebody there faces.</summary>
/// <param name="X">Tiles across from the left.</param>
/// <param name="Y">Tiles down from the top.</param>
/// <param name="Facing">n, e, s or w.</param>
/// <param name="Sit">
/// Whether somebody here sits: a seat of a sofa, a meeting table or a chair,
/// rather than a place to stand. Somebody waiting there sits rather than
/// standing at it. A desk's seat says false, because what its sitter is doing
/// decides how they are.
/// </param>
public sealed record OfficeSpot(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("facing")] string Facing = "s",
    [property: JsonPropertyName("sit")] bool Sit = false);

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
/// <param name="Kind">The kit tag it was placed as (desk, lift...), so a part drawn in the kit's shapes can say what it is.</param>
/// <param name="Sides">
/// Where in the picture it is seen facing each way, by the way its front faces
/// (n, e, s, w), so a floor turned a quarter shows its side rather than the
/// same picture on its side; null for a piece with one picture.
/// </param>
/// <param name="Facing">Which way its front faces: s, towards the viewer, unless a floor has been turned.</param>
/// <param name="Hung">Whether it is made to hang on a wall behind it - a board, a lift's doors - so a view that draws it standing can put it against the wall rather than in the middle of its cell.</param>
public sealed record OfficeProp(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("piece")] string? Piece,
    [property: JsonPropertyName("source")] IReadOnlyList<int>? Source,
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("w")] int W = 1,
    [property: JsonPropertyName("h")] int H = 1,
    [property: JsonPropertyName("blocks")] bool Blocks = true,
    [property: JsonPropertyName("depth")] string Depth = "sorted",
    [property: JsonPropertyName("kind")] string? Kind = null,
    [property: JsonPropertyName("sides")] IReadOnlyDictionary<string, IReadOnlyList<int>>? Sides = null,
    [property: JsonPropertyName("facing")] string Facing = "s",
    [property: JsonPropertyName("hung")] bool Hung = false);

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
/// <param name="Gender">Who the sheet draws, one of <see cref="DeskNames.Genders"/>, so the name on their desk fits; null for no one in particular.</param>
/// <param name="Lap">
/// Where in a frame a desk's top meets the body when they sit at it, in pixels
/// from the top: at a desk side-on the desk is drawn over the legs below it and
/// the arms above it are drawn again over the desk, so the hands are on the
/// keyboard rather than under the desk. Null draws the desk over all of them.
/// </param>
/// <param name="Faces">
/// Where the eyes and mouth are in each frame that shows a face, by frame number,
/// for drawing expressions over it. A frame not listed is drawn as it is.
/// </param>
/// <param name="Face">What the face layer draws with: the person's skin, brow and lip colours, and what to leave alone.</param>
public sealed record OfficeSheet(
    [property: JsonPropertyName("piece")] string Piece,
    [property: JsonPropertyName("frame")] IReadOnlyList<int> Frame,
    [property: JsonPropertyName("anchor")] IReadOnlyList<int> Anchor,
    [property: JsonPropertyName("animations")] IReadOnlyDictionary<string, OfficeAnimation> Animations,
    [property: JsonPropertyName("gender")] string? Gender = null,
    [property: JsonPropertyName("lap")] int? Lap = null,
    [property: JsonPropertyName("faces")] IReadOnlyDictionary<string, OfficeFacePlace>? Faces = null,
    [property: JsonPropertyName("face")] OfficeFaceLook? Face = null);

/// <summary>Where a face is in one frame, in the frame's pixels.</summary>
/// <param name="Eyes">Each eye's centre, x then y; the y is the eye's lower row. One eye from the side, the near one first from three-quarters.</param>
/// <param name="Mouth">The centre of the mouth's row.</param>
public sealed record OfficeFacePlace(
    [property: JsonPropertyName("eyes")] IReadOnlyList<IReadOnlyList<double>> Eyes,
    [property: JsonPropertyName("mouth")] IReadOnlyList<double> Mouth);

/// <summary>What the face layer draws a person's expressions with.</summary>
/// <param name="Skin">Their skin, as #rrggbb, for painting over what an expression replaces.</param>
/// <param name="Brow">Their brows and lashes.</param>
/// <param name="Lip">Their mouth; one lighter than their skin (a grin's teeth) is drawn darker instead.</param>
/// <param name="Traits">What expressions must leave alone: <see cref="Traits"/>.</param>
public sealed record OfficeFaceLook(
    [property: JsonPropertyName("skin")] string? Skin = null,
    [property: JsonPropertyName("brow")] string? Brow = null,
    [property: JsonPropertyName("lip")] string? Lip = null,
    [property: JsonPropertyName("traits")] IReadOnlyList<string>? Traits = null)
{
    /// <summary>
    /// glasses: the eyes are behind lenses, so only the mouth changes. beard: the
    /// mouth is in a beard, so nothing is painted over with skin. eyes_closed: the
    /// eyes are already closed in a smile and stay so. grin: the mouth is teeth.
    /// </summary>
    public static readonly IReadOnlyList<string> Known = ["glasses", "beard", "eyes_closed", "grin"];
}

/// <summary>One of several tile pictures a scene draws from.</summary>
/// <param name="Picture">The picture, or null to draw in the kit's colours.</param>
/// <param name="Tiles">How many tiles it holds; a scene's tile numbers run on from one atlas to the next.</param>
/// <param name="Lower">The terrain its tiles call lower, so the kit's colours know what to draw.</param>
/// <param name="Upper">And upper.</param>
/// <param name="Corners">
/// For a picture, which of its tiles has which corner pattern, as a kit's
/// tileset gives them: the page turns such a floor by finding the tile with
/// the turned pattern. Null for the built-in atlas, whose tile numbers are
/// their patterns.
/// </param>
/// <remarks>
/// A generated floor draws solid walls, glass partitions and the curtain wall
/// from separate tilesets, so it needs more than the one picture a hand-made
/// scene names in <see cref="OfficeScene.Tiles"/>. Tile 0 of the second atlas
/// is numbered straight after the last tile of the first.
/// </remarks>
public sealed record OfficeAtlas(
    [property: JsonPropertyName("picture")] string? Picture,
    [property: JsonPropertyName("tiles")] int Tiles,
    [property: JsonPropertyName("lower")] string Lower,
    [property: JsonPropertyName("upper")] string Upper,
    [property: JsonPropertyName("corners")] IReadOnlyDictionary<string, int>? Corners = null);

/// <summary>A room of a generated floor: what it is, where, and what it is for on the dashboard.</summary>
/// <param name="Name">Its name, unique on the floor: lead-office, meeting-1, kitchen.</param>
/// <param name="Kind">The rules' room kind.</param>
/// <param name="X">Left tile, inside its walls.</param>
/// <param name="Y">Top tile, inside its walls.</param>
/// <param name="W">Width in tiles, inside its walls.</param>
/// <param name="H">Depth in tiles, inside its walls.</param>
/// <param name="Function">What clicking it offers: questions, controls, summary, idle, and so on; null for none.</param>
/// <param name="Floor">What it is floored with, a kit material less its <c>floor-</c>; null to be floored like the level around it.</param>
/// <param name="Run">The run whose it is, on a floor teams share; null for a room everybody on the floor shares.</param>
/// <param name="Access">Who may use it: team (its run's people) or lead (its run's lead); null for everyone.</param>
public sealed record OfficeArea(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("w")] int W,
    [property: JsonPropertyName("h")] int H,
    [property: JsonPropertyName("function")] string? Function = null,
    [property: JsonPropertyName("floor")] string? Floor = null,
    [property: JsonPropertyName("run")] string? Run = null,
    [property: JsonPropertyName("access")] string? Access = null);

/// <summary>One team's seats on a floor teams share, the lead's first.</summary>
/// <param name="Run">The run.</param>
/// <param name="Part">Which of the run's floors this is: 0 its first.</param>
/// <param name="Desks">Its people's seats, the lead's first.</param>
public sealed record OfficeTeamSeats(
    [property: JsonPropertyName("run")] string Run,
    [property: JsonPropertyName("part")] int Part,
    [property: JsonPropertyName("desks")] IReadOnlyList<OfficeSpot> Desks);

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
/// <param name="Atlases">Several tile pictures in place of <paramref name="Tiles"/>, numbered one after another; a generated floor has one per wall kind.</param>
/// <param name="Areas">The rooms of a generated floor, for their popups; none on a hand-made scene.</param>
/// <param name="Surface">
/// The floor and walls as drawn, on a grid half a tile off the cells': one more
/// each way than the room, its tile at each point where four cells meet, drawn
/// centred there and chosen by those four cells. A wall one cell wide is then
/// drawn one cell wide, its edges falling inside its own cell, where tiles
/// chosen cell by cell spread it over half of each neighbour. Null to draw
/// <paramref name="Floor"/> and <paramref name="Walls"/> instead; what blocks is
/// read from those either way.
/// </param>
/// <param name="Teams">On a floor teams share, each team's seats; null on a floor of one run, whose seats are <paramref name="Desks"/>.</param>
/// <param name="Ground">
/// What anything in no room is floored with, a kit material less its
/// <c>floor-</c>; null for the carpet of <paramref name="Surface"/>.
/// </param>
/// <param name="Cast">
/// For each role, the sheets its people are drawn from, in order, with "worker"
/// for any other: a kit's <see cref="OfficeKit.Skins"/>, carried onto the floors
/// built from it. Where it is given it wins over <paramref name="Skins"/>, which
/// can name only one sheet a role.
/// </param>
/// <remarks>
/// <para>
/// A set that is one hand-made room, beside the kits the building is made from.
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
    [property: JsonPropertyName("sheets")] IReadOnlyDictionary<string, OfficeSheet>? Sheets = null,
    [property: JsonPropertyName("atlases")] IReadOnlyList<OfficeAtlas>? Atlases = null,
    [property: JsonPropertyName("areas")] IReadOnlyList<OfficeArea>? Areas = null,
    [property: JsonPropertyName("cast")] IReadOnlyDictionary<string, IReadOnlyList<string>>? Cast = null,
    [property: JsonPropertyName("surface")] IReadOnlyList<IReadOnlyList<int>>? Surface = null,
    [property: JsonPropertyName("ground")] string? Ground = null,
    [property: JsonPropertyName("teams")] IReadOnlyList<OfficeTeamSeats>? Teams = null)
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
    /// This is not art: it names no picture, so the page draws every tile, desk
    /// and person as the kit's shapes. It exists so a scene can be tested
    /// without any pictures to hand.
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
    /// a directory of pictures. Null to leave the pictures unchecked, for a
    /// scene made from a kit whose check has already measured them: the
    /// planner has none to measure.
    /// </param>
    public static IReadOnlyList<string> Problems(OfficeScene scene, Func<string, (int Width, int Height)?>? sizeOf)
    {
        ArgumentNullException.ThrowIfNull(scene);

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

        var tileCount = scene.Atlases is { Count: > 0 } atlases
            ? Atlases(scene, atlases, sizeOf, problems)
            : Atlas(scene, sizeOf, problems);

        Grid(scene, "floor", scene.Floor, scene.Width, scene.Height, tileCount, problems);

        if (scene.Walls is not null)
        {
            Grid(scene, "walls", scene.Walls, scene.Width, scene.Height, tileCount, problems);
        }

        if (scene.Surface is not null)
        {
            Grid(scene, "surface", scene.Surface, scene.Width + 1, scene.Height + 1, tileCount, problems);
        }

        Props(scene, sizeOf, problems);

        // Only once the grids are the right shape: a room whose rows are the
        // wrong length has no meaningful answer to where somebody can walk.
        if (problems.Count == 0)
        {
            Places(scene, problems);
        }

        Sheets(scene, sizeOf, problems);
        Areas(scene, problems);

        return problems;
    }

    private static int? Atlases(
        OfficeScene scene,
        IReadOnlyList<OfficeAtlas> atlases,
        Func<string, (int Width, int Height)?>? sizeOf,
        List<string> problems)
    {
        var total = 0;

        foreach (var atlas in atlases)
        {
            if (atlas.Tiles < 1)
            {
                problems.Add($"atlas '{atlas.Picture ?? atlas.Upper}' holds {atlas.Tiles} tiles; it has to hold at least one.");
            }

            if (atlas.Picture is { } picture && sizeOf is not null && scene.Tile is >= 4 and <= 128)
            {
                if (sizeOf(picture) is not { } size)
                {
                    problems.Add($"atlas '{picture}' is not a PNG in the set.");
                }
                else if (size.Width / scene.Tile * (size.Height / scene.Tile) < atlas.Tiles)
                {
                    problems.Add($"atlas '{picture}' is {size.Width}x{size.Height}, too small for the {atlas.Tiles} tiles it claims.");
                }
            }

            if (atlas.Corners is { } corners)
            {
                var absent = OfficeKit.CornerPatterns.Where(pattern => !corners.ContainsKey(pattern)).ToList();

                if (absent.Count > 0)
                {
                    problems.Add($"atlas '{atlas.Picture ?? atlas.Upper}' gives no tile for the corners {string.Join(", ", absent)}.");
                }

                foreach (var (pattern, index) in corners)
                {
                    if (!OfficeKit.CornerPatterns.Contains(pattern))
                    {
                        problems.Add($"atlas '{atlas.Picture ?? atlas.Upper}' names corners '{pattern}'; a pattern is four of l and u.");
                    }
                    else if (index < 0 || index >= atlas.Tiles)
                    {
                        problems.Add($"atlas '{atlas.Picture ?? atlas.Upper}' puts corners '{pattern}' at tile {index}, outside its {atlas.Tiles}.");
                    }
                }
            }

            total += Math.Max(0, atlas.Tiles);
        }

        return total;
    }

    private static void Areas(OfficeScene scene, List<string> problems)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var area in scene.Areas ?? [])
        {
            if (!names.Add(area.Name))
            {
                problems.Add($"area '{area.Name}' appears twice.");
            }

            if (area.W < 1 || area.H < 1
                || !scene.Inside(area.X, area.Y) || !scene.Inside(area.X + area.W - 1, area.Y + area.H - 1))
            {
                problems.Add($"area '{area.Name}' at {area.X},{area.Y} ({area.W}x{area.H}) is not inside the room.");
            }
        }
    }

    private static int? Atlas(OfficeScene scene, Func<string, (int Width, int Height)?>? sizeOf, List<string> problems)
    {
        if (scene.Tiles is null || sizeOf is null)
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
        int width,
        int height,
        int? tileCount,
        List<string> problems)
    {
        if (rows.Count != height)
        {
            problems.Add(height == scene.Height
                ? $"{name} has {rows.Count} rows; the room is {scene.Height} tall."
                : $"{name} has {rows.Count} rows; it is one more than the room, {height}.");

            return;
        }

        for (var y = 0; y < rows.Count; y++)
        {
            if (rows[y].Count != width)
            {
                problems.Add(width == scene.Width
                    ? $"{name} row {y} has {rows[y].Count} tiles; the room is {scene.Width} wide."
                    : $"{name} row {y} has {rows[y].Count} tiles; it is one more than the room, {width}.");

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

    private static void Props(OfficeScene scene, Func<string, (int Width, int Height)?>? sizeOf, List<string> problems)
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

            if (prop.Piece is null || sizeOf is null)
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

    private static void Sheets(OfficeScene scene, Func<string, (int Width, int Height)?>? sizeOf, List<string> problems)
    {
        if (scene.Skins is null && scene.Sheets is null && scene.Cast is null)
        {
            return;
        }

        var sheets = scene.Sheets ?? new Dictionary<string, OfficeSheet>();
        var skins = scene.Skins ?? new Dictionary<string, string>();

        if (scene.Cast is { } cast)
        {
            if (!cast.ContainsKey("worker"))
            {
                problems.Add("cast has no 'worker', which draws every role the set does not name.");
            }

            foreach (var (role, names) in cast)
            {
                if (names.Count == 0)
                {
                    problems.Add($"cast gives '{role}' nobody to draw it with.");
                }

                foreach (var name in names.Where(name => !sheets.ContainsKey(name)))
                {
                    problems.Add($"cast draws '{role}' with sheet '{name}', and there is no such sheet.");
                }
            }
        }
        else if (!skins.ContainsKey("worker"))
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

        SheetProblems(sheets, sizeOf, problems);
    }

    /// <summary>
    /// Everything wrong with a set of sprite sheets: frames, anchors, pictures,
    /// the animations every sheet must have and the frames each one uses.
    /// </summary>
    /// <remarks>Shared by scenes and kits, which hold sheets the same way.</remarks>
    internal static void SheetProblems(
        IReadOnlyDictionary<string, OfficeSheet> sheets,
        Func<string, (int Width, int Height)?>? sizeOf,
        List<string> problems)
    {
        foreach (var (name, sheet) in sheets)
        {
            var called = $"sheet '{name}'";

            if (sheet.Gender is { } gender && !DeskNames.Genders.Contains(gender))
            {
                problems.Add($"{called} is drawn as '{gender}'; it has to be one of {string.Join(", ", DeskNames.Genders)}.");
            }

            if (sheet.Frame is not [var fw, var fh] || fw < 1 || fh < 1)
            {
                problems.Add($"{called} needs a frame of width and height.");

                continue;
            }

            if (sheet.Anchor is not [var ax, var ay] || ax < 0 || ay < 0 || ax > fw || ay > fh)
            {
                problems.Add($"{called} needs an anchor of x and y inside its {fw}x{fh} frame.");
            }

            if (sheet.Lap is { } lap && (lap < 0 || lap > fh))
            {
                problems.Add($"{called} puts its lap at {lap}, outside its {fh} pixel high frame.");
            }

            foreach (var trait in sheet.Face?.Traits ?? [])
            {
                if (!OfficeFaceLook.Known.Contains(trait))
                {
                    problems.Add($"{called} has a face trait '{trait}'; the face layer knows {string.Join(", ", OfficeFaceLook.Known)}.");
                }
            }

            foreach (var (key, place) in sheet.Faces ?? new Dictionary<string, OfficeFacePlace>())
            {
                var points = place.Eyes.Append(place.Mouth).ToList();

                if (!int.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    problems.Add($"{called} has a face for '{key}', which is not a frame number.");
                }
                else if (place.Eyes.Count is < 1 or > 2 || points.Any(point => point is not [var x, var y] || x < 0 || y < 0 || x >= fw || y >= fh))
                {
                    problems.Add($"{called} frame {key}: a face needs one or two eyes and a mouth, inside its {fw}x{fh} frame.");
                }
            }

            int? frames = null;

            if (sizeOf is null)
            {
                // Measured by the kit's check; nothing to count frames against here.
            }
            else if (sizeOf(sheet.Piece) is not { } size)
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
