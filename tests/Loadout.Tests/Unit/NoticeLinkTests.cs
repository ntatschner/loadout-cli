using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Configuration;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Where a notice about a run sends whoever reads it.
/// </summary>
/// <remarks>
/// A notice is kept in somebody else's chat history, so the property that
/// matters most is the one a reader cannot see: the link never carries the
/// dashboard's token. The daemon used to send the address it printed, which
/// ends in ?token=, to every channel it told.
/// </remarks>
public sealed class NoticeLinkTests
{
    private const string Serving = "http://127.0.0.1:8477/?token=secret-token-value";

    [Fact]
    public void Without_a_link_address_it_links_to_the_dashboard_without_its_token()
    {
        var link = NoticeLink.For(null, Serving, "20261027-1059-75ed");

        link.Should().Be("http://127.0.0.1:8477/studio/#teams/20261027-1059-75ed");
        link.Should().NotContain("token").And.NotContain("secret-token-value");
    }

    [Fact]
    public void A_link_address_takes_the_place_of_where_the_dashboard_listens()
    {
        NoticeLink.For("https://desk.example.ts.net:4800", Serving, "run-1")
            .Should().Be("https://desk.example.ts.net:4800/studio/#teams/run-1");
    }

    [Fact]
    public void A_link_address_behind_a_path_keeps_its_path()
    {
        NoticeLink.For("https://example.com/loadout", Serving, "run-1")
            .Should().Be("https://example.com/loadout/studio/#teams/run-1");
    }

    [Fact]
    public void A_token_in_the_link_address_is_dropped_as_well()
    {
        // Refused when it is set; this is what a hand-edited file gets.
        NoticeLink.For("https://desk.example.ts.net/?token=secret-token-value", Serving, "run-1")
            .Should().Be("https://desk.example.ts.net/studio/#teams/run-1");
    }

    [Fact]
    public void An_empty_link_address_counts_as_unset()
    {
        NoticeLink.For("  ", Serving, "run-1").Should().Be("http://127.0.0.1:8477/studio/#teams/run-1");
    }

    [Fact]
    public void With_nowhere_to_link_it_names_the_command()
    {
        NoticeLink.For(null, null, "run-1").Should().Be("loadout team status run-1");
    }

    [Fact]
    public void The_run_is_escaped_into_the_route()
    {
        NoticeLink.For("https://desk.example", null, "a b").Should().EndWith("#teams/a%20b");
    }

    [Fact]
    public void A_page_link_drops_the_token_like_a_run_link()
    {
        NoticeLink.To(null, Serving, "teams", "loadout team queue list")
            .Should().Be("http://127.0.0.1:8477/studio/#teams");
    }

    [Fact]
    public void A_page_link_with_nowhere_to_go_says_what_it_was_given()
    {
        NoticeLink.To(null, null, "teams", "loadout team queue list").Should().Be("loadout team queue list");
    }

    [Theory]
    [InlineData("desk.example.ts.net")]
    [InlineData("ftp://desk.example")]
    [InlineData("https://desk.example/?token=abc")]
    [InlineData("https://desk.example/#teams")]
    public void The_setting_refuses_what_cannot_be_a_link_address(string value)
    {
        var key = ConfigKeys.Find("team-link-address")!;
        var machine = new MachineConfig();

        var setting = () => key.Write(new LauncherConfig(), machine, value);

        setting.Should().Throw<FormatException>();
        machine.Teams.LinkAddress.Should().BeEmpty();
    }

    [Fact]
    public void The_setting_keeps_an_address_and_clears_with_empty()
    {
        var key = ConfigKeys.Find("team-link-address")!;
        var machine = new MachineConfig();

        key.Write(new LauncherConfig(), machine, " https://desk.example.ts.net:4800 ");
        machine.Teams.LinkAddress.Should().Be("https://desk.example.ts.net:4800");

        key.Write(new LauncherConfig(), machine, string.Empty);
        machine.Teams.LinkAddress.Should().BeEmpty();
    }
}
