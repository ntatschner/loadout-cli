using FluentAssertions;
using Loadout.Core.Voice;
using Loadout.Models.Voice;
using Xunit;

namespace Loadout.Tests.Unit;

public sealed class VoiceMiningTests
{
    private static readonly IReadOnlyList<VoiceEvidence> Batch =
    [
        new("e1", "doc", "docs/a.md", "We picked a colour.\nIt   suits us.", "h1"),
        new("e2", "commit", "abc", "Tidy the colour handling (#4)", "h2"),
    ];

    private static string Answer(string excerpt, string evidence = "e1", string check = """{"type":"avoid-phrase","phrases":["leverage"]}""") =>
        $$"""{"contract":"voice-mine/1","rules":[{"id":"no-leverage","statement":"Never say leverage.","check":{{check}},"surfaces":[],"citations":[{"evidence":"{{evidence}}","excerpt":"{{excerpt}}"}]}]}""";

    [Fact]
    public void A_faithful_answer_becomes_rules_carrying_their_sources_and_hashes()
    {
        var read = VoiceMining.Read(Answer("It suits us."), VoiceGuides.Prose(), Batch);

        read.Succeeded.Should().BeTrue(read.Error);
        var citation = read.Value!.Single().Citations.Single();
        (citation.Source, citation.Reference, citation.Hash).Should().Be(("doc", "docs/a.md", "h1"));
        citation.Excerpt.Should().Be("It suits us.", "spacing in the excerpt is evened out, not its words");
    }

    [Fact]
    public void An_excerpt_not_in_the_writing_word_for_word_is_refused()
    {
        VoiceMining.Read(Answer("It fits us."), VoiceGuides.Prose(), Batch)
            .Error.Should().Contain("not in it exactly");
    }

    [Fact]
    public void A_citation_of_a_piece_that_was_not_given_is_refused()
    {
        VoiceMining.Read(Answer("It suits us.", evidence: "e9"), VoiceGuides.Prose(), Batch)
            .Error.Should().Contain("e9");
    }

    [Fact]
    public void A_check_type_the_guide_does_not_have_is_refused()
    {
        VoiceMining.Read(Answer("It suits us.", check: """{"type":"passive-ratio"}"""), VoiceGuides.Prose(), Batch)
            .Error.Should().Contain("passive-ratio");
    }

    [Fact]
    public void An_answer_in_another_shape_is_refused()
    {
        VoiceMining.Read("""{"contract":"dump/1","rules":[]}""", VoiceGuides.Prose(), Batch).Failed.Should().BeTrue();
        VoiceMining.Read(null, VoiceGuides.Prose(), Batch).Failed.Should().BeTrue();
    }

    [Fact]
    public void The_same_rule_from_two_batches_pools_its_citations()
    {
        var one = new VoiceRule { Id = "x", Statement = "first", Citations = [new VoiceCitation { Reference = "a", Excerpt = "1" }] };
        var two = new VoiceRule { Id = "x", Statement = "second", Citations = [new VoiceCitation { Reference = "b", Excerpt = "2" }, new VoiceCitation { Reference = "a", Excerpt = "1" }] };

        var merged = VoiceMining.Merge([one, two]);

        merged.Should().ContainSingle().Which.Statement.Should().Be("first");
        merged[0].Citations.Select(c => c.Reference).Should().Equal("a", "b");
    }

    [Fact]
    public void The_prompt_carries_the_writing_and_the_rules_already_known()
    {
        var prompt = VoiceMining.Prompt(
            VoiceGuides.Prose(), Batch, [new VoiceRule { Id = "british", Statement = "Write British spellings." }]);

        prompt.Should().Contain("=== e2 (commit) ===").And.Contain("Tidy the colour handling")
            .And.Contain("british: Write British spellings.");
    }
}
