using Loadout.Core.Ideas;
using Loadout.Models.Ideas;
using Loadout.Models.Tasks;

namespace Loadout.Core.Backfill;

/// <summary>Which sort of record a backfill finding is about.</summary>
public enum BackfillKind
{
    /// <summary>An idea with a plan, not yet accepted.</summary>
    Idea,

    /// <summary>An open or blocked task, including an accepted idea's.</summary>
    Task,
}

/// <summary>What a list shows beside a record the contract has something to say about.</summary>
public enum ContractMark
{
    /// <summary>Nothing: it has what the contract asks for, or is not something a pass looks at.</summary>
    None,

    /// <summary>A gap no pass has dealt with, including one whose last attempt failed.</summary>
    Missing,

    /// <summary>A task with a proposed done-when for a person to confirm or edit.</summary>
    ProposalWaiting,
}

/// <summary>One record a pass looks at, and what it would do with it.</summary>
/// <param name="Kind">An idea or a task.</param>
/// <param name="Project">The list it is on, or null for the workspace-wide one.</param>
/// <param name="Id">Its id.</param>
/// <param name="Title">Its title.</param>
/// <param name="Missing">The contract fields it lacks, by name.</param>
/// <param name="Skipped">Why the pass leaves it alone, or null when it fills it.</param>
/// <param name="LastFailure">What went wrong the last time a pass tried it, or null.</param>
public sealed record BackfillItem(
    BackfillKind Kind,
    string? Project,
    string Id,
    string Title,
    IReadOnlyList<string> Missing,
    string? Skipped,
    string? LastFailure)
{
    /// <summary>Whether a real pass would ask an agent about it.</summary>
    public bool Fills => Skipped is null;
}

/// <summary>
/// Which records fall short of the current contract, what a pass would do
/// about each, and the marks the lists show for them.
/// </summary>
/// <remarks>
/// <para>
/// Worked out from the records alone, with nothing stored about the check
/// itself, so the marks always follow the contract as it is now: a field the
/// next contract adds shows as missing the moment it ships, without a
/// migration to flag every old record.
/// </para>
/// <para>
/// One agent call fills everything a record lacks, so the number of records
/// a pass fills is the number of calls it makes, and the dry run's count is
/// exact rather than estimated.
/// </para>
/// </remarks>
public static class ContractBackfill
{
    /// <summary>The states a pass looks at. Work under way already has someone judging it.</summary>
    public static bool InScope(TaskState state) => state is TaskState.Open or TaskState.Blocked;

    /// <summary>The finding for a task, or null when a pass has nothing to do with it.</summary>
    /// <param name="project">Its list, or null for the workspace-wide one.</param>
    /// <param name="task">The task.</param>
    public static BackfillItem? ForTask(string? project, TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (task.Kind != TaskKind.Task || !InScope(task.State) || HasDoneWhen(task))
        {
            return null;
        }

        var proposal = task.Proposal;

        return new BackfillItem(
            BackfillKind.Task,
            project,
            task.Id,
            task.Title,
            [IdeaContract.DoneWhen],
            proposal is { DoneWhen.Count: > 0 }
                ? "A proposed done-when is waiting for someone to confirm or edit it."
                : null,
            proposal is { Error.Length: > 0, DoneWhen.Count: 0 } ? proposal.Error : null);
    }

    /// <summary>The finding for an idea, or null when a pass has nothing to do with it.</summary>
    /// <param name="project">Its list, or null for the workspace-wide one.</param>
    /// <param name="task">The idea's task.</param>
    /// <param name="record">Its working record, or null when it has none.</param>
    /// <param name="running">Whether a round on it is running on this machine.</param>
    public static BackfillItem? ForIdea(string? project, TaskItem task, IdeaRecord? record, bool running = false)
    {
        ArgumentNullException.ThrowIfNull(task);

        // Ideas still being questioned are left out: their plan, when it
        // comes, is asked for under the current contract.
        if (task.Kind != TaskKind.Idea || record is not { Plan: { } plan, Accepted: null })
        {
            return null;
        }

        var missing = IdeaContract.Missing(plan);

        if (missing.Count == 0)
        {
            return null;
        }

        return new BackfillItem(
            BackfillKind.Idea,
            project,
            task.Id,
            plan.Title is { Length: > 0 } title ? title : task.Title,
            [.. missing.Select(field => field.Name)],
            IdeaSkipped(record, missing, running),
            record.GapFillFailure is { Error.Length: > 0 } failed ? failed.Error : null);
    }

    /// <summary>The mark a list shows for a task.</summary>
    public static ContractMark MarkOf(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (task.Kind != TaskKind.Task || !InScope(task.State) || HasDoneWhen(task))
        {
            return ContractMark.None;
        }

        return task.Proposal is { DoneWhen.Count: > 0 } ? ContractMark.ProposalWaiting : ContractMark.Missing;
    }

    /// <summary>The mark a list shows for an idea, from its working record.</summary>
    public static ContractMark MarkOf(IdeaRecord? record) =>
        record is { Plan: { } plan, Accepted: null } && IdeaContract.Missing(plan).Count > 0
            ? ContractMark.Missing
            : ContractMark.None;

    /// <summary>The words a list shows for a mark, or null for none.</summary>
    public static string? Label(ContractMark mark) => mark switch
    {
        ContractMark.Missing => "missing",
        ContractMark.ProposalWaiting => "proposal waiting",
        _ => null,
    };

    /// <summary>The name a mark goes by in JSON, or null for none.</summary>
    public static string? Name(ContractMark mark) => mark switch
    {
        ContractMark.Missing => "missing",
        ContractMark.ProposalWaiting => "proposalWaiting",
        _ => null,
    };

    /// <summary>The findings a capped run gets to, in the order they were found; skipped ones are never counted.</summary>
    public static IReadOnlyList<BackfillItem> Capped(IReadOnlyList<BackfillItem> items, int? limit)
    {
        ArgumentNullException.ThrowIfNull(items);

        var filling = items.Where(item => item.Fills);

        return [.. limit is { } most ? filling.Take(Math.Max(0, most)) : filling];
    }

    private static bool HasDoneWhen(TaskItem task) => task.DoneWhen.Any(one => one.Trim().Length > 0);

    /// <summary>Why a pass leaves an idea alone, or null when it fills it.</summary>
    /// <remarks>
    /// Only a plan the person has nothing pending against. A gap-fill writes a
    /// new revision, and a revision is what tells an idea that answers or a
    /// request have been taken in: written over a pending one, it would make
    /// what the person said look read when no plan has read it.
    /// </remarks>
    private static string? IdeaSkipped(IdeaRecord record, IReadOnlyList<IdeaPlanField> missing, bool running)
    {
        if (running)
        {
            return "A round on it is running on this machine.";
        }

        if (IdeaWork.Unanswered(record).Count > 0)
        {
            return "It has questions waiting on an answer. Answer them and refine it; the next plan carries every field.";
        }

        if (record.Request.Trim().Length > 0)
        {
            return "It has a request for the whole plan waiting. Refine it; the next plan carries every field.";
        }

        if (IdeaWork.StageOf(record) != IdeaStage.Proposed)
        {
            return "It has changes the plan has not taken in yet. Refine it; the next plan carries every field.";
        }

        if (!missing.Any(field => field.Fillable))
        {
            return $"A pass cannot write {string.Join(", ", missing.Select(field => field.Name))}. Refine it instead.";
        }

        return null;
    }
}
