using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Globs naming a dotted file or folder at the repository's root, such as
/// <c>.github</c>, through each of the things that match one.
/// </summary>
public sealed class DottedGlobTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "loadout-dotted-" + Guid.NewGuid().ToString("N"));

    public DottedGlobTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth failing a run over a temp directory.
        }
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Theory]
    [InlineData(".github/workflows/*.yml", ".github/workflows/ci.yml", true)]
    [InlineData("**/.github/workflows/*.yml", ".github/workflows/ci.yml", true)]
    [InlineData("**/.github/workflows/*.yml", "sub/.github/workflows/ci.yml", true)]
    [InlineData("**/.dockerignore", ".dockerignore", true)]
    [InlineData(".editorconfig", ".editorconfig", true)]
    [InlineData(".github/**", @".github\CODEOWNERS", true)]
    // The dot is part of the name, so a folder without it is another folder.
    [InlineData("github/*.yml", ".github/ci.yml", false)]
    [InlineData(".github/*.yml", "github/ci.yml", false)]
    // A name may start with more than one dot.
    [InlineData("..hidden", "..hidden", true)]
    public void A_leading_dot_is_part_of_the_name(string glob, string path, bool expected) =>
        RuleService.Matches(glob, path).Should().Be(expected);

    [Theory]
    [InlineData("src/*.cs", "./src/a.cs")]
    [InlineData("src/*.cs", "/src/a.cs")]
    [InlineData("src/*.cs", "../src/a.cs")]
    [InlineData("src/*.cs", @".\src\a.cs")]
    [InlineData(".github/*.yml", "./.github/ci.yml")]
    [InlineData(".github/*.yml", "/.github/ci.yml")]
    [InlineData("./.github/*.yml", ".github/ci.yml")]
    [InlineData("./src/*.cs", "src/a.cs")]
    public void A_prefix_that_only_says_where_to_start_is_not(string glob, string path) =>
        RuleService.Matches(glob, path).Should().BeTrue();

    [Theory]
    [InlineData("./")]
    [InlineData("")]
    [InlineData("  ")]
    public void A_glob_that_names_nothing_matches_nothing(string glob) =>
        RuleService.Matches(glob, "./").Should().BeFalse();

    [Fact]
    public async Task A_rule_scoped_to_workflows_is_selected_for_one()
    {
        Write(
            Path.Combine(_root, "global", "rules", "workflows.md"),
            "---\nglobs: .github/workflows/**\n---\nBody.");

        var rules = new RuleService();
        var loaded = (await rules.LoadAsync(_root, "none")).Value!;

        rules.Select(loaded, [".github/workflows/ci.yml"]).Should().ContainSingle();
        rules.Select(loaded, ["src/ci.yml"]).Should().BeEmpty();
    }

    [Fact]
    public async Task A_framework_known_by_a_dotted_folder_is_offered_by_the_repository()
    {
        Write(
            Path.Combine(_root, "global", "specialists", "framework", "storybook.md"),
            "---\nid: framework.storybook\nkind: framework\ntitle: Storybook\nsummary: Stories.\n"
            + "globs:\n  - '**/.storybook/*.ts'\n---\n\n## Cares about\n\nStories.\n");

        var catalogue = await new SpecialistLibrary().LoadAsync(_root);

        var result = new SpecialistResolver().Resolve(new SpecialistRequest(
            catalogue,
            null,
            "tidy up the helpers",
            null,
            null,
            null,
            new RepositoryEvidence(
                [".storybook/main.ts"],
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [".ts"] = 1 },
                [],
                false),
            null,
            0));

        result.Selected.Should().ContainSingle(s => s.Specialist.Id == "framework.storybook")
            .Which.Trigger.Should().Be(SpecialistTrigger.RepositoryEvidence);
    }

    [Fact]
    public void A_symbol_scan_leaves_out_the_dotted_folder_it_was_told_to()
    {
        var options = SymbolScanOptions.Default with { Ignore = [".storybook/**"] };

        options.Excludes(".storybook/main.ts").Should().BeTrue();
        options.Excludes("src/main.ts").Should().BeFalse();
    }
}
