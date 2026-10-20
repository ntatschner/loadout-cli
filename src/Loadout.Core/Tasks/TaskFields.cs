using Loadout.Core.Lists;
using Loadout.Models.Tasks;

namespace Loadout.Core.Tasks;

/// <summary>What a task list can be filtered and sorted on, on every surface.</summary>
public static class TaskFields
{
    /// <summary>The states in the order a list shows them: what to start, what is under way, what waits, then what is closed.</summary>
    public static readonly IReadOnlyList<string> States = ["open", "doing", "blocked", "done", "dropped"];

    /// <summary>
    /// Done and dropped work is hidden unless asked for, as a filter that can
    /// be seen and cleared rather than a rule nobody can see.
    /// </summary>
    public const string DefaultQuery = "-state:done,dropped";

    /// <summary>The fields that need <see cref="TaskActivity"/>, which a list reads only when a query names one.</summary>
    public static readonly IReadOnlyList<string> Counted = ["runs", "launches"];

    /// <summary>The fields with nothing counted, so runs and launches read zero: for checking a query, not applying one that counts.</summary>
    public static readonly ListFields<TaskItem> All = For(TaskActivity.None);

    /// <summary>What the project field reads for the workspace-wide list, which has no slug.</summary>
    public const string WorkspaceWide = "global";

    /// <summary>The fields for one list, with runs and launches read from what was counted.</summary>
    /// <param name="activity">What was started on the list's tasks.</param>
    /// <param name="project">The list's project, by slug, or null for the workspace-wide list.</param>
    public static ListFields<TaskItem> For(TaskActivity activity, string? project = null)
    {
        ArgumentNullException.ThrowIfNull(activity);

        return For(task => activity.RunsOf(task.Id), task => activity.LaunchesOf(task.Id), _ => project);
    }

    /// <summary>
    /// The fields, for tasks that may come from more than one list: each
    /// task's counts and project read by what is given.
    /// </summary>
    /// <param name="runs">Team runs started for a task.</param>
    /// <param name="launches">Sessions launched on a task.</param>
    /// <param name="project">The list a task is on, by slug, or null for the workspace-wide list.</param>
    /// <remarks>
    /// Counts are read through the task rather than by its id alone, because
    /// two projects can each have a task with the same id, and a run on one
    /// is no run on the other.
    /// </remarks>
    public static ListFields<TaskItem> For(
        Func<TaskItem, int> runs,
        Func<TaskItem, int> launches,
        Func<TaskItem, string?> project)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(launches);
        ArgumentNullException.ThrowIfNull(project);

        return new(
            "tasks",
            [
                ListField<TaskItem>.Text("id", "the task's id", task => task.Id),
                ListField<TaskItem>.Text("title", "what the task is", task => task.Title),
                ListField<TaskItem>.Choice("state", "where it stands", States, task => task.State.ToString().ToLowerInvariant()),
                ListField<TaskItem>.Choice("kind", "work, or an idea", ["task", "idea"], task => task.Kind.ToString().ToLowerInvariant()),
                ListField<TaskItem>.Text("project", $"the project whose list it is on, or {WorkspaceWide} for the workspace-wide one", task => project(task) ?? WorkspaceWide),
                ListField<TaskItem>.Text("by", "who declared it last", task => task.DeclaredBy),
                ListField<TaskItem>.Date("updated", "when it was last declared", task => task.DeclaredUtc),
                ListField<TaskItem>.Text("note", "the note beside it", task => task.Note),
                ListField<TaskItem>.Text("detail", "the full description", task => task.Detail),
                ListField<TaskItem>.Number("runs", "team runs started for it", task => runs(task)),
                ListField<TaskItem>.Number("launches", "sessions launched on it", task => launches(task)),
            ],
            [new ListSort("state", false), new ListSort("project", false), new ListSort("id", false)],
            DefaultQuery);
    }

    /// <summary>Whether the query filters or sorts on something counted, so the counts have to be read.</summary>
    public static bool Counts(ListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return query.Terms.Any(term => Counted.Contains(term.Field, StringComparer.OrdinalIgnoreCase))
            || query.Sorts.Any(sort => Counted.Contains(sort.Field, StringComparer.OrdinalIgnoreCase));
    }
}
