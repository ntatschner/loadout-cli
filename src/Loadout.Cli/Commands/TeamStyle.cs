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
