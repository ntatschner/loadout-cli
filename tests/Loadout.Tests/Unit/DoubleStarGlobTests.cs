using FluentAssertions;
using Loadout.Core.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// A doubled star followed by a slash stands for whole folders, so what comes
/// after it has to start a name rather than finish one.
/// </summary>
public sealed class DoubleStarGlobTests
{
    [Theory]
    [InlineData("**/Dockerfile", "Dockerfile")]
    [InlineData("**/Dockerfile", "src/api/Dockerfile")]
    [InlineData("**/*.cs", "File.cs")]
    [InlineData("**/*.cs", "src/Core/File.cs")]
    [InlineData("src/**/File.cs", "src/File.cs")]
    [InlineData("src/**/File.cs", "src/a/b/File.cs")]
    [InlineData("**/server/**", "server/index.ts")]
    [InlineData("**/server/**", "apps/server/index.ts")]
    public void Folders_it_stands_for_may_be_none_or_many(string glob, string path) =>
        RuleService.Matches(glob, path).Should().BeTrue();

    [Theory]
    [InlineData("**/Dockerfile", "notDockerfile")]
    [InlineData("**/Dockerfile", "src/notDockerfile")]
    [InlineData("**/settings.py", "tests/test_settings.py")]
    [InlineData("**/server.ts", "src/lib/releases.server.ts")]
    [InlineData("**/server/**", "crates/app-server/Cargo.toml")]
    [InlineData("src/**/File.cs", "src/NotFile.cs")]
    [InlineData("src/**/File.cs", "srcFile.cs")]
    public void What_follows_it_starts_a_name(string glob, string path) =>
        RuleService.Matches(glob, path).Should().BeFalse();

    [Theory]
    // Not at a folder boundary, so it is not folders: it still matches
    // across separators as it always has.
    [InlineData("src**/File.cs", "src/a/File.cs", true)]
    [InlineData("src**/File.cs", "srcxFile.cs", true)]
    // Nothing follows it, so it still means everything below.
    [InlineData("src/**/", "src/a/b.cs", true)]
    [InlineData("src/**", "src/a/b.cs", true)]
    public void Elsewhere_it_keeps_its_old_meaning(string glob, string path, bool expected) =>
        RuleService.Matches(glob, path).Should().Be(expected);
}
