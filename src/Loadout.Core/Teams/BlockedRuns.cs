using System.Globalization;
using Loadout.Models.Teams;

namespace Loadout.Core.Teams;

/// <summary>What a person can do about a run that ended short of its done-when.</summary>
public enum BlockedAction
{
    /// <summary>Start the next run where this one left off, telling its lead what stopped this one and what you said.</summary>
    CarryOn,

    /// <summary>Carry on with more of the rounds or money this one ran out of. Only for a limited run.</summary>
    Raise,

    /// <summary>Put the task back at the front of its team's queue for the next window.</summary>
    Requeue,

    /// <summary>Change the task's wording or done-when, then put it back at the front of the queue.</summary>
    EditRequeue,

    /// <summary>File the run as dealt with and put the task back to open, for a person to pick up.</summary>
    SetAside,

    /// <summary>File the run as dealt with and drop the task.</summary>
    Drop,
}

/// <summary>Which limit a limited run ran out of.</summary>
public enum BlockedCap
{
    /// <summary>It did not run out of anything.</summary>
    None,

    /// <summary>The rounds it was given.</summary>
    Rounds,

    /// <summary>The money it was given.</summary>
    Budget,
}

/// <summary>What somebody decided to do about a run that ended short of its done-when.</summary>
/// <param name="At">When.</param>
/// <param name="Action">The action, as it is spelt: see <see cref="BlockedActions.Spell"/>.</param>
/// <param name="By">Who decided.</param>
/// <param name="Answer">What they said, for the next lead, or null.</param>
/// <param name="Next">The run started to carry on from it, or null where none was.</param>
/// <param name="Cap">For a raise, which limit was raised: <c>rounds</c> or <c>budget</c>.</param>
/// <param name="Figure">For a raise, the figure the next run was given.</param>
public sealed record RunResolution(
    DateTimeOffset At,
    string Action,
    string By,
    string? Answer = null,
    string? Next = null,
    string? Cap = null,
    decimal? Figure = null);

/// <summary>What the lead last said about why it stopped, read from its final report.</summary>
/// <param name="Blocker">What stopped the work, or null where it never said.</param>
/// <param name="UnblockedBy">What it said would unblock it, or null.</param>
/// <param name="Questions">The decisions it asked a person for.</param>
/// <param name="Next">What it said should happen next, or null.</param>
/// <param name="Summary">What it said happened, or null.</param>
public sealed record BlockedLead(
    string? Blocker,
    string? UnblockedBy,
    IReadOnlyList<string> Questions,
    string? Next = null,
    string? Summary = null)
{
    /// <summary>Reads it from a report, or null where there is no report to read.</summary>
    public static BlockedLead? From(Report? report) =>
        report is null
            ? null
            : new BlockedLead(
                Said(report.Blocker?.What),
                Said(report.Blocker?.UnblockedBy),
                [.. (report.Questions ?? []).Select(one => one.Question.Trim()).Where(one => one.Length > 0)],
                Said(report.Next),
                Said(report.Summary));

    private static string? Said(string? text) => text is { } some && some.Trim() is { Length: > 0 } trimmed ? trimmed : null;
}

/// <summary>The action offered first for a blocked run, and why, in words a person reads.</summary>
/// <param name="Action">What is suggested.</param>
/// <param name="Reason">Why, said about this run.</param>
/// <param name="Rule">Which rule chose it, for a test or a reader asking why.</param>
public sealed record BlockedSuggestion(BlockedAction Action, string Reason, string Rule);

/// <summary>The figure a raise is offered with, beside what the run actually used.</summary>
/// <param name="Cap">Which limit.</param>
/// <param name="Figure">The same again: what the run was given.</param>
/// <param name="Used">What it used of it: rounds, or dollars.</param>
public sealed record BlockedRaise(BlockedCap Cap, decimal Figure, decimal Used)
{
    /// <summary>The figure and what was used, said the way the person reads them.</summary>
    public string Describe() => Cap == BlockedCap.Rounds
        ? $"{Figure:0} more round(s); it used {Used:0} of {Figure:0}"
        : $"${Figure:0.00} more; it spent ${Used:0.00} of ${Figure:0.00}";
}

/// <summary>A run that ended short of its done-when and has not been dealt with.</summary>
/// <param name="Run">The run.</param>
/// <param name="Kind">How it ended: <c>blocked</c>, <c>failed</c>, <c>limited</c>, <c>unrecorded</c>, <c>dead</c>, <c>handover</c>, <c>stalled</c> or <c>unfinished</c>.</param>
/// <param name="Cap">For a limited run, what it ran out of.</param>
/// <param name="Lead">What its lead last said, or null where it never reported.</param>
/// <param name="Chain">How many runs in a row on its task, this one included, have ended without getting there.</param>
/// <param name="Suggested">What is offered first, and why.</param>
/// <param name="Offered">Every action open to it, the suggestion first.</param>
/// <param name="Raise">The figure a raise starts from, or null where a raise is not offered.</param>
public sealed record BlockedRun(
    RunSummary Run,
    string Kind,
    BlockedCap Cap,
    BlockedLead? Lead,
    int Chain,
    BlockedSuggestion Suggested,
    IReadOnlyList<BlockedAction> Offered,
    BlockedRaise? Raise);

/// <summary>How the actions are spelt, typed and labelled.</summary>
public static class BlockedActions
{
    /// <summary>The word an action is typed and written as.</summary>
    public static string Spell(BlockedAction action) => action switch
    {
        BlockedAction.CarryOn => "carry-on",
        BlockedAction.Raise => "raise",
        BlockedAction.Requeue => "requeue",
        BlockedAction.EditRequeue => "edit-requeue",
        BlockedAction.SetAside => "set-aside",
        BlockedAction.Drop => "drop",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    /// <summary>What an action is called where a person picks it.</summary>
    public static string Label(BlockedAction action) => action switch
    {
        BlockedAction.CarryOn => "Carry on now",
        BlockedAction.Raise => "Raise the limit and carry on",
        BlockedAction.Requeue => "Re-queue at the front",
        BlockedAction.EditRequeue => "Edit the task, then re-queue",
        BlockedAction.SetAside => "Set aside",
        BlockedAction.Drop => "Drop the task",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    /// <summary>Reads an action back from the word somebody typed.</summary>
    public static bool TryParse(string? word, out BlockedAction action)
    {
        action = default;

        if (string.IsNullOrWhiteSpace(word))
        {
            return false;
        }

        foreach (var known in Enum.GetValues<BlockedAction>())
        {
            if (string.Equals(word.Trim(), Spell(known), StringComparison.OrdinalIgnoreCase))
            {
                action = known;

                return true;
            }
        }

        return false;
    }

    /// <summary>Every action, as typed, in the order they are offered.</summary>
    public static IReadOnlyList<string> Names => [.. Enum.GetValues<BlockedAction>().Select(Spell)];
}

/// <summary>
/// Which ended runs need a person to decide what happens next, and what to
/// suggest they do.
/// </summary>
/// <remarks>
/// <para>
/// Pure, so every ending and every rule is a row in a table test. Reading the
/// runs and their reports is <see cref="BlockedRunReader"/>'s.
/// </para>
/// <para>
/// A stopped run and one waiting on a decision are left out on purpose: a
/// person stopped the one, and the other is already asking somebody. What is
/// in is every other ending that did not get to done - and a run whose
/// process has gone while its journal still says it is running, because
/// nothing else will ever end it.
/// </para>
/// <para>
/// A run that handed criteria over is in too, although its outcome is
/// needs-decision. Its process has exited, so nothing is asking anybody, and
/// without a place here it would vanish from view until somebody went
/// looking for it.
/// </para>
/// <para>
/// The suggestion is a fixed rule table, first match wins, rather than a
/// model's judgement. The person confirms it either way, and a rule they can
/// read the reason for is one they can disagree with.
/// </para>
/// </remarks>
public static class BlockedRuns
{
    /// <summary>
    /// How many runs in a row on one task may end short before carrying on
    /// stops being the suggestion.
    /// </summary>
    /// <remarks>
    /// A task that keeps failing could be carried on for ever, a run at a
    /// time, each one paid for. Three is enough to say the next is unlikely to
    /// go differently.
    /// </remarks>
    public const int ChainLimit = 3;

    /// <summary>
    /// How a run ended, in the words the Blocked section uses, or null for a
    /// run that does not belong there.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="dead">Whether its journal says it is running while the process driving it has gone.</param>
    public static string? KindOf(RunSummary run, bool dead = false)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.Resolved is not null)
        {
            return null;
        }

        if (run.Running)
        {
            return dead ? "dead" : null;
        }

        // Only while something is left to tick. One with every item ticked is
        // judged again, and is not waiting on anybody.
        if (RunHandovers.IsHandover(run.Ended) && run.Handover is { Items.Count: > 0, Settled: false })
        {
            return "handover";
        }

        return RunOutcomes.Of(run) switch
        {
            RunOutcome.Blocked => "blocked",
            RunOutcome.Failed => "failed",
            RunOutcome.Limited => "limited",
            RunOutcome.Unrecorded => "unrecorded",

            // Closed with 'team runs close' after its driver died: the same
            // run as one still read as running with nothing behind it, only
            // tidied, so it is held the same way.
            RunOutcome.Abandoned => "dead",
            RunOutcome.Stalled => "stalled",

            // The lead said done over a worker that went silent twice. The
            // work is not all there, and nobody is going to say so unless
            // this does. Only once the run has ended: while it is going, the
            // lead is the one deciding what to do about it.
            RunOutcome.Done when run.Unfinished.Count > 0 => "unfinished",
            _ => null,
        };
    }

    /// <summary>Which limit a limited run ran out of, read from how it said it ended.</summary>
    public static BlockedCap CapOf(RunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var ended = run.Ended?.Trim() ?? string.Empty;

        return ended.StartsWith("round limit:", StringComparison.OrdinalIgnoreCase) ? BlockedCap.Rounds
            : ended.StartsWith("budget spent:", StringComparison.OrdinalIgnoreCase) ? BlockedCap.Budget
            : BlockedCap.None;
    }

    /// <summary>
    /// The run as the Blocked section shows it, or null for one that does not
    /// belong there.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="lead">What its lead last said, or null.</param>
    /// <param name="chain">How many runs in a row on its task, this one included, ended short.</param>
    /// <param name="dead">Whether its process has gone while its journal says it is running.</param>
    /// <param name="stalledBefore">Whether a run earlier on its chain stalled too.</param>
    public static BlockedRun? Judge(RunSummary run, BlockedLead? lead, int chain = 1, bool dead = false, bool stalledBefore = false)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (KindOf(run, dead) is not { } kind)
        {
            return null;
        }

        var cap = kind == "limited" ? CapOf(run) : BlockedCap.None;
        var facts = new Facts(run, kind, cap, lead, Math.Max(1, chain), stalledBefore);
        var open = Open(facts);
        var suggested = Suggest(facts, open);

        return new BlockedRun(
            run,
            kind,
            cap,
            lead,
            facts.Chain,
            suggested,
            [suggested.Action, .. open.Where(one => one != suggested.Action)],
            open.Contains(BlockedAction.Raise) ? RaiseOf(run, cap) : null);
    }

    /// <summary>The figure a raise starts from: the same again, beside what was used.</summary>
    public static BlockedRaise? RaiseOf(RunSummary run, BlockedCap cap)
    {
        ArgumentNullException.ThrowIfNull(run);

        return cap switch
        {
            BlockedCap.Rounds when run.RoundLimit > 0 => new BlockedRaise(cap, run.RoundLimit, run.Rounds),
            BlockedCap.Budget when run.BudgetUsd is { } budget && budget > 0m => new BlockedRaise(cap, budget, run.CostUsd),
            _ => null,
        };
    }

    /// <summary>The actions this run allows, in the order they are listed.</summary>
    private static List<BlockedAction> Open(Facts facts)
    {
        var run = facts.Run;
        var open = new List<BlockedAction>();

        // A run needs a team and a project to carry on with. A run from
        // before either was written down cannot be carried on, only filed.
        // Not a run that handed over: carrying it on gives its lead a turn
        // with nothing in it that is the lead's to do, and it hands the same
        // criteria over again, paid for.
        var carries = run.Team.Length > 0 && run.Project is { Length: > 0 } && facts.Kind != "handover";
        var tasked = run.Task is { Length: > 0 } && run.Project is { Length: > 0 };

        if (carries)
        {
            open.Add(BlockedAction.CarryOn);

            if (facts.Kind == "limited" && RaiseOf(run, facts.Cap) is not null)
            {
                open.Add(BlockedAction.Raise);
            }
        }

        if (tasked && run.Team.Length > 0)
        {
            open.Add(BlockedAction.Requeue);
            open.Add(BlockedAction.EditRequeue);
        }

        open.Add(BlockedAction.SetAside);

        if (tasked)
        {
            open.Add(BlockedAction.Drop);
        }

        return open;
    }

    /// <summary>The first rule that matches and names an action this run allows.</summary>
    private static BlockedSuggestion Suggest(Facts facts, IReadOnlyCollection<BlockedAction> open)
    {
        foreach (var rule in Rules)
        {
            if (open.Contains(rule.Action) && rule.Matches(facts))
            {
                return new BlockedSuggestion(rule.Action, rule.Reason(facts), rule.Name);
            }
        }

        // Every run allows setting aside, and the last rule matches anything,
        // so this is only reached by a table somebody has broken.
        return new BlockedSuggestion(BlockedAction.SetAside, "Nothing more specific applies, so file it and look at the task yourself.", "fallback");
    }

    private sealed record Facts(RunSummary Run, string Kind, BlockedCap Cap, BlockedLead? Lead, int Chain, bool StalledBefore = false)
    {
        public string Ended => Run.Ended?.Trim() ?? string.Empty;

        public bool EndedWith(string opening) => Ended.StartsWith(opening, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record Rule(string Name, BlockedAction Action, Func<Facts, bool> Matches, Func<Facts, string> Reason);

    private static string HandedOver(RunSummary run)
    {
        var handover = run.Handover!;
        var open = handover.Open;
        var first = open[0];
        var number = handover.Items.ToList().IndexOf(first) + 1;

        return $"Nothing left is the run's to do. It handed {open.Count} criterion(s) to you, starting with: "
            + $"{first.Criterion.TrimEnd('.')}. {first.ToDo.TrimEnd('.')}. Tick each off on its page, or with "
            + $"'loadout team handover {run.RunId} --check {number}', and once every one is ticked it is judged again. "
            + "Set it aside only to file it without doing that.";
    }

    private static string Unfinished(RunSummary run)
    {
        var stuck = run.Stuck.Where(one => one.Branch is { Length: > 0 }).ToList();

        return stuck.Count == 1
            ? $"{stuck[0].Node} went silent twice and was stopped, leaving branch '{stuck[0].Branch}' unfinished."
            : $"{stuck.Count} nodes went silent twice and were stopped, leaving branches "
                + string.Join(", ", stuck.Select(one => $"'{one.Branch}'")) + " unfinished.";
    }

    private static string Money(decimal usd) => "$" + usd.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// The rules, in order. The first that matches and names an action the
    /// run allows is the suggestion; a rule whose action is not open falls
    /// through to the next.
    /// </summary>
    private static readonly IReadOnlyList<Rule> Rules =
    [
        // First, and above the chain: a run that handed over did not fall
        // short of anything another run could do, so it is not one more of
        // the same. What it needs is the person doing what it handed them.
        new("handover", BlockedAction.SetAside,
            facts => facts.Kind == "handover",
            facts => HandedOver(facts.Run)),

        // Before anything else: past the limit, carrying on again is the one
        // thing not to suggest, however the run ended.
        new("chain", BlockedAction.SetAside,
            facts => facts.Chain >= ChainLimit,
            facts => $"This is run {facts.Chain} in a row on this task that ended without reaching its done-when. "
                + "Another of the same is unlikely to go differently: look at the task yourself, or drop it."),

        new("budget", BlockedAction.Raise,
            facts => facts.Cap == BlockedCap.Budget,
            facts => $"It ran out of money: it spent {Money(facts.Run.CostUsd)} of {Money(facts.Run.BudgetUsd ?? 0m)} "
                + $"over {facts.Run.Rounds} round(s). If that was going somewhere, the same again should finish it."),

        new("rounds", BlockedAction.Raise,
            facts => facts.Cap == BlockedCap.Rounds,
            facts => $"It used all {facts.Run.RoundLimit} round(s) it was given, having spent {Money(facts.Run.CostUsd)}. "
                + "If it was getting closer, more rounds should finish it."),

        new("blocked-unblock", BlockedAction.CarryOn,
            facts => facts.Kind == "blocked" && facts.Lead?.UnblockedBy is not null,
            facts => $"The lead was blocked: {facts.Lead!.Blocker ?? "it did not say on what"}. "
                + $"It said this would unblock it: {facts.Lead.UnblockedBy}. Answer that, and the next lead is told."),

        new("blocked-question", BlockedAction.CarryOn,
            facts => facts.Kind == "blocked" && facts.Lead is { Questions.Count: > 0 },
            facts => $"The lead was blocked on {facts.Lead!.Questions.Count} question(s) only you can answer, "
                + $"starting with: {facts.Lead.Questions[0]} Answer, and the next lead is told."),

        new("blocked", BlockedAction.EditRequeue,
            facts => facts.Kind == "blocked",
            facts => "The lead said it was blocked but not on what, which usually means the task is not clear enough "
                + "to work from. Say it differently and put it back."),

        new("unmet", BlockedAction.CarryOn,
            facts => facts.Kind == "failed" && facts.EndedWith("the lead reported done"),
            facts => $"The lead said done, but {(facts.Run.Outstanding.Count is var unmet and > 0 ? $"{unmet} criterion(s) were" : "the goal was")} "
                + "not met. Carrying on starts from what it did."),

        new("no-progress", BlockedAction.EditRequeue,
            facts => facts.Kind == "failed" && facts.EndedWith("no progress:"),
            facts => "Two rounds went by without the lead asking for anything or finishing, which usually means it "
                + "could not see how to start. Say the task differently and put it back."),

        new("not-started", BlockedAction.Requeue,
            facts => facts.Kind == "failed" && facts.EndedWith("the lead could not be started"),
            facts => "The lead never started, so nothing was tried. That is usually the machine rather than the task, "
                + "so the next window should do."),

        new("halted", BlockedAction.SetAside,
            facts => facts.Kind == "failed" && facts.EndedWith("halted:"),
            facts => $"It was halted: {facts.Ended["halted:".Length..].Trim()}. Look at what it did before running it again."),

        // Once a chain has stalled before, carrying on unattended is how one
        // task goes on hanging a lead and paying for it.
        new("stalled-again", BlockedAction.SetAside,
            facts => facts.Kind == "stalled" && facts.StalledBefore,
            facts => "Its lead went silent twice, and a run before it on this task stalled the same way. "
                + "Something about the task or the machine is hanging the lead: look at it before running it again."),

        new("stalled", BlockedAction.CarryOn,
            facts => facts.Kind == "stalled",
            facts => "Its lead wrote nothing for longer than the team allows, was started again, and went silent again. "
                + "Carrying on starts a new lead from the work it left on its branches."),

        new("unfinished", BlockedAction.CarryOn,
            facts => facts.Kind == "unfinished",
            facts => $"The lead reported done, but {Unfinished(facts.Run)} The next lead is told, and can merge or finish it."),

        new("dead", BlockedAction.CarryOn,
            facts => facts.Kind == "dead",
            facts => "Whatever was driving it went away before it finished, so it never said how it went. "
                + "Carrying on starts from the work it left on its branches."),

        new("unrecorded", BlockedAction.Requeue,
            facts => facts.Kind == "unrecorded",
            facts => "It finished without saying how it went, so there is nothing to carry on from. "
                + "Running it again in the next window is the simplest next step."),

        new("failed", BlockedAction.SetAside,
            facts => facts.Kind == "failed",
            facts => $"It failed: {(facts.Ended.Length > 0 ? facts.Ended : "it did not say why")}. "
                + "Nothing says what would go differently next time, so look at it yourself."),

        new("anything", BlockedAction.SetAside,
            _ => true,
            facts => $"It ended {facts.Kind} and nothing more specific applies. Look at it yourself."),
    ];
}
