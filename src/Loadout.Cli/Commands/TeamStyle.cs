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
        "run.started" or "run.finished" or "round.started" or "run.budget" => "bold",
        "node.asked" => "yellow",
        "node.answered" or "permission.asked" => entry.Yes("allowed") ? "green" : "red",
        "node.failed" or "report.unreadable" or "request.refused" => "red",
        "node.launched" => "blue",
        _ => null,
    };
}
