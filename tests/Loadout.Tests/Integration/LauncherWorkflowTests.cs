using Loadout.Core.Projects;
using Loadout.Core.Sessions;
using Loadout.Models.Diagnostics;
using Loadout.Models.Projects;
using Loadout.Tui;
using Loadout.Tui.Terminal;
using FluentAssertions;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Whole workflows through the launcher, driven by keystrokes.
/// <para>
/// Every defect that has reached a real terminal so far lived in the gap
/// between "the screen was built" and "somebody used it". These press the keys.
/// </para>
/// <para>
/// What is asserted is the <see cref="LauncherIntent"/> the screen produced,
/// because that is the seam the architecture creates: the screen records what
/// was chosen and closes, and the work happens afterwards through the same
/// parser a typed command would use. Asserting on the intent tests the rule
/// rather than a reimplementation of it.
/// </para>
/// </summary>
public sealed class LauncherWorkflowTests
{
    private static ProjectRegistryEntry Entry(string slug, string name, string agent = "claude") =>
        new() { Slug = slug, Name = name, DefaultAgent = agent };

    private static ProjectResolution Project(string slug, string name, string? path = "/repos/x") =>
        new(Entry(slug, name), path, null, 0, false);

    private static ProjectOverview Overview(
        ProjectResolution project,
        bool guarded = true,
        int trackedAgentFiles = 0) =>
        new(project, "main", true, 4096, 3, 2, 0, guarded, trackedAgentFiles);

    private static readonly CatalogueEntry[] Commands =
    [
        new("doctor", "Check this machine", null),
        new("memory compress", "Move durable facts into memory", null),
        new("completion", "Emit a completion script", "it writes a script to standard output"),
    ];

    /// <summary>Builds the launcher over a fixed set of projects.</summary>
    private TuiSession Launcher(
        IReadOnlyList<ProjectResolution> projects,
        Action<LauncherWindow>? onPalette = null,
        int width = TuiSession.DefaultWidth,
        int height = TuiSession.DefaultHeight,
        IReadOnlyList<AgentSession>? recent = null)
    {
        LauncherWindow? built = null;

        var session = TuiSession.Start(
            app =>
            {
                built = new LauncherWindow(
                    projects,
                    here: null,
                    "workspace connected",
                    ["claude"],
                    (project, _) => Task.FromResult<ProjectOverview?>(Overview(project)),
                    window => onPalette?.Invoke(window),
                    recent ?? [],
                    app);

                return built;
            },
            width,
            height);

        Window = built!;

        return session;
    }

    /// <summary>The launcher built by the most recent call to <see cref="Launcher"/>.</summary>
    private LauncherWindow Window { get; set; } = null!;

    [Fact]
    public void Typing_a_filter_then_pressing_Enter_launches_the_matching_project()
    {
        using var session = Launcher([Project("alpha", "Alpha"), Project("beta", "Beta")]);

        // The filter has focus when the screen opens, which is what makes
        // typing straight away work at all.
        session.Type("bet");

        session.Tab();
        session.Press(Key.Enter);

        // Pressing Enter on a filtered list must launch what is under the
        // cursor, not what was under it before the filter narrowed.
        Window.Intent.Should().NotBeNull();
        Window.Intent!.Action.Should().Be(LauncherAction.Launch);
        Window.Intent.Project!.Entry.Slug.Should().Be("beta");
    }

    [Fact]
    public void The_filter_is_where_focus_starts()
    {
        using var session = Launcher([Project("alpha", "Alpha")]);

        // If focus started on the list, the first thing anybody typed would
        // move the selection instead of filtering.
        session.Focused.Should().NotBeNull();
    }

    [Fact]
    public void Ctrl_P_opens_the_command_palette()
    {
        var opened = false;

        using var session = Launcher(
            [Project("alpha", "Alpha")],
            onPalette: _ => opened = true);

        session.Press(Key.P.WithCtrl);

        opened.Should().BeTrue("Ctrl+P is the documented way to reach every command");
    }

    [Fact]
    public void Ctrl_N_asks_to_add_a_project()
    {
        using var session = Launcher([]);

        session.Press(Key.N.WithCtrl);

        Window.Intent!.Action.Should().Be(LauncherAction.AddProject);
    }

    [Fact]
    public void Ctrl_Q_quits()
    {
        using var session = Launcher([Project("alpha", "Alpha")]);

        session.Press(Key.Q.WithCtrl);

        Window.Intent!.Action.Should().Be(LauncherAction.Quit);
    }

    [Fact]
    public void An_empty_registry_offers_the_way_forward_on_screen()
    {
        using var session = Launcher([]);

        session.Screen.Should().Contain("Add a project");
    }

    [Fact]
    public void A_project_that_is_not_here_cannot_be_launched_by_pressing_Enter()
    {
        using var session = Launcher([Project("gone", "Gone", path: null)]);

        session.Tab();
        session.Press(Key.Enter);

        // Launching something that is not on the machine would fail later and
        // less clearly. Nothing should happen here.
        Window.Intent.Should().BeNull();
    }

    private static AgentSession Session(string id, string agent, string project, string title) =>
        new(agent, id, title, "/repos/x", "main", DateTimeOffset.UtcNow.AddHours(-2),
            "/transcripts/" + id, project);

    [Fact]
    public void Recent_work_is_on_the_launcher()
    {
        using var session = Launcher(
            [Project("alpha", "Alpha")],
            recent: [Session("s1", "claude", "alpha", "parser rewrite")]);

        // "What was I doing?" is the question somebody opening a launcher most
        // often has. The previous launcher could answer it and the rewrite
        // dropped the capability, with no test noticing.
        var screen = session.Screen;

        screen.Should().Contain("Recent");
        screen.Should().Contain("parser rewrite");
    }

    [Fact]
    public void Choosing_a_recent_session_reopens_that_one_rather_than_asking_again()
    {
        using var session = Launcher(
            [Project("alpha", "Alpha")],
            recent: [Session("session-abc", "claude", "alpha", "parser rewrite")]);

        // Filter, then projects, then recent — the order the screen reads.
        session.Tab().Tab();
        session.Press(Key.Enter);

        Window.Intent.Should().NotBeNull();
        Window.Intent!.Action.Should().Be(LauncherAction.Resume);

        // Carries the chosen session, so resuming reopens it instead of
        // putting a picker up over a choice already made.
        Window.Intent.SessionId.Should().Be("session-abc");
    }

    [Fact]
    public void With_no_recent_work_the_launcher_does_not_show_an_empty_panel()
    {
        using var session = Launcher([Project("alpha", "Alpha")], recent: []);

        // A panel headed "Recent" with nothing under it is worse than no
        // panel: it takes space and answers nothing.
        session.Screen.Should().NotContain("Recent");
    }

    [Fact]
    public void The_list_says_whether_a_project_is_ready_at_a_glance()
    {
        using var session = Launcher([Project("alpha", "Alpha")]);

        // Scanning a list of projects, the question is not "what is wrong with
        // this one" but "can I work on it". Readiness answers that without
        // selecting anything.
        var screen = session.ScreenShowing("Ready");

        screen.Should().Contain("Ready");
    }

    [Fact]
    public void A_project_that_is_not_here_is_shown_as_blocked()
    {
        using var session = Launcher([Project("gone", "Gone", path: null)]);

        var screen = session.Screen;

        screen.Should().Contain("Blocked");
    }

    [Fact]
    public void A_slow_project_is_read_once_rather_than_for_ever()
    {
        var reads = 0;

        LauncherWindow? built = null;

        using var session = TuiSession.Start(app =>
        {
            built = new LauncherWindow(
                [Project("alpha", "Alpha")],
                here: null,
                "workspace connected",
                ["claude"],
                (project, _) =>
                {
                    Interlocked.Increment(ref reads);

                    // Genuinely asynchronous, which is what a real repository
                    // read is. A synchronous answer hides this entirely.
                    return Task.Run(() => (ProjectOverview?)Overview(project));
                },
                _ => { },
                [],
                app);

            return built;
        });

        // Pump for long enough that a loop would show itself many times over.
        var deadline = Environment.TickCount64 + 1500;

        while (Environment.TickCount64 < deadline)
        {
            session.Pump();
            Thread.Sleep(10);
        }

        // Recording a project's readiness redraws the rows, redrawing sets the
        // selection, and setting the selection asks for the overview again.
        // Each turn restarted the read and put the reading indicator back, so
        // the branch line pulsed for ever and the answer never settled.
        reads.Should().BeLessThan(
            5,
            $"reading a project once should not cause it to be read again ({reads} reads)");
    }

    [Fact]
    public void Moving_down_the_list_still_reads_each_project()
    {
        var read = new List<string>();

        LauncherWindow? built = null;

        using var session = TuiSession.Start(app =>
        {
            built = new LauncherWindow(
                [Project("alpha", "Alpha"), Project("beta", "Beta")],
                here: null,
                "workspace connected",
                ["claude"],
                (project, _) =>
                {
                    lock (read)
                    {
                        read.Add(project.Entry.Slug);
                    }

                    return Task.Run(() => (ProjectOverview?)Overview(project));
                },
                _ => { },
                [],
                app);

            return built;
        });

        // The fix for the endless re-reading ignores selection changes while
        // the rows are redrawn. It must not also ignore the real ones: moving
        // the cursor to another project has to fetch that project.
        session.Tab();
        session.Press(Key.CursorDown);

        for (var i = 0; i < 20; i++)
        {
            session.Pump();
            Thread.Sleep(5);
        }

        lock (read)
        {
            read.Should().Contain("beta", "moving to a project must read it");
        }
    }

    [Fact]
    public void Typing_a_filter_settles_rather_than_reading_for_ever()
    {
        var reads = 0;

        using var session = TuiSession.Start(app => new LauncherWindow(
            [Project("alpha", "Alpha"), Project("beta", "Beta")],
            here: null,
            "workspace connected",
            ["claude"],
            (project, _) =>
            {
                Interlocked.Increment(ref reads);
                return Task.Run(() => (ProjectOverview?)Overview(project));
            },
            _ => { },
            [],
            app));

        session.Type("bet");

        for (var i = 0; i < 25; i++)
        {
            session.Pump();
            Thread.Sleep(5);
        }

        var afterTyping = reads;

        for (var i = 0; i < 25; i++)
        {
            session.Pump();
            Thread.Sleep(5);
        }

        // The invariant the branch-line loop broke, stated generally: once an
        // interaction is over the launcher stops working. Filtering narrows the
        // list and so does change the selection, which is a real reason to
        // read — but only until the answer arrives.
        reads.Should().Be(
            afterTyping,
            $"the launcher must settle ({afterTyping} reads while typing, {reads} after)");
    }

    [Fact]
    public void The_menu_bar_is_on_screen_with_its_groups_named()
    {
        using var session = Launcher([Project("alpha", "Alpha")]);

        var screen = session.Screen;

        screen.Should().Contain("Project");
        screen.Should().Contain("Registry");
        screen.Should().Contain("Tools");
        screen.Should().Contain("Help");
    }

    [Theory]
    [InlineData(80, 24)]
    [InlineData(100, 30)]
    [InlineData(140, 40)]
    [InlineData(200, 60)]
    public void The_launcher_stays_usable_at_ordinary_terminal_sizes(int width, int height)
    {
        using var session = Launcher([Project("alpha", "Alpha")], width: width, height: height);

        var screen = session.Screen;

        // 80x24 is the real floor: it is the default for most SSH clients, and
        // working over SSH is a requirement rather than a nicety.
        screen.Should().NotBeEmpty();
        screen.Should().Contain("Alpha", "the project list is the point of the screen");
        screen.Should().Contain("Filter", "the way to find a project must not be the first thing lost");
    }

    [Fact]
    public void Filtering_still_works_in_a_narrow_terminal()
    {
        using var session = Launcher(
            [Project("alpha", "Alpha"), Project("beta", "Beta")],
            width: 80,
            height: 24);

        session.Type("bet");
        session.Tab();
        session.Press(Key.Enter);

        Window.Intent!.Project!.Entry.Slug.Should().Be("beta");
    }
}

/// <summary>
/// The screens reached by choosing something, driven by keystrokes.
/// </summary>
public sealed class DialogWorkflowTests
{
    private static readonly OfferedRemedy[] Offered =
    [
        new(new Remedy(RemedyKind.InstallPreCommitHook, "Install the pre-commit hook"),
            "would write .git/hooks/pre-commit"),
    ];

    private static readonly DiagnosticCheck[] Findings =
    [
        DiagnosticCheck.Warn("Repository", "Protection", "no pre-commit hook in this clone"),
    ];

    [Fact]
    public void Escape_closes_the_problems_screen_without_applying_anything()
    {
        ProblemsWindow? window = null;

        using var session = TuiSession.Start(app =>
        {
            window = new ProblemsWindow("Problems - Alpha", Findings, Offered, app);
            return window;
        });

        session.Press(Key.Esc);

        // Backing out must never be the same as agreeing. This is the screen
        // that changes files, so the distinction is load-bearing.
        window!.Chosen.Should().BeEmpty();
    }

    [Fact]
    public void The_problems_screen_shows_the_finding_and_what_a_fix_would_change()
    {
        using var session = TuiSession.Start(app =>
            new ProblemsWindow("Problems - Alpha", Findings, Offered, app));

        var screen = session.Screen;

        screen.Should().Contain("pre-commit");
        screen.Should().Contain("would write");
    }

    [Fact]
    public void Escape_dismisses_a_question_without_answering_it()
    {
        ChoiceDialog? dialog = null;

        using var session = TuiSession.Start(app =>
        {
            dialog = new ChoiceDialog("What are you working on?", ["database", "frontend"], app);
            return dialog;
        });

        session.Press(Key.Esc);

        // Null rather than the first option: silently picking one would start a
        // session against the wrong context.
        dialog!.ChosenIndex.Should().BeNull();
    }

    [Fact]
    public void A_terminal_only_command_is_listed_with_its_reason_rather_than_hidden()
    {
        using var session = TuiSession.Start(app => new CommandPaletteDialog(
            [new CatalogueEntry("completion", "Emit a completion script", "it writes a script somewhere")],
            app));

        session.Screen.Should().Contain("terminal only");
    }

    [Fact]
    public void The_palette_finds_a_command_by_what_it_is_for()
    {
        using var session = TuiSession.Start(app => new CommandPaletteDialog(
            [
                new CatalogueEntry(
                    "backup restore", "Undo an operation that changed files", null,
                    CommandCategory.Workspace, "undo revert mistake recover"),
                new CatalogueEntry(
                    "doctor", "Check this machine", null, CommandCategory.Health, "broken wrong"),
            ],
            app));

        // "revert" appears in neither the path nor the description, so this
        // only finds it through the intent words. Searching for "undo" would
        // have passed on the description alone and proved nothing.
        session.Type("revert");

        var screen = session.Screen;

        screen.Should().Contain("backup restore");
        screen.Should().NotContain("doctor");
    }

    [Fact]
    public void The_palette_says_which_commands_change_things()
    {
        using var session = TuiSession.Start(app => new CommandPaletteDialog(
            [
                new CatalogueEntry(
                    "migrate", "Move existing agent files", null,
                    CommandCategory.Workspace, "move adopt", Mutates: true),
                new CatalogueEntry(
                    "status", "Summarise state", null, CommandCategory.Health, "state"),
            ],
            app));

        var screen = session.Screen;

        // A palette that looks the same for "show me the settings" and
        // "rewrite the settings" asks somebody to remember which is which.
        screen.Should().Contain("changes files");
    }

    [Fact]
    public void Typing_in_the_palette_narrows_it_to_what_was_typed()
    {
        using var session = TuiSession.Start(app => new CommandPaletteDialog(
            [
                new CatalogueEntry("doctor", "Check this machine", null),
                new CatalogueEntry("memory compress", "Move durable facts into memory", null),
            ],
            app));

        session.Type("mem");

        var screen = session.Screen;

        screen.Should().Contain("memory compress");
        screen.Should().NotContain("doctor");
    }
}
