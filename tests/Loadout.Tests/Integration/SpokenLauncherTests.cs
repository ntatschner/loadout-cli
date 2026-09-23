using System.Drawing;
using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Models.Configuration;
using Loadout.Models.Projects;
using Loadout.Tui;
using Loadout.Tui.Terminal;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Views;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The full-screen launcher saying what it is showing.
/// </summary>
/// <remarks>
/// <para>
/// A terminal toolkit has no accessibility provider on Windows or macOS, so a
/// full-screen application cannot be announced the way a window is. It has to
/// announce itself, which is what this is — and the thing that most needs
/// checking is not the words but <em>whether</em>: silence for everybody who
/// did not ask, one line for the person who did.
/// </para>
/// <para>
/// <strong>Nobody has heard this.</strong> There is no screen reader on the
/// machine it was written on. What is covered is which lines are handed to the
/// channel and when; whether they are useful to listen to is a question only a
/// person using one can answer, and the docs say so.
/// </para>
/// </remarks>
public sealed class SpokenLauncherTests
{
    private const int Width = 140;
    private const int Height = 40;

    private static ProjectResolution Project(string name, string? path) =>
        new(
            new ProjectRegistryEntry { Slug = name.ToLowerInvariant(), Name = name },
            path, null, 0, false);

    private static ReadingProfile Asked(string speech) =>
        new(AccessibilityProfile.Resolve(new AccessibilitySettings
        {
            Display = new AccessibilityDisplay { Speech = speech },
        }));

    [Fact]
    public void Nobody_who_did_not_ask_is_spoken_to()
    {
        // Including, deliberately, the screen-reader preset: that gets the text
        // launcher, which people have actually used. This has been heard by
        // nobody, so it is opted into rather than inferred.
        new ReadingProfile(null).Speaks.Should().BeFalse();

        new ReadingProfile(AccessibilityProfile.Resolve(
            new AccessibilitySettings { Preset = AccessibilityPresets.ScreenReader }))
            .Speaks.Should().BeFalse("the screen-reader preset gets the text launcher instead");

        Asked("off").Speaks.Should().BeFalse();
    }

    [Fact]
    public void Somebody_who_asked_in_as_many_words_is()
    {
        Asked("screen-reader").Speaks.Should().BeTrue();
        Asked("SCREEN-READER").Speaks.Should().BeTrue("a setting is not case somebody has to get right");
    }

    [Fact]
    public void A_row_is_announced_by_its_name_first()
    {
        // Most-distinguishing first: somebody moving down a list hears the
        // first word of each row and interrupts as soon as it is wrong.
        LauncherWindow.Announce(Project("Alpha", @"D:\code\alpha"))
            .Should().StartWith("Alpha.");
    }

    [Fact]
    public void A_row_says_what_the_columns_say_and_not_what_the_colours_do()
    {
        LauncherWindow.Announce(Project("Alpha", @"D:\code\alpha"))
            .Should().Contain("on this machine");

        // The one thing a sighted person reads off this row that changes what
        // they would do next.
        LauncherWindow.Announce(Project("Beta", null))
            .Should().Contain("not on this machine");
    }

    [Fact]
    public void The_launcher_says_the_row_the_cursor_lands_on()
    {
        var said = new List<string>();

        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        using var window = new LauncherWindow(
            [Project("Alpha", @"D:\code\alpha"), Project("Beta", null)],
            here: null,
            "clean",
            ["claude"],
            (_, _) => Task.FromResult<ProjectOverview?>(null),
            _ => { },
            [],
            app,
            line => said.Add(line));

        app.Begin(window);
        app.LayoutAndDraw();

        // Whatever the cursor starts on, the person is told what they are on
        // rather than left to guess from silence.
        said.Should().NotBeEmpty();
        said[0].Should().StartWith("Alpha.");
    }

    [Fact]
    public void A_launcher_nobody_asked_to_speak_hands_nothing_to_anything()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        // No delegate at all is the ordinary case, and the one every other
        // test in this suite builds. It must not need a speech channel to
        // exist.
        using var window = new LauncherWindow(
            [Project("Alpha", @"D:\code\alpha")],
            here: null,
            "clean",
            ["claude"],
            (_, _) => Task.FromResult<ProjectOverview?>(null),
            _ => { },
            [],
            app);

        app.Begin(window);
        app.LayoutAndDraw();

        window.Should().NotBeNull();
    }
}
