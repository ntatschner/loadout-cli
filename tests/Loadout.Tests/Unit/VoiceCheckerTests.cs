using FluentAssertions;
using Loadout.Core.Voice;
using Loadout.Models.Voice;
using Xunit;

namespace Loadout.Tests.Unit;

public sealed class VoiceCheckerTests
{
    private static VoiceRule Rule(string id, VoiceCheck? check, params string[] surfaces) =>
        new() { Id = id, Statement = $"rule {id}.", Check = check, Surfaces = [.. surfaces] };

    [Fact]
    public void An_avoided_phrase_is_found_as_whole_words_on_its_line()
    {
        var rule = Rule("no-leverage", new VoiceCheck { Type = VoiceChecker.AvoidPhrase, Phrases = ["leverage", "in order to"] });

        var result = VoiceChecker.Check("We fixed it.\nWe leverage this In  order to win.\nLeverages differ.", "doc", [rule]);

        result.Findings.Select(f => (f.Line, f.Type)).Should().BeEquivalentTo(
            [(2, VoiceChecker.AvoidPhrase), (2, VoiceChecker.AvoidPhrase)],
            "'leverages' is a different word, and the phrase matches across a run of spaces");
        result.Findings.Should().OnlyContain(f => !f.Gates, "nothing gates until it has been promoted");
    }

    [Fact]
    public void A_spelling_names_the_form_the_person_prefers()
    {
        var rule = Rule("british", new VoiceCheck
        {
            Type = VoiceChecker.PreferSpelling,
            Spellings = new() { ["color"] = "colour", ["organize"] = "organise" },
        });

        var result = VoiceChecker.Check("Pick a Color.\nWe organise things.", "doc", [rule]);

        result.Findings.Should().ContainSingle()
            .Which.Detail.Should().Contain("'Color'").And.Contain("'colour'");
    }

    [Fact]
    public void Code_is_not_prose()
    {
        var rule = Rule("british", new VoiceCheck
        {
            Type = VoiceChecker.PreferSpelling,
            Spellings = new() { ["color"] = "colour" },
        });

        var text = "Set `color` here.\n```css\na { color: red; }\n```\nThe color is red.";

        VoiceChecker.Check(text, "doc", [rule]).Findings.Should().ContainSingle()
            .Which.Line.Should().Be(5, "inline code and fenced blocks are blanked, and line numbers still match");
    }

    [Fact]
    public void A_title_case_heading_is_flagged_and_a_sentence_case_one_is_not()
    {
        var rule = Rule("headings", new VoiceCheck { Type = VoiceChecker.HeadingCase });

        var text = "# Getting Started With Loadout\n## Getting started with Loadout\n## The CLI and MCP\n### Windows Sandbox";

        VoiceChecker.Check(text, "doc", [rule]).Findings.Should().ContainSingle()
            .Which.Line.Should().Be(1, "acronyms and a heading with one long word after the first are left alone");
    }

    [Fact]
    public void A_long_sentence_wrapped_over_lines_counts_as_one()
    {
        var rule = Rule("short", new VoiceCheck { Type = VoiceChecker.MaxSentenceWords, Limit = 8 });

        var text = "Short one. Another short one.\n\nThis sentence goes on\nfor rather more words than eight allows.";

        VoiceChecker.Check(text, "doc", [rule]).Findings.Should().ContainSingle()
            .Which.Line.Should().Be(3, "the finding points at the start of the paragraph");
    }

    [Fact]
    public void A_commit_subject_is_checked_only_on_a_commit()
    {
        var rule = Rule("subject", new VoiceCheck { Type = VoiceChecker.CommitSubject, Limit = 20 });

        var commit = VoiceChecker.Check("fix the parser so it stops.\n\nBody.", "commit", [rule]);

        commit.Findings.Select(f => f.Detail).Should().HaveCount(3)
            .And.Contain(d => d.Contains("over 20", StringComparison.Ordinal))
            .And.Contain(d => d.Contains("full stop", StringComparison.Ordinal))
            .And.Contain(d => d.Contains("lower-case", StringComparison.Ordinal));

        VoiceChecker.Check("fix the parser so it stops.", "doc", [rule]).Findings.Should().BeEmpty();
        VoiceChecker.Check("Fix the parser", "commit", [rule]).Findings.Should().BeEmpty();
    }

    [Fact]
    public void A_rule_for_other_surfaces_is_left_out()
    {
        var rule = Rule("docs-only", new VoiceCheck { Type = VoiceChecker.AvoidPhrase, Phrases = ["simply"] }, "doc");

        VoiceChecker.Check("Simply run it", "commit", [rule]).Findings.Should().BeEmpty();
        VoiceChecker.Check("Simply run it", "doc", [rule]).Findings.Should().ContainSingle();
    }

    [Fact]
    public void What_cannot_be_checked_says_why_rather_than_passing_quietly()
    {
        var rules = new[]
        {
            Rule("judged", check: null),
            Rule("unknown", new VoiceCheck { Type = "passive-ratio" }),
            Rule("bad-pattern", new VoiceCheck { Type = VoiceChecker.Regex, Pattern = "([" }),
            Rule("no-limit", new VoiceCheck { Type = VoiceChecker.MaxSentenceWords }),
        };

        var result = VoiceChecker.Check("Anything at all.", "doc", rules);

        result.Findings.Should().BeEmpty();
        result.Unchecked.Select(u => u.Rule).Should().BeEquivalentTo(["judged", "unknown", "bad-pattern", "no-limit"]);
    }

    [Fact]
    public void A_regex_reports_and_never_gates()
    {
        var rule = Rule("exclaim", new VoiceCheck { Type = VoiceChecker.Regex, Pattern = "!+" });

        VoiceChecker.Check("Done!", "doc", [rule]).Findings.Should().ContainSingle()
            .Which.Gates.Should().BeFalse();
    }

    [Fact]
    public void The_prose_guide_offers_every_check_type_this_version_can_run()
    {
        var guide = VoiceGuides.Prose();

        guide.Reference.Should().Be("prose/1");
        guide.CheckTypes.Should().BeEquivalentTo(VoiceChecker.Types);
        guide.Evidence.Citations.Should().Be(3);
        guide.Evidence.Sources.Should().Be(2);
        guide.Surfaces.Should().BeEquivalentTo(["doc", "commit", "pr"]);
    }
}
