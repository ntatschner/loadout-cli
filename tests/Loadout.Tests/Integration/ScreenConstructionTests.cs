using System.Drawing;
using Loadout.Models.Diagnostics;
using Loadout.Models.Projects;
using Loadout.Core.Projects;
using Loadout.Core.Sessions;
using Loadout.Tui;
using Loadout.Tui.Terminal;
using FluentAssertions;
using Loadout.Core.Configuration;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;
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
    public void Choosing_a_command_in_the_palette_returns_it()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        using var palette = new CommandPaletteDialog(
            [new CatalogueEntry("doctor", "Check this machine", null)],
            app);

        app.Begin(palette);
        app.LayoutAndDraw();

        // The list, focused, exactly as somebody arrowing down to a command
        // leaves it.
        var list = palette.SubViews.OfType<ListView>().Single();
        list.SetFocus();

        list.NewKeyDownEvent(Key.Enter);

        // The palette exists to hand a command back. Everything else about it
        // working while this does not is indistinguishable, from the outside,
        // from the launcher being broken.
        palette.Chosen.Should().Be("doctor");
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
            new Loadout.Models.Configuration.MachineConfig(),
            [("Shared settings", "/config/config.yaml"), ("This machine", "/state/machines.yaml")],
            ["claude"],
            "code",
            ["Agents"],
            app);

        app.Begin(settings);
        app.LayoutAndDraw();

        var screen = app.Driver?.ToString() ?? string.Empty;

        // Both halves at once, which is the point: the printed version could
        // show the settings or change one, never both.
        screen.Should().Contain("workspace.git");

        // The groups down the side, so what is not on the open page is at
        // least visibly reachable rather than absent.
        screen.Should().Contain("Workspace");
        screen.Should().Contain("Agent status line");
        screen.Should().Contain("Paths");
    }

    [Fact]
    public void Every_setting_the_config_command_has_can_be_changed_on_the_screen()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        using var settings = new SettingsWindow(
            new Loadout.Models.Configuration.LauncherConfig(),
            new Loadout.Models.Configuration.MachineConfig(),
            [("Shared settings", "/config/config.yaml")],
            ["claude"],
            "code",
            [],
            app);

        // Held against the registry rather than against a list written here,
        // because a list written here is exactly what went wrong: the screen
        // named six settings by hand out of twenty-one, and the fifteen it
        // omitted were unreachable from anywhere but 'loadout config set'.
        var expected = ConfigKeys.All
            .Select(entry => entry.Key)
            .Where(key => key != "editor-profiles")
            .ToList();

        settings.Editable.Should().BeEquivalentTo(
            expected,
            "a setting 'loadout config' can change must be changeable on the screen for changing settings");
    }

    [Fact]
    public void The_agent_to_editor_profile_map_gets_a_row_per_agent_rather_than_a_syntax()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        using var settings = new SettingsWindow(
            new Loadout.Models.Configuration.LauncherConfig(),
            new Loadout.Models.Configuration.MachineConfig(),
            [("Shared settings", "/config/config.yaml")],
            ["claude", "codex"],
            "code",
            ["Agents"],
            app);

        // The one setting deliberately not shown as itself. Its value is
        // written "claude=Agents;codex=Codex", and a field holding a syntax
        // somebody has to look up is worse than a row per installed agent.
        settings.Editable.Should().NotContain("editor-profiles");

        app.Begin(settings);
        app.LayoutAndDraw();

        // Reachable, even though it is not the page that opens.
        (app.Driver?.ToString() ?? string.Empty).Should().Contain("Editor");
    }

    [Fact]
    public void Looking_at_the_settings_does_not_change_them()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        using var settings = new SettingsWindow(
            new Loadout.Models.Configuration.LauncherConfig(),
            new Loadout.Models.Configuration.MachineConfig(),
            [("Shared settings", "/config/config.yaml")],
            [],
            "code",
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

    [Fact]
    public void Pressing_enter_on_a_choice_selects_it()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        using var choice = new ChoiceDialog("What are you working on?", ["database", "frontend"], app);

        app.Begin(choice);
        app.LayoutAndDraw();

        var list = choice.SubViews.OfType<ListView>().Single();
        list.SetFocus();
        list.SelectedItem = 1;

        list.NewKeyDownEvent(Key.Enter);

        // Enter on the highlighted row is how anybody answers a list of
        // options. Requiring them to tab to a button first is not the same
        // thing, and neither is silently doing nothing.
        choice.ChosenIndex.Should().Be(1);
    }

    [Fact]
    public void Pressing_enter_on_a_project_launches_it()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        var project = new ProjectResolution(
            new ProjectRegistryEntry { Slug = "alpha", Name = "Alpha" },
            Path.GetTempPath(), null, 0, false);

        using var window = new LauncherWindow(
            [project],
            null,
            "workspace ready",
            ["claude"],
            (_, _) => Task.FromResult<ProjectOverview?>(null),
            _ => { },
            [],
            app);

        app.Begin(window);
        app.LayoutAndDraw();

        var list = FindProjectList(window);
        list.SetFocus();
        list.SelectedItem = 0;

        list.NewKeyDownEvent(Key.Enter);

        // Enter on a project is the reason the launcher exists. It closes the
        // screen with an intent to launch; doing nothing at all is
        // indistinguishable from the key not having registered.
        window.Intent.Should().NotBeNull();
        window.Intent!.Action.Should().Be(LauncherAction.Launch);
    }

    /// <summary>The projects list, wherever it has been nested this week.</summary>
    private static ListView FindProjectList(View root)
    {
        foreach (var child in root.SubViews)
        {
            if (child is KeyedListView keyed)
            {
                return keyed;
            }

            var found = FindProjectListOrNull(child);

            if (found is not null)
            {
                return found;
            }
        }

        throw new InvalidOperationException("No project list was found on the launcher window.");
    }

    private static ListView? FindProjectListOrNull(View root)
    {
        foreach (var child in root.SubViews)
        {
            if (child is KeyedListView keyed)
            {
                return keyed;
            }

            var found = FindProjectListOrNull(child);

            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    [Fact]
    public void Pressing_enter_on_a_recent_session_resumes_it()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        var project = new ProjectResolution(
            new ProjectRegistryEntry { Slug = "alpha", Name = "Alpha" },
            Path.GetTempPath(), null, 0, false);

        var session = new AgentSession(
            "claude", "2b7c1d64", "Fix the upload path", Path.GetTempPath(),
            "main", DateTimeOffset.UtcNow, Path.GetTempPath(), "alpha");

        using var window = new LauncherWindow(
            [project],
            null,
            "workspace ready",
            ["claude"],
            (_, _) => Task.FromResult<ProjectOverview?>(null),
            _ => { },
            [session],
            app);

        app.Begin(window);
        app.LayoutAndDraw();

        // The recent list is the second list on the window; the projects list
        // is a KeyedListView and this one is not.
        // Found by what it is showing rather than by its type, so the test
        // cannot quietly pick the projects list and prove nothing.
        var lists = AllViews(window).OfType<ListView>().ToList();

        lists.Should().HaveCountGreaterThan(1, "the window should show projects and recent work");

        var recent = lists.Single(view =>
            (view.Source?.ToList()?.Cast<object?>() ?? [])
                .Any(row => (row?.ToString() ?? string.Empty).Contains("Fix the upload path", StringComparison.Ordinal)));

        recent.SetFocus();
        recent.SelectedItem = 0;

        recent.NewKeyDownEvent(Key.Enter);

        // The last handler on a list still using Accepted. The palette's did
        // not fire and every command in it was dead, so this one is worth
        // holding down rather than assuming.
        window.Intent.Should().NotBeNull();
        window.Intent!.Action.Should().Be(LauncherAction.Resume);
    }

    private static IEnumerable<View> AllViews(View root)
    {
        foreach (var child in root.SubViews)
        {
            yield return child;

            foreach (var descendant in AllViews(child))
            {
                yield return descendant;
            }
        }
    }

    [Fact]
    public void The_launch_button_in_the_detail_pane_launches()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        var project = new ProjectResolution(
            new ProjectRegistryEntry { Slug = "alpha", Name = "Alpha" },
            Path.GetTempPath(), null, 0, false);

        using var window = new LauncherWindow(
            [project],
            null,
            "workspace ready",
            ["claude"],
            (_, _) => Task.FromResult<ProjectOverview?>(null),
            _ => { },
            [],
            app);

        app.Begin(window);
        app.LayoutAndDraw();

        var launch = AllViews(window)
            .OfType<Button>()
            .Single(b => (b.Text ?? string.Empty).Contains("Launch", StringComparison.Ordinal));

        launch.SetFocus();
        launch.NewKeyDownEvent(Key.Enter);

        // The detail pane had no test of any kind, and its four buttons are the
        // launcher's primary actions. A button that looks enabled and does
        // nothing is the defect the command palette already shipped once.
        window.Intent.Should().NotBeNull();
        window.Intent!.Action.Should().Be(LauncherAction.Launch);
    }

    [Fact]
    public void Applying_a_ticked_fix_returns_it_and_leaves_the_rest_alone()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        using var window = new ProblemsWindow(
            "Alpha",
            [DiagnosticCheck.Warn("Repository", "Protection", "no pre-commit hook in this clone")],
            [
                new OfferedRemedy(
                    new Remedy(RemedyKind.InstallPreCommitHook, "Install the pre-commit hook"),
                    "would write .git/hooks/pre-commit"),
                new OfferedRemedy(
                    new Remedy(RemedyKind.RepairGlobalExcludes, "Repair the global excludes"),
                    "would write the global excludes file"),
            ],
            app);

        app.Begin(window);
        app.LayoutAndDraw();

        // The window shows two lists and the findings one is built first, so
        // picking by position marks the wrong thing and proves nothing. This is
        // the one that offers fixes.
        var remedies = AllViews(window).OfType<ListView>().Single(view => view.ShowMarks);

        // Tick the second one only. Applying everything offered regardless of
        // what was ticked would be the worst possible reading of this screen.
        remedies.Source!.SetMark(1, true);

        var apply = AllViews(window)
            .OfType<Button>()
            .Single(b => (b.Text ?? string.Empty).Contains("Apply", StringComparison.Ordinal));

        apply.SetFocus();
        apply.NewKeyDownEvent(Key.Enter);

        window.Chosen.Should().ContainSingle()
            .Which.Kind.Should().Be(RemedyKind.RepairGlobalExcludes);
    }

    [Fact]
    public void Ctrl_P_opens_the_palette_while_the_list_has_focus()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        var project = new ProjectResolution(
            new ProjectRegistryEntry { Slug = "alpha", Name = "Alpha" },
            Path.GetTempPath(), null, 0, false);

        var opened = 0;

        using var window = new LauncherWindow(
            [project],
            null,
            "workspace ready",
            ["claude"],
            (_, _) => Task.FromResult<ProjectOverview?>(null),
            _ => opened++,
            [],
            app);

        app.Begin(window);
        app.LayoutAndDraw();

        // Where the focus actually is when somebody presses it. A list claims
        // Ctrl+P for extending a selection, and a window's bindings are not
        // consulted while a child has the focus, so the key did nothing but
        // nudge the highlight on a screen that advertises it as "all commands".
        var list = AllViews(window).OfType<ListView>().First(v => v is KeyedListView);
        list.SetFocus();

        list.HasFocus.Should().BeTrue("the list is what has the focus on this screen");

        // This does not prove the fix, and says so rather than implying it.
        // Ctrl+P was reported as doing nothing in a real terminal, and it
        // passes here whether the list keeps its own claim on Ctrl+P or not,
        // and whether the window binds the key for itself or for the whole
        // application. Raising a key at the application in a headless harness
        // evidently dispatches differently from a console driver delivering one
        // to a focused view.
        //
        // What it holds down is that the palette is reachable by that key at
        // all, which is worth having. What it cannot tell you is whether it
        // reaches it on somebody's machine.

        // Raised at the application, which is where a real keystroke arrives.
        // Raising it on the focused view instead only exercises that view's own
        // bindings and the ones it bubbles to, and never reaches a binding held
        // for the whole application — so the test would fail for a reason that
        // has nothing to do with the key working.
        app.Keyboard.RaiseKeyDownEvent(Key.P.WithCtrl);

        opened.Should().Be(1, "Ctrl+P is on the status line, so it has to reach the palette");
    }

    [Fact]
    public void Ctrl_N_adds_a_project_while_the_list_has_focus()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        var project = new ProjectResolution(
            new ProjectRegistryEntry { Slug = "alpha", Name = "Alpha" },
            Path.GetTempPath(), null, 0, false);

        using var window = new LauncherWindow(
            [project],
            null,
            "workspace ready",
            ["claude"],
            (_, _) => Task.FromResult<ProjectOverview?>(null),
            _ => { },
            [],
            app);

        app.Begin(window);
        app.LayoutAndDraw();

        var list = AllViews(window).OfType<ListView>().First(v => v is KeyedListView);
        list.SetFocus();

        app.Keyboard.RaiseKeyDownEvent(Key.N.WithCtrl);

        // The same fault as Ctrl+P and found with it: the list claimed this one
        // too, for extending a selection downwards.
        window.Intent.Should().NotBeNull();
        window.Intent!.Action.Should().Be(LauncherAction.AddProject);
    }
}
