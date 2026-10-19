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

    /// <summary>The fields, with runs and launches read from what was counted.</summary>
    public static ListFields<TaskItem> For(TaskActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        return new(
            "tasks",
            [
                ListField<TaskItem>.Text("id", "the task's id", task => task.Id),
                ListField<TaskItem>.Text("title", "what the task is", task => task.Title),
                ListField<TaskItem>.Choice("state", "where it stands", States, task => task.State.ToString().ToLowerInvariant()),
                ListField<TaskItem>.Choice("kind", "work, or an idea", ["task", "idea"], task => task.Kind.ToString().ToLowerInvariant()),
                ListField<TaskItem>.Text("by", "who declared it last", task => task.DeclaredBy),
                ListField<TaskItem>.Date("updated", "when it was last declared", task => task.DeclaredUtc),
                ListField<TaskItem>.Text("note", "the note beside it", task => task.Note),
                ListField<TaskItem>.Number("runs", "team runs started for it", task => activity.RunsOf(task.Id)),
                ListField<TaskItem>.Number("launches", "sessions launched on it", task => activity.LaunchesOf(task.Id)),
            ],
            [new ListSort("state", false), new ListSort("id", false)],
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
