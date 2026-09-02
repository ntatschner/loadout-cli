using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contracts;

/// <summary>
/// The version the README tells somebody to download.
/// </summary>
/// <remarks>
/// The install examples name a file: <c>loadout-0.14.0-linux-x64.tar.gz</c> is
/// what a reader types, and a concrete name is worth more than a placeholder
/// they have to fill in. It also rots silently — it sat at 0.9.2 through five
/// releases, so the first command in the README downloaded a file that no
/// longer existed at the top of the releases page.
/// </remarks>
public sealed class ReadmeVersionTests
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

        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        var named = Artefact.Matches(readme)
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

        named.Should().NotBeEmpty("the README has to show what to download");

        named.Should().OnlyContain(found => found == version,
            $"the README offers a download and the current version is {version}");
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
