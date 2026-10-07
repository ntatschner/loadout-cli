using Loadout.Models.Tasks;

namespace Loadout.Core.Tasks;

/// <summary>
/// Whether a team run can be started for a task.
/// </summary>
/// <remarks>
/// One rule, read by <c>team run --task</c> and by the dashboard before it
/// types that command, so the page can say why in the command's own words
/// rather than guess them from an exit code.
/// </remarks>
public static class TaskRuns
{
    /// <summary>Why a run cannot be started for the task, or null when it can.</summary>
    /// <remarks>
    /// Checked before the run starts rather than discovered by the runner,
    /// which would otherwise declare a misspelt id into existence as a new
    /// task in doing - a row nobody wrote, for work nobody asked for. An idea
    /// is refused too: it has not been accepted, so there is no plan for a
    /// lead to work from, only the sentence somebody first typed.
    /// </remarks>
    public static string? Rejection(string id, string project, IReadOnlyList<TaskItem> tasks)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(tasks);

        var found = tasks.FirstOrDefault(one => string.Equals(one.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

        return found switch
        {
            null => $"There is no task '{id}' on {project}'s list. "
                + $"See what there is with: loadout task list --project {project}",
            { Kind: TaskKind.Idea } =>
                $"'{id}' is an idea, not a task yet. Accept it first, so the run has its plan to work from.",
            { State: TaskState.Done or TaskState.Dropped } =>
                $"'{id}' is {found.State.ToString().ToLowerInvariant()}. Reopen it first if there is more to do: "
                + $"loadout task declare {found.Id} open --project {project}",
            _ => null,
        };
    }

    /// <summary>What the lead is told: the task's title, and its note beneath when it has one.</summary>
    public static string Goal(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var title = task.Title.Trim().Length > 0 ? task.Title.Trim() : task.Id;

        return task.Note is { Length: > 0 } note && note.Trim().Length > 0
            ? $"{title}\n\n{note.Trim()}"
            : title;
    }
}
