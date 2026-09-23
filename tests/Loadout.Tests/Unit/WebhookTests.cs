using FluentAssertions;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Configuration;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Whether something outside this machine may start a run.
/// </summary>
/// <remarks>
/// <para>
/// The only thing Loadout serves that changes anything, so this is the whole of
/// the boundary and it is deliberately two separate acts. A token says who may
/// ask; a list this machine wrote in advance says what they may ask for. Having
/// one without the other grants nothing, because the failure worth designing
/// against is somebody turning the webhook on to try it and forgetting that
/// they did.
/// </para>
/// <para>
/// A run costs money and edits a repository. Every branch here that returns
/// null is a branch that spends somebody's money.
/// </para>
/// </remarks>
public sealed class WebhookTests
{
    private const string Token = "3f2a91c0";

    [Fact]
    public void A_machine_with_no_token_accepts_nothing()
    {
        // The default. Nothing has been turned on, so this is not "refused" so
        // much as "there is no such thing here", and it says so.
        var refused = Webhook.Refuse(Token, expected: null, "docs-crew", ["docs-crew"]);

        refused!.Status.Should().Be(404);
        refused.Detail.Should().NotContain(Token, "a refusal never quotes what it was given");
    }

    [Fact]
    public void A_wrong_token_is_refused_whatever_the_teams_list_says()
    {
        Webhook.Refuse("not-it", Token, "docs-crew", ["docs-crew"])!.Status.Should().Be(403);
        Webhook.Refuse(null, Token, "docs-crew", ["docs-crew"])!.Status.Should().Be(403);
        Webhook.Refuse(string.Empty, Token, "docs-crew", ["docs-crew"])!.Status.Should().Be(403);
    }

    [Fact]
    public void A_right_token_alone_starts_nothing()
    {
        // The half somebody forgets. Turning the webhook on is not the same
        // decision as naming what it may run, and conflating them is how a
        // token meant for one team ends up able to start every team.
        var refused = Webhook.Refuse(Token, Token, "release-crew", allowed: []);

        refused!.Status.Should().Be(403);
        refused.Detail.Should().Contain("team-webhook-teams", "the refusal says what to do about it");
    }

    [Fact]
    public void A_named_team_with_the_right_token_proceeds()
    {
        Webhook.Refuse(Token, Token, "docs-crew", ["docs-crew", "bug-hunt"]).Should().BeNull();
    }

    [Fact]
    public void Naming_one_team_does_not_name_another()
    {
        // Exactly, not by prefix and not by "any team really". A machine that
        // meant both says both.
        Webhook.Refuse(Token, Token, "release-crew", ["docs-crew"])!.Status.Should().Be(403);
        Webhook.Refuse(Token, Token, "docs-crew-extra", ["docs-crew"])!.Status.Should().Be(403);
    }

    [Theory]
    [InlineData("DOCS-CREW")]
    [InlineData(" docs-crew ")]
    public void A_team_name_is_matched_the_way_a_person_would_type_it(string asked)
    {
        // Team names are lowercase and hyphenated by convention, and a caller
        // typing one in a shell script gets the case or the spacing wrong
        // before they get the name wrong.
        Webhook.Refuse(Token, Token, asked, ["docs-crew"]).Should().BeNull();
    }

    [Fact]
    public void A_request_naming_no_team_is_refused_before_anything_else_is_considered()
    {
        Webhook.Refuse(Token, Token, "   ", ["docs-crew"])!.Status.Should().Be(400);
    }

    [Fact]
    public void Nothing_binds_beyond_this_machine_unless_somebody_said_so()
    {
        // Never inferred from the webhook being on: a machine accepting
        // triggered runs from its own git hook wants nothing on the network.
        Webhook.Listen(null).Should().Be("127.0.0.1");
        Webhook.Listen(new MachineTeams()).Should().Be("127.0.0.1");
        Webhook.Listen(new MachineTeams { WebhookListen = "  " }).Should().Be("127.0.0.1");
        Webhook.Listen(new MachineTeams { WebhookListen = " 0.0.0.0 " }).Should().Be("0.0.0.0");
    }

    [Fact]
    public void A_token_is_long_enough_that_guessing_it_is_not_a_plan()
    {
        var token = Webhook.NewToken();

        token.Length.Should().Be(64, "32 bytes, hex");
        token.Should().NotBe(Webhook.NewToken(), "two machines must not share one");
    }
}
