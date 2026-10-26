using Loadout.Models.Tasks;

namespace Loadout.Core.Tasks;

/// <summary>What a ranking of tasks is for.</summary>
public enum TaskPick
{
    /// <summary>No order asked for: longest waiting first.</summary>
    Any,

    /// <summary>What looks smallest and best defined.</summary>
    Easy,

    /// <summary>What has waited longest or reads as broken.</summary>
    NeedsWorkFirst,
}

/// <summary>A task on some project's list, with where it came out and why.</summary>
/// <param name="Project">The project's slug, or empty for the workspace-wide list.</param>
/// <param name="Task">The task itself.</param>
/// <param name="Score">Higher ranks first. Only meaningful against other scores from the same pick.</param>
/// <param name="Reasons">Each signal that moved the score, in words.</param>
public sealed record RankedTask(string Project, TaskItem Task, int Score, IReadOnlyList<string> Reasons);

/// <summary>
/// Ranks tasks for "easy" and "needs work first" from the fields tasks have.
/// </summary>
/// <remarks>
/// <para>
/// A heuristic, and it says so: every pick carries the reasons that put it
/// there, so a ranking that is wrong is wrong in a way somebody can see and
/// argue with. Tasks have no declared priority or effort yet; the length of a
/// description and the age of a claim stand in for them. When those fields
/// arrive (task-priority-and-effort) they become signals here, ahead of these.
/// </para>
/// <para>
/// Deterministic on purpose. The same list ranks the same way every time,
/// which is what lets a plan card be checked against a dry run and lets the
/// ranking be tested at all.
/// </para>
/// </remarks>
public static class TaskScorer
{
    /// <summary>A description under this many characters reads as a small piece of work.</summary>
    internal const int ShortDetail = 400;

    /// <summary>A description over this many characters reads as a large one.</summary>
    internal const int LongDetail = 1500;

    private static readonly string[] DefectWords =
        ["fix", "bug", "broken", "fails", "failing", "crash", "regression", "wrong", "lost", "error"];

    /// <summary>Ranks the tasks given, best first, for what was asked.</summary>
    /// <param name="tasks">Tasks with the project each is on.</param>
    /// <param name="pick">What the ranking is for.</param>
    /// <param name="now">The time to measure ages against.</param>
    public static IReadOnlyList<RankedTask> Rank(
        IEnumerable<(string Project, TaskItem Task)> tasks,
        TaskPick pick,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        return tasks
            .Select(one => Score(one.Project, one.Task, pick, now))
            .OrderByDescending(ranked => ranked.Score)
            .ThenBy(ranked => Started(ranked.Task))
            .ThenBy(ranked => ranked.Project, StringComparer.OrdinalIgnoreCase)
            .ThenBy(ranked => ranked.Task.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>One task's score and the reasons for it.</summary>
    public static RankedTask Score(string project, TaskItem task, TaskPick pick, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(task);

        var score = 0;
        var reasons = new List<string>();

        void Add(int weight, string reason)
        {
            score += weight;
            reasons.Add(reason);
        }

        var days = Math.Max(0, (int)(now - Started(task)).TotalDays);
        var detail = task.Detail.Trim().Length;
        var wasBlocked = task.History.Any(one => one.State == TaskState.Blocked);

        switch (pick)
        {
            case TaskPick.Easy:
                if (task.DoneWhen.Count > 0)
                {
                    Add(3, $"has {Plural(task.DoneWhen.Count, "done-when")}, so the finish line is stated");
                }
                else
                {
                    Add(-1, "has no done-when yet");
                }

                if (detail < ShortDetail)
                {
                    Add(2, detail == 0 ? "described in its title alone" : "short description");
                }
                else if (detail > LongDetail)
                {
                    Add(-2, "long description, likely a larger piece of work");
                }

                if (wasBlocked)
                {
                    Add(-2, "has been blocked before");
                }

                break;

            case TaskPick.NeedsWorkFirst:
                if (days >= 7)
                {
                    Add(Math.Min(days / 7, 4), $"waiting {days} days");
                }

                if (Reads(task, DefectWords) is { } word)
                {
                    Add(2, $"reads as a defect ('{word}')");
                }

                if (wasBlocked)
                {
                    Add(1, "open again after being blocked");
                }

                if (task.DoneWhen.Count > 0)
                {
                    Add(1, "has a done-when, so a team can take it");
                }

                break;

            case TaskPick.Any:
            default:
                break;
        }

        return new RankedTask(project, task, score, reasons);
    }

    /// <summary>When the task was first claimed, as near as the record says.</summary>
    internal static DateTimeOffset Started(TaskItem task) =>
        task.History.Count > 0 ? task.History.Min(one => one.Utc) : task.DeclaredUtc;

    private static string? Reads(TaskItem task, string[] words)
    {
        var title = task.Title.ToLowerInvariant();

        return words.FirstOrDefault(word =>
            title.Split([' ', '-', ',', '.', ':', '\'', '"', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
                .Any(part => part == word));
    }

    private static string Plural(int count, string noun) => count == 1 ? $"a {noun}" : $"{count} {noun}s";
}
