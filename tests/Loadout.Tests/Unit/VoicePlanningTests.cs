using FluentAssertions;
using Loadout.Core.Voice;
using Loadout.Models.Voice;
using Xunit;

namespace Loadout.Tests.Unit;

public sealed class VoicePlanningTests
{
    private static VoiceEvidence Piece(string id, string source, int length) =>
        new(id, source, id, new string('x', length), "h" + id);

    [Fact]
    public void Writing_is_batched_without_splitting_a_piece()
    {
        var guide = VoiceGuides.Prose();
        guide.Cap.BatchCharacters = 100;

        var batches = VoicePlanning.Batches([Piece("e1", "doc", 60), Piece("e2", "doc", 30), Piece("e3", "doc", 150), Piece("e4", "doc", 10)], guide);

        batches.Select(b => string.Join(",", b.Select(p => p.Id))).Should().Equal("e1,e2", "e3", "e4");
    }

    [Fact]
    public void The_estimate_uses_earlier_runs_once_there_are_any()
    {
        var guide = VoiceGuides.Prose();

        var seeded = VoicePlanning.Estimate(4, new VoiceLearnState(), guide);
        seeded.Should().Be(new VoiceEstimate(4, 4 * guide.Cap.SeedCallUsd, guide.Cap.SeedCallUsd, FromHistory: false));

        var state = new VoiceLearnState { Runs = [new VoiceRun { Calls = 2, CostUsd = 0.20m }, new VoiceRun { Calls = 2, CostUsd = 0.60m }] };

        VoicePlanning.Estimate(4, state, guide).Should().Be(new VoiceEstimate(4, 0.80m, 0.20m, FromHistory: true));
    }

    [Theory]
    [InlineData(null, null, VoiceCapQuestion.First)]
    [InlineData(5, 1.00, VoiceCapQuestion.None)]
    [InlineData(5, 0.50, VoiceCapQuestion.Larger)]
    [InlineData(1, 5.00, VoiceCapQuestion.Larger)]
    [InlineData(20, 2.00, VoiceCapQuestion.Lower)]
    [InlineData(20, 1.40, VoiceCapQuestion.None)]
    public void The_cap_asks_only_when_it_has_to(int? calls, double? usd, VoiceCapQuestion expected)
    {
        // Two calls at the seed of 0.30: an estimate of 0.60, proposed at 0.75.
        var guide = VoiceGuides.Prose();
        var estimate = VoicePlanning.Estimate(2, new VoiceLearnState(), guide);
        var stored = calls is null ? null : new VoiceCap { Calls = calls.Value, Usd = (decimal)usd!.Value };

        VoicePlanning.Question(stored, estimate, guide).Should().Be(expected);
    }

    [Fact]
    public void A_typed_cap_allows_at_least_one_call()
    {
        var estimate = new VoiceEstimate(4, 1.20m, 0.30m, false);

        VoicePlanning.Typed(0.01m, estimate, DateTimeOffset.UnixEpoch).Calls.Should().Be(1);
        VoicePlanning.Typed(0.90m, estimate, DateTimeOffset.UnixEpoch).Calls.Should().Be(3);
    }

    [Fact]
    public void Only_citations_whose_text_still_holds_count_towards_the_threshold()
    {
        var guide = VoiceGuides.Prose();
        var rule = new VoiceRule
        {
            Citations =
            [
                new VoiceCitation { Source = "doc", Reference = "a.md", Hash = "1" },
                new VoiceCitation { Source = "doc", Reference = "b.md", Hash = "2" },
                new VoiceCitation { Source = "commit", Reference = "c1", Hash = "3" },
                new VoiceCitation { Source = "interview", Reference = "run", Hash = null },
            ],
        };

        var all = new HashSet<(string, string)> { ("a.md", "1"), ("b.md", "2"), ("c1", "3") };
        VoicePlanning.Count(rule, guide, all).Should().Be(new VoiceEvidenceCount(3, 2, Meets: true));

        var lapsed = new HashSet<(string, string)> { ("a.md", "changed"), ("b.md", "2"), ("c1", "3") };
        VoicePlanning.Count(rule, guide, lapsed).Should().Be(new VoiceEvidenceCount(2, 2, Meets: false));

        // Three citations, all of one kind: enough of them, from too few kinds.
        var docsOnly = new VoiceRule
        {
            Citations =
            [
                new VoiceCitation { Source = "doc", Reference = "a.md", Hash = "1" },
                new VoiceCitation { Source = "doc", Reference = "b.md", Hash = "2" },
                new VoiceCitation { Source = "doc", Reference = "c.md", Hash = "3" },
            ],
        };
        var docs = new HashSet<(string, string)> { ("a.md", "1"), ("b.md", "2"), ("c.md", "3") };
        VoicePlanning.Count(docsOnly, guide, docs).Should().Be(
            new VoiceEvidenceCount(3, 1, Meets: false), "two kinds of writing are needed, not one");
    }
}
