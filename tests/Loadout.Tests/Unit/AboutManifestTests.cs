using FluentAssertions;
using Loadout.Core.About;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The feature matrix and the guide table shipped in the binary: every guide
/// link comes from one table, so a feature naming a guide the table lacks would
/// be a dead link on every surface at once.
/// </summary>
public sealed class AboutManifestTests
{
    private const string Prefix = "https://github.com/ntatschner/loadout-cli/blob/main/";

    [Fact]
    public void Every_feature_names_a_guide_the_table_has()
    {
        var keys = AboutManifest.Guides().Select(g => g.Key).ToHashSet(StringComparer.Ordinal);

        AboutManifest.Features().Should().NotBeEmpty();
        AboutManifest.Features().Select(f => f.Guide).Should().OnlyContain(guide => keys.Contains(guide));
    }

    [Fact]
    public void Every_guide_points_at_docs_on_github_that_exist()
    {
        var root = RepositoryRoot();

        foreach (var guide in AboutManifest.Guides())
        {
            guide.Url.Should().StartWith(Prefix + "docs/", guide.Key);

            var relative = guide.Url[Prefix.Length..].Replace('/', Path.DirectorySeparatorChar);

            File.Exists(Path.Combine(root, relative)).Should().BeTrue($"{guide.Key} points at {relative}, which must exist");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Path.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "The repository root could not be found from the test output directory.");
    }
}
