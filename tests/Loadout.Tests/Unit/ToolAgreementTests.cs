using FluentAssertions;
using Loadout.Core.Tools;
using Loadout.Models.Configuration;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// A person agreeing to a draft's harness run from what the registry shows
/// them, rather than from an agreement a test writes for itself.
/// </summary>
/// <remarks>
/// <see cref="ToolStoreFixture.Agreed" /> builds its agreement by hand, and for
/// a while nothing a person could run built one at all: every verify on a real
/// machine was held. These go through <see cref="IToolRegistry.Agreement" />,
/// which is what the command records.
/// </remarks>
public sealed class ToolAgreementTests : IDisposable
{
    private const string Script = "param([string]$CachePath)\nGet-ChildItem $CachePath | Remove-Item\n";

    private readonly ToolStoreFixture _store = new();

    public void Dispose() => _store.Dispose();

    [Fact]
    public async Task A_persons_answer_to_what_the_registry_shows_lets_the_draft_run()
    {
        var (registry, _) = _store.Registry();
        var draft = _store.Draft(ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        var held = await registry.VerifyAsync(draft, new ToolTestConsent(null, []));
        held.Value!.Ruling.Should().Be(RemedyRuling.Ask);

        var answered = ToolHarness.Answered(new ToolTestConsent(null, []), Agreeing(registry, draft));
        var verified = await registry.VerifyAsync(draft, answered);

        verified.Value!.Ruling.Should().Be(RemedyRuling.Run, verified.Value.Because);
        verified.Value.Gate!.Passed.Should().BeTrue(verified.Value.Because);
    }

    [Fact]
    public void The_agreement_names_the_script_and_every_case_it_covers()
    {
        var (registry, _) = _store.Registry();
        var cases = ToolStoreFixture.Cases();
        var draft = _store.Draft(ToolStoreFixture.Manifest("free-cache", "1.0"), Script, cases);

        var agreement = registry.Agreement(draft).Value!;

        agreement.Remedy.Should().Be("tool-test:free-cache@1.0");
        agreement.ScriptPath.Should().Be(Path.Combine(draft, "tool.ps1"));
        agreement.Cases.Should().BeEquivalentTo(cases.Select(one => one.Name));
    }

    [Fact]
    public async Task A_case_changed_after_the_answer_is_held_again()
    {
        var (registry, _) = _store.Registry();
        var draft = _store.Draft(ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());
        var answered = ToolHarness.Answered(new ToolTestConsent(null, []), Agreeing(registry, draft));

        // A different argument, not a comment: the cases are read before they
        // are fingerprinted, so only what would run differently counts.
        var file = Directory.GetFiles(Path.Combine(draft, "cases")).Order(StringComparer.Ordinal).First();
        File.WriteAllText(file, File.ReadAllText(file).Replace("{tmp}/cache", "{tmp}/elsewhere", StringComparison.Ordinal));

        var verified = await registry.VerifyAsync(draft, answered);

        verified.Value!.Ruling.Should().Be(RemedyRuling.Ask);
        verified.Value.Gate.Should().BeNull();
        verified.Value.Because.Should().Contain("changed since");
    }

    [Fact]
    public async Task A_machine_that_says_never_is_not_overruled_by_an_answer()
    {
        var (registry, _) = _store.Registry();
        var draft = _store.Draft(ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        var answered = ToolHarness.Answered(new ToolTestConsent(RemedyRules.Never, []), Agreeing(registry, draft));
        var verified = await registry.VerifyAsync(draft, answered);

        answered.Rule.Should().Be(RemedyRules.Never);
        verified.Value!.Ruling.Should().Be(RemedyRuling.Refuse);
        verified.Value.Gate.Should().BeNull();
    }

    [Fact]
    public void An_answer_replaces_an_earlier_agreement_to_the_same_draft_and_keeps_the_rest()
    {
        var other = new TrustedRemedy { Team = "system-watch", Remedy = "free-disk", Fingerprint = "aa" };
        var stale = new TrustedRemedy { Remedy = "tool-test:free-cache@1.0", Fingerprint = "bb" };
        var fresh = new TrustedRemedy { Remedy = "tool-test:free-cache@1.0", Fingerprint = "cc" };

        var answered = ToolHarness.Answered(new ToolTestConsent("ask", [other, stale]), fresh);

        answered.Trusted.Should().BeEquivalentTo([other, fresh]);
    }

    private static TrustedRemedy Agreeing(ToolRegistry registry, string draft)
    {
        var agreement = registry.Agreement(draft);
        agreement.Succeeded.Should().BeTrue(agreement.Error);

        return new TrustedRemedy { Remedy = agreement.Value!.Remedy, Fingerprint = agreement.Value.Fingerprint };
    }
}
