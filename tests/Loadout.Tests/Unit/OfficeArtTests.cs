using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The art the office draws desks with, which somebody installs and Loadout
/// never ships.
/// </summary>
/// <remarks>
/// <para>
/// Two things have to hold. A set is whatever directory is there, so nothing in
/// the code lists the sets and nothing has to be kept in step with a folder.
/// And a name is a name: these become paths, they arrive from a browser, and
/// <c>team message '..\probe'</c> has already made a directory outside the runs
/// root once on this project. The same rule, applied before the mistake rather
/// than after it.
/// </para>
/// </remarks>
public sealed class OfficeArtTests : IDisposable
{
    /// <summary>Stands in for the state directory.</summary>
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "loadout-office-" + Guid.NewGuid().ToString("N"));

    /// <summary>Where sets live under it, which is what the art functions take.</summary>
    private string Office => Path.Combine(_root, "teams", "office");

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

    /// <summary>A set with the named pieces in it. The bytes do not matter here.</summary>
    private string Set(string name, params string[] pieces)
    {
        var directory = Path.Combine(Office, name);

        Directory.CreateDirectory(directory);

        foreach (var piece in pieces)
        {
            File.WriteAllBytes(Path.Combine(directory, piece), [0x89, 0x50, 0x4E, 0x47]);
        }

        return directory;
    }

    [Theory]
    [InlineData("open-office")]
    [InlineData("network_ops")]
    [InlineData("newsroom")]
    [InlineData("lead.png")]
    [InlineData("a")]
    public void A_name_that_is_a_name_is_allowed(string name)
    {
        OfficeArt.Names(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../secrets")]
    [InlineData("..\\secrets")]
    [InlineData("sets/open-office")]
    [InlineData("sets\\open-office")]
    [InlineData(".hidden")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("")]
    [InlineData(null)]
    public void A_name_that_is_a_path_is_not(string? name)
    {
        // Every one of these is a file somewhere else, which is the whole
        // reason this function exists rather than a Path.Combine.
        OfficeArt.Names(name).Should().BeFalse();
    }

    [Fact]
    public void A_name_that_is_a_path_cannot_reach_a_file_either()
    {
        Set("open-office", "lead.png");

        // The rule above stops it, and so does the check on what came out.
        // Both, because the first is about names and the second is about the
        // path, and only the second survives somebody widening the first.
        OfficeArt.FileOf(Office, "open-office", "../../secrets.png").Should().BeNull();
        OfficeArt.FileOf(Office, "..", "lead.png").Should().BeNull();
    }

    [Fact]
    public void The_sets_are_whatever_directories_are_there()
    {
        Set("open-office", "lead.png");
        Set("newsroom", "lead.png");

        // Not a list in the code. A list would be a second place to keep in
        // step with a folder, and the folder wins.
        OfficeArt.Sets(Office).Should().Equal("newsroom", "open-office");
    }

    [Fact]
    public void A_machine_with_no_art_has_no_sets_and_says_so_quietly()
    {
        OfficeArt.Sets(Office).Should().BeEmpty();
        OfficeArt.FileOf(Office, "open-office", "lead").Should().BeNull();
    }

    [Fact]
    public void A_piece_is_named_by_what_it_draws_rather_than_by_its_extension()
    {
        Set("open-office", "lead.png", "implementer.webp", "reviewer.gif");

        // The page asks for "lead" and does not have to know what somebody
        // saved it as.
        OfficeArt.FileOf(Office, "open-office", "lead").Should().EndWith("lead.png");
        OfficeArt.FileOf(Office, "open-office", "implementer").Should().EndWith("implementer.webp");
    }

    [Fact]
    public void Anything_that_is_not_an_image_is_not_a_piece()
    {
        Set("open-office", "lead.png", "licence.txt", "notes.md", "install.exe");

        // The directory is the person's own, but a set unzipped from somewhere
        // carries whatever it carries, and the answer for those is a refusal
        // rather than a content type invented on the spot.
        OfficeArt.FileOf(Office, "open-office", "licence.txt").Should().BeNull();
        OfficeArt.TypeOf("install.exe").Should().BeNull();
        OfficeArt.TypeOf("lead.png").Should().Be("image/png");
    }

    [Fact]
    public void A_set_that_is_not_installed_draws_the_built_in_office_rather_than_half_a_thing()
    {
        Set("open-office", "lead.png");

        var paths = new StubPaths(_root);

        OfficeArt.Chosen(paths, "open-office").Set.Should().Be("open-office");

        // A misspelt name comes back as the built-in set. The Tech office is
        // what an unconfigured Loadout looks like, and it should be what a
        // misspelt one looks like too - rather than a page that serves a 404
        // for every picture on it.
        OfficeArt.Chosen(paths, "open-offcie").Set.Should().Be(OfficeArt.BuiltIn);
        OfficeArt.Chosen(paths, "../elsewhere").Set.Should().Be(OfficeArt.BuiltIn);
        OfficeArt.Chosen(paths, "").Set.Should().Be(OfficeArt.BuiltIn);
        OfficeArt.Chosen(paths, null).Set.Should().Be(OfficeArt.BuiltIn);
    }

    [Fact]
    public void The_built_in_office_is_unpacked_whole_and_passes_the_kit_check()
    {
        OfficeArt.Unpack(Office).Should().BeTrue();

        // A kit the building can be made from, every picture it names in the
        // folder: the art Loadout ships has to pass the check a person's own
        // does.
        var check = OfficeKits.Check(Office, OfficeArt.BuiltIn);

        check.Problems.Should().BeEmpty();
        check.Fit.Should().BeTrue();
        check.Kit!.Facade.Should().NotBeNull();
        check.Kit.Sheets.Should().NotBeEmpty();

        // And a second start finds it already there and touches nothing.
        var written = File.GetLastWriteTimeUtc(Path.Combine(Office, OfficeArt.BuiltIn, OfficeKit.FileName));

        OfficeArt.Unpack(Office).Should().BeTrue();
        File.GetLastWriteTimeUtc(Path.Combine(Office, OfficeArt.BuiltIn, OfficeKit.FileName)).Should().Be(written);
    }

    [Fact]
    public void Every_room_the_built_in_rules_lay_out_has_a_piece_of_the_built_in_office_to_be_it()
    {
        OfficeArt.Unpack(Office).Should().BeTrue();

        var check = OfficeKits.Check(Office, OfficeArt.BuiltIn);
        var rules = check.Rules;

        // What makes each room that room, from the pieces that say so, on the
        // room's own level: a kitchen with no counter is a corridor.
        foreach (var (room, rule) in rules.Rooms!)
        {
            check.Kit!.Pieces.Values
                .Where(piece => piece.Suits is { Role: "main" } suits && suits.Fits(room, rule.Level))
                .Should().NotBeEmpty($"room '{room}' on {rule.Level} needs a main piece");
        }

        // And every piece that can stand in a room says where.
        check.Kit!.Pieces
            .Where(one => one.Value.Place != "facade" && !one.Key.StartsWith("shared-door", StringComparison.Ordinal))
            .Where(one => one.Value.Suits is null)
            .Select(one => one.Key)
            .Should().BeEmpty();
    }

    [Fact]
    public void A_folder_of_that_name_somebody_made_is_never_written_over()
    {
        Set(OfficeArt.BuiltIn, "lead.png");

        OfficeArt.Unpack(Office).Should().BeFalse("it is not the built-in set's to write over");
        Directory.EnumerateFiles(Path.Combine(Office, OfficeArt.BuiltIn)).Select(Path.GetFileName).Should().Equal("lead.png");

        // Asked for by name it is that person's set; it is not the default.
        var paths = new StubPaths(_root);

        OfficeArt.Chosen(paths, OfficeArt.BuiltIn).Set.Should().Be(OfficeArt.BuiltIn);
        OfficeArt.Chosen(paths, null).Set.Should().BeEmpty();
    }

    [Fact]
    public void Older_built_in_art_is_brought_up_to_date_and_what_it_dropped_goes()
    {
        var folder = Path.Combine(Office, OfficeArt.BuiltIn);

        OfficeArt.Unpack(Office).Should().BeTrue();
        File.WriteAllText(Path.Combine(folder, ".loadout-built-in"), "an older build");
        File.WriteAllBytes(Path.Combine(folder, "dropped.png"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(folder, OfficeKit.FileName), "{}");

        OfficeArt.Unpack(Office).Should().BeTrue();

        File.Exists(Path.Combine(folder, "dropped.png")).Should().BeFalse();
        OfficeKits.Check(Office, OfficeArt.BuiltIn).Fit.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("open-offcie")]
    [InlineData("../elsewhere")]
    public void Not_choosing_a_set_does_not_hide_the_ones_that_are_there(string? asked)
    {
        Set("open-office", "lead.png");
        Set("newsroom", "lead.png");

        var paths = new StubPaths(_root);
        var chosen = OfficeArt.Chosen(paths, asked);

        // Which set is the default has nothing to do with where the sets live,
        // and both callers hand this root to the dashboard as "where the art
        // is". When it came back null for an empty or misspelt name, a machine
        // with every office installed served none of them - not the configured
        // one, any of them - and the page could not even list them to offer a
        // choice.
        chosen.Root.Should().Be(Office);
        OfficeArt.Sets(chosen.Root).Should().Equal(OfficeArt.BuiltIn, "newsroom", "open-office");
    }

    /// <summary>Paths whose state directory is the one this test wrote into.</summary>
    private sealed class StubPaths : Loadout.Platform.Abstractions.IPlatformPaths
    {
        private readonly string _state;

        public StubPaths(string state) => _state = state;

        public Loadout.Models.Platform.HostPlatform Host =>
            new(
                Loadout.Models.Platform.HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST");

        public Loadout.Models.Platform.PlatformPathSet Paths =>
            new(_state, _state, _state, _state, _state);

        public void EnsureDirectoriesExist()
        {
        }

        public string CreateRuntimeDirectory() =>
            throw new NotSupportedException("Nothing here launches anything.");
    }
}
