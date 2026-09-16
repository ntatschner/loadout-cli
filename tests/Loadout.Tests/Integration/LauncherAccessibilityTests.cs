using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Configuration;
using Loadout.Tui.Terminal;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// What the full-screen launcher does for somebody who asked for something
/// different.
/// </summary>
/// <remarks>
/// <para>
/// This is not the screen-reader path and does not pretend to be: no terminal
/// toolkit can announce a full-screen application on Windows or macOS, which
/// is why a profile that says so gets the text launcher instead. What this
/// covers is the person who can see it and needs it drawn differently.
/// </para>
/// <para>
/// Two things, and both small on purpose. Colour comes down to the sixteen the
/// person's own terminal theme defines, because that is the only way somebody
/// who needs particular contrast can fix it for themselves. And the boxes go,
/// because Terminal.Gui draws every line style from the same block of
/// characters and a console font that has none of them draws nothing legible
/// from any of them.
/// </para>
/// <para>
/// Most of this needs no application: the theme is asked what it would draw
/// rather than a screen being drawn and read back. One test starts the
/// toolkit, because a driver is the only thing that can be asked about
/// colours.
/// </para>
/// </remarks>
public sealed class LauncherAccessibilityTests
{
    private static AccessibilitySettings For(string preset) =>
        AccessibilityProfile.Resolve(new AccessibilitySettings { Preset = preset });

    [Fact]
    public void Nobody_who_asked_for_nothing_gets_a_different_launcher()
    {
        LauncherTheme.Apply(null);

        LauncherTheme.Lines.Should().Be(LineStyle.Rounded);
    }

    [Fact]
    public void A_profile_that_wants_no_box_drawing_gets_no_boxes()
    {
        // No border rather than a plainer one: every line style comes from the
        // same characters, so a font without them draws nothing legible from
        // any of them.
        LauncherTheme.Apply(For(AccessibilityPresets.ScreenReader));

        LauncherTheme.Lines.Should().Be(LineStyle.None);

        // Put back, because this is static and the next screen drawn anywhere
        // in the suite uses it.
        LauncherTheme.Apply(null);
    }

    [Fact]
    public void A_profile_that_only_changes_colour_keeps_its_boxes()
    {
        LauncherTheme.Apply(For(AccessibilityPresets.LowVision));

        LauncherTheme.Lines.Should().Be(LineStyle.Rounded, "nothing there asked about glyphs");

        LauncherTheme.Apply(null);
    }

    [Fact]
    public void A_profile_that_wants_the_sixteen_gets_only_the_sixteen()
    {
        // InitLegibly starts the application itself - that pairing is the
        // whole reason it exists - so this must not start one first.
        using IApplication application = Application.Create();

        try
        {
            application.InitLegibly(For(AccessibilityPresets.LowVision));

            if (application.Driver is { SupportsTrueColor: true } driver)
            {
                driver.Force16Colors.Should().BeTrue();
            }
        }
        finally
        {
            LauncherTheme.Apply(null);
        }
    }
}
