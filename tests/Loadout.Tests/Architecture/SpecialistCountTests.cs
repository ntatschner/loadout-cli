using System.Text.RegularExpressions;
using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Xunit;

namespace Loadout.Tests.Contracts;

/// <summary>
/// The number of specialists the documentation claims.
/// </summary>
/// <remarks>
/// <para>
/// Two sentences state it outright — one in the README and one in
/// <c>docs/specialists.md</c>, the second with a breakdown by kind. Adding a
/// specialist without editing both leaves the documentation quietly wrong, and
/// it has already happened: the count sat at 71 after the library had moved on.
/// </para>
/// <para>
/// Counted from the embedded resources rather than the files on disk,
/// deliberately. A specialist added to the directory but never marked
/// <c>EmbeddedResource</c> is absent from the shipped binary while looking
/// perfectly present in the repository, and counting files would agree with the
/// mistake instead of catching it.
/// </para>
/// </remarks>
public sealed class SpecialistCountTests
{
    private static readonly Regex Total = new(
        @"(\d+) specialists",
        RegexOptions.Compiled);

    private static readonly Regex Skills = new(
        @"(\d+) skills",
        RegexOptions.Compiled);

    [Fact]
    public async Task The_documented_total_is_the_number_that_ships()
    {
        var catalogue = await new SpecialistLibrary().LoadAsync(workspaceRoot: null);

        var shipped = catalogue.All.Count();

        shipped.Should().BeGreaterThan(0, "the embedded library has to be loading at all");

        foreach (var (file, text) in Documentation())
        {
            foreach (Match match in Total.Matches(text))
            {
                int.Parse(match.Groups[1].Value).Should().Be(
                    shipped,
                    $"{file} tells the reader how many specialists there are");
            }
        }
    }

    [Fact]
    public async Task The_documented_breakdown_is_the_number_that_ships()
    {
        var catalogue = await new SpecialistLibrary().LoadAsync(workspaceRoot: null);

        var skills = catalogue.OfKind(SpecialistKind.Skill).Count();

        skills.Should().BeGreaterThan(0, "the library ships skills");

        foreach (var (file, text) in Documentation())
        {
            foreach (Match match in Skills.Matches(text))
            {
                int.Parse(match.Groups[1].Value).Should().Be(
                    skills,
                    $"{file} breaks the library down by kind");
            }
        }
    }

    private static IEnumerable<(string File, string Text)> Documentation()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("the repository has to be findable from the tests");

        var found = 0;

        // Named rather than scanned, because docs/specialists-architecture.md
        // says "52 specialists" and is right to: it is the survey of the
        // external bundle this library was written instead of, not a claim
        // about what ships. A blanket scan would fail on a historical figure.
        string[] pages =
        [
            "README.md",
            Path.Combine("docs", "features.md"),
            Path.Combine("docs", "specialists.md"),
        ];

        foreach (var relative in pages)
        {
            var path = Path.Combine(root!.FullName, relative);

            if (File.Exists(path))
            {
                found++;

                yield return (relative, File.ReadAllText(path));
            }
        }

        found.Should().Be(pages.Length, "every page stating a count has to be found");
    }
}
