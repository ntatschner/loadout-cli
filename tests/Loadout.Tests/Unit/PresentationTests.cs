using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Configuration;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which of the two dashboards somebody is served.
/// </summary>
/// <remarks>
/// The page was built plain, because the semantics had to be right before
/// anything was laid over them. It stayed plain for everybody, which is a
/// different decision: somebody who has said nothing about how they read a
/// screen was being handed the presentation designed for the hardest case.
/// </remarks>
public sealed class PresentationTests
{
    [Fact]
    public void Somebody_who_has_said_nothing_gets_the_page_with_its_chrome_on()
    {
        Presenting.For(null).Should().Be(Presentation.Rich);
        Presenting.For(new AccessibilitySettings()).Should().Be(Presentation.Rich);
    }

    [Theory]
    [InlineData(AccessibilityPresets.ScreenReader)]
    [InlineData(AccessibilityPresets.LowVision)]
    public void A_profile_about_reading_a_screen_gets_the_plain_page(string preset)
    {
        Presenting.For(new AccessibilitySettings { Preset = preset })
            .Should().Be(Presentation.Plain);
    }

    [Theory]
    [InlineData(AccessibilityPresets.ColourBlind)]
    [InlineData(AccessibilityPresets.Dyslexia)]
    [InlineData(AccessibilityPresets.Adhd)]
    [InlineData(AccessibilityPresets.PlainLanguage)]
    public void A_profile_about_something_else_keeps_the_rich_page(string preset)
    {
        // None of these four is helped by taking the chrome away, and two of
        // them are about prose rather than pictures. What they do carry - a
        // colour-safe palette, less motion - is honoured inside the rich page,
        // because those say how a thing is drawn and not whether to draw it.
        Presenting.For(new AccessibilitySettings { Preset = preset })
            .Should().Be(Presentation.Rich);
    }

    [Fact]
    public void Asking_for_no_colour_at_all_gets_the_plain_page()
    {
        // The one display setting that cannot be honoured inside a rich page:
        // its chrome is largely made of colour, and drawn without any what is
        // left is the plain page with worse spacing.
        Presenting.For(new AccessibilitySettings
        {
            Display = new AccessibilityDisplay { Colour = "none" },
        }).Should().Be(Presentation.Plain);
    }

    [Theory]
    [InlineData(AccessibilityPresets.ScreenReader, "none")]
    [InlineData(AccessibilityPresets.LowVision, "reduced")]
    [InlineData(AccessibilityPresets.Adhd, "reduced")]
    [InlineData(AccessibilityPresets.Dyslexia, "full")]
    public void How_much_the_page_may_move_comes_from_the_profile(string preset, string expected)
    {
        Presenting.Motion(new AccessibilitySettings { Preset = preset }).Should().Be(expected);
    }

    [Fact]
    public void A_colour_safe_palette_is_asked_for_by_the_profile_that_needs_one()
    {
        Presenting.Colour(new AccessibilitySettings { Preset = AccessibilityPresets.ColourBlind })
            .Should().Be("safe");

        Presenting.Colour(new AccessibilitySettings()).Should().Be("full");
        Presenting.Colour(null).Should().Be("full");
    }

    [Fact]
    public void The_page_carries_what_it_was_told_on_its_own_opening_tag()
    {
        // Written into the markup rather than fetched, so the page is right on
        // its first paint. A page that asked afterwards would draw itself one
        // way and then redecorate, which is a flash of the wrong thing for
        // everybody and a redraw for the people most likely to have asked for
        // none.
        var page = DashboardServer.Page(Presentation.Plain, "none", "safe");

        page.Should().Contain("data-presentation=\"plain\"")
            .And.Contain("data-motion=\"none\"")
            .And.Contain("data-colour=\"safe\"");

        DashboardServer.Page(Presentation.Rich, "full", "full")
            .Should().Contain("data-presentation=\"rich\"");
    }

    [Fact]
    public void The_opening_tag_the_settings_are_written_into_is_the_one_the_page_has()
    {
        // The failure this catches is silent: reword the page's opening tag and
        // the dashboard goes on serving perfectly while ignoring the profile.
        // The chrome would simply always be on, which is what it looks like
        // when nothing is wrong.
        DashboardServer.Page().Should().Contain(DashboardServer.Opening);
    }

    [Fact]
    public void The_page_carries_a_renderer_for_each_presentation_and_chooses_between_them()
    {
        // The rich page is not the plain one with paint on it - it has its own
        // structure, which means there are two renderers over one set of
        // facts. That is the cost of the decision and this is the guard on it:
        // deleting or renaming either leaves the page serving one shape to
        // everybody, which is what it did before and looks like nothing wrong.
        //
        // What this cannot check is that they say the same things. Nothing
        // here runs the page's script; that was checked in a browser against
        // the runs on this machine, and docs/teams.md says so.
        var page = DashboardServer.Page();

        page.Should().Contain("function drawList(", "the plain page's renderer")
            .And.Contain("function drawRuns(", "the rich page's renderer")
            .And.Contain("return rich() ? drawRuns(runs) : drawList(runs);", "and the choice between them");
    }

    [Fact]
    public void A_typed_view_beats_the_profile_for_one_run()
    {
        var server = new DashboardServer(new RunJournal(new StubPlatformPaths()));
        var reader = new AccessibleMode(
            true,
            AccessibilityPresets.ScreenReader,
            AccessibilityProfile.Resolve(new AccessibilitySettings
            {
                Preset = AccessibilityPresets.ScreenReader,
            }));

        TeamDashboardCommand.Presented(server, view: null, reader);
        server.Look.Should().Be(Presentation.Plain, "the profile says so");

        TeamDashboardCommand.Presented(server, "rich", reader);
        server.Look.Should().Be(Presentation.Rich, "somebody typed it, and meant it for this run");

        // But not the motion. Somebody asking to see the rich page has asked
        // about its chrome, not for their own motion setting to be overruled,
        // and a page that moved because a flag was typed would be the one
        // thing that setting exists to stop.
        server.Motion.Should().Be("none");

        TeamDashboardCommand.Presented(server, "plain", AccessibleMode.Off);
        server.Look.Should().Be(Presentation.Plain);
        server.Motion.Should().Be("full", "nobody asked for less");
    }

    /// <summary>Paths that answer, for a server nothing is served from.</summary>
    private sealed class StubPlatformPaths : Loadout.Platform.Abstractions.IPlatformPaths
    {
        private static readonly string Where = Path.Combine(Path.GetTempPath(), "loadout-presentation");

        public Loadout.Models.Platform.HostPlatform Host =>
            new(
                Loadout.Models.Platform.HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST");

        public Loadout.Models.Platform.PlatformPathSet Paths => new(Where, Where, Where, Where, Where);

        public void EnsureDirectoriesExist()
        {
        }

        public string CreateRuntimeDirectory() =>
            throw new NotSupportedException("Nothing here launches anything.");
    }
}
