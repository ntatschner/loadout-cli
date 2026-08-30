using Loadout.Core.Projects;
using Loadout.Core.Sessions;
using Loadout.Models.Diagnostics;
using Loadout.Models.Projects;
using Loadout.Tui;
using Loadout.Tui.Terminal;
using FluentAssertions;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
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
        IReadOnlyList<AgentSession>? recent = null,
        ProjectResolution? here = null)
    {
        LauncherWindow? built = null;

        var session = TuiSession.Start(
            app =>
            {
                built = new LauncherWindow(
                    projects,
                    here,
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
    public void A_ctrl_chord_nothing_is_bound_to_offers_the_keys()
    {
        using var session = Launcher([Project("alpha", "Alpha")]);

        // The moment this exists for: somebody reaching for a key, guessing,
        // and missing. The guess is a real keystroke on every platform and
        // over SSH, which is what makes it a better signal than a timer —
        // holding a modifier down sends nothing at all from a remote terminal.
        session.Press(Key.B.WithCtrl);

        var screen = session.Screen;

        screen.Should().Contain("all commands", "the key list is what an unbound chord offers");
        screen.Should().Contain("not bound", "it says what was pressed, not just what exists");
    }

    [Fact]
    public void A_bound_chord_does_not_offer_the_keys()
    {
        var opened = false;

        using var session = Launcher(
            [Project("alpha", "Alpha")],
            onPalette: _ => opened = true);

        session.Press(Key.P.WithCtrl);

        opened.Should().BeTrue();

        // The offer is for a guess that missed. Putting the list up after a
        // chord that worked would interrupt somebody who knew exactly what
        // they were doing, which is the more common case by far.
        session.Screen.Should().NotContain("not bound");
    }

    [Fact]
    public void A_modifier_on_its_own_offers_nothing()
    {
        using var session = Launcher([Project("alpha", "Alpha")]);

        // Ctrl on its own is the start of a chord, not a missed one. Offering
        // the list the moment somebody touched the modifier would fire on the
        // way to every key they knew perfectly well.
        //
        // Holding it was tried as a second trigger and removed: a terminal
        // sends nothing while a modifier is held alone, and the Windows console
        // driver does not report it either, so the handler never once ran.
        session.Press(new Key(Terminal.Gui.Drivers.KeyCode.CtrlMask));

        session.Screen.Should().NotContain("not bound");
    }

    /// <summary>Redraws until the screen stops showing something, or patience runs out.</summary>
    private static bool Gone(TuiSession session, string text, int timeoutMilliseconds = 9000)
    {
        var deadline = System.Environment.TickCount64 + timeoutMilliseconds;

        do
        {
            session.Application.TimedEvents?.RunTimers();
            session.Application.RaiseIteration();
            session.Application.LayoutAndDraw();

            if (!session.Screen.Contains(text, StringComparison.Ordinal))
            {
                return true;
            }

            Thread.Sleep(25);
        }
        while (System.Environment.TickCount64 < deadline);

        return false;
    }

    [Fact]
    public void An_offer_nobody_asked_for_takes_itself_away()
    {
        using var session = Launcher([Project("alpha", "Alpha")]);

        session.Press(Key.B.WithCtrl);

        session.Screen.Should().Contain("not bound");

        // A mistyped chord should not cost more than the mistyped chord did.
        // Leaving the list sitting over the projects until it was dismissed
        // would make a slip into an interruption.
        Gone(session, "not bound")
            .Should().BeTrue("an overlay nobody asked for should take itself away");
    }

    [Fact]
    public void A_list_asked_for_deliberately_stays()
    {
        using var session = Launcher([Project("alpha", "Alpha")]);

        session.Press(Key.CursorDown);
        session.Press(new Key('?'));

        session.Screen.Should().Contain("all commands");

        // The timer belongs to the offer, not to the panel. Taking away a list
        // somebody opened to read would be the launcher overruling them, and
        // the two paths share enough code for that to be an easy mistake.
        Gone(session, "all commands", timeoutMilliseconds: 7000)
            .Should().BeFalse("a list opened on purpose stays until it is closed on purpose");
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
    public void A_project_with_nothing_in_the_way_carries_no_badge()
    {
        using var session = Launcher([Project("alpha", "Alpha")]);

        var screen = session.Screen;

        // Scanning a list of projects, the question is not "what is wrong with
        // this one" but "which one am I working on". Only a project that
        // cannot be started interrupts that, because only that changes the
        // answer. A badge on every row distinguishes no row from any other.
        screen.Should().Contain("Alpha");
        screen.Should().NotContain("[+ Ready]");
        screen.Should().NotContain("[! Attention]");
    }

    [Fact]
    public void A_project_nobody_has_looked_at_is_not_reported_as_needing_attention()
    {
        using var session = Launcher([
            Project("alpha", "Alpha"),
            Project("beta", "Beta"),
            Project("gamma", "Gamma"),
        ]);

        var screen = session.Screen;

        // The launcher reads a project's details only when it is selected. Ask
        // for the readiness of one it has never looked at and it handed back a
        // null overview — which means "it was read and there was nothing good
        // to say", not "it has not been read". So every project the cursor had
        // not touched wore a warning: fifteen of sixteen, on the machine where
        // this was found, all of them fine.
        screen.Should().Contain("Beta");
        screen.Should().Contain("Gamma");
        screen.Should().NotContain("Attention");
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

    [Fact]
    public void Buttons_are_drawn_with_characters_a_console_font_has()
    {
        using var session = Launcher([Project("alpha", "Alpha")]);

        var screen = session.Screen;

        // Terminal.Gui brackets every button in U+27E6 and U+27E7, the
        // mathematical white square brackets. Cascadia Mono has neither, and
        // Cascadia Mono is what a Windows console draws with unless somebody
        // has changed it, so every button in the launcher read as:
        //
        //     ⊡ Launch claude ⊡    ⊡ Resume ⊡    ⊡ Shell ⊡
        //
        // The text was right the whole time. A missing glyph is a decision the
        // font makes long after the character has left this program, which is
        // why it took a photograph of a real console to find.
        screen.Should().NotContain("⟦");
        screen.Should().NotContain("⟧");
        screen.Should().Contain("[ Launch");
    }

    [Fact]
    public void The_project_you_are_standing_in_stays_marked_once_its_details_arrive()
    {
        var here = Project("beta", "Beta");

        using var session = Launcher([Project("alpha", "Alpha"), here], here: here);

        // Waiting for the state to arrive is the point. The marker was passed
        // in once and used only for the first draw; every redraw after that —
        // and one happens per project as its details come back — rebuilt the
        // rows without it. So it was correct until the moment anything else
        // happened, which is to say it was never actually seen.
        var screen = session.ScreenShowing("Ready");

        screen.Should().Contain("▸ Beta");
    }

    /// <summary>
    /// Where the cursor is, read from the detail pane rather than from the
    /// list, because the pane names one project and the list names them all.
    /// </summary>
    private static void ShouldBeLookingAt(string screen, string slug) =>
        screen.Should().Contain($"/repos/{slug}");

    [Fact]
    public void Down_from_the_filter_moves_into_the_list()
    {
        using var session = Launcher([
            Project("alpha", "Alpha", "/repos/alpha"),
            Project("beta", "Beta", "/repos/beta"),
        ]);

        // The filter has the focus when the screen opens, so that typing
        // narrows the list without pressing anything first. Reaching the list
        // afterwards took a Tab, which is a keystroke nobody expects: an arrow
        // key next to a filtered list means "move down the list" in every
        // other tool that has one.
        session.Press(Key.CursorDown);

        ShouldBeLookingAt(session.Screen, "beta");
    }

    [Fact]
    public void J_and_K_move_the_selection_like_the_arrows()
    {
        using var session = Launcher([
            Project("alpha", "Alpha", "/repos/alpha"),
            Project("beta", "Beta", "/repos/beta"),
            Project("gamma", "Gamma", "/repos/gamma"),
        ]);

        session.Press(Key.CursorDown);
        session.Press(Key.J);

        ShouldBeLookingAt(session.Screen, "gamma");

        session.Press(Key.K);

        ShouldBeLookingAt(session.Screen, "beta");
    }

    [Fact]
    public void Letters_still_reach_the_filter_rather_than_moving_the_cursor()
    {
        using var session = Launcher([
            Project("alpha", "Alpha", "/repos/alpha"),
            Project("jamboree", "Jamboree", "/repos/jamboree"),
        ]);

        // j and k move the cursor in the list and nowhere else. Binding them
        // on the window would make the filter unable to spell "jamboree",
        // which is a worse bargain than the keystroke it saves.
        session.Type("jam");

        var screen = session.Screen;

        screen.Should().Contain("Jamboree");
        screen.Should().NotContain("Alpha");
    }

    [Fact]
    public void The_keys_are_one_key_away()
    {
        using var session = Launcher([Project("alpha", "Alpha")]);

        session.Press(Key.CursorDown);
        session.Press(new Key('?'));

        // Help behind a menu behind F9 is help nobody finds. ? is what every
        // other terminal application uses.
        //
        // Asserted on a line only the key list carries. "Ctrl+P" was the first
        // choice and proved nothing: the status bar along the bottom already
        // says "Ctrl+P commands", so the test passed before the key existed.
        session.Screen.Should().Contain("launch the selected project");
    }

    [Fact]
    public void Settings_has_a_key_of_its_own()
    {
        using var session = Launcher([Project("alpha", "Alpha")]);

        session.Press(Key.F2);

        Window.Intent.Should().NotBeNull();
        Window.Intent!.Action.Should().Be(LauncherAction.Settings);
    }

    [Fact]
    public void The_menu_key_it_advertises_is_the_menu_key_it_has()
    {
        using var session = Launcher([Project("alpha", "Alpha")]);

        var screen = session.Screen;

        // It said F9 and F9 did nothing. Terminal.Gui used F9 in version 1 and
        // uses F10 in version 2, so the status line and the help panel had both
        // been naming a dead key for as long as the launcher had been on
        // version 2 — and agreed with each other the whole time, which is why
        // nothing caught it. Pressing it in a real console was what caught it.
        screen.Should().Contain(
            $"{MenuBar.DefaultKey} menu",
            "the key named on screen has to be the one the menu bar answers to");

        screen.Should().NotContain("F9 menu");
    }

    [Fact]
    public void Enter_in_the_filter_opens_what_the_filter_narrowed_to()
    {
        using var session = Launcher([
            Project("alpha", "Alpha", "/repos/alpha"),
            Project("beta", "Beta", "/repos/beta"),
        ]);

        // Reported from use: opening a project did not work and had to be
        // tried again. The filter is where the cursor starts, so narrowing the
        // list and pressing Enter is the shortest path anybody would take —
        // and Enter was handled by the list alone, so from the filter it did
        // nothing whatever. No message, no launch. Going back and arrowing
        // into the list first worked, which is what made it look intermittent.
        session.Type("bet");
        session.Press(Key.Enter);

        Window.Intent.Should().NotBeNull("Enter in the filter has to open something");
        Window.Intent!.Action.Should().Be(LauncherAction.Launch);
        Window.Intent.Project!.Entry.Slug.Should().Be("beta");
    }

    [Fact]
    public void A_project_that_cannot_be_opened_says_so_rather_than_nothing()
    {
        using var session = Launcher([Project("gone", "Gone", path: null)]);

        session.Press(Key.Enter);

        // The old code was a single pattern match, and a project that is not
        // on this machine simply failed it: no launch, no message, nothing to
        // tell it apart from a keystroke that never arrived. Silence is the
        // one answer a person cannot act on.
        Window.Intent.Should().BeNull("there is nothing here to open");

        // Asserted on the part only the refusal adds. "not on this machine"
        // was the first choice and proved nothing: the detail pane says that
        // already, as the reason the project is Blocked, so the test passed
        // with the message deleted.
        session.Screen.Should().Contain("Clone onto this machine");
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
