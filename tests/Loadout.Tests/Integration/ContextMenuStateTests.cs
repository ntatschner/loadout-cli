using System.Drawing;
using Loadout.Core.Projects;
using Loadout.Models.Projects;
using Loadout.Tui.Terminal;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Whether the two switches that change what a session is given say where they
/// currently stand.
/// <para>
/// Both are toggles, and a toggle whose current state is nowhere on the screen
/// is a guess: you press it to find out, and finding out costs you the thing
/// you were trying to check. The detail pane says what a project carries only
/// once it carries something, so with both off — the ordinary case — the menu
/// was the only place left to say so, and it said nothing.
/// </para>
/// </summary>
public sealed class ContextMenuStateTests
{
    private const int Width = 120;
    private const int Height = 40;

    private static LauncherWindow Launcher(
        out IApplication app,
        bool carriesTasks,
        bool carriesCodeMap)
    {
        app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        var project = new ProjectResolution(
            new ProjectRegistryEntry { Slug = "alpha", Name = "Alpha" },
            Path.GetTempPath(), null, 0, false);

        var overview = new ProjectOverview(
            project,
            Branch: "main",
            IsClean: true,
            AlwaysLoadedBytes: 0,
            ScopedRules: 0,
            MemoryTopics: 0,
            PendingImports: 0,
            Protected: true,
            TrackedAgentFiles: 0,
            CarriesTasks: carriesTasks,
            CarriesCodeMap: carriesCodeMap);

        var window = new LauncherWindow(
            [project],
            null,
            "workspace ready",
            ["claude"],
            (_, _) => Task.FromResult<ProjectOverview?>(overview),
            _ => { },
            [],
            app);

        app.Begin(window);
        app.LayoutAndDraw();

        return window;
    }

    [Fact]
    public void A_switch_that_is_off_says_so()
    {
        using var window = Launcher(out var app, carriesTasks: false, carriesCodeMap: false);
        using (app)
        {
            LauncherWindow.ContextTitle(LauncherWindow.CarryTasks, on: false)
                .Should().Be("Carry open _tasks into sessions  (off)");

            LauncherWindow.ContextTitle(LauncherWindow.InlineCodeMap, on: false)
                .Should().EndWith("(off)");
        }
    }

    [Fact]
    public void A_switch_that_is_on_says_so()
    {
        LauncherWindow.ContextTitle(LauncherWindow.CarryTasks, on: true)
            .Should().Be("Carry open _tasks into sessions  (on)");
    }

    [Fact]
    public void The_label_itself_does_not_move_between_states()
    {
        // The wording stays put so the item stays findable, and only the state
        // after it changes. A menu whose verb flips under you has to be read
        // again every time.
        LauncherWindow.ContextTitle(LauncherWindow.CarryTasks, on: true)
            .Should().StartWith(LauncherWindow.CarryTasks);

        LauncherWindow.ContextTitle(LauncherWindow.CarryTasks, on: false)
            .Should().StartWith(LauncherWindow.CarryTasks);
    }

    [Fact]
    public void The_state_is_written_out_rather_than_ticked()
    {
        // Terminal.Gui decorates with characters a stock console font may have
        // no glyph for, and the ANSI harness cannot see a missing glyph: the
        // text it asserts on is correct either way. Seven of the toolkit's
        // glyphs already render blank in Cascadia Mono, so this stays ASCII.
        var title = LauncherWindow.ContextTitle(LauncherWindow.CarryTasks, on: true);

        title.Should().MatchRegex("^[\\x20-\\x7E]+$", "a console font cannot fail to draw ASCII");
    }

    [Fact]
    public void The_menu_shows_what_the_selected_project_actually_carries()
    {
        using var window = Launcher(out var app, carriesTasks: true, carriesCodeMap: false);
        using (app)
        {
            var (tasks, map) = window.ContextLabels;

            tasks.Should().StartWith(LauncherWindow.CarryTasks, "the switch is still named");
            tasks.Should().EndWith("(on)", "this project carries its open tasks");

            map.Should().StartWith(LauncherWindow.InlineCodeMap);
            map.Should().EndWith("(off)", "and does not inline the code map");
        }
    }

    [Fact]
    public void A_project_carrying_nothing_says_off_on_both()
    {
        using var window = Launcher(out var app, carriesTasks: false, carriesCodeMap: false);
        using (app)
        {
            var (tasks, map) = window.ContextLabels;

            // The ordinary case, and the one that used to say nothing at all.
            tasks.Should().EndWith("(off)");
            map.Should().EndWith("(off)");
        }
    }
}
