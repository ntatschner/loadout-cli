using System.Text;
using Loadout.Models;
using Loadout.Models.Results;

namespace Loadout.Core.Teams;

/// <summary>
/// What a run started to carry on from another one is told about it.
/// </summary>
/// <param name="Run">The run carried on from.</param>
/// <param name="Lead">What its lead last said, or null.</param>
/// <param name="Unmerged">Work it left on branches its merge gate never took, by node.</param>
/// <remarks>
/// <para>
/// Only the run immediately before, never the whole chain. Each run's own
/// Previously already said what the one before it was told, and a lead given
/// the history of every attempt in full would spend its first turn reading.
/// </para>
/// <para>
/// One reading for every way of carrying on - carry on, a raise, and a
/// re-queued task the queue starts later - so the lead is told the same
/// things whichever it was.
/// </para>
/// </remarks>
public sealed record RunFollowing(RunSummary Run, BlockedLead? Lead, IReadOnlyDictionary<string, string> Unmerged)
{
    /// <summary>Reads the run to carry on from, or says why it cannot be.</summary>
    public static OperationResult<RunFollowing> Read(IRunJournal journal, string runId)
    {
        ArgumentNullException.ThrowIfNull(journal);

        var summary = journal.Summarise(runId);

        if (summary.Failed)
        {
            return OperationResult<RunFollowing>.Fail(
                $"There is nothing to carry on from: {summary.Error}", summary.ExitCode);
        }

        var run = summary.Value!;

        var unmerged = run.Nodes
            .Where(node => node.Branch is { Length: > 0 } branch && !run.Merged.Contains(branch, StringComparer.Ordinal))
            .GroupBy(node => node.Node, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Branch!, StringComparer.Ordinal);

        return OperationResult<RunFollowing>.Ok(new RunFollowing(run, BlockedRunReader.Lead(run), unmerged));
    }

    /// <summary>The section that goes at the end of the new lead's first brief.</summary>
    public string Previously()
    {
        var text = new StringBuilder();
        var how = BlockedRuns.KindOf(Run with { Resolved = null }) ?? (Run.Running ? "dead" : "short");

        text.AppendLine("## Previously").AppendLine();
        text.AppendLine(
            $"This run carries on from run {Run.RunId}, which worked on the same goal and ended {how}"
            + (Run.Ended is { Length: > 0 } ended ? $": {ended}." : "."));
        text.AppendLine(
            "A person looked at how it ended and chose to carry on. Start from where it got to rather than "
            + "from the beginning, and do not redo what it finished.").AppendLine();

        if (Lead?.Summary is { } summary)
        {
            text.AppendLine($"- Its lead last said: {summary}");
        }

        if (Lead?.Blocker is { } blocker)
        {
            text.AppendLine($"- What stopped it: {blocker}");
        }

        if (Lead?.UnblockedBy is { } unblock)
        {
            text.AppendLine($"- What it said would unblock it: {unblock}");
        }

        foreach (var question in Lead?.Questions ?? [])
        {
            text.AppendLine($"- It asked: {question}");
        }

        if (Run.Outstanding is { Count: > 0 } unmet)
        {
            text.AppendLine($"- Criteria it had not met: {string.Join("; ", unmet.Select(one => one.Criterion))}");
        }

        var stuck = Run.Stuck.Select(one => one.Node).ToHashSet(StringComparer.Ordinal);

        foreach (var (node, branch) in Unmerged)
        {
            if (stuck.Contains(node))
            {
                text.AppendLine($"- {node} went silent twice and was stopped before it finished, leaving branch `{branch}` unfinished. Merge it if it got far enough, or have it finished.");
                continue;
            }

            text.AppendLine($"- {node} left work on branch `{branch}`, which was never merged. Look at it before asking for the same work again.");
        }

        if (Run.Resolved is { } decided)
        {
            if (decided.Answer is { Length: > 0 } answer)
            {
                text.AppendLine($"- What the person said when they chose to carry on: {answer}");
            }

            if (decided.Figure is { } figure && decided.Cap is { Length: > 0 } cap)
            {
                text.AppendLine(cap == "rounds"
                    ? $"- It ran out of rounds after {Run.Rounds}. This run has {figure:0} of its own."
                    : $"- It ran out of money after spending ${Run.CostUsd:0.00}. This run may spend ${figure:0.00}.");
            }
        }

        return text.ToString();
    }
}
