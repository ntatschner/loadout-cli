using System.Buffers.Binary;
using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The tile scenes the office walks people about in, and the check that
/// decides whether one is fit to draw.
/// </summary>
/// <remarks>
/// A scene is a hand-edited or exported file in somebody's own directory. One
/// wrong number draws wrong for a whole run - a desk nobody can reach leaves a
/// person stuck in the doorway - so each rule is pinned by a scene that breaks
/// it, and the message has to name the place.
/// </remarks>
public sealed class OfficeSceneTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "loadout-scene-" + Guid.NewGuid().ToString("N"));

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

    /// <summary>A five-by-five room of plain floor, the door at the bottom and one desk at the top.</summary>
    private static OfficeScene Small(IReadOnlyList<OfficeProp>? props = null) =>
        new(
            OfficeScene.Version,
            16,
            5,
            5,
            [.. Enumerable.Range(0, 5).Select(_ => (IReadOnlyList<int>)[0, 0, 0, 0, 0])],
            null,
            null,
            props,
            [new OfficeSpot(2, 0, "n")],
            new OfficeSpot(2, 4));

    private static OfficeSheet Sheet(string piece, params string[] leaveOut)
    {
        var animations = new Dictionary<string, OfficeAnimation>(StringComparer.Ordinal);

        foreach (var name in OfficeScene.Animations)
        {
            foreach (var facing in OfficeScene.Facings)
            {
                if (!leaveOut.Contains($"{name}_{facing}"))
                {
                    animations[$"{name}_{facing}"] = new OfficeAnimation([0, 1]);
                }
            }
        }

        return new OfficeSheet(piece, [16, 32], [8, 30], animations);
    }

    [Fact]
    public void The_kit_scene_is_fit_to_draw()
    {
        OfficeScenes.Problems(OfficeScene.Kit(), NoPictures).Should().BeEmpty();
    }

    [Fact]
    public void The_kit_scene_seats_a_dozen_and_names_no_picture()
    {
        var kit = OfficeScene.Kit();

        kit.Desks.Should().HaveCount(12);
        kit.Tiles.Should().BeNull();
        kit.Props!.Should().OnlyContain(prop => prop.Piece == null);
    }

    [Fact]
    public void A_small_room_with_a_desk_it_can_reach_is_fit()
    {
        OfficeScenes.Problems(Small(), NoPictures).Should().BeEmpty();
    }

    [Fact]
    public void A_desk_nobody_can_walk_to_from_the_door_is_named()
    {
        // A counter right across the middle of the room.
        var scene = Small([new OfficeProp("counter", null, null, 0, 2, W: 5)]);

        OfficeScenes.Problems(scene, NoPictures).Should().ContainSingle()
            .Which.Should().Be("desk 0 at 2,0 cannot be walked to from the door.");
    }

    [Fact]
    public void A_counter_people_can_walk_through_does_not_wall_anybody_off()
    {
        var scene = Small([new OfficeProp("rug", null, null, 0, 2, W: 5, Blocks: false, Depth: "floor")]);

        OfficeScenes.Problems(scene, NoPictures).Should().BeEmpty();
    }

    [Fact]
    public void A_desk_under_a_wall_is_somewhere_nobody_can_stand()
    {
        var walls = Enumerable.Range(0, 5).Select(y => (IReadOnlyList<int>)(y == 0 ? [-1, -1, 0, -1, -1] : [-1, -1, -1, -1, -1])).ToList();

        OfficeScenes.Problems(Small() with { Walls = walls }, NoPictures).Should().ContainSingle()
            .Which.Should().Be("desk 0 at 2,0 is not somewhere a person can stand.");
    }

    [Fact]
    public void A_door_in_the_void_is_named()
    {
        OfficeScenes.Problems(Small() with { Door = new OfficeSpot(9, 9) }, NoPictures)
            .Should().Contain("the door at 9,9 is not somewhere a person can stand.");
    }

    [Fact]
    public void A_row_of_the_wrong_length_is_named_and_nothing_is_guessed_about_walking()
    {
        var floor = Small().Floor.ToList();

        floor[3] = [0, 0, 0];

        OfficeScenes.Problems(Small() with { Floor = floor }, NoPictures).Should().ContainSingle()
            .Which.Should().Be("floor row 3 has 3 tiles; the room is 5 wide.");
    }

    [Fact]
    public void A_tile_past_the_end_of_the_atlas_is_named_with_the_range_there_is()
    {
        var floor = Small().Floor.ToList();

        floor[1] = [0, 1, 2, 1, 0];

        // Thirty-two by sixteen holds two sixteen-pixel tiles.
        var problems = OfficeScenes.Problems(
            Small() with { Floor = floor, Tiles = "tiles" },
            piece => piece == "tiles" ? (32, 16) : null);

        problems.Should().ContainSingle()
            .Which.Should().Be("floor at 2,1 is tile 2; 'tiles' has tiles 0 to 1.");
    }

    [Fact]
    public void An_atlas_that_is_not_in_the_set_is_named()
    {
        OfficeScenes.Problems(Small() with { Tiles = "tiles" }, NoPictures)
            .Should().Contain("tiles names 'tiles', and the set has no PNG by that name.");
    }

    [Fact]
    public void A_prop_cut_from_outside_its_picture_is_named()
    {
        var scene = Small([new OfficeProp("plant", "props", [40, 0, 16, 16], 0, 0)]);

        OfficeScenes.Problems(scene, piece => piece == "props" ? (48, 48) : null).Should().ContainSingle()
            .Which.Should().StartWith("prop 'plant' needs a source");
    }

    [Fact]
    public void A_sheet_missing_one_facing_says_which()
    {
        var scene = Small() with
        {
            Skins = new Dictionary<string, string> { ["worker"] = "clerk" },
            Sheets = new Dictionary<string, OfficeSheet> { ["clerk"] = Sheet("clerk", "type_w") },
        };

        OfficeScenes.Problems(scene, piece => piece == "clerk" ? (64, 64) : null).Should().ContainSingle()
            .Which.Should().Be("sheet 'clerk' has no type_w.");
    }

    [Fact]
    public void A_frame_past_the_end_of_the_sheet_is_named()
    {
        var whole = Sheet("clerk");
        var sheet = whole with
        {
            Animations = new Dictionary<string, OfficeAnimation>(whole.Animations) { ["walk_e"] = new([0, 9]) },
        };
        var scene = Small() with
        {
            Skins = new Dictionary<string, string> { ["worker"] = "clerk" },
            Sheets = new Dictionary<string, OfficeSheet> { ["clerk"] = sheet },
        };

        // Sixty-four by sixty-four in sixteen by thirty-two frames: eight of them.
        OfficeScenes.Problems(scene, piece => piece == "clerk" ? (64, 64) : null).Should().ContainSingle()
            .Which.Should().Be("sheet 'clerk' walk_e uses frame 9; 'clerk' has frames 0 to 7.");
    }

    [Fact]
    public void Skins_need_a_worker_and_every_skin_needs_its_sheet()
    {
        var scene = Small() with
        {
            Skins = new Dictionary<string, string> { ["reviewer"] = "judge" },
            Sheets = new Dictionary<string, OfficeSheet>(),
        };

        OfficeScenes.Problems(scene, NoPictures).Should().BeEquivalentTo(
            "skins has no 'worker', which draws every role the set does not name.",
            "skins draws 'reviewer' with sheet 'judge', and there is no such sheet.");
    }

    [Fact]
    public void Another_schema_is_refused_by_name()
    {
        OfficeScenes.Problems(Small() with { Schema = "loadout.office/3" }, NoPictures)
            .Should().Contain("schema is 'loadout.office/3'; this Loadout reads 'loadout.office/2'.");
    }

    [Fact]
    public void A_scene_on_disk_is_read_checked_and_its_pictures_measured()
    {
        var set = Path.Combine(_root, "tiled");

        Directory.CreateDirectory(set);
        File.WriteAllText(Path.Combine(set, OfficeScene.FileName), JsonSerializer.Serialize(Small() with { Tiles = "tiles" }));
        WritePng(Path.Combine(set, "tiles.png"), 16, 16);

        var check = OfficeScenes.Check(_root, "tiled");

        check.Problems.Should().BeEmpty();
        check.Fit.Should().BeTrue();
        OfficeScenes.Has(_root, "tiled").Should().BeTrue();
    }

    [Fact]
    public void A_scene_that_will_not_parse_is_a_problem_rather_than_an_exception()
    {
        var set = Path.Combine(_root, "broken");

        Directory.CreateDirectory(set);
        File.WriteAllText(Path.Combine(set, OfficeScene.FileName), "{ not json");

        var check = OfficeScenes.Check(_root, "broken");

        check.Fit.Should().BeFalse();
        check.Problems.Should().ContainSingle().Which.Should().StartWith("scene.json is not a scene");
    }

    [Fact]
    public void A_set_rebuilt_gets_a_new_stamp_so_its_pictures_are_fetched_afresh()
    {
        var set = Path.Combine(_root, "tiled");
        var sheet = Path.Combine(set, "skin-lead.png");

        Directory.CreateDirectory(set);
        WritePng(sheet, 48, 48);
        File.SetLastWriteTimeUtc(sheet, new DateTime(2026, 9, 30, 9, 0, 0, DateTimeKind.Utc));

        var before = OfficeScenes.Stamp(_root, "tiled");

        File.SetLastWriteTimeUtc(sheet, new DateTime(2026, 9, 30, 9, 5, 0, DateTimeKind.Utc));

        before.Should().NotBeEmpty();
        OfficeScenes.Stamp(_root, "tiled").Should().NotBe(before);
        OfficeScenes.Stamp(_root, "..").Should().BeEmpty();
    }

    [Fact]
    public void A_picture_is_measured_from_its_png_header_and_anything_else_is_not_a_png()
    {
        Directory.CreateDirectory(_root);

        var png = Path.Combine(_root, "a.png");
        var fake = Path.Combine(_root, "b.png");

        WritePng(png, 48, 32);

        // A whole header with a believable size, only not a PNG's: a picture
        // saved as something else and given the wrong extension.
        WritePng(fake, 48, 32);

        var bytes = File.ReadAllBytes(fake);

        bytes[1] = (byte)'J';
        File.WriteAllBytes(fake, bytes);

        OfficeScenes.PictureSize(png).Should().Be((48, 32));
        OfficeScenes.PictureSize(fake).Should().BeNull();
        OfficeScenes.PictureSize(Path.Combine(_root, "c.webp")).Should().BeNull();
    }

    /// <summary>The first twenty-four bytes of a PNG, which is all anything here reads.</summary>
    private static void WritePng(string path, int width, int height)
    {
        var header = new byte[24];

        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(header, 0);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(8), 13);
        "IHDR"u8.CopyTo(header.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20), height);

        File.WriteAllBytes(path, header);
    }
}
