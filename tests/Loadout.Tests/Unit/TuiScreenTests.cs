using Loadout.Tui;
using FluentAssertions;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Covers the frame every launcher screen is drawn in.
/// <para>
/// The launcher used to append to whatever the previous screen had left behind:
/// nothing ever cleared, only two of seven lists bounded their height, and a
/// long list pushed the heading off the top. What is worth testing is the part
/// that decides how much room a list may take, and that the frame degrades
/// rather than writing escape sequences into something that would print them.
/// </para>
/// </summary>
public sealed class TuiScreenTests
{
    /// <summary>A console that can be redrawn, as a real terminal can.</summary>
    private static TestConsole Terminal(int height = 40)
    {
        // EmitAnsiSequences keeps the control codes in the recorded output;
        // without it TestConsole strips them, and the two assertions about
        // entering and leaving the alternate screen would have nothing to see.
        var console = new TestConsole().Interactive().EmitAnsiSequences();

        console.Profile.Capabilities.Ansi = true;
        console.Profile.Height = height;
        console.Profile.Width = 100;

        return console;
    }

    [Fact]
    public void A_list_leaves_room_for_the_frame_around_it()
    {
        var screen = new TuiScreen(Terminal(height: 24));

        // Whatever it chooses has to fit inside the window with the title, the
        // prompt and the margins still on screen.
        screen.PageSize.Should().BeLessThan(24);
        screen.PageSize.Should().BeGreaterThan(4);
    }

    [Fact]
    public void A_taller_window_shows_more_of_a_list()
    {
        var shortWindow = new TuiScreen(Terminal(height: 20)).PageSize;
        var tallWindow = new TuiScreen(Terminal(height: 40)).PageSize;

        // Fixed at fifteen before this, which was too tall for a small terminal
        // and wasted half a large one.
        tallWindow.Should().BeGreaterThan(shortWindow);
    }

    [Fact]
    public void A_very_short_window_still_gets_a_usable_list()
    {
        // Subtracting the frame from a ten-row terminal leaves almost nothing,
        // and a list of one is not a list.
        new TuiScreen(Terminal(height: 10)).PageSize.Should().BeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public void A_very_tall_window_does_not_produce_a_wall()
    {
        // Beyond a point a longer list is harder to read, not easier, and the
        // prompt's own search is the better way through a long one.
        new TuiScreen(Terminal(height: 200)).PageSize.Should().BeLessThanOrEqualTo(18);
    }

    [Fact]
    public void A_screen_draws_its_title()
    {
        var console = Terminal();

        new TuiScreen(console).Begin("Settings");

        console.Output.Should().Contain("Settings");
    }

    [Fact]
    public void A_subtitle_carries_the_context()
    {
        var console = Terminal();

        new TuiScreen(console).Begin("alpha", "/home/n/code/alpha");

        // Which project this is belongs in the title bar, not buried in the
        // body where it scrolls away.
        console.Output.Should().Contain("alpha");
        console.Output.Should().Contain("/home/n/code/alpha");
    }

    [Fact]
    public void A_console_that_cannot_be_redrawn_still_gets_its_title()
    {
        // A pipe, a captured console, a terminal without ANSI. Clearing there
        // would either do nothing or destroy what somebody is reading, so the
        // frame degrades to plain sequential output.
        var console = new TestConsole();

        console.Profile.Capabilities.Ansi = false;

        var act = () => new TuiScreen(console).Begin("Loadout");

        act.Should().NotThrow();
        console.Output.Should().Contain("Loadout");
    }

    [Fact]
    public async Task The_body_still_runs_when_there_is_no_alternate_screen()
    {
        var console = new TestConsole();

        console.Profile.Capabilities.Ansi = false;

        var ran = false;

        var result = await new TuiScreen(console).RunAsync(() =>
        {
            ran = true;
            return Task.FromResult(7);
        });

        ran.Should().BeTrue();
        result.Should().Be(7);
    }

    [Fact]
    public async Task The_alternate_screen_is_left_even_when_the_body_throws()
    {
        var console = Terminal();

        var act = async () => await new TuiScreen(console).RunAsync<int>(
            () => throw new InvalidOperationException("something went wrong"));

        await act.Should().ThrowAsync<InvalidOperationException>();

        // Leaving somebody in the alternate buffer looks exactly like a
        // terminal that has lost its history, and they have no obvious way back.
        console.Output.Should().Contain("?1049l");
    }

    [Fact]
    public async Task The_alternate_screen_is_entered_and_left_in_order()
    {
        var console = Terminal();

        await new TuiScreen(console).RunAsync(() => Task.FromResult(0));

        var entered = console.Output.IndexOf("?1049h", StringComparison.Ordinal);
        var left = console.Output.IndexOf("?1049l", StringComparison.Ordinal);

        entered.Should().BeGreaterThanOrEqualTo(0);
        left.Should().BeGreaterThan(entered);
    }
}
