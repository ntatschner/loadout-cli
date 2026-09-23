using FluentAssertions;
using Loadout.Cli.Infrastructure;
using Loadout.Models.Configuration;
using Spectre.Console;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Whether Loadout's own output follows the person's profile.
/// </summary>
/// <remarks>
/// The third place the profile acts. An agent that has been made quiet and a
/// launcher that still draws box borders and spins have between them helped
/// with half the session.
/// </remarks>
public sealed class AccessibleModeTests
{
    [Fact]
    public void Nobody_who_asked_for_nothing_gets_anything()
    {
        var mode = AccessibleMode.Resolve([], null, new AccessibilitySettings());

        mode.IsOn.Should().BeFalse();
    }

    [Theory]
    [InlineData("--accessible")]
    [InlineData("--accessible=screen-reader")]
    public void The_flag_turns_it_on(string argument)
    {
        var mode = AccessibleMode.Resolve(["team", "list", argument], null, null);

        mode.IsOn.Should().BeTrue();
        mode.Name.Should().Be("screen-reader");
        mode.Profile.Display.Glyphs.Should().Be("ascii");
    }

    [Fact]
    public void The_flag_takes_a_preset_by_name()
    {
        AccessibleMode.Resolve(["--accessible", "dyslexia", "team"], null, null)
            .Name.Should().Be("dyslexia");
    }

    [Fact]
    public void A_word_after_the_flag_that_is_not_a_preset_is_the_command()
    {
        // "loadout --accessible team list" asked for accessible output and a
        // team list, not for a profile called team.
        var mode = AccessibleMode.Resolve(["--accessible", "team", "list"], null, null);

        mode.Name.Should().Be("screen-reader");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("screen-reader")]
    public void The_environment_turns_it_on(string value)
    {
        AccessibleMode.Resolve([], value, null).IsOn.Should().BeTrue();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("none")]
    public void The_environment_can_say_no(string value)
    {
        AccessibleMode.Resolve([], value, null).IsOn.Should().BeFalse();
    }

    [Fact]
    public void What_was_typed_beats_the_shell_which_beats_the_file()
    {
        var configured = new AccessibilitySettings { Preset = AccessibilityPresets.Adhd };

        AccessibleMode.Resolve(["--accessible=dyslexia"], "low-vision", configured)
            .Name.Should().Be("dyslexia");

        AccessibleMode.Resolve([], "low-vision", configured)
            .Name.Should().Be("low-vision");

        AccessibleMode.Resolve([], null, configured)
            .Name.Should().Be("adhd");
    }

    [Fact]
    public void A_person_can_see_that_it_took()
    {
        // A profile that is on and invisible cannot be told from one that was
        // ignored, and the second is what somebody will assume.
        AccessibleMode.Resolve(["--accessible=adhd"], null, null).Line
            .Should().Contain("adhd").And.Contain("loadout config set");
    }

    [Theory]
    [InlineData("a — b", "a - b")]
    [InlineData("working…", "working...")]
    [InlineData("┌─┐", "---")]
    [InlineData("✓ done", "ok done")]
    [InlineData("a → b", "a -> b")]
    [InlineData("plain ascii", "plain ascii")]
    public void The_characters_a_console_cannot_draw_are_folded(string given, string expected)
    {
        AccessibleMode.Ascii(given).Should().Be(expected);
    }

    [Fact]
    public void Somebody_s_own_words_are_left_alone()
    {
        // Most of what reaches this is data: a branch name, a commit subject,
        // a line of somebody's log. Folding a name into question marks would
        // be a worse failure than the box drawing this is for.
        AccessibleMode.Ascii("Björn fixed étude").Should().Be("Björn fixed étude");
    }

    [Fact]
    public void A_profile_that_asks_for_ascii_gets_it_through_the_console()
    {
        var console = New(ColorSystem.TrueColor, unicode: true);

        AccessibleMode.Resolve(["--accessible"], null, null).Apply(console, noColour: null);

        console.Profile.Capabilities.Unicode.Should().BeFalse("every table and tree is drawn from this");

        console.Write(new Spectre.Console.Text("a — b │ c"));

        console.Output.Should().NotContainAny("—", "│");
    }

    [Fact]
    public void NO_COLOR_is_obeyed_whether_or_not_anybody_set_a_profile()
    {
        // Including the escapes that are not colour. Bold is an escape too,
        // and a terminal promised none should get none.
        var console = New(ColorSystem.TrueColor, unicode: true);

        AccessibleMode.Off.Apply(console, noColour: "1");

        console.Profile.Capabilities.Ansi.Should().BeFalse();

        console.Markup("[bold red]loud[/]");

        console.Output.Should().Be("loud").And.NotContain("");
    }

    [Fact]
    public void A_profile_that_wants_sixteen_colours_gets_the_sixteen()
    {
        // The only ones a person's own terminal theme can remap, and so the
        // only ones somebody who needs particular contrast can fix.
        var console = New(ColorSystem.TrueColor, unicode: true);

        AccessibleMode.Resolve(["--accessible=low-vision"], null, null).Apply(console, noColour: null);

        console.Profile.Capabilities.ColorSystem.Should().Be(ColorSystem.Legacy);
    }

    [Fact]
    public void A_profile_that_refuses_redraws_stops_the_console_animating()
    {
        // Safe only because the menus are numbered under the same setting:
        // this capability is also what a selection prompt needs to move its
        // own cursor.
        var console = New(ColorSystem.TrueColor, unicode: true);

        AccessibleMode.Resolve(["--accessible"], null, null).Apply(console, noColour: null);

        console.Profile.Capabilities.Interactive.Should().BeFalse();
    }

    [Fact]
    public void A_profile_that_says_nothing_about_redraws_leaves_them_alone()
    {
        var console = New(ColorSystem.TrueColor, unicode: true);

        AccessibleMode.Resolve(["--accessible=dyslexia"], null, null).Apply(console, noColour: null);

        console.Profile.Capabilities.Interactive.Should().BeTrue();
    }

    [Fact]
    public void A_table_becomes_lines_that_each_carry_their_own_heading()
    {
        // A table read aloud is a column of values with no idea which column
        // each one is in: the headings were said once, at the top, and by the
        // fourth row nobody is holding them.
        var console = New(ColorSystem.TrueColor, unicode: true);

        AccessibleMode.Resolve(["--accessible"], null, null).Apply(console, noColour: null);

        var table = new Table();
        table.AddColumn("Project");
        table.AddColumn("Agent");
        table.AddRow("loadout-cli", "claude");
        table.AddRow("starstats", "codex");

        console.Write(table);

        var lines = console.Output
            .Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => line.Length > 0)
            .ToList();

        lines.Should().Equal(
            "Project: loadout-cli",
            "Agent: claude",
            "Project: starstats",
            "Agent: codex");
    }

    [Fact]
    public void A_column_with_no_heading_carries_its_value_alone()
    {
        // The project list has one: a marker column whose heading is empty on
        // purpose, where ": *" would be worse than the asterisk by itself.
        var console = New(ColorSystem.TrueColor, unicode: true);

        AccessibleMode.Resolve(["--accessible"], null, null).Apply(console, noColour: null);

        var table = new Table();
        table.AddColumn(string.Empty);
        table.AddColumn("Project");
        table.AddRow("*", "loadout-cli");

        console.Write(table);

        console.Output.Should().Contain("*").And.Contain("Project: loadout-cli");
        console.Output.Should().NotContain(": *");
    }

    [Fact]
    public void A_long_value_is_not_broken_in_the_middle()
    {
        // The table wrapped a path across two rows to fit its column. On one
        // line per value there is no column to fit, and a path split down the
        // middle is one nobody can copy.
        var console = New(ColorSystem.TrueColor, unicode: true);
        console.Profile.Width = 200;

        AccessibleMode.Resolve(["--accessible"], null, null).Apply(console, noColour: null);

        var table = new Table();
        table.AddColumn("Location");
        table.AddRow("D:/git/RSIStarCitizenTools/StarStats/src/deep/nested/place");

        console.Write(table);

        console.Output.Should().Contain("Location: D:/git/RSIStarCitizenTools/StarStats/src/deep/nested/place");
    }

    [Fact]
    public void A_person_who_wants_tables_keeps_them()
    {
        var console = New(ColorSystem.TrueColor, unicode: true);

        AccessibleMode.Resolve(["--accessible=dyslexia"], null, null).Apply(console, noColour: null);

        var table = new Table();
        table.AddColumn("Project");
        table.AddRow("loadout-cli");

        console.Write(table);

        console.Output.Should().NotContain("Project: loadout-cli", "nothing asked for lists");
    }

    private static Spectre.Console.Testing.TestConsole New(ColorSystem colours, bool unicode)
    {
        var console = new Spectre.Console.Testing.TestConsole();

        console.Profile.Capabilities.ColorSystem = colours;
        console.Profile.Capabilities.Unicode = unicode;
        console.Profile.Capabilities.Ansi = true;
        console.Profile.Capabilities.Interactive = true;
        console.Profile.Width = 200;

        return console;
    }
}
