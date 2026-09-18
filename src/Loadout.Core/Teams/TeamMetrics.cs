namespace Loadout.Core.Teams;

/// <summary>How one role has done, across every run it has been in.</summary>
/// <param name="Role">The role, as a team file names it.</param>
/// <param name="Model">The model it ran on, or null where the runs did not say.</param>
/// <param name="Runs">How many runs it appeared in.</param>
/// <param name="Turns">How many exchanges it had in all.</param>
/// <param name="CostUsd">What it cost in all.</param>
/// <param name="Accepted">Reports the checker took.</param>
/// <param name="Rejected">Reports the checker would not take.</param>
/// <param name="Denials">Tool calls it asked for and was refused.</param>
/// <param name="Seconds">How long it was up for, added together.</param>
public sealed record RolePerformance(
    string Role,
    string? Model,
    int Runs,
    int Turns,
    decimal CostUsd,
    int Accepted,
    int Rejected,
    int Denials,
    int Seconds)
{
    /// <summary>What share of its reports were taken, or null where it made none.</summary>
    public double? Accepting =>
        Accepted + Rejected > 0 ? (double)Accepted / (Accepted + Rejected) : null;

    /// <summary>What one run of this role costs, on average.</summary>
    public decimal Each => Runs > 0 ? decimal.Round(CostUsd / Runs, 4) : 0m;

    /// <summary>How many tool calls it is refused per run, on average.</summary>
    public double Refusals => Runs > 0 ? (double)Denials / Runs : 0;
}

/// <summary>
/// What the machine has learned about its own team file.
/// </summary>
/// <remarks>
/// <para>
/// One run refusing twenty tool calls is a bad afternoon. Every run of one role
/// refusing twenty is a role whose permissions are written wrong, and the
/// second only shows up when the first is counted across runs. Same for cost:
/// what a reviewer costs once is noise, and what it costs every time is a line
/// in a team file somebody should change.
/// </para>
/// <para>
/// Split by model as well as by role, because the question worth answering is
/// not "what does the reviewer cost" but "what does the reviewer cost on this
/// model rather than that one, and does it finish". Runs recorded before the
/// model was written down are grouped under no model at all rather than
/// guessed at - a comparison against a model nobody knows is not a comparison.
/// </para>
/// </remarks>
public static class TeamMetrics
{
    /// <summary>How the roles have done, dearest first.</summary>
    /// <param name="journal">Where the runs are.</param>
    /// <param name="most">How many of the most recent runs to read.</param>
    public static IReadOnlyList<RolePerformance> Across(IRunJournal journal, int most = 40)
    {
        ArgumentNullException.ThrowIfNull(journal);

        var found = new Dictionary<(string Role, string? Model), RolePerformance>();

        foreach (var id in journal.List(Math.Clamp(most, 1, 200)))
        {
            var read = journal.Summarise(id);

            if (read.Failed || read.Value is not { } run)
            {
                continue;
            }

            foreach (var node in run.Nodes)
            {
                var key = (node.Role, node.Model);

                var turns = run.Turns.Where(turn =>
                    string.Equals(turn.Node, node.Node, StringComparison.Ordinal)).ToList();

                var was = found.TryGetValue(key, out var already)
                    ? already
                    : new RolePerformance(node.Role, node.Model, 0, 0, 0m, 0, 0, 0, 0);

                found[key] = was with
                {
                    Runs = was.Runs + 1,
                    Turns = was.Turns + node.Turns,
                    CostUsd = was.CostUsd + node.CostUsd,

                    // Its own word for it, and a turn that produced no report
                    // at all counts as neither: it is a turn that did not
                    // finish, which is a different thing from one whose report
                    // was refused.
                    Accepted = was.Accepted + turns.Count(turn =>
                        string.Equals(turn.Outcome, "accepted", StringComparison.OrdinalIgnoreCase)),

                    Rejected = was.Rejected + turns.Count(turn =>
                        turn.Outcome is { Length: > 0 }
                        && !string.Equals(turn.Outcome, "accepted", StringComparison.OrdinalIgnoreCase)),

                    Denials = was.Denials + node.Denials,
                    Seconds = was.Seconds + (node.Took is { } took ? (int)took.TotalSeconds : 0),
                };
            }
        }

        // Dearest first, because the row somebody acts on is the expensive one
        // and a table sorted by name buries it wherever the alphabet puts it.
        return [.. found.Values
            .OrderByDescending(one => one.CostUsd)
            .ThenBy(one => one.Role, StringComparer.OrdinalIgnoreCase)];
    }
}
