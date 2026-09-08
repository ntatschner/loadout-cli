using Loadout.Core.Projects;
using Loadout.Models.Projects;
using Loadout.Tui.Terminal;
using FluentAssertions;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// What the launcher looks like, as distinct from what it says.
/// <para>
/// The other screen tests read the text back from the headless driver. These
/// read the colour of particular cells back as well, because the restyle they
/// cover is mostly colour: a frame that is grey until you are in it and warm
/// while you are, a key picked out from what it does, a heading in bold.
/// None of that is visible in the text, and a screen can say all the right
/// words in all the wrong colours — which, before this, it did: every frame
/// was drawn in the weight of the text inside it, and a scheme believed to
/// colour them was colouring nothing.
/// </para>
/// </summary>
public sealed class LauncherLookTests
{
    private const string Accent = "#E0A458";
    private const string Dim = "#6B7383";

    private static ProjectResolution Project(string slug, string name) =>
        new(new ProjectRegistryEntry { Slug = slug, Name = name, DefaultAgent = "claude" }, "/repos/x", null, 0, false);

    private static ProjectOverview Overview(ProjectResolution project, bool guarded = true) =>
        new(project, "main", true, 4096, 3, 2, 0, guarded, 0);

    private static TuiSession Launcher(IReadOnlyList<ProjectResolution> projects, out LauncherWindow window)
    {
        LauncherWindow? built = null;

        var session = TuiSession.Start(app => built = new LauncherWindow(
            projects,
            null,
            "workspace connected",
            ["claude"],
            (project, _) => Task.FromResult<ProjectOverview?>(Overview(project)),
            _ => { },
            [],
            app));

        window = built!;

        return session;
    }

    /// <summary>The foreground colour a cell was drawn in, as the theme writes it.</summary>
    private static string Foreground(TuiSession session, int row, int column)
    {
        var contents = session.Application.Driver?.Contents
            ?? throw new InvalidOperationException("The driver has no contents to read.");

        var attribute = contents[row, column].Attribute
            ?? throw new InvalidOperationException($"Nothing was drawn at row {row}, column {column}.");

        return attribute.Foreground.ToString();
    }

    /// <summary>Where the first line of the screen containing some text is.</summary>
    private static (int Row, int Column) Find(TuiSession session, string text)
    {
        var lines = session.Screen.Split('\n');

        for (var row = 0; row < lines.Length; row++)
        {
            var column = lines[row].IndexOf(text, StringComparison.Ordinal);

            if (column >= 0)
            {
                return (row, column);
            }
        }

        throw new InvalidOperationException($"'{text}' is not on the screen.");
    }

    [Fact]
    public void The_frame_you_are_in_is_the_warm_one()
    {
        using var session = Launcher([Project("alpha", "Alpha"), Project("beta", "Beta")], out _);

        var corner = Find(session, "╭┤Projects");

        // At rest, with the focus in the filter above it, the frame is grey.
        Foreground(session, corner.Row, corner.Column).Should().Be(Dim);

        // An arrow from the filter moves the focus into the list.
        session.Press(Key.CursorDown);

        Foreground(session, corner.Row, corner.Column).Should().Be(Accent,
            "the frame the focus is in should be the one drawn in the accent");
    }

    [Fact]
    public void Buttons_are_flat()
    {
        using var session = Launcher([Project("alpha", "Alpha")], out _);

        var screen = session.ScreenShowing("[ Launch");

        // The shadow is drawn in block glyphs under and beside each button.
        screen.Should().NotContain("▀", "a button should not cast a shadow");
        screen.Should().NotContain("▖");
    }

    [Fact]
    public void The_filter_says_what_it_is_for_until_something_is_typed()
    {
        using var session = Launcher([Project("alpha", "Alpha")], out _);

        session.Screen.Should().Contain("Filter projects");

        session.Type("al");

        session.Screen.Should().NotContain("Filter projects",
            "the prompt should give way to what was typed");
    }

    [Fact]
    public void The_project_list_says_how_many_it_holds_and_how_many_it_is_showing()
    {
        using var session = Launcher([Project("alpha", "Alpha"), Project("beta", "Beta")], out _);

        session.Screen.Should().Contain("Projects (2)");

        session.Type("bet");

        session.Screen.Should().Contain("Projects (1 of 2)");
    }

    [Fact]
    public void What_needs_attention_is_a_heading_not_a_box()
    {
        LauncherWindow? built = null;

        using var session = TuiSession.Start(app => built = new LauncherWindow(
            [Project("alpha", "Alpha")],
            null,
            "workspace connected",
            ["claude"],
            (project, _) => Task.FromResult<ProjectOverview?>(Overview(project, guarded: false)),
            _ => { },
            [],
            app));

        var screen = session.ScreenShowing("Needs attention");

        screen.Should().Contain("Needs attention");
        screen.Should().NotContain("┤Needs attention├", "a heading inside a surface does not need a frame");

        var heading = Find(session, "Needs attention");

        Foreground(session, heading.Row, heading.Column).Should().Be("#D9736A",
            "the heading is in the colour that means something is wrong");
    }

    [Fact]
    public void The_keys_along_the_bottom_are_picked_out_from_what_they_do()
    {
        using var session = Launcher([Project("alpha", "Alpha")], out _);

        var key = Find(session, "Ctrl+P commands");

        Foreground(session, key.Row, key.Column).Should().Be(Accent, "the key is what you would press");
        Foreground(session, key.Row, key.Column + "Ctrl+P ".Length).Should().Be(Dim, "what it does is secondary");
    }

    [Fact]
    public void Something_said_along_the_bottom_covers_the_keys_until_the_cursor_moves()
    {
        var away = new ProjectResolution(
            new ProjectRegistryEntry { Slug = "far", Name = "Far", DefaultAgent = "claude" }, null, null, 0, false);

        using var session = Launcher([Project("alpha", "Alpha"), away], out _);

        // An arrow from the filter lands on the second row, and Enter on a
        // project that is not here says so rather than launching.
        session.Press(Key.CursorDown);
        session.Press(Key.Enter);

        session.Screen.Should().Contain("Far is not on this machine");
        session.Screen.Should().NotContain("Ctrl+P commands");

        session.Press(Key.CursorUp);

        session.Screen.Should().Contain("Ctrl+P commands", "the keys come back once the message is stale");
    }

    [Fact]
    public void A_project_being_read_does_not_wear_the_last_one_s_running_session()
    {
        // Alpha answers at once and has a session running. Beta does not
        // answer until asked to stop, so the pane stays in its reading state,
        // which is where the previous project's line was being left behind.
        // The read honours the token, so closing the screen ends it rather
        // than leaving a read pending in the test host after the test.
        var alpha = Project("alpha", "Alpha");
        var beta = Project("beta", "Beta");

        var never = new TaskCompletionSource<ProjectOverview?>();

        using var session = TuiSession.Start(app => new LauncherWindow(
            [alpha, beta],
            null,
            "workspace connected",
            ["claude"],
            (project, token) => project == alpha
                ? Task.FromResult<ProjectOverview?>(Overview(alpha) with { RunningSessions = 1 })
                : never.Task.WaitAsync(token),
            _ => { },
            [],
            app));

        session.ScreenShowing("a session is running here").Should().Contain("a session is running here");

        // An arrow from the filter lands on the second row.
        session.Press(Key.CursorDown);

        var reading = session.ScreenShowing("Beta");

        reading.Should().Contain("Beta");
        reading.Should().NotContain("a session is running here",
            "what was true of the last project is not known of this one yet");
    }

    [Fact]
    public void The_screen_has_no_frame_of_its_own()
    {
        using var session = Launcher([Project("alpha", "Alpha")], out _);

        // The menu is the first row of the screen, not the second row inside a
        // border, and there is no title frame above it.
        session.Screen.Split('\n')[0].Should().Contain("Project");
        session.Screen.Should().NotContain("┤Loadout├");
    }
}
