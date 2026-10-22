using Loadout.Models.Tasks;
using Loadout.Models.Teams;

namespace Loadout.Core.Teams;

/// <summary>What a queue should do next.</summary>
public enum QueueVerdict
{
    /// <summary>Start the item named.</summary>
    Start,

    /// <summary>Nothing can start now, but something may later in the window.</summary>
    Wait,

    /// <summary>Nothing more starts in this window.</summary>
    Stop,
}

/// <summary>An item the queue passed over, and why.</summary>
public sealed record QueueSkip(QueueItem Item, string Reason);

/// <summary>One decision, with the reason it was made.</summary>
/// <param name="Verdict">Start, wait or stop.</param>
/// <param name="Item">The item to start, when the verdict is start.</param>
/// <param name="Reason">Why, in a sentence a person reads in the morning.</param>
/// <param name="Skipped">Items passed over on the way, each with its reason.</param>
public sealed record QueueDecision(
    QueueVerdict Verdict,
    QueueItem? Item,
    string Reason,
    IReadOnlyList<QueueSkip> Skipped);

/// <summary>A window's limits, and when it opened.</summary>
/// <param name="Opened">When the window opened; runs started before it are not counted against it.</param>
/// <param name="CapUsd">What it may spend, or null for no cap.</param>
/// <param name="CapRuns">How many runs it may start, or null for no cap.</param>
public sealed record QueueWindow(DateTimeOffset Opened, decimal? CapUsd, int? CapRuns);

/// <summary>
/// What may join a queue, and what runs next.
/// </summary>
/// <remarks>
/// <para>
/// Pure, so every rule is tested without a daemon, a runner or a clock: the
/// daemon and 'team queue run-next' both ask here and carry out what they are
/// told, which is how a run started by hand and one started overnight follow
/// the same rules.
/// </para>
/// <para>
/// Every refusal and every wait comes with its reason. A queue that stalls at
/// three in the morning without saying why is one somebody finds idle at nine
/// with no idea what to fix.
/// </para>
/// </remarks>
public static class QueueRules
{
    /// <summary>How many of the queue's past runs are kept.</summary>
    public const int Kept = 50;

    /// <summary>
    /// Why a task cannot be queued, or null when it can.
    /// </summary>
    /// <param name="item">The item, naming the task, its project and what it comes after.</param>
    /// <param name="tasks">Every task on the item's project's list.</param>
    /// <param name="queue">The queue as it stands, without the item.</param>
    /// <remarks>
    /// Run when an item is added and again when it is about to start, because
    /// a task can be edited, dropped or finished in between.
    /// </remarks>
    public static string? Admission(QueueItem item, IReadOnlyList<TaskItem> tasks, TeamQueue queue)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(queue);

        var task = Find(tasks, item.Task);

        if (task is null)
        {
            return $"There is no task '{item.Task}' on {item.Project}'s list.";
        }

        if (task.Kind == TaskKind.Idea)
        {
            return $"'{task.Id}' is an idea, not a task yet. Accept it first, and confirm its done-when as you do.";
        }

        if (task.State is TaskState.Done or TaskState.Dropped)
        {
            return $"'{task.Id}' is {Spell(task.State)}, so there is nothing to run.";
        }

        if (task.Title.Trim().Length == 0)
        {
            return $"'{task.Id}' has no title, so a run would have no goal to work from.";
        }

        // The rule the whole queue rests on: nobody is there to judge an
        // unattended run, so it has to carry its own finish line.
        if (!task.DoneWhen.Any(one => one.Trim().Length > 0))
        {
            return $"'{task.Id}' has no done-when, and an unattended run with no stated finish line can't be judged. "
                + $"Give it one: loadout task declare {task.Id} {Spell(task.State)} --done-when \"...\" --project {item.Project}";
        }

        if (item.After.Length > 0)
        {
            if (Same(item.After, item.Task))
            {
                return $"'{task.Id}' can't wait for itself.";
            }

            var before = Find(tasks, item.After);

            if (before is null)
            {
                return $"'{task.Id}' is to run after '{item.After}', which is not on {item.Project}'s list.";
            }

            if (before.State == TaskState.Dropped)
            {
                return $"'{task.Id}' is to run after '{before.Id}', which was dropped, so it would wait for ever.";
            }

            if (Cycle(item, queue) is { } loop)
            {
                return $"'{task.Id}' running after '{item.After}' makes a loop ({loop}), so none of them would ever start.";
            }
        }

        return null;
    }

    /// <summary>
    /// What to do next.
    /// </summary>
    /// <param name="queue">The queue, with its runs.</param>
    /// <param name="tasksOf">Every task on a project's list, or null when the list cannot be read.</param>
    /// <param name="window">The window being worked in, or null when a person asked by hand.</param>
    /// <remarks>
    /// A person asking by hand is not held to a window's caps, because they
    /// are there to see what it costs. One run at a time and the cap on held
    /// runs still apply: both protect the person's own tree and attention,
    /// whoever started the run.
    /// </remarks>
    public static QueueDecision Next(
        TeamQueue queue,
        Func<string, IReadOnlyList<TaskItem>?> tasksOf,
        QueueWindow? window)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(tasksOf);

        if (queue.Runs.FirstOrDefault(run => run.State == QueueRunState.Running) is { } running)
        {
            return Decided(QueueVerdict.Wait, $"'{running.Task}' is still running, and a queue runs one at a time.");
        }

        var held = queue.Runs.Count(run => run.State == QueueRunState.Held);

        if (held >= Math.Max(queue.Settings.HeldCap, 0))
        {
            return Decided(
                QueueVerdict.Stop,
                $"{held} run(s) are waiting for an answer, the most this queue lets wait at once. "
                + "Answer one, and the queue carries on.");
        }

        if (window is not null)
        {
            var counted = queue.Runs.Where(run => run.StartedUtc >= window.Opened).ToList();

            if (window.CapRuns is { } most && counted.Count >= most)
            {
                return Decided(QueueVerdict.Stop, $"This window has started {counted.Count} run(s), its cap.");
            }

            var spent = counted.Sum(run => run.CostUsd);

            if (window.CapUsd is { } cap && spent >= cap)
            {
                return Decided(QueueVerdict.Stop, $"This window has spent ${spent:0.00} of its ${cap:0.00}.");
            }
        }

        if (queue.Items.Count == 0)
        {
            return Decided(QueueVerdict.Stop, "The queue is empty.");
        }

        var skipped = new List<QueueSkip>();

        foreach (var item in queue.Items)
        {
            var tasks = tasksOf(item.Project);

            if (tasks is null)
            {
                skipped.Add(new QueueSkip(item, $"{item.Project}'s task list could not be read."));

                continue;
            }

            if (Admission(item, tasks, Without(queue, item)) is { } refused)
            {
                skipped.Add(new QueueSkip(item, refused));

                continue;
            }

            if (Waiting(item, tasks, queue) is { } waits)
            {
                skipped.Add(new QueueSkip(item, waits));

                continue;
            }

            return new QueueDecision(QueueVerdict.Start, item, $"'{item.Task}' on {item.Project} is next.", skipped);
        }

        return new QueueDecision(
            QueueVerdict.Wait,
            null,
            "Nothing in the queue can start yet: " + string.Join(" ", skipped.Select(one => one.Reason)),
            skipped);
    }

    /// <summary>Why an admitted item still cannot start, or null when it can.</summary>
    private static string? Waiting(QueueItem item, IReadOnlyList<TaskItem> tasks, TeamQueue queue)
    {
        if (item.After.Length == 0)
        {
            return null;
        }

        if (Find(tasks, item.After) is { State: TaskState.Done })
        {
            return null;
        }

        var last = queue.Runs.LastOrDefault(run => Same(run.Task, item.After) && Same(run.Project, item.Project));

        return last?.State switch
        {
            QueueRunState.Met => null,
            QueueRunState.Failed =>
                $"'{item.Task}' waits for '{item.After}', whose run failed ({last.Reason}). Fix that first, or take 'after' off.",
            QueueRunState.Running or QueueRunState.Held => $"'{item.Task}' waits for '{item.After}', which is still going.",
            _ => $"'{item.Task}' waits for '{item.After}', which has not run yet.",
        };
    }

    /// <summary>The chain an item's 'after' would close into a loop, or null when it does not.</summary>
    private static string? Cycle(QueueItem item, TeamQueue queue)
    {
        var chain = new List<string> { item.Task };
        var next = item.After;

        while (next.Length > 0)
        {
            chain.Add(next);

            if (Same(next, item.Task))
            {
                return string.Join(" after ", chain.AsEnumerable().Reverse());
            }

            var current = next;
            var queued = queue.Items.FirstOrDefault(one => Same(one.Task, current) && Same(one.Project, item.Project));

            if (queued is null || chain.Count > queue.Items.Count + 2)
            {
                return null;
            }

            next = queued.After;
        }

        return null;
    }

    /// <summary>The queue without one item, for checking that item against the rest.</summary>
    private static TeamQueue Without(TeamQueue queue, QueueItem item) => new()
    {
        Items = [.. queue.Items.Where(one => !ReferenceEquals(one, item))],
        Runs = queue.Runs,
        Settings = queue.Settings,
    };

    private static QueueDecision Decided(QueueVerdict verdict, string reason) => new(verdict, null, reason, []);

    private static TaskItem? Find(IReadOnlyList<TaskItem> tasks, string id) =>
        tasks.FirstOrDefault(one => Same(one.Id, id.Trim()));

    /// <summary>Whether two ids or slugs name the same thing, ignoring case and spaces at the ends.</summary>
    public static bool Same(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Spell(TaskState state) => state.ToString().ToLowerInvariant();
}
