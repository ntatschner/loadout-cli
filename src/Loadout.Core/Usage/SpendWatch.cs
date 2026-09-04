using Loadout.Core.Configuration;

namespace Loadout.Core.Usage;

/// <summary>Says where spending stands, when somebody asked to be told.</summary>
public interface ISpendWatch
{
    /// <summary>
    /// Sentences worth putting in front of somebody about to start work, or
    /// nothing at all.
    /// </summary>
    Task<IReadOnlyList<string>> WarningsAsync(
        string? projectSlug,
        CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// One dependency for the launcher rather than three, and one place where the
/// decision not to scan lives. Everything here is skipped outright unless a
/// threshold is set: working out what has been spent means reading the agents'
/// transcripts, which was measured at about two seconds, and that is not a cost
/// to put in front of everybody who never asked for a threshold.
/// </para>
/// <para>
/// Warnings only. A launch is never refused over a number, because the launcher
/// is out of the loop the moment the agent starts: a limit enforced at the door
/// would be crossed by the session it let in, and nothing here would see it.
/// </para>
/// </remarks>
internal sealed class SpendWatch : ISpendWatch
{
    private readonly IConfigurationService _configuration;
    private readonly IUsageService _usage;
    private readonly IPlanHeadroomReader _headroom;
    private readonly TimeProvider _time;

    public SpendWatch(
        IConfigurationService configuration,
        IUsageService usage,
        IPlanHeadroomReader headroom,
        TimeProvider time)
    {
        _configuration = configuration;
        _usage = usage;
        _headroom = headroom;
        _time = time;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> WarningsAsync(
        string? projectSlug,
        CancellationToken ct = default)
    {
        var config = await _configuration.LoadConfigAsync(ct).ConfigureAwait(false);

        if (config.Failed)
        {
            // A launch is not held up because a threshold could not be read.
            return [];
        }

        var settings = config.Value!.Spend;

        if (!SpendThresholds.AnySet(settings, projectSlug))
        {
            return [];
        }

        var said = new List<string>();

        // One day, because both token thresholds are about a day. A wider
        // window would cost more to read and answer a question nobody asked.
        var today = await _usage.ReportAsync(new UsageQuery(Days: 1), ct).ConfigureAwait(false);

        if (today.Succeeded)
        {
            var all = today.Value!.Totals.Total;

            var onProject = projectSlug is { Length: > 0 } slug
                ? today.Value!.Projects
                    .Where(p => string.Equals(p.Name, slug, StringComparison.OrdinalIgnoreCase))
                    .Sum(p => p.Totals.Total)
                : 0;

            foreach (var crossed in SpendThresholds.Crossed(settings, projectSlug, all, onProject))
            {
                said.Add(
                    $"{crossed.Subject}: {crossed.Spent:N0} tokens against a threshold of "
                    + $"{crossed.Threshold:N0}. Nothing is stopped by this.");
            }
        }

        var reading = await _headroom.LatestAsync(ct).ConfigureAwait(false);

        if (reading.Succeeded
            && SpendThresholds.Plan(settings, reading.Value) is { } plan)
        {
            // Always with its age. The figure is whatever the agent last
            // mentioned, and an hours-old percentage shown as a live gauge is
            // worse than no gauge.
            var age = plan.Reading.Age(_time.GetUtcNow());

            said.Add(
                $"{plan.Reading.Agent} reported {plan.Reading.UsedFraction:P0} of its "
                + $"{plan.Reading.WindowName} used, as of {Ago(age)}.");
        }

        return said;
    }

    private static string Ago(TimeSpan age) => age.TotalMinutes switch
    {
        < 2 => "just now",
        < 60 => $"{age.TotalMinutes:N0} minutes ago",
        < 48 * 60 => $"{age.TotalHours:N0} hours ago",
        _ => $"{age.TotalDays:N0} days ago",
    };
}
