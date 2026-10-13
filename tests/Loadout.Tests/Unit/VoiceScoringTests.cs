using FluentAssertions;
using Loadout.Core.Voice;
using Loadout.Models.Voice;
using Xunit;

namespace Loadout.Tests.Unit;

public sealed class VoiceScoringTests
{
    private static readonly VoiceRule Leverage = new()
    {
        Id = "no-leverage",
        Statement = "Do not write leverage.",
        Check = new VoiceCheck { Type = VoiceChecker.AvoidPhrase, Phrases = ["leverage"] },
    };

    private static VoiceEvidence Own(string reference, string source, string text) =>
        new(reference, source, reference, text, "h" + reference);

    private static VoiceEditPair Pair(string before, string after) => new(1, "abc", before, after);

    [Fact]
    public void Own_writing_agrees_when_the_rule_does_not_fire_on_it()
    {
        var score = VoiceScoring.Score(
            [Leverage],
            [Own("a.md", "doc", "We use what we have."), Own("b.md", "doc", "We leverage it.")],
            [],
            VoiceGuides.Prose());

        var rule = score.Rules.Single();
        (rule.Own, rule.OwnAgreed, rule.Agreement).Should().Be((2, 1, 0.5));
    }

    [Fact]
    public void An_edit_pair_counts_only_when_the_rule_has_an_opinion_on_it()
    {
        var pairs = new[]
        {
            Pair("Leverage the cache", "Use the cache"),     // fired on the draft only: agrees
            Pair("Leverage the cache", "Leverage it anyway"), // fired on what was kept: disagrees
            Pair("Tidy the docs", "Tidy up the docs"),        // no opinion: not counted
        };

        var rule = VoiceScoring.Score([Leverage], [], pairs, VoiceGuides.Prose()).Rules.Single();

        (rule.Edit, rule.EditAgreed, rule.Agreement).Should().Be((2, 1, 0.5));
    }

    [Fact]
    public void A_rule_meets_the_promotion_thresholds_only_with_enough_edit_samples()
    {
        var guide = VoiceGuides.Prose();
        var pairs = Enumerable.Range(0, guide.Promotion.EditFloor).Select(_ => Pair("Leverage it", "Use it")).ToList();

        VoiceScoring.Score([Leverage], [], pairs, guide).Rules.Single().Meets.Should().BeTrue();
        VoiceScoring.Score([Leverage], [], pairs.Skip(1).ToList(), guide).Rules.Single().Meets.Should().BeFalse();
    }

    [Fact]
    public void A_regex_rule_never_meets_them_because_it_never_gates()
    {
        var regex = new VoiceRule
        {
            Id = "no-leverage-pattern",
            Check = new VoiceCheck { Type = VoiceChecker.Regex, Pattern = "(?i)leverage" },
        };
        var pairs = Enumerable.Range(0, 5).Select(_ => Pair("Leverage it", "Use it")).ToList();

        var rule = VoiceScoring.Score([regex], [], pairs, VoiceGuides.Prose()).Rules.Single();

        rule.Agreement.Should().Be(1.0);
        rule.Meets.Should().BeFalse();
    }

    [Fact]
    public void A_judged_rule_is_recorded_as_unscored_with_the_reason()
    {
        var judged = new VoiceRule { Id = "plain", Statement = "Say it plainly." };

        var rule = VoiceScoring.Score([judged], [Own("a.md", "doc", "Plain.")], [], VoiceGuides.Prose()).Rules.Single();

        rule.Agreement.Should().BeNull();
        rule.Unscored.Should().Contain("judged");
    }

    [Fact]
    public void A_rule_is_scored_only_on_the_surfaces_it_covers()
    {
        var subjects = new VoiceRule
        {
            Id = "subjects",
            Surfaces = [VoiceGuides.Commit],
            Check = new VoiceCheck { Type = VoiceChecker.AvoidPhrase, Phrases = ["leverage"] },
        };

        var rule = VoiceScoring.Score(
            [subjects],
            [Own("c1", "commit", "Use the cache"), Own("a.md", "doc", "We leverage it.")],
            [],
            VoiceGuides.Prose()).Rules.Single();

        (rule.Own, rule.OwnAgreed).Should().Be((1, 1), "a doc is not a surface the rule covers");
    }

    [Fact]
    public void The_same_pieces_are_held_out_on_every_run()
    {
        var guide = VoiceGuides.Prose();
        var evidence = Enumerable.Range(0, 200).Select(i => Own($"doc-{i}.md", "doc", "x")).ToList();

        var first = VoiceScoring.HoldOut(evidence, guide).HeldOut.Select(e => e.Reference).ToList();
        var second = VoiceScoring.HoldOut(evidence.AsEnumerable().Reverse().ToList(), guide).HeldOut.Select(e => e.Reference).ToList();

        second.Should().BeEquivalentTo(first, "which pieces are held back depends on each piece, not on the run");
        first.Count.Should().BeInRange(20, 60, "about a fifth of two hundred");

        guide.Scoring.HoldOut = 0;
        VoiceScoring.HoldOut(evidence, guide).HeldOut.Should().BeEmpty();
    }
}
