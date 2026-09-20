using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Whether a remediator may run a script on somebody's machine with nobody
/// watching.
/// </summary>
/// <remarks>
/// Two keys, and neither of them an agent's: a person at this machine trusts a
/// particular script, and this machine's own configuration says what that kind
/// of task may do. Every test here is one of the ways that can go wrong.
/// </remarks>
public sealed class RemedyCeilingTests
{
    private const string Script = "Remove-Item D:\\builds -Recurse\n";

    private static Remedy Registered(string kind = "disk", bool trusted = false, string? script = Script) =>
        new()
        {
            Name = "clear-build-cache",
            Kind = kind,
            Script = "clear-build-cache.ps1",
            Trust = trusted ? Remedy.Trusted : Remedy.Untrusted,
            Fingerprint = trusted && script is not null ? RemedyCeiling.Fingerprint(script) : string.Empty,
        };

    [Fact]
    public void Nothing_runs_unattended_that_nobody_has_trusted()
    {
        // The ordinary case, and the one a fresh machine has to get right: a
        // team registers a script, the machine is happy for that kind of task
        // to run unattended, and nobody has read this one.
        var decided = RemedyCeiling.Decide(Registered(), RemedyRules.Trusted, Script);

        decided.Ruling.Should().Be(RemedyRuling.Ask);
        decided.Because.Should().Contain("Nobody has trusted");
    }

    [Fact]
    public void Trust_alone_does_not_let_anything_run()
    {
        // The other half. A person reading a script and saying it is fine is
        // not the same as a machine saying that kind of task may happen with
        // nobody watching, and a remedy that ran on one of those would make
        // the second setting decorative.
        var decided = RemedyCeiling.Decide(Registered(trusted: true), RemedyRules.Ask, Script);

        decided.Ruling.Should().Be(RemedyRuling.Ask);
        decided.Because.Should().Contain("holds every remediation of kind 'disk'");
    }

    [Fact]
    public void Both_keys_turned_is_the_only_way_anything_runs_unattended()
    {
        var decided = RemedyCeiling.Decide(Registered(trusted: true), RemedyRules.Trusted, Script);

        decided.Ruling.Should().Be(RemedyRuling.Run);
    }

    [Fact]
    public void Never_means_never_however_trusted_the_remedy_is()
    {
        // Checked before anything about the remedy, so a machine that has said
        // never is not talked round by a script somebody liked.
        var decided = RemedyCeiling.Decide(Registered(trusted: true), RemedyRules.Never, Script);

        decided.Ruling.Should().Be(RemedyRuling.Refuse);
        decided.Because.Should().Contain("refuses remediation of kind 'disk' outright");
    }

    [Fact]
    public void Improving_a_remedy_spends_the_trust_it_was_given()
    {
        // The failure this exists to stop. The declaration that tells a team to
        // keep improving what it registers is exactly the thing that would
        // otherwise carry one script's trust onto another, and the second
        // script is the one nobody read.
        var decided = RemedyCeiling.Decide(
            Registered(trusted: true),
            RemedyRules.Trusted,
            Script + "Remove-Item C:\\ -Recurse\n");

        decided.Ruling.Should().Be(RemedyRuling.Ask);
        decided.Because.Should().Contain("changed since it was trusted");
    }

    [Fact]
    public void A_remedy_trusted_without_a_fingerprint_is_asked_about()
    {
        // Trusted by something that did not record what it trusted, or before
        // there was anything to record. The safe reading of "I cannot tell
        // whether this is what you agreed to" is to ask.
        var remedy = Registered(trusted: true);
        remedy.Fingerprint = string.Empty;

        RemedyCeiling.Decide(remedy, RemedyRules.Trusted, Script)
            .Ruling.Should().Be(RemedyRuling.Ask);
    }

    [Fact]
    public void A_kind_this_machine_has_never_heard_of_is_asked_about()
    {
        // Asking rather than refusing. A remediator that cannot ask is one that
        // stops, and a person who is never asked never learns the kind exists
        // to write a rule for it.
        RemedyCeiling.Decide(Registered("something-new", trusted: true), rule: null, Script)
            .Ruling.Should().Be(RemedyRuling.Ask);

        RemedyRules.Default.Should().Be(RemedyRules.Ask);
    }

    [Fact]
    public void A_remedy_that_is_not_there_is_refused_rather_than_asked_about()
    {
        var decided = RemedyCeiling.Decide(null, RemedyRules.Trusted);

        decided.Ruling.Should().Be(RemedyRuling.Refuse);
        decided.Because.Should().Contain("no remedy by that name");
    }

    [Fact]
    public void A_script_that_crossed_between_machines_is_the_same_script()
    {
        // Line endings are not a change anybody meant to make, and a person who
        // trusted a script on Windows would say the same file on Linux is the
        // one they trusted.
        var remedy = Registered(trusted: true, script: "one\ntwo\n");

        RemedyCeiling.Matches(remedy, "one\r\ntwo\r\n").Should().BeTrue();
        RemedyCeiling.Matches(remedy, "one\ntwo\nthree\n").Should().BeFalse();
    }
}
