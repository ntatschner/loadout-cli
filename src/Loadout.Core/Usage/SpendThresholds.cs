using Loadout.Models.Configuration;

namespace Loadout.Core.Usage;

/// <summary>A threshold somebody set, and where the spend stands against it.</summary>
/// <param name="Subject">What the threshold was about, as a person would name it.</param>
/// <param name="Spent">Tokens counted.</param>
/// <param name="Threshold">The number that was set.</param>
public sealed record SpendWarning(string Subject, long Spent, long Threshold)
{
    /// <summary>How far past the line, as a share of it.</summary>
    public double Share => Threshold <= 0 ? 0 : (double)Spent / Threshold;
}

/// <summary>A plan window closer to full than somebody wanted to be told about.</summary>
/// <param name="Reading">What the agent last reported, with when it said it.</param>
/// <param name="Threshold">The share that was set.</param>
public sealed record PlanWarning(PlanHeadroom Reading, double Threshold);

/// <summary>
/// Says where spending stands against the thresholds somebody set.
/// </summary>
/// <remarks>
/// Warnings only. Nothing here refuses anything, and there is deliberately no
/// way to make it: the launcher is out of the loop once the agent starts, so a
/// limit it enforced at the door would be crossed by the session it let in and
/// nothing would notice.
/// </remarks>
public static class SpendThresholds
{
    /// <summary>
    /// Whether anything is set that would need spending to be worked out.
    /// </summary>
    /// <remarks>
    /// Asked before any scanning, because reading the agents' transcripts takes
    /// seconds. Somebody who set no threshold pays nothing for this feature,
    /// which is the difference between it being optional and it being a tax.
    /// </remarks>
    public static bool AnySet(SpendSettings? settings, string? projectSlug)
    {
        if (settings is null)
        {
            return false;
        }

        if (settings.DailyTokens > 0 || settings.PlanWarnAt > 0)
        {
            return true;
        }

        return projectSlug is { Length: > 0 } slug
            && settings.ProjectDailyTokens.TryGetValue(slug, out var project)
            && project > 0;
    }

    /// <summary>Thresholds today's spending has crossed.</summary>
    /// <param name="settings">What was set.</param>
    /// <param name="projectSlug">The project this launch is for, if any.</param>
    /// <param name="spentToday">Tokens across everything today.</param>
    /// <param name="spentTodayOnProject">Tokens on this project today.</param>
    public static IReadOnlyList<SpendWarning> Crossed(
        SpendSettings? settings,
        string? projectSlug,
        long spentToday,
        long spentTodayOnProject)
    {
        var crossed = new List<SpendWarning>();

        if (settings is null)
        {
            return crossed;
        }

        // The project first. Somebody who set both wants to know which one they
        // are near, and the narrower answer is the more actionable of the two.
        if (projectSlug is { Length: > 0 } slug
            && settings.ProjectDailyTokens.TryGetValue(slug, out var perProject)
            && perProject > 0
            && spentTodayOnProject >= perProject)
        {
            crossed.Add(new SpendWarning(slug, spentTodayOnProject, perProject));
        }

        if (settings.DailyTokens > 0 && spentToday >= settings.DailyTokens)
        {
            crossed.Add(new SpendWarning("today", spentToday, settings.DailyTokens));
        }

        return crossed;
    }

    /// <summary>
    /// Whether the plan window is fuller than somebody wanted to be told about.
    /// </summary>
    /// <remarks>
    /// No reading is not the same as plenty of room. Only one of the agents
    /// records this and only sometimes, so an absent reading answers nothing at
    /// all rather than answering that everything is fine.
    /// </remarks>
    public static PlanWarning? Plan(SpendSettings? settings, PlanHeadroom? headroom) =>
        settings is { PlanWarnAt: > 0 } && headroom is not null
            && headroom.UsedFraction >= settings.PlanWarnAt
                ? new PlanWarning(headroom, settings.PlanWarnAt)
                : null;
}
