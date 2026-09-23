using System.Globalization;

namespace Loadout.Core.Teams;

/// <summary>What a run is costing, as against what it has cost.</summary>
/// <param name="Spent">What it has cost so far.</param>
/// <param name="PerMinute">What it is costing per minute, over its whole life.</param>
/// <param name="Projected">
/// What it would cost if the rounds it has left cost what the rounds it has
/// used cost. Null where nothing has been used yet, or where there is no limit
/// to project against.
/// </param>
/// <param name="Budget">What the team said it may spend, if it said.</param>
/// <param name="Overrunning">Whether the projection goes past the budget.</param>
public sealed record Burn(
    decimal Spent,
    decimal PerMinute,
    decimal? Projected,
    decimal? Budget,
    bool Overrunning);

/// <summary>Where a run's time went, by round.</summary>
/// <param name="Round">Which round.</param>
/// <param name="Seconds">How long it took, or has taken.</param>
/// <param name="Requests">How many nodes the lead asked for in it.</param>
/// <param name="Running">Whether it is the one still going.</param>
public sealed record RoundCost(int Round, int Seconds, int Requests, bool Running);

/// <summary>Where a run's time went, by node.</summary>
/// <param name="Node">Which node.</param>
/// <param name="Role">What it was.</param>
/// <param name="Seconds">How long it was up.</param>
/// <param name="CostUsd">What it cost.</param>
public sealed record NodeCost(string Node, string Role, int Seconds, decimal CostUsd);

/// <summary>The shapes a run goes wrong in.</summary>
/// <param name="Denials">Tool calls a node asked for and was refused.</param>
/// <param name="Rejected">Reports the checker would not accept.</param>
/// <param name="Retried">Turns that had to be asked for a second time.</param>
/// <param name="QuietRounds">Rounds in a row that asked for nothing.</param>
/// <param name="Conflicts">Branches that would not merge.</param>
/// <remarks>
/// These are about the team file rather than the run. One run refused twenty
/// tool calls is a bad afternoon; every run of one role refusing twenty is a
/// role whose permissions are written wrong, and the second only shows up
/// when the first is counted.
/// </remarks>
public sealed record Trouble(int Denials, int Rejected, int Retried, int QuietRounds, int Conflicts)
{
    /// <summary>Whether anything went wrong at all.</summary>
    public bool Any => Denials + Rejected + Retried + QuietRounds + Conflicts > 0;
}

/// <summary>What a run's numbers say, as against what they are.</summary>
/// <param name="Money">What it is costing, and where that ends up.</param>
/// <param name="Rounds">Where the time went, by round.</param>
/// <param name="Nodes">Where the time went, by node.</param>
/// <param name="Trouble">The shapes it went wrong in.</param>
public sealed record RunNumbers(
    Burn Money,
    IReadOnlyList<RoundCost> Rounds,
    IReadOnlyList<NodeCost> Nodes,
    Trouble Trouble);

/// <summary>
/// The four numbers that change a decision, rather than the many that
/// decorate a page.
/// </summary>
/// <remarks>
/// <para>
/// What a run has cost is on the page already and nobody acts on it. What it
/// is costing per minute, and where that ends up if it uses the rounds it has
/// left, is the number somebody stops a run over - so it is the one computed
/// here rather than left for a person to do in their head at three in the
/// morning.
/// </para>
/// <para>
/// All of it from what the run wrote down while it happened. Nothing is mined
/// out of an agent's own transcript files afterwards: those formats have
/// changed before, and a figure that quietly becomes wrong when somebody else
/// ships a release is worse than no figure.
/// </para>
/// </remarks>
public static class RunMetrics
{
    /// <summary>A run's numbers, against a clock.</summary>
    public static RunNumbers For(RunSummary run, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new RunNumbers(
            Money(run, now),
            [.. run.RoundsTaken.Select(round => new RoundCost(
                round.Number,
                (int)round.Took(now).TotalSeconds,
                round.Requests,
                round.Ended is null && run.Running))],
            [.. run.Nodes.Select(node => new NodeCost(
                node.Node,
                node.Role,
                node.Took is { } took ? (int)took.TotalSeconds : 0,
                node.CostUsd))],
            Went(run));
    }

    /// <summary>What it is costing, and where that ends up.</summary>
    /// <remarks>
    /// Projected from rounds rather than from the clock. A run does not spend
    /// evenly through time - it spends when a node is up and nothing while the
    /// lead thinks - but it does spend roughly per round, because a round is
    /// what buys nodes.
    /// </remarks>
    public static Burn Money(RunSummary run, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);

        var elapsed = run.Running ? now - run.Started : run.Elapsed;
        var minutes = Math.Max(elapsed.TotalMinutes, 1d / 60d);

        var perMinute = decimal.Round(run.CostUsd / (decimal)minutes, 4);

        decimal? projected = run.Rounds > 0 && run.RoundLimit > run.Rounds && run.Running
            ? decimal.Round(run.CostUsd / run.Rounds * run.RoundLimit, 2)
            : null;

        return new Burn(
            run.CostUsd,
            perMinute,
            projected,
            run.BudgetUsd,
            run.BudgetUsd is { } cap && (projected ?? run.CostUsd) > cap);
    }

    /// <summary>The shapes it went wrong in.</summary>
    public static Trouble Went(RunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new Trouble(
            run.Nodes.Sum(node => node.Denials),

            // Its own word for it. "accepted" is the one that is not trouble,
            // and a turn that produced no report at all is counted where it
            // was retried rather than twice.
            run.Turns.Count(turn => turn.Outcome is { Length: > 0 } outcome
                && !string.Equals(outcome, "accepted", StringComparison.OrdinalIgnoreCase)),

            run.Turns.Count(turn => turn.Attempt > 1),
            run.QuietRounds,
            run.Conflicts);
    }

    /// <summary>What a rate reads as, for a person rather than a chart.</summary>
    /// <remarks>
    /// Per hour once per minute goes under a penny, because "$0.0041 a minute"
    /// is a number nobody has a feel for and "25 cents an hour" is one
    /// everybody does.
    /// </remarks>
    public static string Rate(decimal perMinute) =>
        perMinute >= 0.01m
            ? string.Create(CultureInfo.InvariantCulture, $"${perMinute:0.00} a minute")
            : string.Create(CultureInfo.InvariantCulture, $"${perMinute * 60m:0.00} an hour");
}
