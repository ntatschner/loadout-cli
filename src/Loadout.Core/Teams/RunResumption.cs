using System.Text.Json;

namespace Loadout.Core.Teams;

/// <summary>
/// Where an ended run got to, read back from what it wrote, so it can be
/// picked up rather than started over.
/// </summary>
/// <param name="Summary">The run as its journal tells it.</param>
/// <param name="Criteria">
/// What it is judged on: the latest agreed, else what it was started with,
/// else null for a run held to its goal alone.
/// </param>
/// <param name="Proposed">
/// Whether the lead has already been asked to propose criteria, so a resumed
/// run does not ask again.
/// </param>
/// <param name="Branches">
/// Work still on branches of its own, node by node: what the lead asked for
/// and the merge gate has not taken back yet.
/// </param>
/// <param name="Decisions">What each gate node last decided, by node.</param>
/// <param name="LeadSession">
/// The agent session the lead was in, or null for a run written before it was
/// recorded. With it the lead resumes its own conversation; without it a fresh
/// lead is told where the run got to.
/// </param>
/// <param name="LastReport">
/// The lead's last report as a JSON document, for telling a fresh lead where
/// things stand. Null where it never reported.
/// </param>
public sealed record RunResumption(
    RunSummary Summary,
    IReadOnlyList<string>? Criteria,
    bool Proposed,
    IReadOnlyDictionary<string, string> Branches,
    IReadOnlyDictionary<string, string> Decisions,
    string? LeadSession,
    string? LastReport)
{
    /// <summary>The lead's node name: the first node the run launched.</summary>
    public string? Lead => Summary.Nodes.Count > 0 ? Summary.Nodes[0].Node : null;

    /// <summary>
    /// What the lead's next turn is likely to cost, said before it starts, or
    /// null where there is nothing to go on.
    /// </summary>
    /// <param name="spent">What the run has spent so far.</param>
    /// <param name="cap">What it may spend in all, or null for no budget.</param>
    /// <remarks>
    /// <para>
    /// Only for a lead resuming its own session, which carries the whole
    /// conversation into every exchange, so its last turn is the best guess at
    /// its next. A fresh lead starts small and this would overstate it.
    /// </para>
    /// <para>
    /// Said rather than enforced. The budget is checked between rounds, and a
    /// turn that crosses it finishes, because stopping a node mid-turn loses the
    /// work already paid for. One lead turn of a long run cost 15.08 over 14
    /// exchanges against 10.53 left, and nothing said so until it had been spent.
    /// </para>
    /// </remarks>
    public string? LikelyCost(decimal spent, decimal? cap)
    {
        if (LeadSession is not { Length: > 0 } || Lead is not { } lead)
        {
            return null;
        }

        var last = Summary.Turns.LastOrDefault(one =>
            string.Equals(one.Node, lead, StringComparison.Ordinal) && one.CostUsd > 0m);

        if (last is null)
        {
            return null;
        }

        var said =
            $"The lead's last turn cost ${last.CostUsd:0.00} over {last.Exchanges} exchange(s), "
            + "and it carries that whole conversation into the next, so expect about as much.";

        if (cap is { } limit && last.CostUsd > limit - spent)
        {
            said += limit > spent
                ? $" That is more than the ${limit - spent:0.00} the budget has left, so one turn could take the run past it."
                : " The budget is already spent, so any turn takes the run past it.";
        }

        return said;
    }

    /// <summary>
    /// Reads an ended run back, or says why it cannot be picked up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only an ended run. One still going already has everything this would
    /// rebuild, in memory, in the process driving it; a second process picking
    /// it up would be two coordinators answering one lead.
    /// </para>
    /// <para>
    /// A branch counts as still out when a node was launched on it and the run
    /// never merged it. Those are the ones a lead picking the run up has to
    /// know about: they are finished work waiting on the gate, and a lead told
    /// nothing about them asks for the same work again.
    /// </para>
    /// </remarks>
    public static (RunResumption? Resumption, string? Why) Read(IRunJournal journal, string runId)
    {
        ArgumentNullException.ThrowIfNull(journal);

        var read = journal.Read(runId);

        if (read.Failed)
        {
            return (null, read.Error);
        }

        var directory = journal.DirectoryOf(runId);
        var events = read.Value!;
        var summary = RunJournal.Fold(runId, directory, events);

        if (summary.Running)
        {
            return (null,
                $"Run {runId} has not ended. Raise its budget with 'team budget', or say something to "
                + "its lead with 'team message', rather than picking it up.");
        }

        if (summary.Nodes.Count == 0)
        {
            return (null, $"Run {runId} ended before its lead started, so there is nothing to pick up. Run it again instead.");
        }

        IReadOnlyList<string>? criteria = null;
        var proposed = false;

        foreach (var entry in events)
        {
            switch (entry.Kind)
            {
                case "criteria.agreed":
                    criteria = entry.Words("criteria") is { Count: > 0 } agreed ? agreed : criteria;
                    proposed = true;
                    break;

                case "criteria.none":
                    proposed = true;
                    break;
            }
        }

        var lead = summary.Nodes[0].Node;

        // Criteria somebody typed with --done-when are in the lead's brief and
        // nowhere else.
        criteria ??= BriefCriteria(Path.Combine(directory, $"brief-{lead}.json"));

        if (criteria is { Count: > 0 })
        {
            proposed = true;
        }

        var branches = new Dictionary<string, string>(StringComparer.Ordinal);
        var decisions = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var node in summary.Nodes)
        {
            if (node.Branch is { Length: > 0 } branch
                && !summary.Merged.Contains(branch, StringComparer.Ordinal))
            {
                branches[node.Node] = branch;
            }

        }

        // From the reports themselves, which is where a gate node's decision
        // is: the journal records that a node reported, not the one word it
        // handed back. Oldest round first, so the latest decision wins, the
        // same as it did while the run was going.
        foreach (var (file, _) in ReportFiles(directory))
        {
            if (Safely(file) is { Length: > 0 } text
                && ReportReader.Read(text) is { Succeeded: true, Value: { } report })
            {
                foreach (var deliverable in report.Deliverables
                    .Where(one => one.Kind == Loadout.Models.Teams.DeliverableKind.Decision))
                {
                    decisions[BaseNode(report.Node)] = deliverable.Ref.Trim();
                }
            }
        }

        var last = Path.Combine(directory, "final-report.json");

        return (new RunResumption(
            summary,
            criteria,
            proposed,
            branches,
            decisions,
            summary.Nodes[0].Session,
            File.Exists(last) ? Safely(last) : null), null);
    }

    /// <summary>A run's worker reports, oldest round first.</summary>
    private static IEnumerable<(string File, int Round)> ReportFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "report-*.json")
            .Select(file =>
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var dash = name.LastIndexOf('-');

                return (file, dash > 0 && int.TryParse(name[(dash + 1)..], out var round) ? round : 0);
            })
            .OrderBy(one => one.Item2)
            .ToList();
    }

    private static string BaseNode(string node) =>
        node.IndexOf('/', StringComparison.Ordinal) is var slash and > 0 ? node[..slash] : node;

    private static string? Safely(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string>? BriefCriteria(string path)
    {
        if (Safely(path) is not { Length: > 0 } text)
        {
            return null;
        }

        try
        {
            using var brief = JsonDocument.Parse(text);

            if (brief.RootElement.ValueKind != JsonValueKind.Object
                || !brief.RootElement.TryGetProperty("criteria", out var listed)
                || listed.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var criteria = listed.EnumerateArray()
                .Where(one => one.ValueKind == JsonValueKind.String)
                .Select(one => one.GetString()!.Trim())
                .Where(one => one.Length > 0)
                .ToList();

            return criteria.Count > 0 ? criteria : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
