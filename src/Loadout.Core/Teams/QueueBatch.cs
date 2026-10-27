using Loadout.Models.Tasks;
using Loadout.Models.Teams;

namespace Loadout.Core.Teams;

/// <summary>
/// Puts a chosen set of tasks at the front of a team's queue, in the order
/// given, so they are what runs next.
/// </summary>
/// <remarks>
/// For a batch somebody approved to run now under one cap. Anything already
/// waiting stays in the queue, behind them, in its own order: the cap was
/// approved for these tasks, and spending it on others that happened to be
/// ahead would be spending it on something nobody looked at.
/// </remarks>
public static class QueueBatch
{
    /// <summary>
    /// Adds each item not already queued and moves every one to the front, or
    /// says why one can't go in. Changes nothing when any is refused.
    /// </summary>
    /// <param name="queue">The queue, under its lock.</param>
    /// <param name="items">The tasks, in the order they should run.</param>
    /// <param name="tasksOf">Each project's task list, or null when it could not be read.</param>
    /// <returns>Null when done, or the first refusal.</returns>
    public static string? ToFront(
        TeamQueue queue,
        IReadOnlyList<QueueItem> items,
        Func<string, IReadOnlyList<TaskItem>?> tasksOf)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(tasksOf);

        var front = new List<QueueItem>(items.Count);

        foreach (var item in items)
        {
            var queued = queue.Items.FirstOrDefault(one =>
                QueueRules.Same(one.Task, item.Task) && QueueRules.Same(one.Project, item.Project));

            if (queued is not null)
            {
                front.Add(queued);

                continue;
            }

            if (queue.Runs.Any(run => QueueRules.Same(run.Task, item.Task) && QueueRules.Same(run.Project, item.Project)
                    && run.State is QueueRunState.Running or QueueRunState.Held))
            {
                return $"'{item.Task}' on {item.Project} already has a queued run going.";
            }

            if (tasksOf(item.Project) is not { } tasks)
            {
                return $"{item.Project}'s task list could not be read.";
            }

            if (QueueRules.Admission(item, tasks, queue) is { } refused)
            {
                return refused;
            }

            front.Add(item);
        }

        var rest = queue.Items.Where(one => !front.Contains(one)).ToList();

        queue.Items.Clear();
        queue.Items.AddRange(front);
        queue.Items.AddRange(rest);

        return null;
    }

    /// <summary>What is left of a cap once some has been spent, never below zero.</summary>
    public static decimal Left(decimal cap, decimal spent) => Math.Max(0m, cap - spent);
}
