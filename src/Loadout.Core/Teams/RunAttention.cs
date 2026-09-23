namespace Loadout.Core.Teams;

/// <summary>Why a run wants somebody.</summary>
public enum AttentionKind
{
    /// <summary>It has stopped and asked a question.</summary>
    Asking,

    /// <summary>It is taking rounds and asking for nothing.</summary>
    GoingNowhere,

    /// <summary>A node has said nothing for far longer than it usually takes.</summary>
    Quiet,

    /// <summary>It is spending faster than it can afford to finish.</summary>
    Spending,
}

/// <summary>One reason a run is in the rail.</summary>
/// <param name="Kind">Which reason.</param>
/// <param name="Detail">What to show, as a sentence somebody can act on.</param>
/// <param name="Clears">What would take it out of the rail, said plainly.</param>
public sealed record Attention(AttentionKind Kind, string Detail, string Clears);

/// <summary>
/// What the person running these teams needs to look at.
/// </summary>
/// <remarks>
/// <para>
/// A view rather than a status filter, and the difference is the whole design:
/// <strong>every reason here has to clear itself</strong>. Temporal's Task
/// Failures View flags a workflow after five consecutive failed tasks and
/// unflags it the moment one succeeds, and that self-clearing is what stops
/// such a list becoming a pile nobody reads. A rail that only ever grows is
/// worse than no rail, because it teaches somebody to skim the one thing that
/// was meant to be unskimmable.
/// </para>
/// <para>
/// So each reason below is computed fresh from the run's current state, never
/// remembered, and each says what would clear it. Nothing is sticky, nothing
/// is dismissed, and nothing needs a person to tidy it up.
/// </para>
/// <para>
/// Pure, over a summary and a clock, so the rules can be argued with in tests
/// rather than only seen on a screen.
/// </para>
/// </remarks>
public static class RunAttention
{
    /// <summary>
    /// How much longer than its own usual turn a node may be silent before it
    /// is worth mentioning.
    /// </summary>
    /// <remarks>
    /// Against the node's own average rather than a fixed duration, because a
    /// reviewer reading one diff and an implementer running a suite have
    /// nothing in common. Three times is slow enough to be unusual and not so
    /// slow that a genuinely long turn cries wolf.
    /// </remarks>
    public const int QuietFactor = 3;

    /// <summary>
    /// The one state a node is in while a process of its own is running.
    /// </summary>
    /// <remarks>
    /// Written down here because the rule that matters is "could this be
    /// speaking", and answering it by listing the states where it could not
    /// is how the lead came to be reported as quiet for most of every run.
    /// The journal sets this on <c>node.launched</c> and replaces it on
    /// <c>node.reported</c> or <c>node.ended</c>.
    /// </remarks>
    public const string Working = "working";

    /// <summary>
    /// The least a node may be silent before any of this applies.
    /// </summary>
    /// <remarks>
    /// Without a floor, a node whose turns average four seconds would be
    /// reported as quiet after twelve, which is noise.
    /// </remarks>
    public static readonly TimeSpan QuietFloor = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How much of its budget a run may be projected to overrun before it is
    /// worth saying so.
    /// </summary>
    /// <remarks>
    /// A projection is arithmetic on a guess, so a run projected a penny over
    /// is not news. Ten per cent is enough to mean something.
    /// </remarks>
    public const decimal Overrun = 1.1m;

    /// <summary>Why this run wants somebody, or nothing at all.</summary>
    /// <param name="run">The run, as its journal reads back.</param>
    /// <param name="now">The clock, so this can be argued with in a test.</param>
    public static IReadOnlyList<Attention> For(RunSummary run, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);

        // A finished run wants nothing. Whatever was true while it ran stopped
        // being true when it stopped, and a rail full of yesterday is the
        // failure this is designed against.
        if (!run.Running)
        {
            return [];
        }

        var reasons = new List<Attention>();

        foreach (var gate in run.Waiting)
        {
            reasons.Add(new Attention(
                AttentionKind.Asking,
                gate.Asking,
                "you answer it"));
        }

        // One round that asked for nothing. Two ends the run, so this is the
        // only chance to say anything while it can still be steered.
        if (run.QuietRounds > 0)
        {
            reasons.Add(new Attention(
                AttentionKind.GoingNowhere,
                $"The lead has taken {Rounds(run.QuietRounds)} without asking for anything. "
                + "Another and the run stops itself.",
                "it asks for a node, or finishes"));
        }

        foreach (var node in run.Nodes)
        {
            // Silent because it is waiting on you, which is said above.
            if (run.Waiting.Any(gate => string.Equals(gate.Node, node.Node, StringComparison.Ordinal)))
            {
                continue;
            }

            if (Silence(node, now) is { } silent)
            {
                reasons.Add(silent);
            }
        }

        if (Money(run, now) is { } money)
        {
            reasons.Add(money);
        }

        return reasons;
    }

    /// <summary>
    /// A node that has said nothing for far longer than it usually takes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Usually a hung process or a rate limit, and both look exactly like
    /// thinking from outside. It needs turns to have an average at all, so the
    /// first turn of any node is never reported - which is correct: nothing is
    /// yet known about how long this one takes.
    /// </para>
    /// <para>
    /// Only of a node that is working, because that is the only state in which
    /// being silent means anything. This used to name three states it would
    /// not report on - done, reported and failed - which missed every state a
    /// node reaches by its process ending, and one of those is where the lead
    /// sits for most of a run: it asks for workers, its turn ends, and its last
    /// word is as old as the dispatch while the workers take twenty minutes
    /// doing what it asked for. That is the run working, and it was reported as
    /// the lead having gone quiet, to whoever had notices turned on.
    /// </para>
    /// <para>
    /// <c>reported</c> was never one of them. A node's state comes from its own
    /// report - done, blocked, failed, needs-decision - or from the journal:
    /// working, then ended. Nothing has ever written "reported", so that arm of
    /// the condition has never once been true.
    /// </para>
    /// </remarks>
    private static Attention? Silence(RunNode node, DateTimeOffset now)
    {
        if (node.Turns < 1 || node.Took is not { } took || node.State is not Working)
        {
            return null;
        }

        var average = took / node.Turns;
        var allowed = average * QuietFactor;

        if (allowed < QuietFloor)
        {
            allowed = QuietFloor;
        }

        var silent = now - node.LastSeen;

        return silent <= allowed
            ? null
            : new Attention(
                AttentionKind.Quiet,
                $"{node.Node} has said nothing for {Said(silent)}, and its turns usually take {Said(average)}.",
                "it says anything at all");
    }

    /// <summary>
    /// A run spending faster than it can afford to finish.
    /// </summary>
    /// <remarks>
    /// Projected from what it has spent over what it has taken, times what it
    /// may still take. A ceiling on a guess, and said as one - the alternative
    /// is a number that reads like a promise.
    /// </remarks>
    private static Attention? Money(RunSummary run, DateTimeOffset now)
    {
        if (run.BudgetUsd is not { } budget || budget <= 0m || run.CostUsd <= 0m)
        {
            return null;
        }

        if (run.CostUsd >= budget)
        {
            return new Attention(
                AttentionKind.Spending,
                $"It has spent {run.CostUsd:0.00} of its {budget:0.00} budget.",
                "it finishes, or you stop it");
        }

        var elapsed = run.Elapsed;

        if (run.AtMostRemaining is not { } left || elapsed <= TimeSpan.Zero)
        {
            return null;
        }

        var perSecond = run.CostUsd / (decimal)elapsed.TotalSeconds;
        var projected = run.CostUsd + (perSecond * (decimal)left.TotalSeconds);

        return projected <= budget * Overrun
            ? null
            : new Attention(
                AttentionKind.Spending,
                $"At this rate it reaches about {projected:0.00} against a budget of {budget:0.00}.",
                "it slows down, or finishes under");
    }

    private static string Rounds(int rounds) =>
        rounds == 1 ? "a round" : $"{rounds} rounds";

    /// <summary>A duration as somebody would say it.</summary>
    private static string Said(TimeSpan span) =>
        span < TimeSpan.FromMinutes(1)
            ? $"{span.TotalSeconds:F0} seconds"
            : span < TimeSpan.FromHours(1)
                ? $"{span.TotalMinutes:F0} minutes"
                : $"{span.TotalHours:F1} hours";
}
