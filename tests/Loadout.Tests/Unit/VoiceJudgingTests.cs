using FluentAssertions;
using Loadout.Core.Voice;
using Loadout.Models.Voice;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>Scoring with a model's help: variants of held-out writing, and verdicts on judged rules.</summary>
public sealed class VoiceJudgingTests
{
    private static readonly VoiceRule Plain = new() { Id = "plain", Statement = "Say it plainly." };

    private static readonly VoiceRule Leverage = new()
    {
        Id = "no-leverage",
        Statement = "Do not write leverage.",
        Check = new VoiceCheck { Type = VoiceChecker.AvoidPhrase, Phrases = ["leverage"] },
    };

    private static VoiceEvidence Own(string id, string text) => new(id, "doc", id + ".md", text, "h" + id);

    [Fact]
    public void A_variant_for_something_not_asked_about_is_refused()
    {
        var requests = new[] { (Leverage, new VoiceSample("e1", "We use the cache.")) };

        VoiceJudging.ReadVariants(
            """{"contract":"voice-variants/1","variants":[{"rule":"no-leverage","sample":"e2","text":"We leverage it."}]}""",
            requests).Failed.Should().BeTrue();

        var read = VoiceJudging.ReadVariants(
            """{"contract":"voice-variants/1","variants":[{"rule":"no-leverage","sample":"e1","text":"We leverage the cache."}]}""",
            requests);

        read.Value!.Should().Equal(new VoiceVariant("no-leverage", "e1", "We leverage the cache."));
    }

    [Fact]
    public void A_variant_that_changed_nothing_is_dropped_because_it_tests_nothing()
    {
        var requests = new[] { (Leverage, new VoiceSample("e1", "We use the cache.")) };

        VoiceJudging.ReadVariants(
            """{"contract":"voice-variants/1","variants":[{"rule":"no-leverage","sample":"e1","text":" We use the cache. "}]}""",
            requests).Value!.Should().BeEmpty();
    }

    [Fact]
    public void A_verdict_on_a_sample_that_was_not_shown_is_refused()
    {
        VoiceJudging.ReadVerdicts(
            """{"contract":"voice-judge/1","verdicts":[{"rule":"plain","sample":"s9","breaks":true}]}""",
            [Plain],
            [new VoiceSample("e1", "x")]).Failed.Should().BeTrue();
    }

    [Fact]
    public void A_typed_rule_catches_a_variant_written_to_break_it()
    {
        var heldOut = new[] { Own("e1", "We use the cache.") };

        VoiceScoring.VariantRequests([Leverage], heldOut, [], VoiceGuides.Prose())
            .Should().ContainSingle().Which.Sample.Id.Should().Be("e1");

        var rule = VoiceScoring.Score(
            [Leverage], heldOut, [], VoiceGuides.Prose(),
            [new VoiceVariant("no-leverage", "e1", "We leverage the cache.")]).Rules.Single();

        (rule.Own, rule.OwnAgreed, rule.Generated, rule.GeneratedAgreed).Should().Be((1, 1, 1, 1));
        rule.Agreement.Should().Be(1.0);
    }

    [Fact]
    public void A_typed_rule_with_enough_edits_needs_no_variant()
    {
        var guide = VoiceGuides.Prose();
        var pairs = Enumerable.Range(0, guide.Promotion.EditFloor).Select(i => new VoiceEditPair(i, "c", "Leverage it", "Use it")).ToList();

        VoiceScoring.VariantRequests([Leverage], [Own("e1", "Use it.")], pairs, guide).Should().BeEmpty();
        VoiceScoring.VariantRequests([Plain], [Own("e1", "Use it.")], pairs, guide).Should().ContainSingle(
            "a judged rule's edits are not known until it is judged, so it is always given a variant");
    }

    [Fact]
    public void A_judged_rule_is_scored_on_the_verdicts_and_only_on_what_was_shown()
    {
        var heldOut = new[] { Own("e1", "Short."), Own("e2", "Also short.") };
        var pairs = new[] { new VoiceEditPair(1, "c", "Verbose draft", "Kept") };
        var variants = new[] { new VoiceVariant("plain", "e1", "In a manner of speaking, short.") };

        // e2 was not shown, so it counts neither way.
        var judgement = new VoiceJudgement(
            new HashSet<string> { "e1", "d1", "k1", "v1" },
            new HashSet<(string, string)> { ("plain", "d1"), ("plain", "v1") });

        var rule = VoiceScoring.Score([Plain], heldOut, pairs, VoiceGuides.Prose(), variants, judgement).Rules.Single();

        (rule.Own, rule.OwnAgreed, rule.Edit, rule.EditAgreed, rule.Generated, rule.GeneratedAgreed)
            .Should().Be((1, 1, 1, 1, 1, 1));
        rule.Meets.Should().BeFalse("a judged rule never gates, so it never meets the promotion thresholds");
    }

    [Fact]
    public void A_judged_rule_never_meets_the_promotion_thresholds_however_well_it_scores()
    {
        var guide = VoiceGuides.Prose();
        var pairs = Enumerable.Range(0, guide.Promotion.EditFloor).Select(i => new VoiceEditPair(i, "c", "Draft", "Kept")).ToList();
        var shown = Enumerable.Range(1, pairs.Count).SelectMany(i => new[] { $"d{i}", $"k{i}" }).ToHashSet();
        var broken = Enumerable.Range(1, pairs.Count).Select(i => ("plain", $"d{i}")).ToHashSet();

        var rule = VoiceScoring.Score([Plain], [], pairs, guide, judgement: new VoiceJudgement(shown, broken)).Rules.Single();

        (rule.Edit, rule.Agreement).Should().Be((guide.Promotion.EditFloor, 1.0));
        rule.Meets.Should().BeFalse("only a typed check can gate");
    }

    [Fact]
    public void Without_a_judging_round_a_judged_rule_says_why_it_has_no_score()
    {
        var rule = VoiceScoring.Score(
            [Plain], [Own("e1", "x")], [], VoiceGuides.Prose(), unjudged: "the cap was reached").Rules.Single();

        rule.Agreement.Should().BeNull();
        rule.Unscored.Should().Be("the cap was reached");
    }

    [Fact]
    public void A_judging_round_is_filled_with_variants_first_and_never_splits_an_edit_pair()
    {
        var guide = VoiceGuides.Prose();
        guide.Cap.BatchCharacters = 25;
        var pairs = new[] { new VoiceEditPair(1, "c", "0123456789", "0123456789") };
        var variants = new[] { new VoiceVariant("plain", "e1", "0123456789") };

        var samples = VoiceScoring.JudgeSamples([Plain], [Own("e1", "x")], pairs, variants, guide);

        samples.Select(s => s.Id).Should().Equal(["v1"], "the draft fits but what was kept does not, so neither is judged");
    }
}
