using System.Globalization;
using Loadout.Core.Teams;
using Loadout.Models.Teams;

namespace Loadout.Core.Triggers;

/// <summary>
/// A per-run budget and a spend cap offered for a rule, worked out from what
/// the team's runs have actually spent.
/// </summary>
/// <param name="BudgetUsd">What each run may spend.</param>
/// <param name="CapUsd">What the rule's runs may spend in its window.</param>
/// <param name="Here">How many of the runs counted came from the projects the rule watches.</param>
/// <param name="Elsewhere">How many came from the team's runs on other projects, to make up the number.</param>
/// <param name="Limited">How many of them ran out of rounds or money, which makes the figure a floor.</param>
/// <param name="Starter">Whether there was no history, so this is the fixed starter figure.</param>
/// <param name="Scoped">Whether the rule watches only some projects, so the split means something.</param>
public sealed record BudgetProposal(
    decimal BudgetUsd,
    decimal CapUsd,
    int Here,
    int Elsewhere,
    int Limited,
    bool Starter,
    bool Scoped)
{
    /// <summary>How many finished runs the figure is drawn from.</summary>
    public const int Sample = 20;

    /// <summary>The figure for a team with no history: cheap enough to accept without looking.</summary>
    public const decimal StarterBudgetUsd = 2m;

    /// <summary>The starter cap, five runs' worth.</summary>
    public const decimal StarterCapUsd = 10m;

    /// <summary>
    /// How many runs' worth the proposed cap allows, which is the starter's
    /// own ratio: a cap set from the budget is one line of arithmetic to
    /// explain, and five runs a day is room for a busy day and not a loop.
    /// </summary>
    public const int RunsInCap = 5;

    /// <summary>How many runs it was drawn from.</summary>
    public int Counted => Here + Elsewhere;

    /// <summary>The working, in words, so the figure can be checked rather than taken on trust.</summary>
    public string Working()
    {
        if (Starter)
        {
            return $"${BudgetUsd:0} a run and ${CapUsd:0} in the window: a starter figure, because the team has no "
                + "finished runs to work from yet. Edit the rule after it has run to be offered one from its runs.";
        }

        var from = Scoped
            ? $"{Counted} finished run(s) ({Here} here, {Elsewhere} elsewhere)"
            : $"{Counted} finished run(s)";
        var thin = Counted < Sample ? $", fewer than the {Sample} it would like" : string.Empty;
        var floor = Limited > 0
            ? $" A floor: {Limited} of them ran out of rounds or money, so they would have spent more."
            : string.Empty;

        return $"${BudgetUsd:0} a run, the 90th percentile of the last {from}{thin}, rounded up to a whole dollar; "
            + $"${CapUsd:0} in the window is {RunsInCap} runs' worth.{floor}";
    }

    /// <summary>
    /// The proposal for a team, drawing from the runs on <paramref name="projects"/>
    /// first and the team's other runs after, or from every run when it names none.
    /// </summary>
    /// <param name="team">The team the rule starts.</param>
    /// <param name="projects">The projects the rule watches, or empty for all.</param>
    /// <param name="runs">Runs on this machine, in any order.</param>
    /// <remarks>
    /// <para>
    /// Done and failed runs count, and so do those that ran out of rounds or
    /// money, were blocked, or finished without saying how: each has written
    /// a finish, so what it spent has landed. A run waiting on a decision may
    /// spend more once answered, and one somebody stopped says nothing about
    /// what the work costs, so neither counts. Neither does a run still going.
    /// </para>
    /// <para>
    /// The 90th percentile, so nearly every run fits and one runaway does not
    /// set the figure. A limited run is counted at what it spent and the
    /// figure is labelled a floor, not raised: the figure stays one Loadout can
    /// explain from landed spend.
    /// </para>
    /// </remarks>
    public static BudgetProposal For(string team, IReadOnlyCollection<string> projects, IEnumerable<RunSummary> runs)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(runs);

        var finished = runs
            .Where(one => string.Equals(one.Team, team, StringComparison.OrdinalIgnoreCase)
                && RunOutcomes.Of(one) is RunOutcome.Done or RunOutcome.Failed or RunOutcome.Limited
                    or RunOutcome.Blocked or RunOutcome.Unrecorded)
            .OrderByDescending(one => one.Finished ?? one.Started)
            .ToList();

        bool Watched(RunSummary one) =>
            projects.Contains(one.Project ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        var scoped = projects.Count > 0;
        var here = scoped ? finished.Where(Watched).Take(Sample).ToList() : finished.Take(Sample).ToList();
        var elsewhere = scoped ? finished.Where(one => !Watched(one)).Take(Sample - here.Count).ToList() : [];
        var counted = here.Concat(elsewhere).ToList();

        if (counted.Count == 0)
        {
            return new BudgetProposal(StarterBudgetUsd, StarterCapUsd, 0, 0, 0, true, scoped);
        }

        var spends = counted.Select(one => one.CostUsd).Order().ToList();
        var rank = (int)Math.Ceiling(0.9 * spends.Count) - 1;
        var budget = Math.Max(1m, Math.Ceiling(spends[rank]));

        return new BudgetProposal(
            budget,
            budget * RunsInCap,
            here.Count,
            elsewhere.Count,
            counted.Count(one => RunOutcomes.Of(one) == RunOutcome.Limited),
            false,
            scoped);
    }

    /// <summary>A dollar figure as the command line takes it back.</summary>
    public static string Spell(decimal usd) => usd.ToString("0.##", CultureInfo.InvariantCulture);
}
