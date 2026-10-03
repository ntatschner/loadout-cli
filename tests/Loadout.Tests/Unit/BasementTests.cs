using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The basements: under the street, the core coming down in line, and two rooms
/// off a corridor, each filled with the pieces the page puts things in.
/// </summary>
public sealed class BasementTests
{
    private static readonly OfficeKit Kit = OfficeKit.Kit();
    private static readonly OfficeRules Rules = OfficeRules.Default;

    // The office Loadout ships, whose pieces say which room they are the main piece of.
    private static readonly OfficeKit Tech = Unpacked();

    private static OfficeKit Unpacked()
    {
        var root = Path.Combine(Path.GetTempPath(), "loadout-tech-" + Guid.NewGuid().ToString("N"));

        OfficeArt.Unpack(root);

        return OfficeKits.Check(root, OfficeArt.BuiltIn).Kit!;
    }

    [Theory]
    [InlineData(1, "mail-room", "mail", "storage", "storage", "storage")]
    [InlineData(2, "garbage", "bin", "server-room", "server", "server")]
    public void Each_basement_has_its_two_rooms_with_what_they_are_for_and_the_pieces_to_fill(
        int level, string west, string westFunction, string east, string eastFunction, string eastPiece)
    {
        var scene = FloorPlanner.Basement(Kit, Rules, level).Scene;

        OfficeScenes.Problems(scene, _ => null).Should().BeEmpty();
        scene.Areas!.Should().Contain(area => area.Kind == west && area.Function == westFunction);
        scene.Areas!.Should().Contain(area => area.Kind == east && area.Function == eastFunction);

        var westArea = scene.Areas!.Single(area => area.Kind == west);

        scene.Props!.Count(prop => prop.Kind == westFunction && Inside(westArea, prop)).Should().BeGreaterThanOrEqualTo(6,
            "a room full of pigeonholes or bins, not one of them");
        scene.Props!.Should().Contain(prop => prop.Kind == eastPiece);
    }

    [Theory]
    [InlineData(1, "mail-room", "mail")]
    [InlineData(1, "storage", "storage")]
    [InlineData(2, "garbage", "bin")]
    [InlineData(2, "server-room", "server")]
    public void Each_room_is_filled_with_the_piece_the_set_says_is_its_own(int level, string room, string tag)
    {
        // A copier is tagged storage too; a storeroom full of copiers is not a storeroom.
        var scene = FloorPlanner.Basement(Tech, Rules, level).Scene;
        var area = scene.Areas!.Single(one => one.Kind == room);
        var own = Tech.Pieces.Values
            .Where(piece => piece.Suits is { Role: "main", Rooms: { } rooms } && rooms.Contains(room))
            .Select(piece => piece.Picture)
            .ToList();
        var filled = scene.Props!.Where(prop => prop.Kind == tag && Inside(area, prop)).ToList();

        own.Should().NotBeEmpty($"the Tech set has a main piece for the {room}");
        filled.Should().NotBeEmpty().And.OnlyContain(prop => own.Contains(prop.Piece), $"the {room} holds only its own pieces");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Both_rooms_are_reached_from_the_lift_and_nothing_is_glass(int level)
    {
        var scene = FloorPlanner.Basement(Kit, Rules, level).Scene;
        var reached = scene.Reachable();
        var glass = scene.Atlases![0].Tiles;

        scene.Door.Should().Be(scene.Spots!["lift"] with { Facing = "s" });

        foreach (var room in scene.Areas!.Where(area => area.Function is not null))
        {
            var cells = Enumerable.Range(room.X, room.W).SelectMany(x => Enumerable.Range(room.Y, room.H).Select(y => (x, y)));

            cells.Should().Contain(cell => reached[cell.x, cell.y], $"somebody has to be able to walk into the {room.Name}");
        }

        scene.Walls!.SelectMany(row => row).Should().NotContain(tile => tile >= glass, "there is no glass below the street");
    }

    [Fact]
    public void The_lift_comes_down_where_it_is_on_every_other_level()
    {
        var floor = FloorPlanner.Plan(Kit, Rules, "run-a", 6).Scene.Spots!["lift"];

        FloorPlanner.Basement(Kit, Rules, 1).Scene.Spots!["lift"].Should().Be(floor);
        FloorPlanner.Basement(Kit, Rules, 2).Scene.Spots!["lift"].Should().Be(floor);
    }

    [Fact]
    public void There_are_two_basements_and_no_more()
    {
        var act = () => FloorPlanner.Basement(Kit, Rules, 3);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static bool Inside(OfficeArea area, OfficeProp prop) =>
        prop.X >= area.X && prop.X < area.X + area.W && prop.Y >= area.Y && prop.Y < area.Y + area.H;
}
