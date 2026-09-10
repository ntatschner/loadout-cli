using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contracts;

/// <summary>
/// The version the documentation tells somebody to download.
/// </summary>
/// <remarks>
/// <para>
/// The install examples name a file: <c>loadout-0.14.0-linux-x64.tar.gz</c> is
/// what a reader types, and a concrete name is worth more than a placeholder
/// they have to fill in. It also rots silently — it sat at 0.9.2 through five
/// releases, so the first command in the README downloaded a file that no
/// longer existed at the top of the releases page.
/// </para>
/// <para>
/// This once read the README and nothing else, which is how
/// <c>docs/installing.md</c> came to sit at 0.9.2 for seventeen releases while
/// the check that existed to prevent exactly that reported green. Every page is
/// read now, so a second copy of the install instructions cannot rot out of
/// sight of the first.
/// </para>
/// </remarks>
public sealed class InstallExampleVersionTests
{
    private static readonly Regex Artefact = new(
        @"loadout[-_](\d+\.\d+\.\d+)[-_]",
        RegexOptions.Compiled);

    private static readonly Regex Declared = new(
        @"<Version>(\d+\.\d+\.\d+)</Version>",
        RegexOptions.Compiled);

    [Fact]
    public void The_install_examples_name_the_version_that_ships()
    {
        var root = Repository();

        var build = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));

        var declared = Declared.Match(build);

        declared.Success.Should().BeTrue("the build has to declare a version");

        var version = declared.Groups[1].Value;

        var naming = Documentation(root)
            .Select(page => (page.Relative, Named: Artefact.Matches(page.Text)
                .Select(match => match.Groups[1].Value)
                .Distinct()
                .ToList()))
            .Where(page => page.Named.Count > 0)
            .ToList();

        naming.Should().NotBeEmpty("the documentation has to show what to download");

        foreach (var (relative, named) in naming)
        {
            named.Should().OnlyContain(found => found == version,
                $"{relative} offers a download and the current version is {version}");
        }
    }

    /// <summary>
    /// Every Markdown page a reader might follow: the docs directory and the
    /// pages beside the root README.
    /// </summary>
    /// <remarks>
    /// Discovered rather than listed. A named list is a second thing to keep in
    /// step, and the page that rotted was the one nobody remembered to add.
    /// </remarks>
    private static IEnumerable<(string Relative, string Text)> Documentation(string root)
    {
        var pages = Directory.EnumerateFiles(root, "*.md", SearchOption.TopDirectoryOnly);

        var docs = Path.Combine(root, "docs");

        if (Directory.Exists(docs))
        {
            pages = pages.Concat(Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories));
        }

        foreach (var page in pages.Order(StringComparer.Ordinal))
        {
            yield return (
                Path.GetRelativePath(root, page).Replace('\\', '/'),
                File.ReadAllText(page));
        }
    }

    private static string Repository()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("the repository has to be findable from the tests");

        return root!.FullName;
    }
}
