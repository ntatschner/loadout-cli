using Loadout.Models.Tools;

namespace Loadout.Core.Tools;

/// <summary>How long one active version's cases took at verify.</summary>
/// <param name="Cases">Cases with a recorded time. None for a version verified before times were kept.</param>
/// <param name="MedianSeconds">The median, or null with no cases.</param>
/// <param name="WorstSeconds">The slowest, or null with no cases.</param>
/// <param name="WorstCase">Which case that was.</param>
public sealed record ToolPerformance(int Cases, double? MedianSeconds, double? WorstSeconds, string? WorstCase);

/// <summary>How the recent uses went.</summary>
/// <param name="Uses">Uses counted: the last <see cref="ToolHealth.UsageWindow" /> at most.</param>
/// <param name="FailedRate">Share of those that failed.</param>
/// <param name="WorkaroundRate">Share of those that needed working around.</param>
/// <param name="Teams">Distinct teams among them, counted and never named.</param>
/// <param name="TeamShare">Distinct teams per use: near 1 is used widely, near 0 is one team's habit.</param>
public sealed record ToolUsability(int Uses, double FailedRate, double WorkaroundRate, int Teams, double TeamShare);

/// <summary>What there is to keep up.</summary>
/// <param name="ScriptLines">Non-blank lines in the active script.</param>
/// <param name="Inputs">Inputs its manifest declares.</param>
/// <param name="Dependencies">Dependencies its manifest declares.</param>
/// <param name="Versions">Known-good versions, ever.</param>
/// <param name="RecentPromotions">Promotions in the last <see cref="ToolHealth.ChurnWindowDays" /> days.</param>
public sealed record ToolMaintainability(int ScriptLines, int Inputs, int Dependencies, int Versions, int RecentPromotions);

/// <summary>Whether it is still wanted.</summary>
/// <param name="LastUsed">The last recorded use, or null for none.</param>
/// <param name="DaysIdle">Days since the last use, or since promotion where it was never used; null where neither is known.</param>
/// <param name="SupersededBy">A newer active tool that overlaps it, or null.</param>
public sealed record ToolRelevance(DateTimeOffset? LastUsed, int? DaysIdle, string? SupersededBy);

/// <summary>
/// An active tool measured on the four things correctness and generality do
/// not cover, from what the registry already holds.
/// </summary>
/// <param name="Name">The tool.</param>
/// <param name="Version">Its active version.</param>
/// <param name="Performance">Case times at verify.</param>
/// <param name="Usability">Recent outcomes.</param>
/// <param name="Maintainability">Size and churn.</param>
/// <param name="Relevance">Idleness and whether something newer covers it.</param>
/// <param name="Crossed">Each threshold crossed, in a sentence. Any at all is a reason to look again.</param>
/// <param name="Thresholds">
/// The same crossings by name - failed, workaround, slow, idle - for comparing
/// one measurement with another, where the sentences carry numbers that move.
/// </param>
public sealed record ToolHealth(
    string Name,
    string Version,
    ToolPerformance Performance,
    ToolUsability Usability,
    ToolMaintainability Maintainability,
    ToolRelevance Relevance,
    IReadOnlyList<string> Crossed,
    IReadOnlyList<string> Thresholds)
{
    /// <summary>How many recent uses the rates are taken over.</summary>
    /// <remarks>Recent enough that a fixed tool recovers, long enough that one bad day does not dominate.</remarks>
    public const int UsageWindow = 20;

    /// <summary>The fewest uses a rate is judged on.</summary>
    /// <remarks>Below eight, one failure is more than 12%: a rate that small a sample swings is noise.</remarks>
    public const int MinimumUses = 8;

    /// <summary>The failure or workaround rate that makes a tool worth another look.</summary>
    /// <remarks>One use in four going wrong is a tool teams are routing around, not an unlucky run.</remarks>
    public const double TroubleRate = 0.25;

    /// <summary>The slowest case time, in seconds, above which a tool is worth another look.</summary>
    /// <remarks>
    /// A remediator's call waits on it, and a case is a small representative
    /// input; half a minute on one is a script doing far more than it needs to.
    /// </remarks>
    public const double SlowCaseSeconds = 30;

    /// <summary>Days without a use after which an active tool is worth another look.</summary>
    /// <remarks>
    /// Twice the thirty days a deprecated tool must sit unused before it can be
    /// retired: long enough to span a quiet month, short enough that a dead
    /// tool is noticed before it is trusted out of habit.
    /// </remarks>
    public const int IdleDays = 60;

    /// <summary>The window, in days, version churn is counted over.</summary>
    /// <remarks>
    /// A quarter: long enough to hold the several promotions of a tool that
    /// keeps being reworked, short enough that the burst of fixes after it was
    /// first made has dropped out of the count once it has settled.
    /// </remarks>
    public const int ChurnWindowDays = 90;

    /// <summary>Measures one active version.</summary>
    /// <param name="head">The tool's head.</param>
    /// <param name="active">Its active version's manifest.</param>
    /// <param name="script">The active script's text.</param>
    /// <param name="usage">Every recorded use, oldest first.</param>
    /// <param name="audit">The tool's audit entries, oldest first.</param>
    /// <param name="supersededBy">A newer overlapping active tool, worked out by the caller.</param>
    /// <param name="now">What time it is.</param>
    public static ToolHealth Measure(
        ToolRecord head,
        ToolVersion active,
        string script,
        IReadOnlyList<ToolUsage> usage,
        IReadOnlyList<ToolAuditEntry> audit,
        string? supersededBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(head);
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(audit);

        var times = (active.Tests?.Seconds ?? []).OrderBy(one => one.Value).ToList();
        var performance = new ToolPerformance(
            times.Count,
            times.Count == 0 ? null : Median(times.Select(one => one.Value).ToList()),
            times.Count == 0 ? null : times[^1].Value,
            times.Count == 0 ? null : times[^1].Key);

        var recent = usage.Skip(Math.Max(0, usage.Count - UsageWindow)).ToList();
        var teams = recent.Select(one => one.Team).Where(one => one.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        double Rate(string outcome) =>
            recent.Count == 0 ? 0 : (double)recent.Count(one => one.Outcome == outcome) / recent.Count;
        var usability = new ToolUsability(
            recent.Count,
            Rate(ToolOutcome.Failed),
            Rate(ToolOutcome.Workaround),
            teams,
            recent.Count == 0 ? 0 : (double)teams / recent.Count);

        var promotions = audit.Where(one => one.Action == "promote").ToList();
        var maintainability = new ToolMaintainability(
            (script ?? string.Empty).Split('\n').Count(line => line.Trim().Length > 0),
            active.Inputs.Count,
            active.Dependencies.Count,
            head.KnownGood.Count,
            promotions.Count(one => one.At >= now.AddDays(-ChurnWindowDays)));

        var lastUsed = usage.Select(one => one.At).Where(one => one is not null).Max();
        var since = lastUsed ?? promotions.Select(one => (DateTimeOffset?)one.At).LastOrDefault();
        var relevance = new ToolRelevance(
            lastUsed,
            since is { } from ? Math.Max(0, (int)(now - from).TotalDays) : null,
            supersededBy);

        var crossed = new List<string>();
        var thresholds = new List<string>();

        if (usability.Uses >= MinimumUses && usability.FailedRate >= TroubleRate)
        {
            thresholds.Add("failed");
            crossed.Add($"{usability.FailedRate:P0} of the last {usability.Uses} uses failed.");
        }

        if (usability.Uses >= MinimumUses && usability.WorkaroundRate >= TroubleRate)
        {
            thresholds.Add("workaround");
            crossed.Add($"{usability.WorkaroundRate:P0} of the last {usability.Uses} uses needed a workaround.");
        }

        if (performance.WorstSeconds > SlowCaseSeconds)
        {
            thresholds.Add("slow");
            crossed.Add($"Case '{performance.WorstCase}' took {performance.WorstSeconds:0.#}s at verify.");
        }

        if (head.Lifecycle == ToolLifecycle.Active && relevance.DaysIdle >= IdleDays)
        {
            thresholds.Add("idle");
            crossed.Add($"Nobody has used it in {relevance.DaysIdle} days.");
        }

        return new ToolHealth(head.Name, active.Version, performance, usability, maintainability, relevance, crossed, thresholds);
    }

    private static double Median(List<double> sorted) =>
        sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;
}
