using Loadout.Models.Teams;

namespace Loadout.Core.Teams;

/// <summary>One run a prune is not taking, and the reason it is not.</summary>
/// <param name="Run">The run.</param>
/// <param name="Because">Why it stays, in words fit to print beside it.</param>
public sealed record RunKept(RunSummary Run, string Because);

/// <summary>What a prune would do, before it does any of it.</summary>
/// <param name="Forgetting">The runs it would forget, newest first.</param>
/// <param name="Keeping">The runs it would leave, each with its reason.</param>
public sealed record Pruning(
    IReadOnlyList<RunSummary> Forgetting,
    IReadOnlyList<RunKept> Keeping);

/// <summary>
/// Which of the runs on this machine a prune takes, and which it leaves.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the journal, and pure, because this is the part worth being
/// sure about. Deleting the wrong run is not recoverable — the journal is the
/// only copy — and a rule that lives inside a command can only be checked by
/// running the command against real runs, which is the one way of checking it
/// nobody wants to be wrong about.
/// </para>
/// <para>
/// The conditions are an intersection, not a union. <c>--keep 10 --older-than
/// 30d --failed</c> means "take the failed ones older than thirty days, but
/// never go below the ten newest", which is what somebody typing them means
/// and is also the most cautious reading available. Reading them as a union
/// would make each option quietly widen the others.
/// </para>
/// </remarks>
public static class RunRetention
{
    /// <summary>Works out what a prune would take.</summary>
    /// <param name="runs">Every run on this machine, in any order.</param>
    /// <param name="keep">How many of the newest to keep whatever else is true, or null.</param>
    /// <param name="olderThan">Take nothing younger than this, or null for any age.</param>
    /// <param name="now">The clock, so this can be tested without one.</param>
    /// <param name="includeUnmerged">
    /// Take runs that left a branch nothing merged. Off by default: the
    /// journal is what says which run produced a branch, so forgetting it
    /// leaves the branch with nothing to explain it.
    /// </param>
    /// <param name="outcomes">
    /// Take only runs that ended one of these ways, or null for any ending.
    /// A run whose ending nothing recognises is never taken by this: asking
    /// for the failed ones is asking for the ones known to have failed, not
    /// for everything that could not be ruled out.
    /// </param>
    public static Pruning Choose(
        IReadOnlyList<RunSummary> runs,
        int? keep,
        TimeSpan? olderThan,
        DateTimeOffset now,
        bool includeUnmerged = false,
        IReadOnlyCollection<RunOutcome>? outcomes = null)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var newestFirst = runs
            .OrderByDescending(run => run.Started)
            .ThenByDescending(run => run.RunId, StringComparer.Ordinal)
            .ToList();

        var forgetting = new List<RunSummary>();
        var keeping = new List<RunKept>();

        for (var index = 0; index < newestFirst.Count; index++)
        {
            var run = newestFirst[index];

            // A live run is reading its own directory for the answers it is
            // waiting on. Nothing about how old it is or how many there are
            // gets past this.
            if (run.Running)
            {
                keeping.Add(new RunKept(run, "still going"));

                continue;
            }

            // Asked for first, because it is the thing somebody typing
            // --failed is selecting on: a run left alone for any other reason
            // reads better said as "ended done" than as one of the newest ten.
            if (outcomes is { Count: > 0 })
            {
                var outcome = RunOutcomes.Of(run);

                if (outcome is null)
                {
                    keeping.Add(new RunKept(run, "its ending is not one this knows how to file"));

                    continue;
                }

                if (!outcomes.Contains(outcome.Value))
                {
                    keeping.Add(new RunKept(run, $"ended {RunOutcomes.Spell(outcome.Value)}"));

                    continue;
                }
            }

            if (keep is { } newest && index < newest)
            {
                keeping.Add(new RunKept(run, $"one of the newest {newest}"));

                continue;
            }

            if (olderThan is { } age && now - When(run) < age)
            {
                keeping.Add(new RunKept(run, $"newer than {Spell(age)}"));

                continue;
            }

            if (!includeUnmerged && Unmerged(run) is { Count: > 0 } branches)
            {
                keeping.Add(new RunKept(
                    run,
                    $"left {(branches.Count == 1 ? "a branch" : $"{branches.Count} branches")} "
                    + "nothing merged"));

                continue;
            }

            forgetting.Add(run);
        }

        return new Pruning(forgetting, keeping);
    }

    /// <summary>Branches the run made and never got merged.</summary>
    public static IReadOnlyList<string> Unmerged(RunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return [.. run.Branches.Where(branch => !run.Merged.Contains(branch, StringComparer.Ordinal))];
    }

    /// <summary>
    /// When a run stopped being of interest.
    /// </summary>
    /// <remarks>
    /// Its ending where it has one, and otherwise when it began. A run that
    /// never wrote an ending was killed or crashed; dating it from its start
    /// is the only honest answer, and it errs towards taking it, which for a
    /// run that recorded no outcome is the right direction.
    /// </remarks>
    private static DateTimeOffset When(RunSummary run) => run.Finished ?? run.Started;

    /// <summary>A span as the option that asked for it was written.</summary>
    private static string Spell(TimeSpan span) =>
        span.TotalDays >= 1 ? $"{span.TotalDays:0.##}d"
        : span.TotalHours >= 1 ? $"{span.TotalHours:0.##}h"
        : $"{span.TotalMinutes:0.##}m";
}
