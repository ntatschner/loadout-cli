using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models.Configuration;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Whether a remediator may run a script on somebody's machine with nobody
/// watching.
/// </summary>
/// <remarks>
/// <para>
/// Two keys, and neither of them an agent's: this machine's own configuration
/// says what a kind of task may do, and this machine's own configuration says
/// which exact scripts have been agreed to.
/// </para>
/// <para>
/// Both live here rather than beside the script, and that is the whole point.
/// A remedy's record is in the team's directory, the team's nodes are told
/// where that is and told to put things in it, and <c>role.fixer</c> has
/// unrestricted <c>Write</c>. Trust kept there was trust an agent could grant
/// itself.
/// </para>
/// </remarks>
public sealed class RemedyCeilingTests
{
    private const string Script = "Remove-Item D:\\builds -Recurse\n";

    private static Remedy Registered(string kind = "disk", bool claims = false) =>
        new()
        {
            Name = "clear-build-cache",
            Kind = kind,
            Script = "clear-build-cache.ps1",

            // What the record says about itself, which decides nothing.
            Trust = claims ? Remedy.Trusted : Remedy.Untrusted,
            Fingerprint = claims ? RemedyCeiling.Fingerprint(Script) : string.Empty,
        };

    private static IReadOnlyList<TrustedRemedy> Agreed(
        string remedy = "clear-build-cache",
        string? script = Script,
        string team = "system-watch") =>
        [
            new TrustedRemedy
            {
                Team = team,
                Remedy = remedy,
                Fingerprint = script is null ? string.Empty : RemedyCeiling.Fingerprint(script),
                By = "nigel",
            },
        ];

    [Fact]
    public void A_remedy_cannot_trust_itself()
    {
        // The hole this design had, found by asking whether trust was backed by
        // anything an agent could not write. It was not: the record held the
        // trust, the record is in the team's directory, and a node with Write
        // could claim trusted and compute the fingerprint over its own script.
        // It ran unasked.
        var decided = RemedyCeiling.Decide(
            Registered(claims: true), RemedyRules.Trusted, Script, trusted: []);

        decided.Ruling.Should().Be(RemedyRuling.Ask);
        decided.Because.Should().Contain("Nobody at this machine has agreed");
    }

    [Fact]
    public void Nothing_runs_unattended_that_this_machine_has_not_agreed_to()
    {
        var decided = RemedyCeiling.Decide(Registered(), RemedyRules.Trusted, Script, trusted: []);

        decided.Ruling.Should().Be(RemedyRuling.Ask);
        decided.Because.Should().Contain("Nobody at this machine has agreed");
    }

    [Fact]
    public void Agreement_alone_does_not_let_anything_run()
    {
        // The other key. Somebody reading a script and saying it is fine is not
        // the same as this machine saying that kind of task may happen with
        // nobody watching.
        var decided = RemedyCeiling.Decide(Registered(), RemedyRules.Ask, Script, Agreed());

        decided.Ruling.Should().Be(RemedyRuling.Ask);
        decided.Because.Should().Contain("holds every remediation of kind 'disk'");
    }

    [Fact]
    public void Both_keys_turned_is_the_only_way_anything_runs_unattended()
    {
        RemedyCeiling.Decide(Registered(), RemedyRules.Trusted, Script, Agreed())
            .Ruling.Should().Be(RemedyRuling.Run);
    }

    [Fact]
    public void Never_means_never_however_agreed_the_remedy_is()
    {
        // Checked before anything about the remedy, so a machine that has said
        // never is not talked round by a script somebody liked.
        var decided = RemedyCeiling.Decide(Registered(), RemedyRules.Never, Script, Agreed());

        decided.Ruling.Should().Be(RemedyRuling.Refuse);
        decided.Because.Should().Contain("refuses remediation of kind 'disk' outright");
    }

    [Fact]
    public void Improving_a_remedy_spends_the_agreement_it_was_given()
    {
        // The declaration that tells a team to keep improving what it registers
        // is exactly the thing that would otherwise carry one script's
        // agreement onto another, and the second script is the one nobody read.
        var decided = RemedyCeiling.Decide(
            Registered(),
            RemedyRules.Trusted,
            Script + "Remove-Item C:\\ -Recurse\n",
            Agreed());

        decided.Ruling.Should().Be(RemedyRuling.Ask);
        decided.Because.Should().Contain("changed since it was agreed to");
    }

    [Fact]
    public void An_agreement_with_no_fingerprint_agreed_to_nothing_in_particular()
    {
        RemedyCeiling.Decide(Registered(), RemedyRules.Trusted, Script, Agreed(script: null))
            .Ruling.Should().Be(RemedyRuling.Ask);
    }

    [Fact]
    public void A_script_that_cannot_be_read_is_asked_about()
    {
        // No telling whether it is the one that was agreed to, and the safe
        // reading of that is to ask.
        var decided = RemedyCeiling.Decide(Registered(), RemedyRules.Trusted, script: null, Agreed());

        decided.Ruling.Should().Be(RemedyRuling.Ask);
        decided.Because.Should().Contain("could not be read");
    }

    [Fact]
    public void An_agreement_for_another_team_is_not_an_agreement_for_this_one()
    {
        // For() narrows by team before any of this, so a remedy of the same
        // name in two teams is two decisions.
        RemedyCeiling.For(Agreed(team: "docs-crew"), "system-watch").Should().BeEmpty();
        RemedyCeiling.For(Agreed(), "system-watch").Should().ContainSingle();
    }

    [Fact]
    public void An_agreement_for_another_remedy_is_not_an_agreement_for_this_one()
    {
        RemedyCeiling.Decide(Registered(), RemedyRules.Trusted, Script, Agreed(remedy: "something-else"))
            .Ruling.Should().Be(RemedyRuling.Ask);
    }

    [Fact]
    public void A_kind_this_machine_has_never_heard_of_is_asked_about()
    {
        // Asking rather than refusing. A remediator that cannot ask is one that
        // stops, and a person who is never asked never learns the kind exists
        // to write a rule for it.
        RemedyCeiling.Decide(Registered("something-new"), rule: null, Script, Agreed())
            .Ruling.Should().Be(RemedyRuling.Ask);

        RemedyRules.Default.Should().Be(RemedyRules.Ask);
    }

    [Fact]
    public void A_remedy_that_is_not_there_is_refused_rather_than_asked_about()
    {
        var decided = RemedyCeiling.Decide(null, RemedyRules.Trusted, Script, Agreed());

        decided.Ruling.Should().Be(RemedyRuling.Refuse);
        decided.Because.Should().Contain("no remedy by that name");
    }

    [Fact]
    public void A_script_that_crossed_between_machines_is_the_same_script()
    {
        // Line endings are not a change anybody meant to make, and somebody who
        // agreed to a script on Windows would say the same file on Linux is the
        // one they agreed to.
        var agreed = Agreed(script: "one\ntwo\n")[0];

        RemedyCeiling.Agrees(agreed, "one\r\ntwo\r\n").Should().BeTrue();
        RemedyCeiling.Agrees(agreed, "one\ntwo\nthree\n").Should().BeFalse();
    }
}
