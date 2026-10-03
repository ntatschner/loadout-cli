using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The lobby, the roof and the basements laid out from the rules, the way a
/// floor is: a pack moves, adds or takes away their rooms by changing the rules,
/// and the door, the core and the way to the lift stay where they are.
/// </summary>
public sealed class LevelRulesTests
{
    private static readonly OfficeRules Rules = OfficeRules.Default;

    // The office Loadout ships, whose pieces say where they suit.
    private static readonly OfficeKit Tech = Unpacked();

    private static OfficeKit Unpacked()
    {
        var root = Path.Combine(Path.GetTempPath(), "loadout-tech-" + Guid.NewGuid().ToString("N"));

        OfficeArt.Unpack(root);

        return OfficeKits.Check(root, OfficeArt.BuiltIn).Kit!;
    }

    private static OfficeRules With(params (string Kind, OfficeRoomRule Rule)[] rooms) =>
        Rules.With(new OfficeRules(OfficeRules.Version, Rooms: rooms.ToDictionary(one => one.Kind, one => one.Rule, StringComparer.Ordinal)));

    private static void Sound(OfficeScene scene)
    {
        OfficeScenes.Problems(scene, sizeOf: null).Should().BeEmpty();

        var reached = scene.Reachable();

        reached[scene.Spots!["lift"].X, scene.Spots["lift"].Y].Should().BeTrue("the way to the lift stays open");

        foreach (var room in scene.Areas!.Where(area => area.Function is not null && area.Kind != "lobby-screen"))
        {
            Enumerable.Range(room.X, room.W).SelectMany(x => Enumerable.Range(room.Y, room.H).Select(y => (x, y)))
                .Should().Contain(cell => reached[cell.x, cell.y], $"somebody can walk into the {room.Name}");
        }
    }

    [Fact]
    public void The_built_in_rules_give_each_level_the_rooms_it_has_always_had()
    {
        FloorPlanner.Lobby(Tech, Rules, 8).Scene.Areas!.Select(area => area.Kind).Should().Contain(["reception", "waiting-room", "lobby-screen", "it-help"]);
        FloorPlanner.Roof(Tech, Rules, 8).Scene.Areas!.Select(area => area.Kind).Should().Contain(["break-area", "gym"]);
        FloorPlanner.Basement(Tech, Rules, 1).Scene.Areas!.Select(area => area.Kind).Should().Contain(["mail-room", "storage", "bike-store", "showers"]);
        FloorPlanner.Basement(Tech, Rules, 2).Scene.Areas!.Select(area => area.Kind).Should().Contain(["garbage", "server-room"]);
    }

    [Fact]
    public void A_pack_that_moves_the_gym_to_the_lobby_has_it_there_and_not_on_the_roof()
    {
        var gym = Rules.Rooms!["gym"] with { Level = "lobby" };
        var rules = With(("gym", gym));
        var lobby = FloorPlanner.Lobby(Tech, rules, 8).Scene;
        var roof = FloorPlanner.Roof(Tech, rules, 8).Scene;

        lobby.Areas!.Should().Contain(area => area.Kind == "gym");
        lobby.Areas!.Should().Contain(area => area.Kind == "it-help", "it shares the band, not takes it");
        roof.Areas!.Should().NotContain(area => area.Kind == "gym");

        var room = lobby.Areas!.Single(area => area.Kind == "gym");

        lobby.Props!.Should().Contain(prop => prop.Kind == "treadmill" && prop.X >= room.X && prop.X < room.X + room.W, "it is furnished as a gym");
        Sound(lobby);
        Sound(roof);
    }

    [Fact]
    public void A_room_a_pack_adds_to_a_basement_s_band_is_there_behind_solid_walls()
    {
        var rules = With(("archive", new OfficeRoomRule(["storage"], Min: [5, 3], Max: [6, 4], Level: "basement-2", Count: 1, Where: "band", Walls: ["glass"])));
        var scene = FloorPlanner.Basement(Tech, rules, 2).Scene;
        var glass = scene.Atlases![0].Tiles;

        scene.Areas!.Should().Contain(area => area.Kind == "archive");
        scene.Walls!.SelectMany(row => row).Should().NotContain(tile => tile >= glass, "glass it asked for, but there is no glass below the street");
        Sound(scene);
    }

    [Fact]
    public void A_third_room_off_a_basement_s_corridor_shares_the_width_and_is_filled_with_its_own_pieces()
    {
        var rules = With(("parcels", new OfficeRoomRule(["mail"], Level: "basement-1", Count: 1, Where: "south", Function: "mail")));
        var scene = FloorPlanner.Basement(Tech, rules, 1).Scene;
        var south = scene.Areas!.Where(area => area.Kind is "mail-room" or "parcels" or "storage").OrderBy(area => area.X).ToList();

        south.Select(area => area.Kind).Should().Equal(["mail-room", "parcels", "storage"], "side by side, in the rules' order");
        south.Max(area => area.W).Should().BeLessThanOrEqualTo(south.Min(area => area.W) + 2, "they share the width");

        foreach (var room in south)
        {
            scene.Props!.Count(prop => prop.X >= room.X && prop.X < room.X + room.W && prop.Y >= room.Y && prop.Y < room.Y + room.H)
                .Should().BeGreaterThan(2, $"the {room.Name} is filled");
        }

        Sound(scene);
    }

    [Fact]
    public void A_pack_that_takes_reception_away_has_a_lobby_without_one()
    {
        var rules = With(("reception", Rules.Rooms!["reception"] with { Count = 0 }));
        var scene = FloorPlanner.Lobby(Tech, rules, 8).Scene;

        scene.Areas!.Should().NotContain(area => area.Kind == "reception");
        scene.Spots!.Keys.Should().NotContain("reception-1");
        Sound(scene);
    }

    [Fact]
    public void The_lobby_s_band_stays_put_however_many_are_waiting()
    {
        // The waiting room grows with the queue; IT help, the screen and
        // anything else in the band do not move about while it does.
        string Band(int waiting) => string.Join("|", FloorPlanner.Lobby(Tech, Rules, waiting).Scene.Areas!
            .Where(area => area.Kind is "it-help" or "lobby-screen")
            .Select(area => $"{area.Kind}@{area.X},{area.Y},{area.W}"));

        Band(0).Should().NotBeEmpty();
        Band(4).Should().Be(Band(0));
        Band(16).Should().Be(Band(0));
        Band(40).Should().Be(Band(0));
    }
}
