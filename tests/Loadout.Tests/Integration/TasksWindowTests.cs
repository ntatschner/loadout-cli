using System.Drawing;
using FluentAssertions;
using Loadout.Core.Tasks;
using Loadout.Models.Tasks;
using Loadout.Tui.Terminal;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The tasks screen: the shared query over the list, and the keys that start
/// a session on a task, at once or through the launch sheet.
/// </summary>
public sealed class TasksWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static readonly TaskItem[] Recorded =
    [
        Task("b-login", TaskState.Open, "Fix the login page", 1),
        Task("a-release", TaskState.Doing, "Ship the release", 3),
        Task("c-done", TaskState.Done, "Login copy", 20),
        Task("d-dropped", TaskState.Dropped, "Old idea", 40),
    ];

    [Fact]
    public void It_opens_with_the_query_it_was_given_and_says_how_much_that_hides()
    {
        var (window, app) = Open(TaskFields.DefaultQuery);

        using (app)
        using (window)
        {
            Ids(window).Should().Equal("b-login", "a-release");
            Status(window).Should().Contain("-state:done,dropped").And.Contain("showing 2 of 4");
        }
    }

    [Fact]
    public void A_saved_query_is_what_it_opens_with()
    {
        var (window, app) = Open("login");

        using (app)
        using (window)
        {
            Ids(window).Should().Equal("b-login", "c-done");
        }
    }

    [Fact]
    public void Enter_starts_the_task_and_o_asks_the_sheet_first()
    {
        foreach (var (key, options) in new[] { (Key.Enter, false), (Key.O, true) })
        {
            var (window, app) = Open(TaskFields.DefaultQuery);

            using (app)
            using (window)
            {
                List(window).SelectedItem = 1;
                List(window).NewKeyDownEvent(key);

                window.Chosen.Should().Be(new TaskStart(Recorded[1], options));
            }
        }
    }

    [Fact]
    public void A_dropped_task_cannot_be_started_but_a_done_one_can()
    {
        var (window, app) = Open("state:dropped,done sort:id");

        using (app)
        using (window)
        {
            Ids(window).Should().Equal("c-done", "d-dropped");

            List(window).SelectedItem = 1;
            List(window).NewKeyDownEvent(Key.Enter);
            window.Chosen.Should().BeNull("the launch refuses a dropped task, so the key does nothing");

            List(window).SelectedItem = 0;
            List(window).NewKeyDownEvent(Key.Enter);
            window.Chosen!.Task.Id.Should().Be("c-done");
        }
    }

    [Fact]
    public void All_reset_and_sort_change_the_query_in_force()
    {
        var (window, app) = Open(TaskFields.DefaultQuery);

        using (app)
        using (window)
        {
            List(window).NewKeyDownEvent(Key.A);
            Ids(window).Should().HaveCount(4);
            window.Tasks.Query.Should().BeEmpty();

            List(window).NewKeyDownEvent(Key.S);
            window.Tasks.Query.Should().Be("sort:-updated");
            Ids(window).Should().Equal("b-login", "a-release", "c-done", "d-dropped");

            List(window).NewKeyDownEvent(Key.S);
            Ids(window).Should().Equal("a-release", "b-login", "c-done", "d-dropped");

            List(window).NewKeyDownEvent(Key.R);
            window.Tasks.Query.Should().Be(TaskFields.DefaultQuery);
        }
    }

    [Fact]
    public void A_query_the_list_cannot_take_keeps_the_list_and_says_why_in_the_shared_words()
    {
        var (window, app) = Open(TaskFields.DefaultQuery);

        using (app)
        using (window)
        {
            window.Tasks.Apply("colour:red");

            Ids(window).Should().Equal("b-login", "a-release");
            window.Tasks.Query.Should().Be(TaskFields.DefaultQuery);
            Status(window).Should().Contain("'colour' is not something tasks can be filtered on");
        }
    }

    [Fact]
    public void Slash_moves_to_the_filter_and_Enter_there_applies_it()
    {
        var (window, app) = Open(TaskFields.DefaultQuery);

        using (app)
        using (window)
        {
            List(window).NewKeyDownEvent(new Key('/'));
            var filter = Filter(window);
            filter.HasFocus.Should().BeTrue();

            filter.Text = "state:doing";
            filter.NewKeyDownEvent(Key.Enter);

            Ids(window).Should().Equal("a-release");
            List(window).HasFocus.Should().BeTrue();
        }
    }

    private static (TasksWindow Window, IApplication App) Open(string query)
    {
        var app = Application.Create();
        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, 120, 30);

        var window = new TasksWindow("website", Recorded, query, app, new FixedTime(Now));

        app.Begin(window);
        app.LayoutAndDraw();

        return (window, app);
    }

    private static ListView List(TasksWindow window) => window.Tasks.List;

    private static TextField Filter(TasksWindow window) =>
        window.Tasks.SubViews.OfType<TextField>().Single(v => v.Id == "list-filter");

    private static string Status(TasksWindow window) =>
        window.Tasks.SubViews.OfType<Label>().Single(v => v.Id == "list-status").Text ?? string.Empty;

    private static List<string> Ids(TasksWindow window) => [.. window.Tasks.Shown.Select(task => task.Id)];

    private static TaskItem Task(string id, TaskState state, string title, int daysAgo) => new()
    {
        Id = id,
        State = state,
        Title = title,
        DeclaredBy = "nigel",
        DeclaredUtc = Now.AddDays(-daysAgo),
    };

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
