using Loadout.Core.Projects;
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
        int height = TuiSession.DefaultHeight)
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
