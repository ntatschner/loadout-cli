using System.Drawing;
using Loadout.Core.Projects;
using Loadout.Models.Projects;
using Loadout.Tui.Terminal;
using FluentAssertions;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Drives the launcher screen the way somebody would, and reads back what was
/// actually drawn.
/// <para>
/// The ANSI driver rather than the platform one: it needs no real terminal, so
/// these run identically on all three operating systems and in CI, and it can
/// be asked what it put on the screen. That is a stronger check than the
/// prompt-based launcher allowed — those tests could only assert on text that
/// had been printed, never on what a person would be looking at.
/// </para>
/// </summary>
public sealed class TerminalLauncherTests
{
    /// <summary>
    /// A terminal wide enough that nothing under test is truncated, since a
    /// clipped label would fail an assertion for a reason that has nothing to
    /// do with the behaviour being checked.
    /// </summary>
    private const int ScreenWidth = 140;

    private const int ScreenHeight = 40;

    private static ProjectRegistryEntry Entry(string slug, string name, string agent = "claude") =>
        new() { Slug = slug, Name = name, DefaultAgent = agent };

    private static ProjectResolution Project(
        string slug,
        string name,
        string? path = "/repos/x",
        bool pinned = false) =>
        new(Entry(slug, name), path, null, 0, pinned);

    private static ProjectOverview Overview(
        ProjectResolution project,
        string? branch = "main",
        bool clean = true,
        long bytes = 4096,
        int scopedRules = 3,
        int memoryTopics = 2,
        int pendingImports = 0,
        bool guarded = true,
        int trackedAgentFiles = 0) =>
        new(project, branch, clean, bytes, scopedRules, memoryTopics, pendingImports, guarded, trackedAgentFiles);

    /// <summary>
    /// Stands the screen up, draws it once, and hands back what is on it.
    /// </summary>
    private static void OnScreen(
        IReadOnlyList<ProjectResolution> projects,
        Func<ProjectResolution, ProjectOverview?> overview,
        Action<LauncherWindow, IApplication, string> assert,
        ProjectResolution? here = null)
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);

        // Nothing is drawn without one. Off a real terminal the driver reports
        // a screen of no size, every view lays out to nothing, and assertions
        // about what is on screen quietly pass against an empty string.
        app.Screen = new Rectangle(0, 0, ScreenWidth, ScreenHeight);

        using var window = new LauncherWindow(
            projects,
            here,
            "workspace connected",
            ["Claude Code"],
            (project, _) => Task.FromResult(overview(project)),
            _ => { },
            app);

        app.Begin(window);
        app.LayoutAndDraw();

        assert(window, app, app.Driver?.ToString() ?? string.Empty);
    }

    /// <summary>
    /// Waits for something read off the main loop to reach the screen.
    /// <para>
    /// The details of a project are fetched on a background task on purpose, so
    /// that a slow repository cannot freeze a list somebody is moving through.
    /// That makes the moment they appear genuinely asynchronous, and asserting
    /// immediately after selecting would be a race the test would usually win
    /// and occasionally lose.
    /// </para>
    /// </summary>
    private static string ScreenShowing(IApplication app, string expected)
    {
        var deadline = Environment.TickCount64 + 5000;
        var screen = string.Empty;

        do
        {
            app.RaiseIteration();
            app.LayoutAndDraw();

            screen = app.Driver?.ToString() ?? string.Empty;

            if (screen.Contains(expected, StringComparison.Ordinal))
            {
                return screen;
            }

            Thread.Sleep(10);
        }
        while (Environment.TickCount64 < deadline);

        return screen;
    }

    [Fact]
    public void Every_project_is_on_the_screen()
    {
        var projects = new[] { Project("alpha", "Alpha"), Project("beta", "Beta") };

        OnScreen(
            projects,
            p => Overview(p),
            (_, _, screen) =>
            {
                screen.Should().Contain("Alpha");
                screen.Should().Contain("Beta");
            });
    }

    [Fact]
    public void The_repository_you_are_standing_in_is_offered_first()
    {
        var alpha = Project("alpha", "Alpha");
        var beta = Project("beta", "Beta");

        // Ordered by the launcher rather than by the registry: the repository
        // somebody is in is almost always the one they meant.
        OnScreen(
            [alpha, beta],
            p => Overview(p),
            (window, _, _) => window.Selected!.Entry.Slug.Should().Be("beta"),
            here: beta);
    }

    [Fact]
    public void The_selected_project_is_described_beside_the_list()
    {
        var alpha = Project("alpha", "Alpha");

        OnScreen(
            [alpha],
            project => Overview(project, branch: "release/2", clean: false, memoryTopics: 7),
            (_, app, _) =>
            {
                var screen = ScreenShowing(app, "release/2");

                screen.Should().Contain("release/2");
                screen.Should().Contain("uncommitted changes");
            });
    }

    [Fact]
    public void A_project_that_is_not_on_this_machine_says_so()
    {
        OnScreen(
            [Project("gone", "Gone", path: null)],
            _ => null,
            (_, _, screen) => screen.Should().Contain("not on this machine"));
    }

    [Fact]
    public void The_state_of_the_machine_is_shown_without_being_asked_for()
    {
        OnScreen(
            [Project("alpha", "Alpha")],
            p => Overview(p),
            (_, _, screen) =>
            {
                screen.Should().Contain("workspace connected");
                screen.Should().Contain("Claude Code");
            });
    }

    [Fact]
    public void Typing_in_the_filter_narrows_the_list()
    {
        var projects = new[] { Project("alpha", "Alpha"), Project("beta", "Beta") };

        OnScreen(
            projects,
            p => Overview(p),
            (window, app, _) =>
            {
                var injector = app.GetInputInjector();

                foreach (var key in new[] { Key.B, Key.E, Key.T })
                {
                    injector.InjectKey(key, new InputInjectionOptions());
                }

                injector.ProcessQueue();
                app.LayoutAndDraw();

                // Whatever is left under the cursor must be something that
                // matched: a filter that narrows the list but leaves the
                // selection pointing at a hidden row would launch the wrong
                // project.
                window.Selected.Should().NotBeNull();
                window.Selected!.Entry.Name.Should().Be("Beta");
            });
    }

    [Fact]
    public void Choosing_a_project_records_which_agent_to_start()
    {
        var alpha = Project("alpha", "Alpha");

        OnScreen(
            [alpha],
            p => Overview(p),
            (window, _, _) =>
            {
                window.Close(new LauncherIntent(
                    LauncherAction.Launch, alpha, alpha.Entry.DefaultAgent));

                window.Intent!.Action.Should().Be(LauncherAction.Launch);
                window.Intent.Agent.Should().Be("claude");
                window.Intent.Project!.Entry.Slug.Should().Be("alpha");
            });
    }

    [Fact]
    public void A_command_chosen_from_the_palette_is_carried_out_of_the_screen()
    {
        var alpha = Project("alpha", "Alpha");

        OnScreen(
            [alpha],
            p => Overview(p),
            (window, _, _) =>
            {
                window.RunCommand("memory compress");

                // The same string somebody would have typed, so the parser can
                // take it unaltered rather than it being reassembled here.
                window.Intent!.Action.Should().Be(LauncherAction.Command);
                window.Intent.CommandPath.Should().Be("memory compress");
            });
    }

    [Fact]
    public void An_empty_registry_does_not_leave_a_stale_selection()
    {
        OnScreen(
            [],
            p => Overview(p),
            (window, _, _) => window.Selected.Should().BeNull());
    }

    [Fact]
    public void An_empty_registry_is_given_a_way_forward()
    {
        // The state every new person starts in. A blank screen with nothing to
        // act on is the worst possible answer to it, and naming a command to
        // type is barely better when the launcher is already open.
        OnScreen(
            [],
            p => Overview(p),
            (_, _, screen) => screen.Should().Contain("Add a project"));
    }

    [Fact]
    public void The_menu_names_what_the_launcher_can_do()
    {
        // Discoverable by looking rather than by already knowing which key to
        // press, which was the whole complaint about the prompt-based one.
        OnScreen(
            [Project("alpha", "Alpha")],
            p => Overview(p),
            (_, _, screen) =>
            {
                screen.Should().Contain("Project");
                screen.Should().Contain("Registry");
                screen.Should().Contain("Tools");
            });
    }

    [Fact]
    public void A_project_with_nothing_wrong_does_not_offer_to_fix_anything()
    {
        OnScreen(
            [Project("alpha", "Alpha")],
            p => Overview(p),
            (_, app, _) =>
            {
                var screen = ScreenShowing(app, "main");

                screen.Should().NotContain("Needs attention");
            });
    }

    [Fact]
    public void A_project_with_something_wrong_says_what_and_offers_to_fix_it()
    {
        OnScreen(
            [Project("alpha", "Alpha")],
            p => Overview(p, guarded: false, trackedAgentFiles: 2),
            (_, app, _) =>
            {
                var screen = ScreenShowing(app, "Needs attention");

                screen.Should().Contain("Needs attention");
                screen.Should().Contain("pre-commit");

                // The button appearing at all is the signal that something
                // needs looking at, so its absence would be the bug.
                screen.Should().Contain("Problems");
            });
    }

    [Fact]
    public void Asking_to_add_a_project_leaves_the_screen_to_do_it()
    {
        OnScreen(
            [],
            p => Overview(p),
            (window, _, _) =>
            {
                window.Close(new LauncherIntent(LauncherAction.AddProject));

                window.Intent!.Action.Should().Be(LauncherAction.AddProject);
            });
    }
}
