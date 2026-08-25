using System.Drawing;
using Loadout.Models.Diagnostics;
using Loadout.Tui;
using Loadout.Tui.Terminal;
using FluentAssertions;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Builds every screen the launcher can put up, and draws it.
/// <para>
/// These exist because of a crash that reached a real terminal. The opening
/// animation binds Enter, the view it derives from had already bound Enter, and
/// binding a key twice throws:
/// </para>
/// <code>
/// System.InvalidOperationException: A binding for Enter exists ([Quit], Key=Enter).
/// </code>
/// <para>
/// Nothing caught it. The launcher screen was tested thoroughly, but the
/// animation is skipped when output is redirected — which is always, under a
/// test runner — so the constructor that threw was never once run. The other
/// screens were in the same position for the same reason: reachable only by
/// choosing something, and so never built.
/// </para>
/// <para>
/// Constructing and drawing each one is a low bar deliberately. It is the bar
/// that was not being met.
/// </para>
/// </summary>
public sealed class ScreenConstructionTests
{
    private const int Width = 140;
    private const int Height = 40;

    /// <summary>
    /// Stands up an application the same way the launcher does, and draws
    /// whatever the test builds on it.
    /// </summary>
    private static void Drawn(Func<IApplication, Terminal.Gui.Views.Runnable> build)
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        using var window = build(app);

        app.Begin(window);
        app.LayoutAndDraw();

        // Something has to be on the screen. A window that builds but draws
        // nothing is the other way this fails.
        (app.Driver?.ToString() ?? string.Empty).Should().NotBeEmpty();
    }

    [Fact]
    public void The_opening_animation_can_be_built_and_drawn()
    {
        // The exact thing that crashed on a real terminal.
        Drawn(app => new SplashScreen(app, "reading your projects"));
    }

    [Fact]
    public void The_opening_animation_draws_the_name()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        using var splash = new SplashScreen(app, "reading your projects");

        app.Begin(splash);
        app.LayoutAndDraw();

        var screen = app.Driver?.ToString() ?? string.Empty;

        screen.Should().Contain("█");
        screen.Should().Contain("reading your projects");
    }

    [Fact]
    public void The_command_palette_can_be_built_and_drawn()
    {
        Drawn(app => new CommandPaletteDialog(
            [
                new CatalogueEntry("doctor", "Check this machine", null),
                new CatalogueEntry("completion", "Emit a completion script", "it writes a script to standard output"),
            ],
            app));
    }

    [Fact]
    public void The_command_palette_says_why_something_cannot_run_here()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        using var palette = new CommandPaletteDialog(
            [new CatalogueEntry("completion", "Emit a completion script", "it writes a script to standard output")],
            app);

        app.Begin(palette);
        app.LayoutAndDraw();

        var screen = app.Driver?.ToString() ?? string.Empty;

        // Listed with the reason rather than hidden, which is the whole point.
        screen.Should().Contain("terminal only");
    }

    [Fact]
    public void The_problems_screen_can_be_built_and_drawn()
    {
        Drawn(app => new ProblemsWindow(
            "Alpha",
            [DiagnosticCheck.Warn("Repository", "Protection", "no pre-commit hook in this clone")],
            [new OfferedRemedy(
                new Remedy(RemedyKind.InstallPreCommitHook, "Install the pre-commit hook"),
                "would write .git/hooks/pre-commit")],
            app));
    }

    [Fact]
    public void The_problems_screen_shows_what_a_fix_would_change()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        using var problems = new ProblemsWindow(
            "Alpha",
            [DiagnosticCheck.Warn("Repository", "Protection", "no pre-commit hook in this clone")],
            [new OfferedRemedy(
                new Remedy(RemedyKind.InstallPreCommitHook, "Install the pre-commit hook"),
                "would write .git/hooks/pre-commit")],
            app);

        app.Begin(problems);
        app.LayoutAndDraw();

        var screen = app.Driver?.ToString() ?? string.Empty;

        screen.Should().Contain("pre-commit");
        screen.Should().Contain("would write");
    }

    [Fact]
    public void The_problems_screen_copes_with_nothing_being_wrong()
    {
        // Reachable: somebody fixes everything and looks again.
        Drawn(app => new ProblemsWindow("Alpha", [], [], app));
    }

    [Fact]
    public void Nothing_is_applied_until_it_is_asked_for()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        using var problems = new ProblemsWindow(
            "Alpha",
            [DiagnosticCheck.Warn("Repository", "Protection", "no hook")],
            [new OfferedRemedy(new Remedy(RemedyKind.InstallPreCommitHook, "Install it"), "would write a hook")],
            app);

        app.Begin(problems);
        app.LayoutAndDraw();

        // Opening the screen must never be the same as agreeing to it.
        problems.Chosen.Should().BeEmpty();
    }

    [Fact]
    public void The_settings_screen_shows_the_settings_and_where_they_live()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        var config = new Loadout.Models.Configuration.LauncherConfig
        {
            DefaultAgent = "codex",
        };

        config.Workspace.Remote = "git@example.com:me/workspace.git";

        using var settings = new SettingsWindow(
            config,
            [("Shared settings", "/config/config.yaml"), ("This machine", "/state/machines.yaml")],
            ["Claude Code"],
            app);

        app.Begin(settings);
        app.LayoutAndDraw();

        var screen = app.Driver?.ToString() ?? string.Empty;

        // Both halves at once, which is the point: the printed version could
        // show the settings or change one, never both.
        screen.Should().Contain("workspace.git");
        screen.Should().Contain("codex");
        screen.Should().Contain("config.yaml");
        screen.Should().Contain("machines.yaml");
    }

    [Fact]
    public void Looking_at_the_settings_does_not_change_them()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        using var settings = new SettingsWindow(
            new Loadout.Models.Configuration.LauncherConfig(),
            [("Shared settings", "/config/config.yaml")],
            [],
            app);

        app.Begin(settings);
        app.LayoutAndDraw();

        // Null until Save is chosen. Opening the screen must never be the same
        // as agreeing to whatever is in it.
        settings.Edit.Should().BeNull();
    }

    [Fact]
    public void The_machine_check_uses_the_same_screen_as_a_project_problem()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        // Same screen, different heading. The two are the same shape, so a
        // second implementation would only be the first one drifting.
        using var machine = new ProblemsWindow(
            "This machine",
            [DiagnosticCheck.Warn("Git", "Global exclude file", "no global excludes configured")],
            [new OfferedRemedy(
                new Remedy(RemedyKind.RepairGlobalExcludes, "Repair the global excludes"),
                "would write ~/.config/git/ignore")],
            app);

        app.Begin(machine);
        app.LayoutAndDraw();

        var screen = app.Driver?.ToString() ?? string.Empty;

        screen.Should().Contain("This machine");
        screen.Should().Contain("global excludes");
    }

    [Fact]
    public void A_question_can_be_built_and_drawn()
    {
        Drawn(app => new ChoiceDialog("What are you working on?", ["database", "frontend"], app));
    }

    [Fact]
    public void A_question_that_is_dismissed_has_no_answer()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        using var choice = new ChoiceDialog("What are you working on?", ["database", "frontend"], app);

        app.Begin(choice);
        app.LayoutAndDraw();

        // Null rather than the first option: dismissing is a real answer, and
        // silently picking one would start a session against the wrong context.
        choice.ChosenIndex.Should().BeNull();
    }
}
