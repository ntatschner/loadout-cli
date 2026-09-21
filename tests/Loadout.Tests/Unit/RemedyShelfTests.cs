using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models.Platform;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What is on a team's shelf, including what should not be.
/// </summary>
/// <remarks>
/// A node told by a declaration to register what it worked out writes a file.
/// If that file is malformed it used to disappear: Read caught the
/// YamlException and returned null, All skipped nulls without a word, and
/// nothing anywhere said so. The record was absent from every listing and from
/// the gate, and the declaration that asked for it read as satisfied.
/// </remarks>
public sealed class RemedyShelfTests : IDisposable
{
    private readonly string _root;
    private readonly IPlatformPaths _paths;

    public RemedyShelfTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-shelf-" + Guid.NewGuid().ToString("N"));

        _paths = new LinuxPaths(
            new FakeEnvironmentProvider(
                Path.Combine(_root, "home"),
                new Dictionary<string, string>
                {
                    ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                    ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                    ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                    ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
                }),
            new NoOpFilePermissions(),
            new HostPlatform(
                HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST"));

        _paths.EnsureDirectoriesExist();
    }

    private RemedyBook Book() => new(_paths);

    private string Shelf()
    {
        var shelf = Path.Combine(Book().DirectoryOf("made-up"), "remedies");

        Directory.CreateDirectory(shelf);

        return shelf;
    }

    private const string Good =
        "name: clear-cache\nkind: cleanup\nwhat: Removes build output.\nscript: clear-cache.ps1\n";

    [Fact]
    public void A_record_nothing_can_read_is_named_rather_than_skipped()
    {
        var shelf = Shelf();

        File.WriteAllText(Path.Combine(shelf, "clear-cache.yaml"), Good);

        // What a node writes when it gets the format wrong: a tab where YAML
        // will not take one, so this fails to parse rather than parsing into
        // something empty.
        File.WriteAllText(Path.Combine(shelf, "half-written.yaml"), "name: half\n\tkind: [unclosed\n");

        Book().Unreadable("made-up").Should().ContainSingle()
            .Which.Should().Be("half-written.yaml");
    }

    [Fact]
    public void The_readable_ones_are_still_read()
    {
        // The broken one must not take the shelf down with it: a team with one
        // bad record still has the rest of its fixes.
        var shelf = Shelf();

        File.WriteAllText(Path.Combine(shelf, "clear-cache.yaml"), Good);
        File.WriteAllText(Path.Combine(shelf, "half-written.yaml"), "name: half\n\tkind: [unclosed\n");

        Book().All("made-up").Value!.Should().ContainSingle()
            .Which.Name.Should().Be("clear-cache");
    }

    [Fact]
    public void A_shelf_of_good_records_reports_nothing()
    {
        var shelf = Shelf();

        File.WriteAllText(Path.Combine(shelf, "clear-cache.yaml"), Good);

        Book().Unreadable("made-up").Should().BeEmpty();
    }

    [Fact]
    public void A_team_with_no_shelf_at_all_reports_nothing()
    {
        Book().Unreadable("never-run").Should().BeEmpty();
    }

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
}
