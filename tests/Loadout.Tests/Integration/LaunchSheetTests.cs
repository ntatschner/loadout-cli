using Loadout.Models.Instructions;
using Loadout.Models.Projects;
using Loadout.Tui.Terminal;
using FluentAssertions;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The launch sheet: every path that starts a session comes through it, so
/// what it collects and what it shows are the launch.
/// <para>
/// Driven with keystrokes through the headless driver, and the preview read
/// off the screen, because the fault this sheet replaces was a capability
/// that existed on the command line and could not be reached from a screen —
/// which no test of the command line would ever have noticed.
/// </para>
/// </summary>
public sealed class LaunchSheetTests
{
    private static ProjectResolution Project(string agent = "claude") =>
        new(new ProjectRegistryEntry { Slug = "alpha", Name = "Alpha", DefaultAgent = agent },
            "/repos/alpha", null, 0, false);

    private static SpecialistSelection Selection(
        string id,
        SpecialistKind kind,
        string title,
        string reason,
        int bytes = 400) =>
        new(
            new SpecialistDocument(id, kind, title, "summary", SpecialistActivation.None, "body", bytes),
            SpecialistTrigger.RepositoryEvidence,
            reason,
            60);

    private static EffectiveInstructions Resolved(params SpecialistSelection[] selected) =>
        new(
            "implement",
            selected,
            [],
            [],
            new InstructionContextBudget(
                selected.Sum(s => s.Specialist.Bytes),
                selected.Sum(s => s.Specialist.EstimatedTokens),
                12_000,
                80));

    /// <summary>Sources that answer at once, so the screen can be read without waiting.</summary>
    private static LaunchSheetSources Sources(
        Func<LaunchPreviewRequest, EffectiveInstructions?>? preview = null,
        LaunchChoices? choices = null) =>
        new(
            (_, _, _) => Task.FromResult(choices ?? LaunchChoices.None),
            (request, _) => Task.FromResult(preview?.Invoke(request)));

    private LaunchOptionsDialog Built = null!;

    private TuiSession Sheet(
        ProjectResolution? project = null,
        IReadOnlyList<string>? agents = null,
        LaunchSheetSources? sources = null)
    {
        return TuiSession.Start(app =>
        {
            Built = new LaunchOptionsDialog(
                project ?? Project(),
                agents ?? ["claude"],
                app,
                sources,
                previewDelay: TimeSpan.Zero);

            return Built;
        });
    }

    private static ListView Picker(View root, string containing) =>
        AllViews(root).OfType<ListView>().Single(list =>
            Enumerable.Range(0, list.Source?.Count ?? 0)
                .Any(i => (list.Source!.ToList()[i]?.ToString() ?? string.Empty)
                    .Contains(containing, StringComparison.Ordinal)));

    private static IEnumerable<View> AllViews(View root)
    {
        foreach (var child in root.SubViews)
        {
            yield return child;

            foreach (var grandchild in AllViews(child))
            {
                yield return grandchild;
            }
        }
    }

    private static Button ButtonNamed(View root, string containing) =>
        AllViews(root).OfType<Button>()
            .Single(b => (b.Text ?? string.Empty).Contains(containing, StringComparison.Ordinal));

    private static void Press(View view, Key key)
    {
        view.SetFocus();
        view.NewKeyDownEvent(key);
    }

    [Fact]
    public void Enter_twice_launches_with_the_defaults()
    {
        using var session = Sheet(Project(agent: "codex"), agents: ["claude", "codex"]);

        // Focus starts in the task field, and Enter there is the launch: the
        // fast path costs one keystroke more than the old Enter-on-a-project.
        session.Press(Key.Enter);

        Built.Chosen.Should().NotBeNull();
        Built.Chosen!.Agent.Should().Be("codex", "the project's own agent is offered first");
        Built.Chosen.Task.Should().BeNull();
        Built.Chosen.Mode.Should().BeNull();
        Built.Chosen.Profile.Should().BeNull();
        Built.Chosen.Worktree.Should().BeNull();
    }

    [Fact]
    public void Every_installed_agent_is_offered_and_another_can_be_chosen()
    {
        using var session = Sheet(agents: ["claude", "codex"]);

        session.Screen.Should().Contain("codex", "an agent that is installed must be on the sheet");

        // Second in the list, because the project's default goes first.
        Picker(Built, "codex").SelectedItem = 1;

        Press(ButtonNamed(Built, "Launch"), Key.Enter);

        // This is the capability the rewrite lost: every launch from the
        // screen started the project's default agent, whatever was installed.
        Built.Chosen!.Agent.Should().Be("codex");
    }

    [Fact]
    public void Task_mode_and_flags_reach_the_launch()
    {
        using var session = Sheet();

        session.Type("why is this query so slow");

        // Index zero is "let the task decide", so this picks investigate.
        Picker(Built, "investigate").SelectedItem = 3;

        var offline = AllViews(Built).OfType<CheckBox>()
            .Single(c => (c.Text ?? string.Empty).Contains("offline", StringComparison.OrdinalIgnoreCase));
        offline.Value = CheckState.Checked;

        var handoff = AllViews(Built).OfType<CheckBox>()
            .Single(c => (c.Text ?? string.Empty).Contains("handoff", StringComparison.OrdinalIgnoreCase));
        handoff.Value = CheckState.Checked;

        Press(ButtonNamed(Built, "Launch"), Key.Enter);

        Built.Chosen.Should().NotBeNull();
        Built.Chosen!.Task.Should().Be("why is this query so slow");
        Built.Chosen.Mode.Should().Be("investigate");
        Built.Chosen.Offline.Should().BeTrue();
        Built.Chosen.NoSync.Should().BeFalse();
        Built.Chosen.IncludeHandoff.Should().BeTrue();
    }

    [Fact]
    public void An_empty_task_is_no_task_rather_than_an_empty_one()
    {
        using var session = Sheet();

        session.Type("   ");
        Press(ButtonNamed(Built, "Launch"), Key.Enter);

        // An empty string would be a task, and the resolver would go looking
        // for specialists matching nothing.
        Built.Chosen.Should().NotBeNull();
        Built.Chosen!.Task.Should().BeNull();
        Built.Chosen.Mode.Should().BeNull("index zero means no mode was chosen");
    }

    [Fact]
    public void Dismissing_the_sheet_launches_nothing()
    {
        using var session = Sheet();

        Press(ButtonNamed(Built, "ance"), Key.Enter);

        // Starting a session with the defaults because the sheet was closed
        // would be starting one nobody asked for.
        Built.Chosen.Should().BeNull();
    }

    [Fact]
    public void Profiles_and_working_trees_the_project_has_are_offered()
    {
        var choices = new LaunchChoices(
            [new LaunchChoice("default", null), new LaunchChoice("database  (schema work)", "database")],
            [new LaunchChoice("main  (main working tree)", null), new LaunchChoice("feature-x", "feature-x")]);

        using var session = Sheet(sources: Sources(choices: choices));

        session.Screen.Should().Contain("database");
        session.Screen.Should().Contain("feature-x");

        Picker(Built, "database").SelectedItem = 1;
        Picker(Built, "feature-x").SelectedItem = 1;

        Press(ButtonNamed(Built, "Launch"), Key.Enter);

        // Both by the name the command line takes, so the sheet and
        // --profile / --worktree cannot mean different things.
        Built.Chosen!.Profile.Should().Be("database");
        Built.Chosen.Worktree.Should().Be("feature-x");
    }

    [Fact]
    public void The_main_working_tree_and_the_default_profile_are_no_option_at_all()
    {
        var choices = new LaunchChoices(
            [new LaunchChoice("default", null), new LaunchChoice("database", "database")],
            [new LaunchChoice("main  (main working tree)", null), new LaunchChoice("feature-x", "feature-x")]);

        using var session = Sheet(sources: Sources(choices: choices));

        Press(ButtonNamed(Built, "Launch"), Key.Enter);

        // The launch spells the default as the absence of the option. A sheet
        // that sent "default" or "main" would fail the launch with "no profile
        // named default".
        Built.Chosen!.Profile.Should().BeNull();
        Built.Chosen.Worktree.Should().BeNull();
    }

    [Fact]
    public void The_sheet_shows_what_the_session_would_load_before_it_starts()
    {
        var sources = Sources(preview: _ => Resolved(
            Selection("foundation.change-safety", SpecialistKind.Foundation, "Change safety", "always applies"),
            Selection("language.csharp", SpecialistKind.Language, "C#", "459 .cs files")));

        using var session = Sheet(sources: sources);

        // The point of the sheet. Before it, the only way to see which
        // specialists a task would load was a command that closed the
        // launcher and printed to the terminal.
        session.Screen.Should().Contain("This session would load");
        session.Screen.Should().Contain("C#");
        session.Screen.Should().Contain("459 .cs files");
        session.Screen.Should().Contain("Change safety");
    }

    [Fact]
    public void Typing_a_task_re_resolves_the_preview_with_it()
    {
        var asked = new List<LaunchPreviewRequest>();

        var sources = Sources(preview: request =>
        {
            asked.Add(request);

            return request.Task is { Length: > 0 }
                ? Resolved(Selection("database.postgresql", SpecialistKind.Database, "PostgreSQL", "\"query\" in the task"))
                : Resolved();
        });

        using var session = Sheet(Project(agent: "claude"), sources: sources);

        session.Type("slow query");

        // Re-asked with what was typed, and the answer on screen. The delay
        // is zero here, so the last keystroke is the last request.
        asked.Last().Task.Should().Be("slow query");
        asked.Last().Agent.Should().Be("claude");
        session.Screen.Should().Contain("PostgreSQL");
    }

    [Fact]
    public void Choosing_a_mode_re_resolves_the_preview_with_it()
    {
        var asked = new List<LaunchPreviewRequest>();

        using var session = Sheet(sources: Sources(preview: request =>
        {
            asked.Add(request);
            return Resolved();
        }));

        Picker(Built, "review").SelectedItem = 4;
        session.Press(Key.Tab);

        asked.Last().Mode.Should().Be("review");
    }

    [Fact]
    public void A_preview_that_fails_says_so_rather_than_showing_nothing()
    {
        var sources = new LaunchSheetSources(
            (_, _, _) => Task.FromResult(LaunchChoices.None),
            (_, _) => Task.FromException<EffectiveInstructions?>(
                new InvalidOperationException("No specialist named 'x'.")));

        using var session = Sheet(sources: sources);

        // A faulted task is not completed successfully, so the answer arrives
        // through the main loop the way a slow one would, and the loop has to
        // run for it to be drawn.
        var deadline = Environment.TickCount64 + 5000;

        while (!session.Screen.Contains("could not work out", StringComparison.Ordinal)
            && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(10);
            session.Pump();
        }

        session.Screen.Should().Contain("No specialist named 'x'.");
    }

    [Fact]
    public void The_preview_reads_like_the_explain_command()
    {
        var lines = LaunchOptionsDialog.Describe(Resolved(
            Selection("foundation.change-safety", SpecialistKind.Foundation, "Change safety", "always applies"),
            Selection("foundation.verification", SpecialistKind.Foundation, "Verification", "always applies"),
            Selection("mode.implement", SpecialistKind.Mode, "Implement", "implement mode", bytes: 800),
            Selection("language.csharp", SpecialistKind.Language, "C#", "459 .cs files", bytes: 4000)));

        // Foundation on one line: it loads whatever the task, and four lines
        // saying so would be the first thing anybody learned to skip.
        lines[0].Should().StartWith("foundation").And.Contain("Change safety, Verification");
        lines.Should().Contain(l => l.StartsWith("mode", StringComparison.Ordinal) && l.Contains("implement mode"));
        lines.Should().Contain(l => l.StartsWith("language", StringComparison.Ordinal) && l.Contains("459 .cs files"));
        lines.Last().Should().Contain("tokens").And.Contain("of 12,000");
    }

    [Fact]
    public void The_sheet_fits_an_ordinary_terminal()
    {
        var sources = Sources(preview: _ => Resolved(
            Selection("language.csharp", SpecialistKind.Language, "C#", "459 .cs files")));

        using var session = TuiSession.Start(
            app => new LaunchOptionsDialog(Project(), ["claude", "codex"], app, sources, TimeSpan.Zero),
            width: 80,
            height: 24);

        var screen = session.Screen;

        // 80x24 is the floor the launcher holds itself to. The question, the
        // pickers, the preview and the buttons all have to be on it at once.
        screen.Should().Contain("What are you about to do?");
        screen.Should().Contain("Worktree");
        screen.Should().Contain("C#");
        screen.Should().Contain("Launch");
    }
}
