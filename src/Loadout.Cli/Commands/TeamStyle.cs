using System.Globalization;
using Loadout.Core.Teams;
using Spectre.Console;

namespace Loadout.Cli.Commands;

/// <summary>How a team run's lines look in a terminal.</summary>
/// <remarks>
/// <para>
/// A run's live notes were all printed dim and its log all plain, so a run
/// read as one grey column: the line saying a node had stopped to ask looked
/// the same as the forty saying it was reading a file. Colour goes by what a
/// line means - something wants you, something went wrong, something moved on
/// - so the eye finds those first.
/// </para>
/// <para>
/// Colour only, never the words. The text is identical with colour off, which
/// is what NO_COLOR and a pipe give, so nothing depends on it.
/// </para>
/// </remarks>
internal static class TeamStyle
{
    /// <summary>One journal entry, as a line of markup.</summary>
    public static string Line(RunEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var at = entry.At.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var who = entry.Node is { Length: > 0 } node ? node : "run";
        var said = Markup.Escape(RunJournal.Wording(entry));

        var colour = Colour(entry);

        return $"[grey]{at}[/]  [{(who == "run" ? "bold" : "cyan")}]{Markup.Escape($"{who,-16}")}[/] "
            + (colour is null ? said : $"[{colour}]{said}[/]");
    }

    /// <summary>A note the run makes as it goes, as a line of markup.</summary>
    public static string Note(string line) =>
        $"[cyan]>[/] {Markup.Escape(line)}";

    /// <summary>A verdict, padded so a column of them lines up.</summary>
    internal static string Verdict(string verdict) => verdict switch
    {
        "met" => "[green]+ met          [/]",
        "unmet" => "[yellow]- unmet        [/]",
        "not attempted" => "[yellow]! not attempted[/]",
        _ => $"[dim]? {Markup.Escape(verdict).PadRight(13)}[/]",
    };

    /// <summary>
    /// What became of each criterion, as lines of markup: the criterion and its
    /// verdict, how the lead read it, what it says shows it, and what was
    /// delivered behind it.
    /// </summary>
    /// <remarks>
    /// The same lines at the end of <c>team run</c> and in <c>team status</c>,
    /// so the two cannot say different things about one run. The reading comes
    /// before the evidence because a verdict is only worth the reading it was
    /// given.
    /// </remarks>
    internal static IEnumerable<string> Outcomes(IReadOnlyList<CriterionOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        if (outcomes.Count == 0)
        {
            yield break;
        }

        var judged = outcomes.Count(one => one.Verdict is not null);

        yield return judged > 0
            ? $"  [bold]Done when[/] [dim]{outcomes.Count(one => one.Met)} of {outcomes.Count} met[/]"
            : $"  [bold]Done when[/] [dim]no verdicts yet[/]";

        foreach (var one in outcomes)
        {
            // The project's are marked, because they were set once for every
            // run and nobody asked for them on this one.
            yield return $"  {Verdict(one.Verdict ?? "no verdict")} {Markup.Escape(one.Criterion)}"
                + (one.Source == "project" ? "  [dim](project)[/]" : string.Empty);

            if (one.Reading is { Length: > 0 } reading)
            {
                yield return $"      [dim]taken to mean: {Markup.Escape(reading)}[/]";
            }

            if (one.Because is { Length: > 0 } because)
            {
                yield return $"      [dim]{Markup.Escape(because)}[/]";
            }

            foreach (var delivered in one.Delivered)
            {
                yield return $"      [dim]delivered: {Markup.Escape(delivered.Kind)} {Markup.Escape(delivered.Ref)} "
                    + $"by {Markup.Escape(delivered.Node)}[/]";
            }

            // Cited and nowhere in a worker's report. Said, not hidden: a lead
            // pointing at work nobody handed back is the thing to notice.
            foreach (var cited in one.Unfound)
            {
                yield return $"      [yellow]cited but no worker reported it: {Markup.Escape(cited)}[/]";
            }
        }
    }

    /// <summary>The colour for what an entry means, or null to leave it plain.</summary>
    internal static string? Colour(RunEvent entry) => entry.Kind switch
    {
        // Somebody ended it because its driver had gone: dealt with, but not
        // a run that finished on its own, so it should not read as one.
        "run.finished" when Abandoned(entry) => "yellow",
        "run.started" or "run.finished" or "round.started" or "run.budget" => "bold",
        "node.asked" => "yellow",
        "node.answered" or "permission.asked" => entry.Yes("allowed") ? "green" : "red",
        "node.failed" or "report.unreadable" or "request.refused" or "node.stuck" or "run.stalled" => "red",
        "node.silent" or "node.retried" => "yellow",
        "node.launched" => "blue",
        _ => null,
    };

    private static bool Abandoned(RunEvent entry) =>
        string.Equals(entry.Word("outcome"), "abandoned", StringComparison.OrdinalIgnoreCase)
        || entry.Word("ended")?.StartsWith("abandoned", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Whether anything still drives a run.</summary>
    /// <remarks>
    /// Asked of a run that is still going only. A finished run is finished
    /// whatever its lock says, and asking takes the lock for a moment, so
    /// listing a hundred old runs costs nothing.
    /// </remarks>
    internal static Liveness LivenessOf(RunSummary run, IRunOwnership ownership, DateTimeOffset now)
    {
        if (!run.Running)
        {
            return new Liveness.Finished();
        }

        // The journal's own write time, because a run's last-seen counts node
        // events only and a lead's turn writes others.
        var journal = new FileInfo(Path.Combine(run.Directory, "journal.jsonl"));
        var written = journal.Exists ? new DateTimeOffset(journal.LastWriteTimeUtc, TimeSpan.Zero) : run.LastSeen;

        return RunLiveness.Of(run, ownership.Read(run.Directory), written, now);
    }

    /// <summary>Liveness as the word --json gives it.</summary>
    /// <remarks>
    /// Anything not known here reads as live, the safe default: a case added
    /// later must never make a running run look dead, which is what invites
    /// somebody to end it.
    /// </remarks>
    internal static string LivenessWord(Liveness liveness) => liveness switch
    {
        Liveness.Finished => "finished",
        Liveness.NotResponding => "not-responding",
        Liveness.PossiblyNotResponding => "possibly-not-responding",
        _ => "live",
    };

    /// <summary>Who last drove a run, for --json, or null when that is not known.</summary>
    internal static object? OwnerJson(Liveness liveness) => liveness is Liveness.NotResponding { LastOwner: { } owner }
        ? new { pid = owner.ProcessId, machine = owner.Machine, startedAt = owner.StartedAt }
        : null;

    /// <summary>Whether nothing, or possibly nothing, is driving the run.</summary>
    internal static bool Unowned(Liveness liveness) =>
        liveness is Liveness.NotResponding or Liveness.PossiblyNotResponding;

    /// <summary>
    /// What to say in place of "running" for a run nothing drives, or null
    /// for one that is driven.
    /// </summary>
    internal static string? Unresponsive(Liveness liveness, RunSummary run, DateTimeOffset now) => liveness switch
    {
        Liveness.NotResponding { LastOwner: { } owner } =>
            $"[red]not responding[/] (pid {owner.ProcessId}, last seen {TeamStatusCommand.Elapsed(now - run.LastSeen)} ago)",
        Liveness.NotResponding =>
            $"[red]not responding[/] (last seen {TeamStatusCommand.Elapsed(now - run.LastSeen)} ago)",
        Liveness.PossiblyNotResponding possibly =>
            $"[yellow]possibly not responding[/] (quiet for {TeamStatusCommand.Elapsed(possibly.Quiet)})",
        _ => null,
    };

    /// <summary>The line saying how to end a run nothing drives.</summary>
    internal static string CloseHint(string runId) =>
        $"[dim]End it with:[/] loadout team runs close {Markup.Escape(runId)}";
}
