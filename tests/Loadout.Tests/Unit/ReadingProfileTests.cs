using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Configuration;
using Loadout.Tui;
using Spectre.Console.Testing;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// How a question arrives for somebody who cannot see the cursor move.
/// </summary>
/// <remarks>
/// <para>
/// An arrow-key menu draws its list once and then repaints the line the cursor
/// is on, over and over. What a screen reader is handed is that one row, again
/// and again, and never the shape of the list. The numbered form asks once,
/// says every option, and takes a number back.
/// </para>
/// <para>
/// The same list either way, in the same order, with the same labels. Only how
/// it is read out and how it is answered change.
/// </para>
/// </remarks>
public sealed class ReadingProfileTests
{
    private static ReadingProfile For(string preset) =>
        new(AccessibilityProfile.Resolve(new AccessibilitySettings { Preset = preset }));

    private static TestConsole Console(params string[] typed)
    {
        var console = new TestConsole();

        console.Profile.Capabilities.Interactive = true;
        console.Profile.Width = 200;

        foreach (var line in typed)
        {
            console.Input.PushTextWithEnter(line);
        }

        return console;
    }

    [Fact]
    public void Nobody_who_asked_for_nothing_gets_a_numbered_menu()
    {
        ReadingProfile.None.Numbered.Should().BeFalse();
        ReadingProfile.None.Bell.Should().BeFalse();
    }

    [Fact]
    public void A_profile_that_refuses_redraws_has_refused_arrow_menus()
    {
        // Whether or not it said so. An arrow menu is a redraw: it repaints
        // the line the cursor is on every time the cursor moves.
        var profile = new AccessibilitySettings { Display = new AccessibilityDisplay { Redraw = "never" } };

        new ReadingProfile(profile).Numbered.Should().BeTrue();
    }

    [Fact]
    public void The_options_are_read_out_and_answered_by_number()
    {
        var console = Console("2");

        var chosen = For(AccessibilityPresets.ScreenReader)
            .Ask(console, "What would you like to do?", ["Save", "Review", "Leave"], option => option);

        chosen.Should().Be("Review");

        console.Output.Should().Contain("What would you like to do?");
        console.Output.Should().Contain("1. Save").And.Contain("2. Review").And.Contain("3. Leave");
        console.Output.Should().Contain("Enter a number (1 to 3)");
    }

    [Fact]
    public void A_number_outside_the_list_is_refused_rather_than_rounded()
    {
        // Somebody who types 9 for a list of three meant something by it.
        // Being handed the third thing quietly is the wrong answer to a
        // question they did not ask.
        var console = Console("9", "1");

        var chosen = For(AccessibilityPresets.ScreenReader)
            .Ask(console, "Which?", ["first", "second", "third"], option => option);

        chosen.Should().Be("first");
        console.Output.Should().Contain("Enter a number between 1 and 3");
    }

    [Fact]
    public void Several_things_can_be_chosen_by_number()
    {
        var console = Console("1, 3");

        var chosen = For(AccessibilityPresets.ScreenReader)
            .AskMany(console, "Register any of these?", ["one", "two", "three"], option => option);

        chosen.Should().Equal("one", "three");
    }

    [Fact]
    public void Choosing_nothing_is_an_answer()
    {
        var console = Console(string.Empty);

        For(AccessibilityPresets.ScreenReader)
            .AskMany(console, "Register any of these?", ["one", "two"], option => option)
            .Should().BeEmpty("nothing is chosen unless somebody picks it");
    }

    [Fact]
    public void A_list_with_one_bad_number_is_refused_whole()
    {
        // "1, 3, 7" for a list of four meant something by the seven. Acting on
        // the one and the three acts on half an intention.
        var console = Console("1, 3, 7", "2");

        var chosen = For(AccessibilityPresets.ScreenReader)
            .AskMany(console, "Which?", ["a", "b", "c", "d"], option => option);

        chosen.Should().Equal("b");
        console.Output.Should().Contain("separated by commas");
    }

    [Fact]
    public void A_yes_or_no_question_says_which_words_it_takes()
    {
        var console = Console("y");

        For(AccessibilityPresets.ScreenReader).Confirm(console, "Save them?").Should().BeTrue();

        console.Output.Should().Contain("Answer y or n");
    }

    [Fact]
    public void The_bell_rings_when_an_answer_is_wanted()
    {
        // The one moment it is for. A bell on anything else is noise, and a
        // person who set this would switch it off again.
        var console = Console("1");

        For(AccessibilityPresets.ScreenReader).Ask(console, "Which?", ["only"], option => option);

        console.Output.Should().Contain("\a");
    }

    [Fact]
    public void A_profile_that_did_not_ask_for_a_bell_is_silent()
    {
        var console = Console("1");

        For(AccessibilityPresets.Adhd).Ask(console, "Which?", ["only", "other"], option => option);

        console.Output.Should().NotContain("\a");
    }

    [Fact]
    public void A_question_with_no_answers_cannot_be_asked()
    {
        var act = () => ReadingProfile.None.Ask(Console(), "Which?", Array.Empty<string>(), o => o);

        act.Should().Throw<ArgumentException>();
    }
}
