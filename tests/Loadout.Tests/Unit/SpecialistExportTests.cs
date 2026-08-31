using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Copying a built-in specialist out so it can be edited and reviewed.
/// </summary>
/// <remarks>
/// The specialists ship inside the binary, so changing one means a release. A
/// copy in the workspace is in a repository somebody owns and already wins over
/// the built-in of the same id — but only if it is still a specialist the
/// library will load, which the first version of this was not.
/// </remarks>
public sealed class SpecialistExportTests : IDisposable
{
    private readonly string _root;
    private readonly SpecialistLibrary _library = new();

    public SpecialistExportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-export-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path.Combine(_root, "global", "specialists", "language"));
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
            // A temp directory that outlives the run is not a failed test.
        }
    }

    [Fact]
    public void A_built_in_can_be_read_back_in_full()
    {
        var text = _library.BuiltInText("language.rust");

        text.Should().NotBeNull();

        // The whole file, not the parsed remains of it. Parsing keeps what the
        // launcher needs and discards the rest, and a copy that came back
        // subtly different from its original is the wrong thing to review.
        text.Should().StartWith("---");
        text.Should().Contain("id: language.rust");
        text.Should().Contain("## Working rules");
    }

    [Fact]
    public void Something_that_is_not_built_in_reads_back_as_nothing()
    {
        _library.BuiltInText("language.cobol").Should().BeNull();
        _library.BuiltInText("not-an-id").Should().BeNull();
    }

    [Fact]
    public async Task A_copy_in_the_workspace_replaces_the_built_in()
    {
        var text = _library.BuiltInText("language.rust")!;

        await File.WriteAllTextAsync(
            Path.Combine(_root, "global", "specialists", "language", "rust.md"),
            text.Replace(
                "Lifetimes and ownership",
                "Whatever this workspace decided instead",
                StringComparison.Ordinal));

        var catalogue = await _library.LoadAsync(_root);

        var rust = catalogue.Specialists["language.rust"];

        rust.Origin.Should().Be(SpecialistOrigin.Workspace);
        rust.Body.Should().Contain("Whatever this workspace decided instead");
    }

    [Fact]
    public async Task A_copy_carrying_its_origin_still_loads()
    {
        var text = _library.BuiltInText("language.rust")!;

        // The stamp goes inside the frontmatter, as a YAML comment. The first
        // version of the export put an HTML comment above the opening ---,
        // which left the file with no frontmatter at all: the library refused
        // it, the built-in kept winning, and the export reported success. Only
        // 'instructions validate' said what was wrong.
        var opening = text.IndexOf('\n', StringComparison.Ordinal);

        var stamped = text[..(opening + 1)]
            + "# Copied from the 9.9.9 built-in library." + Environment.NewLine
            + text[(opening + 1)..];

        await File.WriteAllTextAsync(
            Path.Combine(_root, "global", "specialists", "language", "rust.md"), stamped);

        var catalogue = await _library.LoadAsync(_root);

        catalogue.Specialists["language.rust"].Origin.Should().Be(SpecialistOrigin.Workspace);
        catalogue.Findings.Should().NotContain(f => f.Severity == RuleFindingSeverity.Error);
    }

    [Fact]
    public async Task A_stamp_above_the_frontmatter_is_refused()
    {
        var text = _library.BuiltInText("language.rust")!;

        await File.WriteAllTextAsync(
            Path.Combine(_root, "global", "specialists", "language", "rust.md"),
            "<!-- copied from somewhere -->" + Environment.NewLine + text);

        var catalogue = await _library.LoadAsync(_root);

        // Held down because this is what shipped for ten minutes: a file that
        // looks right, is refused, and leaves the built-in quietly in charge.
        catalogue.Specialists["language.rust"].Origin.Should().Be(SpecialistOrigin.BuiltIn);
        catalogue.Findings.Should().Contain(f => f.Severity == RuleFindingSeverity.Error);
    }
}
