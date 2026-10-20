using FluentAssertions;
using Loadout.Core.Teams.Daemon;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which hosted session 'loadout session rename' and 'end' mean: an id, the
/// start of one, or a name, and never a guess between several.
/// </summary>
public sealed class HostedSessionMatchTests
{
    private static readonly HostedSessionRow[] Sessions =
    [
        Row("ab12cd34", "release notes"),
        Row("ab99ee00", "docs"),
        Row("ff001122", "Docs"),
    ];

    [Theory]
    [InlineData("ab12cd34", "ab12cd34")]
    [InlineData("ab1", "ab12cd34")]
    [InlineData("ff", "ff001122")]
    [InlineData("RELEASE NOTES", "ab12cd34")]
    [InlineData("  release notes ", "ab12cd34")]
    public void An_id_a_unique_start_of_one_or_a_name_finds_it(string asked, string id)
    {
        var found = HostedSessionMatch.Find(Sessions, asked);

        found.Succeeded.Should().BeTrue(found.Error);
        found.Value!.Id.Should().Be(id);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("docs")]
    public void Something_that_could_be_several_lists_them_and_picks_none(string asked)
    {
        var found = HostedSessionMatch.Find(Sessions, asked);

        found.Failed.Should().BeTrue();
        found.Error.Should().Contain("nothing was changed").And.Contain("Name one by its id");
    }

    [Fact]
    public void A_whole_id_wins_over_a_name_that_starts_like_another()
    {
        HostedSessionRow[] sessions = [Row("ab12cd34", "x"), Row("ab12cd3400", "y")];

        HostedSessionMatch.Find(sessions, "ab12cd34").Value!.Title.Should().Be("x");
    }

    [Theory]
    [InlineData("zz")]
    [InlineData("")]
    public void Nothing_or_nothing_that_matches_says_so(string asked)
    {
        HostedSessionMatch.Find(Sessions, asked).Failed.Should().BeTrue();
    }

    private static HostedSessionRow Row(string id, string title) =>
        new(id, title, title, false, "starstats", "claude", "launch", null, false, DateTimeOffset.UnixEpoch);
}
