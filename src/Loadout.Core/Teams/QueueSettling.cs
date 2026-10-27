using System.Globalization;
using System.Text;
using Loadout.Models.Tasks;
using Loadout.Models.Teams;

namespace Loadout.Core.Teams;

/// <summary>Where a queued run has got to, as its journal and its process say.</summary>
/// <param name="State">Running, held, met or failed.</param>
/// <param name="RunId">The run's id, or empty when it never got one.</param>
/// <param name="Reason">Why, in a line; empty while it is running.</param>
/// <param name="CostUsd">What it has spent so far.</param>
public sealed record QueueStanding(QueueRunState State, string RunId, string Reason, decimal CostUsd);

/// <summary>
/// How the queue reads its runs, and what it says about them.
/// </summary>
/// <remarks>
/// Pure, so the rules that decide a run has ended, is waiting for a person, or
/// died with its process are argued with in a test rather than at three in
/// the morning.
/// </remarks>
public static class QueueSettling
{
    /// <summary>How long a run may be recorded as going with no journal before it is taken as never started.</summary>
    public static readonly TimeSpan Unstarted = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The journal's account of a queued run: by its id when it has one, otherwise
    /// the run for its task that started after the queue started it.
    /// </summary>
    /// <remarks>
    /// Never the newest run on the machine: a run started meanwhile from a
    /// schedule or the dashboard would otherwise be recorded as this one.
    /// </remarks>
    public static RunSummary? Find(QueueRun run, IEnumerable<RunSummary> runs)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(runs);

        return run.RunId.Length > 0
            ? runs.FirstOrDefault(one => string.Equals(one.RunId, run.RunId, StringComparison.Ordinal))
            : runs
                .Where(one => one.Task is { } task && QueueRules.Same(task, run.Task)
                    && one.Project is { } project && QueueRules.Same(project, run.Project)
                    && one.Started >= run.StartedUtc.AddSeconds(-5))
                .OrderBy(one => one.Started)
                .FirstOrDefault();
    }

    /// <summary>
    /// Where a run stands now.
    /// </summary>
    /// <param name="run">The queue's record of it.</param>
    /// <param name="summary">Its journal, or null when none was found.</param>
    /// <param name="alive">Whether the process running it is still going; null when the queue started none.</param>
    /// <param name="now">The clock.</param>
    public static QueueStanding Standing(QueueRun run, RunSummary? summary, bool? alive, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (summary is null)
        {
            if (alive == false)
            {
                return new QueueStanding(QueueRunState.Failed, string.Empty, "the run stopped before it started", 0m);
            }

            return alive is null && now - run.StartedUtc > Unstarted
                ? new QueueStanding(QueueRunState.Failed, string.Empty, "the run never started", 0m)
                : new QueueStanding(run.State, run.RunId, run.Reason, run.CostUsd);
        }

        if (!summary.Running)
        {
            return summary.Ended switch
            {
                "done" => new QueueStanding(QueueRunState.Met, summary.RunId, "every done-when met", summary.CostUsd),
                null or "" => new QueueStanding(
                    QueueRunState.Failed, summary.RunId, "the run stopped without saying how it ended", summary.CostUsd),
                var ended => new QueueStanding(QueueRunState.Failed, summary.RunId, ended, summary.CostUsd),
            };
        }

        // Its journal says it is going, but the process that was running it
        // has gone: nothing will ever finish it.
        if (alive == false)
        {
            return new QueueStanding(
                QueueRunState.Failed, summary.RunId, "stopped with the process running it", summary.CostUsd);
        }

        return summary.Waiting.Count > 0
            ? new QueueStanding(
                QueueRunState.Held,
                summary.RunId,
                $"waiting for an answer: {summary.Waiting[0].Brief}",
                summary.CostUsd)
            : new QueueStanding(QueueRunState.Running, summary.RunId, string.Empty, summary.CostUsd);
    }

    /// <summary>A team's name as an owner: builders' and docs-crew's.</summary>
    public static string Whose(string team) =>
        team.EndsWith('s') ? $"{team}'" : $"{team}'s";

    /// <summary>The branch a queued task's work goes on.</summary>
    public static string Branch(QueueItem item, DateTimeOffset started)
    {
        ArgumentNullException.ThrowIfNull(item);

        var safe = string.Concat(item.Task.Trim().ToLowerInvariant()
            .Select(one => char.IsAsciiLetterOrDigit(one) || one is '-' or '_' or '.' ? one : '-'));

        return $"queue/{safe}-{started.UtcDateTime.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// A draft pull request's body: the goal, the done-when the person confirmed
    /// with how the lead last read each, and where it came from.
    /// </summary>
    /// <remarks>
    /// The done-when are in it so whoever reviews it in the morning judges it
    /// against the conditions that were agreed, not against a guess at the intent.
    /// </remarks>
    public static string Body(TaskItem task, RunSummary run, string team)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(run);

        var body = new StringBuilder();

        body.Append(task.Title.Trim().Length > 0 ? task.Title.Trim() : task.Id).Append("\n\n");

        if (task.Note.Trim().Length > 0)
        {
            body.Append(task.Note.Trim()).Append("\n\n");
        }

        body.Append("## Done when\n\n");

        foreach (var condition in task.DoneWhen.Where(one => one.Trim().Length > 0))
        {
            var reading = run.Readings.TryGetValue(condition.Trim(), out var read) ? $" - the lead read it as {read}" : string.Empty;

            body.Append("- ").Append(condition.Trim()).Append(reading).Append('\n');
        }

        body.Append(CultureInfo.InvariantCulture, $"\nFrom team {QueueSettling.Whose(team)} queue, run {run.RunId}: {run.Rounds} round(s), ${run.CostUsd:0.00}.\n");
        body.Append("Opened as a draft by the queue. Nothing reaches the base branch until a person merges it.\n");

        return body.ToString();
    }

    /// <summary>
    /// What a window did, in a few lines, for whoever reads it in the morning.
    /// </summary>
    public static (string Title, string Detail) Summary(string team, IReadOnlyList<QueueRun> runs, IReadOnlyList<QueueItem> left)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(left);

        var spent = runs.Sum(one => one.CostUsd);
        var lines = new List<string>();

        foreach (var run in runs)
        {
            lines.Add(run.State switch
            {
                QueueRunState.Met when run.PullRequest.Length > 0 => $"{run.Task}: done-when met, draft pull request {run.PullRequest}",
                QueueRunState.Met => $"{run.Task}: done-when met, on branch {run.Branch} (no pull request)",
                QueueRunState.Held => $"{run.Task}: waiting for you - {run.Reason}",
                QueueRunState.Failed => $"{run.Task}: blocked - {run.Reason}" + (run.RunId.Length > 0 ? $" (run {run.RunId})" : string.Empty),
                _ => $"{run.Task}: still running",
            });
        }

        if (left.Count > 0)
        {
            lines.Add($"{left.Count} still in the queue.");
        }

        var title = runs.Count == 0
            ? $"{QueueSettling.Whose(team)} queue window closed with nothing run"
            : string.Create(CultureInfo.InvariantCulture, $"{QueueSettling.Whose(team)} queue window: {runs.Count} run(s), ${spent:0.00}");

        return (title, lines.Count == 0 ? "Nothing in the queue could start." : string.Join("\n", lines));
    }
}
