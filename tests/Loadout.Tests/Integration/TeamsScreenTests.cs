using System.Drawing;
using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Core.Teams;
using Loadout.Models.Configuration;
using Loadout.Tui;
using Loadout.Tui.Terminal;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The launcher's view of what the team runs are doing.
/// </summary>
/// <remarks>
/// <para>
/// The screen implements nothing. Every key hands back the command somebody
/// would otherwise have typed, so what these check is that the right command
/// comes back and that the right run's identifier is in it — the failure worth
/// catching being a key that reads a run and names a different one.
/// </para>
/// <para>
/// The other one is the list moving under somebody's hands. A run that starts
/// while this is open arrives at the top and pushes everything down, and a
/// screen keeping its cursor on the index would then be showing a different
/// run than the one being read.
/// </para>
/// </remarks>
public sealed class TeamsScreenTests
{
    private const int Width = 140;
    private const int Height = 40;

    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_team_runs_screen_can_be_built_and_drawn()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        using var window = new TeamsWindow(Runs(), Read(Runs()), live: true, app);

        app.Begin(window);
        app.LayoutAndDraw();

        (app.Driver?.ToString() ?? string.Empty).Should().NotBeEmpty();
    }

    [Fact]
    public void A_machine_that_has_run_nothing_is_told_how_to_start_one()
    {
        using var window = Built([]);

        // An empty screen saying nothing is indistinguishable from one that
        // failed to read.
        Rows(window).Should().ContainSingle()
            .Which.Should().Contain("loadout team run", Exactly.Once());
    }

    [Fact]
    public void Where_a_run_got_to_is_written_as_a_word()
    {
        var rows = Rows(Built(Runs()));

        // Not only as a colour, for the reason the dashboard has it: a state
        // somebody has to see the colour of is a state some people cannot read.
        rows.Should().Contain(line => line.Contains("running", StringComparison.Ordinal));
        rows.Should().Contain(line => line.Contains("done", StringComparison.Ordinal));
    }

    [Fact]
    public void The_screen_asks_for_nothing_until_a_key_is_pressed()
    {
        // Built and drawn is not a request. A screen that arrived with a
        // command already chosen would run it the moment it closed.
        Built(Runs()).Chosen.Should().BeNull();
    }

    [Theory]
    [InlineData(0, "Enter", "team log 20260916-1200-aaaa")]
    [InlineData(0, "s", "team status 20260916-1200-aaaa")]
    [InlineData(1, "Enter", "team log 20260916-1100-bbbb")]
    [InlineData(1, "s", "team status 20260916-1100-bbbb")]
    [InlineData(0, "d", "team dashboard")]
    public void A_key_hands_back_the_command_for_the_run_the_cursor_is_on(
        int row, string key, string expected)
    {
        using var window = Built(Runs());

        List(window).SelectedItem = row;

        Press(window, key);

        window.Chosen.Should().Be(expected);
    }

    [Fact]
    public void A_key_with_no_runs_at_all_asks_for_nothing()
    {
        using var window = Built([]);

        Press(window, "Enter");

        // A mis-aim, not a mistake worth a dialog - and certainly not a
        // command naming a run that does not exist.
        window.Chosen.Should().BeNull();
    }

    [Fact]
    public void A_run_starting_while_the_screen_is_open_does_not_move_the_cursor_onto_another()
    {
        using var window = Built(Runs());

        List(window).SelectedItem = 1;

        // Newest first, so a new run arrives at the top and pushes the one
        // being read from index 1 to index 2.
        window.Show([Run("20260916-1300-cccc", "docs-crew", finished: null), .. Runs()]);

        List(window).SelectedItem.Should().Be(2);

        Press(window, "s");

        window.Chosen.Should().Be("team status 20260916-1100-bbbb");
    }

    [Fact]
    public void A_run_that_ends_while_the_screen_is_open_stops_saying_it_is_going()
    {
        using var window = Built(Runs());

        Rows(window)[0].Should().Contain("running");

        window.Show([Run("20260916-1200-aaaa", "bug-hunt", finished: Noon.AddMinutes(9), ended: "done")]);

        Rows(window)[0].Should().Contain("done");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(AccessibilityPresets.None, true)]
    [InlineData(AccessibilityPresets.ScreenReader, false)]
    [InlineData(AccessibilityPresets.LowVision, false)]
    [InlineData(AccessibilityPresets.ColourBlind, true)]
    public void A_screen_refreshes_itself_only_where_that_was_not_refused(string? preset, bool expected)
    {
        // Redraw and motion both answer this, because both are ways of asking
        // for the same thing: low vision asks for reduced motion and nothing
        // about redraws, and a screen repainting every two seconds is motion.
        var profile = preset is null
            ? ReadingProfile.None
            : new ReadingProfile(AccessibilityProfile.Resolve(new AccessibilitySettings { Preset = preset }));

        profile.MayRefreshItself.Should().Be(expected);
    }

    private static IReadOnlyList<RunSummary> Runs() =>
    [
        Run("20260916-1200-aaaa", "bug-hunt", finished: null),
        Run("20260916-1100-bbbb", "iterating-project", finished: Noon.AddMinutes(-20), ended: "done"),
    ];

    private static RunSummary Run(
        string id, string team, DateTimeOffset? finished, string? ended = null) =>
        new(
            id,
            Directory: Path.Combine("state", "teams", "runs", id),
            Team: team,
            Goal: "make the thing work",
            Autonomy: "supervised",
            Started: Noon.AddMinutes(-30),
            Finished: finished,
            Ended: ended,
            CostUsd: 0.12m,
            Rounds: 2,
            Nodes:
            [
                new RunNode("lead", "role.project-lead", "reported", 3, 0.08m, Noon, Started: Noon.AddMinutes(-30)),
                new RunNode("implementer-1", "role.implementer", "running", 1, 0.04m, Noon,
                    Started: Noon.AddMinutes(-4), Doing: "Read src/Program.cs"),
            ],
            Merged: [],
            Branches: []);

    private static Func<CancellationToken, Task<IReadOnlyList<RunSummary>>> Read(
        IReadOnlyList<RunSummary> runs) =>
        _ => Task.FromResult(runs);

    /// <summary>
    /// Builds and draws the screen, and hands it back so its state can be read.
    /// </summary>
    /// <remarks>
    /// Not live. A timer firing during a test would read again while the test
    /// was asserting what the first read drew, and the failure would arrive
    /// somewhere else entirely.
    /// </remarks>
    private static TeamsWindow Built(IReadOnlyList<RunSummary> runs)
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, Width, Height);

        var window = new TeamsWindow(runs, Read(runs), live: false, app);

        app.Begin(window);
        app.LayoutAndDraw();

        return window;
    }

    /// <summary>Presses a key the way the screen would receive it.</summary>
    private static void Press(TeamsWindow window, string key)
    {
        var pressed = key switch
        {
            "Enter" => Key.Enter,
            "s" => Key.S,
            "d" => Key.D,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Not a key this screen binds."),
        };

        window.KeyBindings.TryGet(pressed, out var binding)
            .Should().BeTrue($"'{key}' has to be bound on the team runs screen");

        foreach (var command in binding.Commands)
        {
            window.InvokeCommand(command);
        }
    }

    /// <summary>The rows the screen drew, found by the list's name rather than its place.</summary>
    private static IReadOnlyList<string> Rows(TeamsWindow window)
    {
        var list = List(window);

        return [.. Enumerable.Range(0, list.Source!.Count)
            .Select(i => list.Source.ToList()[i]?.ToString() ?? string.Empty)];
    }

    /// <summary>
    /// The run list, by name. Finding a view by where it sits picks the wrong
    /// one as soon as anything above it changes height, and reads as a fresh
    /// bug.
    /// </summary>
    private static ListView List(TeamsWindow window) =>
        Named<ListView>(window, "teams-runs")
            ?? throw new InvalidOperationException("The team runs screen has no run list.");

    private static T? Named<T>(View root, string id) where T : View
    {
        foreach (var child in root.SubViews)
        {
            if (child is T match && string.Equals(child.Id, id, StringComparison.Ordinal))
            {
                return match;
            }

            if (Named<T>(child, id) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
