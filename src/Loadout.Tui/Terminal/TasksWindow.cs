using Loadout.Core.Tasks;
using Loadout.Models.Tasks;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>A task somebody chose to start, and whether they wanted the launch sheet first.</summary>
internal sealed record TaskStart(TaskItem Task, bool WithOptions);

/// <summary>
/// A project's tasks, filtered and sorted with the shared query, with a key to
/// start a session on one.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is started from here. Like the ideas screen, it closes with what
/// was asked for and the launcher runs it as the launch command, so a start
/// from this screen is the one a terminal types.
/// </para>
/// <para>
/// The keys are bound on the list, never on Activate or Accept, which a click
/// raises: clicking a row chooses it, and must not start a session.
/// </para>
/// </remarks>
internal sealed class TasksWindow : Window
{
    /// <summary>The orders 's' moves through, the default first.</summary>
    internal static readonly string[] Sorts = ["", "-updated", "id", "title"];

    private readonly IApplication _application;

    /// <summary>The list, for the launcher to read the query back from and tests to drive.</summary>
    internal FilterableListView<TaskItem> Tasks { get; }

    /// <summary>What to start once the screen has closed, or null when nothing was asked for.</summary>
    internal TaskStart? Chosen { get; private set; }

    /// <param name="where">Whose tasks these are, for the title.</param>
    /// <param name="tasks">Every task on the list, done and dropped included: the query decides.</param>
    /// <param name="query">The query it opens with: the saved one, or the default.</param>
    /// <param name="application">The running toolkit.</param>
    /// <param name="time">The clock ages are measured against.</param>
    /// <param name="activity">What was started on each task, for sorting and filtering on runs and launches.</param>
    /// <param name="project">The project's slug, for filtering on project.</param>
    internal TasksWindow(string where, IReadOnlyList<TaskItem> tasks, string query, IApplication application, TimeProvider? time = null, TaskActivity? activity = null, string? project = null)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(application);

        _application = application;

        Title = $"Tasks - {where}";
        BorderStyle = LauncherTheme.Lines;

        var frame = new FrameView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            Title = "Filter with / (state:open sort:-updated)",
            BorderStyle = LauncherTheme.Inner,
        };

        Tasks = new FilterableListView<TaskItem>(TaskFields.For(activity ?? TaskActivity.None, project), Row, query, time)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        frame.Add(Tasks);

        var footer = new KeyLine(
        [
            ("Enter", "start"),
            ("o", "start with options"),
            ("/", "filter"),
            ("s", "sort"),
            ("a", "all"),
            ("r", "reset"),
            ("Esc", "close"),
        ])
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
        };

        Add(frame, footer);

        this.Bind(Key.Esc, Command.Quit);
        AddCommand(Command.Quit, () => { _application.RequestStop(this); return true; });

        var list = Tasks.List;
        list.OnKey(Key.Enter, Command.Open, () => Start(withOptions: false));
        list.OnKey(Key.O, Command.Edit, () => Start(withOptions: true));
        list.OnKey(Key.S, Command.Save, () => { Tasks.SortBy(NextSort(Tasks.Sort)); return true; });
        list.OnKey(Key.A, Command.Copy, () => { Tasks.Apply(string.Empty); return true; });
        list.OnKey(Key.R, Command.Undo, () => { Tasks.Apply(TaskFields.DefaultQuery); return true; });

        Tasks.SetItems(tasks);
        list.SetFocus();
    }

    /// <summary>The order after this one in <see cref="Sorts"/>, round to the default.</summary>
    internal static string NextSort(string sort)
    {
        var at = Array.IndexOf(Sorts, sort);

        return Sorts[(at + 1) % Sorts.Length];
    }

    /// <summary>Closes with the task to start. A dropped task, which the launch refuses, does nothing.</summary>
    private bool Start(bool withOptions)
    {
        if (Tasks.Selected is { State: not TaskState.Dropped } task)
        {
            Chosen = new TaskStart(task, withOptions);
            _application.RequestStop(this);
        }

        return true;
    }

    private static string Row(TaskItem task) =>
        $"{Shorten(task.Id, 26),-26} {task.State.ToString().ToLowerInvariant(),-8} "
        + $"{task.DeclaredUtc:yyyy-MM-dd}  {task.Title}";

    /// <summary>Three dots rather than the one character, for profiles that ask for ASCII only.</summary>
    private static string Shorten(string text, int width) =>
        text.Length <= width ? text : text[..Math.Max(1, width - 3)] + "...";
}
