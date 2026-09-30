using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The parts the building is generated from, and the rules it is laid out by.
/// </summary>
/// <remarks>
/// A kit is a pack's box of parts and the rules are the design decisions as
/// data. Both come from somebody's own directory, so both are checked before the
/// planner is allowed near them, and each check is pinned by something that
/// breaks it, with the message naming the part.
/// </remarks>
public sealed class OfficeKitTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "loadout-kit-" + Guid.NewGuid().ToString("N"));

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

    private static OfficeKit Small(params (string Name, OfficePiece Piece)[] pieces) =>
        OfficeKit.Kit() with
        {
            Pieces = pieces.ToDictionary(one => one.Name, one => one.Piece, StringComparer.Ordinal),
        };

    // ---- rules ----

    [Fact]
    public void The_built_in_rules_are_sound()
    {
        OfficeRuleBook.Problems(OfficeRules.Default).Should().BeEmpty();
    }

    [Fact]
    public void The_built_in_rules_are_the_decisions_made()
    {
        var rules = OfficeRules.Default;

        rules.Floor.Should().Equal(24, 16);
        rules.MinFloors.Should().Be(10);
        rules.Moves.Should().Be(new OfficeMoves(20, 300, 60));
        rules.Rooms!["mail-room"].Level.Should().Be("basement-1");
        rules.Rooms["garbage"].Function.Should().Be("bin");
        rules.Rooms["lead-office"].Where.Should().Be("corner");
        rules.Use!["asks-you"].Should().Be("stay", "somebody who needs something keeps their bubble where they are");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(9, 2)]
    [InlineData(10, 3)]
    [InlineData(40, 3)]
    public void Meeting_rooms_come_from_the_table_by_team_size(int team, int rooms)
    {
        OfficeRules.Default.Rooms!["meeting"].CountFor(team).Should().Be(rooms);
    }

    [Fact]
    public void A_room_without_a_table_uses_its_count()
    {
        new OfficeRoomRule(["exit"], Count: 2).CountFor(7).Should().Be(2);
        new OfficeRoomRule(["lift"]).CountFor(7).Should().Be(1);
    }

    [Fact]
    public void A_pack_replaces_the_rooms_it_names_and_keeps_the_rest()
    {
        var changes = new OfficeRules(
            OfficeRules.Version,
            Rooms: new Dictionary<string, OfficeRoomRule> { ["cupboard"] = new(["cupboard"], Count: 0) },
            Use: new Dictionary<string, string> { ["free"] = "kitchen" });

        var merged = OfficeRules.Default.With(changes);

        merged.Rooms!["cupboard"].CountFor(3).Should().Be(0);
        merged.Rooms["meeting"].Should().Be(OfficeRules.Default.Rooms!["meeting"]);
        merged.Use!["free"].Should().Be("kitchen");
        merged.Use["done"].Should().Be("lift");
        merged.Floor.Should().Equal(24, 16);
    }

    [Fact]
    public void Broken_rules_are_named_room_by_room()
    {
        var broken = OfficeRules.Default.With(new OfficeRules(
            OfficeRules.Version,
            Floor: [4, 4],
            Rooms: new Dictionary<string, OfficeRoomRule>
            {
                ["meeting"] = new(["meeting-6"], Min: [5, 5], Max: [3, 3], ByTeam: new Dictionary<string, int> { ["zero"] = 1 }),
                ["attic"] = new(["hay"], Level: "loft"),
            },
            Use: new Dictionary<string, string> { ["free"] = "pub" }));

        OfficeRuleBook.Problems(broken).Should().BeEquivalentTo(
            "floor has to be a width of 12 to 64 tiles and a depth of 8 to 64.",
            "room 'meeting' max 3x3 is smaller than its min 5x5.",
            "room 'meeting' by-team has 'zero'; each key has to be a team size of 1 or more.",
            "room 'attic' is on level 'loft'; it has to be one of floor, lobby, roof, basement-1, basement-2.",
            "use sends 'free' to 'pub', and there is no such room.");
    }

    [Fact]
    public void A_sets_rules_json_is_laid_over_the_built_in_ones()
    {
        var set = Path.Combine(_root, "tower");

        Directory.CreateDirectory(set);
        File.WriteAllText(Path.Combine(set, OfficeRules.FileName), """{"schema":"loadout.rules/1","min-floors":12}""");

        var (rules, problems) = OfficeRuleBook.Read(_root, "tower");

        problems.Should().BeEmpty();
        rules.MinFloors.Should().Be(12);
        rules.Rooms.Should().ContainKey("mail-room");
    }

    // ---- kit ----

    [Fact]
    public void The_built_in_kit_is_sound_and_has_a_part_for_every_tag_the_rules_use()
    {
        OfficeKits.Problems(OfficeKit.Kit(), NoPictures).Should().BeEmpty();
        OfficeKits.Missing(OfficeKit.Kit(), OfficeRules.Default).Should().BeEmpty();
    }

    [Fact]
    public void There_are_sixteen_corner_patterns_and_all_differ()
    {
        OfficeKit.CornerPatterns.Should().HaveCount(16).And.OnlyHaveUniqueItems();
        OfficeKit.CornerPatterns.Should().Contain("llll").And.Contain("uuuu").And.Contain("lllu");
    }

    [Fact]
    public void A_pack_missing_parts_lists_what_the_built_in_kit_will_draw()
    {
        var kit = Small(("desk-a", new OfficePiece(null, null, [3, 1], ["desk"])));

        OfficeKits.Problems(kit, NoPictures).Should().BeEmpty();
        OfficeKits.Missing(kit, OfficeRules.Default).Should().Contain("exec-desk").And.NotContain("desk");
    }

    [Fact]
    public void A_tileset_short_of_a_corner_pattern_says_which()
    {
        var corners = OfficeKit.CornerPatterns.Where(one => one != "ullu").ToDictionary(one => one, _ => 0);
        var kit = Small() with
        {
            Tilesets = new Dictionary<string, OfficeTileset> { ["carpet-wall"] = new(null, "carpet", "wall", corners) },
        };

        OfficeKits.Problems(kit, NoPictures).Should().ContainSingle()
            .Which.Should().Be("tileset 'carpet-wall' has no tile for corner pattern ullu.");
    }

    [Fact]
    public void A_tile_past_the_end_of_the_atlas_is_named()
    {
        var corners = OfficeKit.CornerPatterns.ToDictionary(one => one, one => one == "uuuu" ? 16 : 0);
        var kit = Small() with
        {
            Tilesets = new Dictionary<string, OfficeTileset> { ["carpet-wall"] = new("atlas", "carpet", "wall", corners) },
        };

        // A hundred and twenty-eight square holds sixteen of the kit's
        // thirty-two pixel tiles.
        OfficeKits.Problems(kit, piece => piece == "atlas" ? (128, 128) : null).Should().ContainSingle()
            .Which.Should().Be("tileset 'carpet-wall' draws 'uuuu' with tile 16; 'atlas' has tiles 0 to 15.");
    }

    [Fact]
    public void Pieces_are_checked_for_footprint_seats_place_and_picture()
    {
        var kit = Small(
            ("flat", new OfficePiece(null, null, [0, 1], ["desk"])),
            ("chair", new OfficePiece(null, null, [1, 1], ["desk"], Seats: [new OfficeSeat(3, 0, "up")])),
            ("shelf", new OfficePiece(null, null, [1, 1], ["storage"], Place: "loft")),
            ("sofa", new OfficePiece("props", [40, 0, 16, 16], [1, 1], ["sofa"])));

        OfficeKits.Problems(kit, piece => piece == "props" ? (48, 48) : null).Should().BeEquivalentTo(
            "piece 'flat' needs a footprint of 1 to 32 tiles each way.",
            "piece 'chair' has a seat facing 'up'; it has to be n, e, s or w.",
            "piece 'chair' has a seat at 3,0, which is not on or beside its 1x1 footprint.",
            "piece 'shelf' is placed 'loft'; it has to be one of floor, wall-north, wall-any, desk-top, ceiling, facade.",
            "piece 'sofa' needs a source of x, y, width and height inside 'props', which is 48x48.");
    }

    [Fact]
    public void A_facade_must_be_made_of_facade_pieces_that_exist()
    {
        var kit = Small(("bay", new OfficePiece(null, null, [1, 1], ["facade-bay"], Place: "facade")),
                        ("desk", new OfficePiece(null, null, [3, 1], ["desk"]))) with
        {
            Facade = new OfficeFacade(["bay", "ghost"], Corner: "desk"),
        };

        OfficeKits.Problems(kit, NoPictures).Should().BeEquivalentTo(
            "facade uses piece 'ghost', and there is no such piece.",
            "facade uses piece 'desk', which is placed 'floor' rather than facade.");
    }

    [Fact]
    public void Skins_need_a_worker_and_every_sheet_they_name()
    {
        var kit = Small() with
        {
            Skins = new Dictionary<string, IReadOnlyList<string>> { ["project-lead"] = ["boss"] },
            Sheets = new Dictionary<string, OfficeSheet> { ["clerk"] = new("clerk", [16, 32], [8, 30], new Dictionary<string, OfficeAnimation>()) },
        };

        OfficeKits.Problems(kit, NoPictures).Should()
            .Contain("skins draws 'project-lead' with sheet 'boss', and there is no such sheet.")
            .And.Contain("skins has no 'worker', which draws every role the kit does not name.");
    }

    [Fact]
    public void A_kit_on_disk_is_read_with_its_rules_and_checked()
    {
        var set = Path.Combine(_root, "tower");

        Directory.CreateDirectory(set);
        File.WriteAllText(Path.Combine(set, OfficeKit.FileName), JsonSerializer.Serialize(OfficeKit.Kit()));
        File.WriteAllText(Path.Combine(set, OfficeRules.FileName), """{"schema":"loadout.rules/1","rooms":{"meeting":{"tags":["meeting-6"],"level":"attic"}}}""");

        var check = OfficeKits.Check(_root, "tower");

        OfficeKits.Has(_root, "tower").Should().BeTrue();
        check.Kit.Should().NotBeNull();
        check.Fit.Should().BeFalse();
        check.Problems.Should().ContainSingle().Which.Should().StartWith("room 'meeting' is on level 'attic'");
    }

    [Fact]
    public void A_kit_that_will_not_parse_is_a_problem_rather_than_an_exception()
    {
        var set = Path.Combine(_root, "broken");

        Directory.CreateDirectory(set);
        File.WriteAllText(Path.Combine(set, OfficeKit.FileName), "{ nope");

        OfficeKits.Check(_root, "broken").Problems.Should().ContainSingle()
            .Which.Should().StartWith("kit.json is not a kit");
    }
}
