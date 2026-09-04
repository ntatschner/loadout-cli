using FluentAssertions;
using Loadout.Core.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Whether a topic's one index line can be chosen from.
/// </summary>
/// <remarks>
/// <para>
/// The real descriptions here are taken from a working memory store rather than
/// invented, because the risk in a check like this is not missing a bad one — it
/// is refusing a good one, and a terse real description is exactly what a
/// length rule gets wrong.
/// </para>
/// <para>
/// Nothing here judges whether a description is true. That is not checkable, and
/// a regular expression that claimed to do it would be a guess with a confident
/// face on.
/// </para>
/// </remarks>
public sealed class MemoryDescriptionTests
{
    [Theory]
    [InlineData("windows-restart-manager-disabled", "why installers fail with 1603 over a running app")]
    [InlineData("build-quirks", "things that surprise people about the build")]
    [InlineData("loadout-versioning", "how release versions are chosen, and how to write the notes")]
    [InlineData("palette-accepted", "the command palette list does not raise Accepted, use Accepting")]
    [InlineData("oidc", "why Azure federated credentials must be added as \"Other issuer\"")]
    public void A_description_that_says_what_the_topic_answers_is_kept(string name, string description)
    {
        MemoryDescriptionClassifier.Classify(name, description)
            .Should().Be(DescriptionVerdict.Decidable);
    }

    [Theory]
    [InlineData("build-quirks", "notes")]
    [InlineData("build-quirks", "misc")]
    [InlineData("deploy", "various things")]
    [InlineData("schema", "general information")]
    public void A_line_describing_that_a_note_exists_is_refused(string name, string description)
    {
        MemoryDescriptionClassifier.Classify(name, description)
            .Should().Be(DescriptionVerdict.Placeholder);
    }

    [Fact]
    public void The_line_the_agent_tool_used_to_write_is_refused()
    {
        // What loadout_remember wrote before it was made to ask: it says an
        // agent recorded something, which is the one thing a later session can
        // already see, and it was paid for on every launch.
        MemoryDescriptionClassifier.Classify("deploy", "Recorded by an agent working on starstats.")
            .Should().Be(DescriptionVerdict.Placeholder);
    }

    [Theory]
    [InlineData("build-quirks", "build quirks")]
    [InlineData("windows-restart-manager", "the windows restart manager")]
    [InlineData("deploy-steps", "steps to deploy")]
    [InlineData("build-quirks", "the build")]
    public void A_line_that_says_the_name_back_is_refused(string name, string description)
    {
        // The name is already on the index line. Saying it again doubles the
        // line and adds nothing a session could choose on. Part of the name
        // counts too: "the build" under 'build-quirks' is the same failure in
        // fewer words, and naming it that way tells its author what to fix.
        MemoryDescriptionClassifier.Classify(name, description)
            .Should().Be(DescriptionVerdict.RestatesTheName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("slow")]
    [InlineData("fails with 1603")]
    public void A_line_too_short_to_say_anything_is_refused(string? description)
    {
        MemoryDescriptionClassifier.Classify("build-quirks", description)
            .Should().Be(DescriptionVerdict.TooShort);
    }

    [Fact]
    public void A_description_using_a_placeholder_word_in_a_real_sentence_is_kept()
    {
        // "details" and "notes" mean something inside a sentence. Matching them
        // as substrings would refuse a perfectly good line, which is the failure
        // that teaches people to work around the check.
        MemoryDescriptionClassifier.Classify(
            "release-process",
            "the details nobody remembers when cutting a release")
            .Should().Be(DescriptionVerdict.Decidable);
    }

    [Fact]
    public void A_description_that_adds_one_real_word_to_the_name_is_kept()
    {
        // The line has to add something, not everything. This is the boundary
        // the rule sits on, and it should fall on the permissive side.
        MemoryDescriptionClassifier.Classify(
            "restart-manager",
            "the restart manager is disabled by policy on this machine")
            .Should().Be(DescriptionVerdict.Decidable);
    }

    [Fact]
    public void Every_refusal_can_be_explained_to_whoever_wrote_it()
    {
        foreach (var verdict in Enum.GetValues<DescriptionVerdict>())
        {
            MemoryDescriptionClassifier.Explain(verdict).Should().NotBeNullOrWhiteSpace(
                "a refusal nobody can act on is a refusal that gets worked around");
        }
    }
}
