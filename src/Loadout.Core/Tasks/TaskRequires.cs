using Loadout.Models.Tasks;

namespace Loadout.Core.Tasks;

/// <summary>A requirement not yet met, and why, as somebody would say it.</summary>
/// <param name="Link">The task waited for.</param>
/// <param name="Why">What it is now: open, dropped, gone.</param>
public sealed record TaskUnmet(TaskLink Link, string Why);

/// <summary>
/// How a task names another it requires, and what it means for the link to be
/// met, broken or a loop. Pure: every list is handed in, so the queue, the
/// waiting list and the commands all decide the same way.
/// </summary>
/// <remarks>
/// A link is written <c>id</c> for a task on the same list,
/// <c>project/id</c> for one on a project's, and <c>global/id</c> for the
/// workspace-wide list's, the word every list filter already uses for it.
/// </remarks>
public static class TaskRequires
{
    /// <summary>
    /// Each list's tasks by its key: a project's slug, or empty for the
    /// workspace-wide list. Null for a list that cannot be read, such as one
    /// whose project left the workspace.
    /// </summary>
    public delegate IReadOnlyList<TaskItem>? Lists(string project);

    /// <summary>A link read from what somebody wrote, against the list they are on; or why it is not one.</summary>
    /// <param name="said">What they wrote: <c>id</c>, <c>project/id</c> or <c>global/id</c>.</param>
    /// <param name="here">The list the waiting task is on, a slug or null for the workspace-wide one.</param>
    public static (TaskLink? Link, string? Refusal) Parse(string? said, string? here)
    {
        var text = said?.Trim() ?? string.Empty;
        var parts = text.Split('/');

        if (text.Length == 0 || parts.Length > 2 || parts.Any(part => part.Trim().Length == 0))
        {
            return (null, $"'{text}' is not a task. Name one as its id, project/id, or global/id for the workspace-wide list.");
        }

        var project = parts.Length == 1 ? here ?? string.Empty : Workspace(parts[0].Trim());
        var id = parts[^1].Trim();

        return TaskIds.Rejection(id) is { } bad ? (null, bad) : (new TaskLink { Project = project, Id = id }, null);
    }

    /// <summary>The key a list goes by here: its slug, or empty for the workspace-wide list.</summary>
    public static string Key(string? slug) => slug ?? string.Empty;

    /// <summary>A link written out in full: <c>project/id</c>, or <c>global/id</c>.</summary>
    public static string Name(TaskLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        return $"{List(link.Project)}/{link.Id}";
    }

    /// <summary>A link as somebody on the list keyed <paramref name="list"/> would write it: a bare id for one on the same list.</summary>
    public static string Name(TaskLink link, string list)
    {
        ArgumentNullException.ThrowIfNull(link);

        return Same(link.Project, list) ? link.Id : Name(link);
    }

    /// <summary>Whether two links name the same task.</summary>
    public static bool Same(TaskLink left, TaskLink right) =>
        Same(left.Project, right.Project) && Same(left.Id, right.Id);

    /// <summary>The task a link names, or null when its list cannot be read or has no such task.</summary>
    public static TaskItem? Find(TaskLink link, Lists lists)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(lists);

        return lists(link.Project)?.FirstOrDefault(task => Same(task.Id, link.Id));
    }

    /// <summary>Every requirement of a task that is not met, each with why.</summary>
    /// <remarks>
    /// Done is the only way a link is met. Anything else holds, a target that
    /// went away included, and says which it is so the person knows what to fix.
    /// </remarks>
    public static IReadOnlyList<TaskUnmet> Unmet(TaskItem task, Lists lists)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(lists);

        var unmet = new List<TaskUnmet>();

        foreach (var link in task.Requires)
        {
            var list = lists(link.Project);
            var target = list?.FirstOrDefault(one => Same(one.Id, link.Id));

            var why = list is null
                ? link.Project.Length > 0 ? $"{link.Project} is not a project in this workspace any more" : "the workspace-wide list could not be read"
                : target is null
                    ? "is no longer on its list"
                    : target.State == TaskState.Done
                        ? null
                        : $"is {target.State.ToString().ToLowerInvariant()}";

            if (why is not null)
            {
                unmet.Add(new TaskUnmet(link, why));
            }
        }

        return unmet;
    }

    /// <summary>The unmet requirements in one sentence, or null when there are none.</summary>
    /// <param name="unmet">What is not met.</param>
    /// <param name="list">The key of the list the waiting task is on, for naming tasks on it by id alone; null to name every one in full.</param>
    public static string? Said(IReadOnlyList<TaskUnmet> unmet, string? list = null)
    {
        ArgumentNullException.ThrowIfNull(unmet);

        string Named(TaskLink link) => list is null ? Name(link) : Name(link, list);

        return unmet.Count == 0
            ? null
            : "waits for " + string.Join("; ", unmet.Select(one =>
                one.Why.StartsWith("is ", StringComparison.Ordinal) ? $"{Named(one.Link)}, which {one.Why}" : $"{Named(one.Link)}: {one.Why}"));
    }

    /// <summary>
    /// The loop adding these links would close, as a chain, or null when it
    /// closes none. A loop through another project counts the same.
    /// </summary>
    /// <param name="from">The task the links are added to.</param>
    /// <param name="adding">The links being added.</param>
    /// <param name="lists">Every list.</param>
    public static string? Loop(TaskLink from, IReadOnlyList<TaskLink> adding, Lists lists)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(adding);
        ArgumentNullException.ThrowIfNull(lists);

        foreach (var start in adding)
        {
            if (Path(start, from, lists, []) is { } path)
            {
                return string.Join(" requires ", new[] { Name(from) }.Concat(path.Select(link => Name(link))));
            }
        }

        return null;
    }

    /// <summary>The chain from one task to another along requirements, both ends included, or null.</summary>
    private static List<TaskLink>? Path(TaskLink at, TaskLink to, Lists lists, HashSet<string> seen)
    {
        if (Same(at, to))
        {
            return [at];
        }

        if (!seen.Add($"{at.Project.ToLowerInvariant()}/{at.Id.ToLowerInvariant()}"))
        {
            return null;
        }

        foreach (var next in Find(at, lists)?.Requires ?? [])
        {
            if (Path(next, to, lists, seen) is { } rest)
            {
                rest.Insert(0, at);

                return rest;
            }
        }

        return null;
    }

    /// <summary>The workspace-wide list's key, from the words that name it.</summary>
    private static string Workspace(string project) =>
        project is "-" || string.Equals(project, TaskFields.WorkspaceWide, StringComparison.OrdinalIgnoreCase) ? string.Empty : project;

    private static string List(string project) => project.Length > 0 ? project : TaskFields.WorkspaceWide;

    private static bool Same(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
