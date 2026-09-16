using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Configuration;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The person's own profile, and the guidance it compiles to.
/// </summary>
/// <remarks>
/// <para>
/// This is the widest piece of the accessibility work: one file reaching
/// every session of every agent, with no adapter code at all. So the tests
/// are about what a person is actually promised - that a preset sets what it
/// says it sets, that anything they set by hand survives it, and that a
/// person who wants none of this pays nothing.
/// </para>
/// <para>
/// What is deliberately absent: a font, a readability score, and any
/// detection of a person's needs from their machine. The first two have no
/// evidence behind them and the third is a guess nobody asked for.
/// </para>
/// </remarks>
public sealed class AccessibilityProfileTests
{
    [Fact]
    public void Nobody_who_sets_nothing_pays_anything()
    {
        // No heading, no line, no tokens. Somebody who wants none of this
        // should never learn it exists.
        AccessibilityProfile.IsSet(null).Should().BeFalse();
        AccessibilityProfile.IsSet(new AccessibilitySettings()).Should().BeFalse();
        AccessibilityProfile.Compose(new AccessibilitySettings()).Should().BeNull();
    }

    [Fact]
    public void The_screen_reader_preset_sets_what_it_says_it_sets()
    {
        var profile = AccessibilityProfile.Resolve(
            new AccessibilitySettings { Preset = AccessibilityPresets.ScreenReader });

        profile.Display.Redraw.Should().Be("never", "a spinner is re-read on every frame");
        profile.Display.Glyphs.Should().Be("ascii");
        profile.Display.Tables.Should().Be("lists");
        profile.Display.Menus.Should().Be("numbered");
        profile.Display.Launcher.Should().Be("text");
        profile.Display.Motion.Should().Be("none");
        profile.Display.Bell.Should().BeTrue();
        profile.Questions.OneAtATime.Should().BeTrue();
    }

    [Theory]
    [InlineData(AccessibilityPresets.Dyslexia, 20, 4)]
    [InlineData(AccessibilityPresets.None, 25, 5)]
    public void A_preset_that_shortens_sentences_shortens_them(string preset, int sentences, int paragraph)
    {
        var profile = AccessibilityProfile.Resolve(new AccessibilitySettings { Preset = preset });

        profile.Output.Sentences.Should().Be(sentences);
        profile.Output.Paragraph.Should().Be(paragraph);
    }

    [Fact]
    public void What_the_person_wrote_down_wins_over_the_preset()
    {
        // A preset is a starting point, not a mode. Somebody can take the
        // dyslexia bundle and still want the field's own vocabulary, and a
        // bundle nobody can adjust is one people abandon whole.
        var profile = AccessibilityProfile.Resolve(new AccessibilitySettings
        {
            Preset = AccessibilityPresets.Dyslexia,
            Output = new AccessibilityOutput { Technicality = "technical" },
        });

        profile.Output.Technicality.Should().Be("technical", "they said so");
        profile.Output.Sentences.Should().Be(20, "and the rest of the bundle still applies");
    }

    [Fact]
    public void A_preset_nobody_knows_changes_nothing_rather_than_failing_a_launch()
    {
        // Checked where a person types it, which is the configuration command.
        // A launch that refused over a typo in a preference would be a session
        // lost to a spelling mistake.
        var settings = new AccessibilitySettings { Preset = "screenreader" };

        AccessibilityProfile.Resolve(settings).Display.Redraw.Should().Be("allowed");
    }

    [Fact]
    public void The_compiled_guidance_says_who_it_is_for_and_what_to_do()
    {
        var text = AccessibilityProfile.Compose(
            new AccessibilitySettings { Preset = AccessibilityPresets.Adhd })!;

        text.Should().Contain("accessibility profile (adhd)");
        text.Should().Contain("one question per message");
        text.Should().Contain("question 2 of 4");
        text.Should().Contain("**concise**", "the adhd bundle asks for the concise level");
        text.Should().Contain("cannot be undone");
    }

    [Fact]
    public void The_guidance_never_touches_what_the_agent_quotes()
    {
        // The one thing that would make this dangerous. Shortening a log line
        // or tidying an error message is changing the evidence.
        var text = AccessibilityProfile.Compose(
            new AccessibilitySettings { Preset = AccessibilityPresets.Dyslexia })!;

        text.Should().Contain("never applies to what you quote");
        text.Should().Contain("reproduced exactly");
        text.Should().Contain("changes how things are said, never what is done");
    }

    [Fact]
    public void A_written_answer_profile_asks_for_one_instead_of_offering_choices()
    {
        var text = AccessibilityProfile.Compose(new AccessibilitySettings
        {
            Questions = new AccessibilityQuestions { Style = "written" },
        })!;

        text.Should().Contain("ask for a written answer");
        text.Should().NotContain("numbered list of choices");
    }

    [Fact]
    public void Bionic_formatting_is_offered_where_it_is_asked_for()
    {
        var text = AccessibilityProfile.Compose(new AccessibilitySettings
        {
            Output = new AccessibilityOutput { Bionic = true },
        })!;

        text.Should().Contain("bionic formatting");
        text.Should().Contain("You MUST NOT apply it inside code");
    }

    [Fact]
    public void Bionic_formatting_is_refused_under_the_screen_reader_preset()
    {
        // Bold markup is read aloud as emphasis on every word, so the one
        // setting that is preference elsewhere is harm here.
        var text = AccessibilityProfile.Compose(new AccessibilitySettings
        {
            Preset = AccessibilityPresets.ScreenReader,
            Output = new AccessibilityOutput { Bionic = true },
        })!;

        text.Should().NotContain("bionic");
    }

    [Fact]
    public void A_profile_that_wants_no_tables_says_what_to_write_instead()
    {
        var text = AccessibilityProfile.Compose(
            new AccessibilitySettings { Preset = AccessibilityPresets.ScreenReader })!;

        text.Should().Contain("You MUST NOT use tables. Instead");
        text.Should().Contain("You MUST NOT draw diagrams, trees or boxes out of characters. Instead");
    }

    [Fact]
    public void Every_prohibition_offers_something_to_do_instead()
    {
        // The same rule the team roles follow, and for the same reason: a
        // model told only what not to do finds a worse way to do it.
        var text = AccessibilityProfile.Compose(new AccessibilitySettings
        {
            Preset = AccessibilityPresets.ScreenReader,
            Output = new AccessibilityOutput { Bionic = true, Emphasis = "bold-only" },
        })!;

        foreach (var line in text.Split('\n').Where(l => l.Contains("MUST NOT", StringComparison.Ordinal)))
        {
            line.Should().Contain("Instead", $"'{line.Trim()}' forbids something without saying what to do");
        }
    }

    [Fact]
    public void A_setting_that_is_added_and_forgotten_would_be_a_preference_ignored()
    {
        // Every property is carried across by reflection rather than by hand,
        // so this holds for settings nobody has written yet. The check is that
        // each section survives a round trip through a preset.
        var settings = new AccessibilitySettings
        {
            Preset = AccessibilityPresets.LowVision,
            Display = new AccessibilityDisplay { Bell = true, Glyphs = "ascii" },
        };

        var profile = AccessibilityProfile.Resolve(settings);

        profile.Display.Bell.Should().BeTrue();
        profile.Display.Glyphs.Should().Be("ascii");
        profile.Display.Colour.Should().Be("sixteen", "from the bundle");
    }
}
