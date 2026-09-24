using FluentAssertions;
using Loadout.Core.Tools;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>What ties a tool to the project it was written for.</summary>
public sealed class ToolGenericityTests
{
    private static readonly string[] Known = ["alpha-shop", "system-watch"];

    [Theory]
    [InlineData(@"Remove-Item D:\alpha\build\cache -Recurse", "an absolute path")]
    [InlineData("Get-ChildItem /home/someone/cache", "an absolute path")]
    [InlineData("Clears the cache for alpha-shop builds.", "a project or team name")]
    [InlineData("Learned by system-watch last week.", "a project or team name")]
    [InlineData("git clone https://github.com/someone/thing", "a repository URL")]
    [InlineData("Ask nobody@example.com first.", "an e-mail address")]
    [InlineData("Subscription 3f2504e0-4f89-11d3-9a0c-0305e82c3301", "a GUID")]
    public void Project_detail_is_refused(string text, string expected)
    {
        ToolGenericity.Check(text, Known).Should().Contain(one => one.StartsWith(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void A_secret_is_reported_by_type_and_never_by_value()
    {
        var token = "ghp_" + new string('a', 36);

        var found = ToolGenericity.Check("$token = '" + token + "'", Known);

        found.Should().Contain(one => one.Contains("GitHub token", StringComparison.Ordinal));
        found.Should().NotContain(one => one.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public void Generic_text_passes()
    {
        ToolGenericity.Check(
            "Clears {tmp}/cache of files older than OlderThanDays, as an alpha-shopfront would. Run: pwsh -File tool.ps1 -CachePath ./cache",
            Known).Should().BeEmpty();
    }
}
