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

    public static readonly ListFields<TaskItem> All = new(
        "tasks",
        [
            ListField<TaskItem>.Text("id", "the task's id", task => task.Id),
            ListField<TaskItem>.Text("title", "what the task is", task => task.Title),
            ListField<TaskItem>.Choice("state", "where it stands", States, task => task.State.ToString().ToLowerInvariant()),
            ListField<TaskItem>.Choice("kind", "work, or an idea", ["task", "idea"], task => task.Kind.ToString().ToLowerInvariant()),
            ListField<TaskItem>.Text("by", "who declared it last", task => task.DeclaredBy),
            ListField<TaskItem>.Date("updated", "when it was last declared", task => task.DeclaredUtc),
            ListField<TaskItem>.Text("note", "the note beside it", task => task.Note),
        ],
        [new ListSort("state", false), new ListSort("id", false)],
        DefaultQuery);
}
