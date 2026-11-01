using Loadout.Core.Tasks;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Tasks;

namespace Loadout.Core.Teams;

/// <summary>Where a follow-up a run proposed stands.</summary>
public enum FollowUpState
{
    /// <summary>Waiting for a person to accept or dismiss it.</summary>
    Proposed,

    /// <summary>Made into a task.</summary>
    Accepted,

    /// <summary>Decided against, on purpose.</summary>
    Dismissed,
}

/// <summary>A piece of work a run left for later, and what became of it.</summary>
/// <param name="Id">F1, F2 and so on, in the order the report listed them.</param>
/// <param name="Title">What the work is, as the task would be titled.</param>
/// <param name="Why">Why it is worth doing and why not now, or null.</param>
/// <param name="State">Waiting, accepted or dismissed.</param>
/// <param name="Task">For an accepted one, the task it became.</param>
/// <param name="By">Who accepted or dismissed it.</param>
/// <param name="Reason">Why it was dismissed, when somebody said.</param>
public sealed record RunFollowUp(
    string Id,
    string Title,
    string? Why,
    FollowUpState State,
    string? Task = null,
    string? By = null,
    string? Reason = null);

/// <summary>
/// The follow-ups a finished run's report named: each held on the run until a
/// person accepts it as a task or dismisses it.
/// </summary>
/// <remarks>
/// <para>
/// Held rather than made into tasks as the run ends, so nothing reaches the
/// task list without somebody saying so, and recorded in the run's own
/// journal, so a dismissed follow-up is never offered again and a run with
/// some still waiting can be found by reading the runs.
/// </para>
/// <para>
/// The task goes on the run's project, the list somebody looking for the work
/// would read first; <c>task move</c> takes it elsewhere. Its source names the
/// run, which is how the deferral sweep knows the follow-up has a task.
/// </para>
/// </remarks>
public sealed class RunFollowUps
{
    /// <summary>A follow-up the run's final report named.</summary>
    public const string Proposed = "followup.proposed";

    /// <summary>A follow-up made into a task.</summary>
    public const string Accepted = "followup.accepted";

    /// <summary>A follow-up decided against.</summary>
    public const string Dismissed = "followup.dismissed";

    private readonly IRunJournal _journal;
    private readonly ITaskService _tasks;
    private readonly TimeProvider _time;

    public RunFollowUps(IRunJournal journal, ITaskService tasks, TimeProvider time)
    {
        _journal = journal;
        _tasks = tasks;
        _time = time;
    }

    /// <summary>A run's follow-ups as its events leave them, in the order they were proposed.</summary>
    public static IReadOnlyList<RunFollowUp> Fold(IReadOnlyList<RunEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var held = new List<RunFollowUp>();

        foreach (var entry in events)
        {
            var id = entry.Text("id")?.Trim();

            if (id is not { Length: > 0 })
            {
                continue;
            }

            var at = held.FindIndex(one => Same(one.Id, id));

            switch (entry.Kind)
            {
                // The first proposal of an id stands: a run picked up again
                // that ends a second time numbers its follow-ups afresh, and
                // must not overwrite a decision already made about F1.
                case Proposed when at < 0 && entry.Text("title") is { Length: > 0 } title:
                    held.Add(new RunFollowUp(id, title, entry.Text("why") is { Length: > 0 } why ? why : null, FollowUpState.Proposed));
                    break;

                case Accepted when at >= 0:
                    held[at] = held[at] with { State = FollowUpState.Accepted, Task = entry.Text("task"), By = entry.Text("by") };
                    break;

                case Dismissed when at >= 0:
                    held[at] = held[at] with { State = FollowUpState.Dismissed, By = entry.Text("by"), Reason = entry.Text("why") };
                    break;
            }
        }

        return held;
    }

    /// <summary>How many of a run's follow-ups nobody has accepted or dismissed.</summary>
    public static int Waiting(RunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return run.FollowUps.Count(one => one.State == FollowUpState.Proposed);
    }

    /// <summary>Makes a follow-up into a task on the run's project, and records that it was.</summary>
    /// <param name="runId">The run.</param>
    /// <param name="id">The follow-up, such as F1.</param>
    /// <param name="by">Who is accepting it.</param>
    /// <param name="dryRun">Check it could be accepted, and change nothing.</param>
    /// <param name="ct">Stops the work.</param>
    /// <returns>The task it became, or would become.</returns>
    public async Task<OperationResult<TaskItem>> AcceptAsync(string runId, string id, string by, bool dryRun, CancellationToken ct = default)
    {
        var found = Find(runId, id);

        if (found.Failed)
        {
            return OperationResult<TaskItem>.Fail(found.Error!, found.ExitCode);
        }

        var (run, followUp) = found.Value;
        var add = new TaskOperation(
            TaskOperation.Add,
            run.Project,
            Title: followUp.Title,
            Note: $"A follow-up from run {run.RunId}.",
            Detail: followUp.Why ?? string.Empty,
            By: by,
            Source: new TaskSource { Kind = TaskSourceKind.Run, Id = run.RunId });

        if (dryRun)
        {
            // The task as it would be added; its id is only chosen against
            // the list as it stands when it is.
            return OperationResult<TaskItem>.Ok(new TaskItem
            {
                Title = add.Title!,
                Note = add.Note!,
                Detail = add.Detail!,
                DeclaredBy = by,
                Source = add.Source,
            });
        }

        var made = await _tasks.ApplyAsync([add], ct).ConfigureAwait(false);

        if (made.Failed)
        {
            return OperationResult<TaskItem>.Fail(made.Error!, made.ExitCode);
        }

        var outcome = made.Value!.Outcomes[0];

        if (!outcome.Applied)
        {
            return OperationResult<TaskItem>.Fail(outcome.Error!, outcome.Code);
        }

        var recorded = Record(runId, followUp.Id, Accepted, new { id = followUp.Id, task = outcome.Task!.Id, by });

        if (recorded.Failed)
        {
            // Somebody else decided about it between the read and the write.
            // The task made here would be a second one for the same work.
            await _tasks.ApplyAsync([new TaskOperation(TaskOperation.Remove, run.Project, outcome.Task.Id)], ct).ConfigureAwait(false);

            return OperationResult<TaskItem>.Fail(recorded.Error!, recorded.ExitCode);
        }

        return OperationResult<TaskItem>.Ok(outcome.Task);
    }

    /// <summary>Records that a follow-up was decided against, so it is not offered again.</summary>
    public OperationResult<RunFollowUp> Dismiss(string runId, string id, string by, string? why, bool dryRun)
    {
        var found = Find(runId, id);

        if (found.Failed)
        {
            return OperationResult<RunFollowUp>.Fail(found.Error!, found.ExitCode);
        }

        var followUp = found.Value.FollowUp;
        var dismissed = followUp with { State = FollowUpState.Dismissed, By = by, Reason = why };

        if (dryRun)
        {
            return OperationResult<RunFollowUp>.Ok(dismissed);
        }

        var recorded = Record(runId, followUp.Id, Dismissed, new { id = followUp.Id, by, why });

        return recorded.Failed
            ? OperationResult<RunFollowUp>.Fail(recorded.Error!, recorded.ExitCode)
            : OperationResult<RunFollowUp>.Ok(dismissed);
    }

    private OperationResult<(RunSummary Run, RunFollowUp FollowUp)> Find(string runId, string id)
    {
        var read = _journal.Summarise(runId);

        if (read.Failed)
        {
            return OperationResult<(RunSummary, RunFollowUp)>.Fail(read.Error!, read.ExitCode);
        }

        return Decidable(read.Value!, id) is { } refusal
            ? OperationResult<(RunSummary, RunFollowUp)>.Fail(refusal, ExitCode.InvalidArguments)
            : OperationResult<(RunSummary, RunFollowUp)>.Ok((read.Value!, read.Value!.FollowUps.First(one => Same(one.Id, id.Trim()))));
    }

    /// <summary>Appends one decision, checked again under the journal's hold.</summary>
    private OperationResult Record(string runId, string id, string kind, object data)
    {
        var written = RunJournalAppend.Append(_journal, runId, _time.GetUtcNow(), run =>
            Decidable(run, id) is { } refusal
                ? OperationResult<IReadOnlyList<RunJournalAppend.Line>>.Fail(refusal, ExitCode.InvalidArguments)
                : OperationResult<IReadOnlyList<RunJournalAppend.Line>>.Ok([new RunJournalAppend.Line(kind, data)]));

        return written.Failed ? OperationResult.Fail(written.Error!, written.ExitCode) : OperationResult.Ok();
    }

    /// <summary>Why a follow-up of this run cannot be decided now, or null when it can.</summary>
    private static string? Decidable(RunSummary run, string id)
    {
        var named = id?.Trim() ?? string.Empty;

        if (run.FollowUps.FirstOrDefault(one => Same(one.Id, named)) is not { } followUp)
        {
            return run.FollowUps.Count == 0
                ? $"Run {run.RunId} proposed no follow-ups."
                : $"Run {run.RunId} has no follow-up '{named}'. It has {string.Join(", ", run.FollowUps.Select(one => one.Id))}.";
        }

        return followUp.State switch
        {
            FollowUpState.Accepted => $"{followUp.Id} of run {run.RunId} was already accepted, as the task {followUp.Task}.",
            FollowUpState.Dismissed => $"{followUp.Id} of run {run.RunId} was already dismissed.",
            _ => null,
        };
    }

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
