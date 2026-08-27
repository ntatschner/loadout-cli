using System.Globalization;
using System.Text.Json;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Usage;

/// <summary>
/// How much of a plan's allowance has been used, as an agent last reported it.
/// </summary>
/// <param name="Agent">Which agent reported it.</param>
/// <param name="UsedFraction">The share of the window spent, from 0 to 1.</param>
/// <param name="Window">How long the window is.</param>
/// <param name="ResetsAt">When it starts again, when the agent said.</param>
/// <param name="Observed">
/// When this was reported. Always shown: the figure is a snapshot from whenever
/// the agent last mentioned it, and an hours-old percentage presented as a live
/// gauge is worse than no gauge.
/// </param>
/// <param name="Plan">The plan name the agent reported, when it named one.</param>
public sealed record PlanHeadroom(
    string Agent,
    double UsedFraction,
    TimeSpan Window,
    DateTimeOffset? ResetsAt,
    DateTimeOffset Observed,
    string? Plan)
{
    /// <summary>How stale the figure is.</summary>
    public TimeSpan Age(DateTimeOffset now) => now - Observed;

    /// <summary>
    /// A plain description of the window, since "10080 minutes" means nothing
    /// to anybody.
    /// </summary>
    public string WindowName => Window.TotalDays >= 6.5
        ? "week"
        : Window.TotalHours >= 23
            ? "day"
            : $"{Window.TotalHours:N0} hours";
}

/// <summary>The most recent plan allowance an agent mentioned.</summary>
public interface IPlanHeadroomReader
{
    /// <summary>
    /// The newest reading, or null when no session recorded one.
    /// </summary>
    Task<OperationResult<PlanHeadroom?>> LatestAsync(CancellationToken ct = default);
}

/// <summary>
/// Reads the plan allowance Codex records alongside its token counts.
/// </summary>
/// <remarks>
/// <para>
/// On a subscription this is the number that actually constrains the work.
/// Money is not what runs out — the rate window is — and Codex is the only one
/// of the two agents that writes its own standing in that window to disk.
/// Claude Code records nothing equivalent locally.
/// </para>
/// <para>
/// It is also intermittent. Of the last forty sessions on this machine, nine
/// carried it, and some <c>token_count</c> events arrive with nothing in them
/// at all. So this is deliberately not presented as a gauge: it returns one
/// reading with the time it was taken, and the caller says how old it is. A
/// stale percentage shown as though it were current is the kind of number
/// somebody plans their afternoon around and should not.
/// </para>
/// </remarks>
public sealed class CodexPlanHeadroom : IPlanHeadroomReader
{
    /// <summary>
    /// How many recent rollouts to look through.
    /// </summary>
    /// <remarks>
    /// Enough to find a reading given how rarely they appear, few enough that
    /// asking is instant. Searching the whole history to find an older figure
    /// would be work spent making the answer worse.
    /// </remarks>
    private const int Recent = 60;

    private readonly IEnvironmentProvider _environment;

    public CodexPlanHeadroom(IEnvironmentProvider environment) => _environment = environment;

    private string Root => Path.Combine(_environment.HomeDirectory, ".codex", "sessions");

    /// <inheritdoc />
    public async Task<OperationResult<PlanHeadroom?>> LatestAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(Root))
        {
            return OperationResult<PlanHeadroom?>.Ok(null);
        }

        List<FileInfo> files;

        try
        {
            files = new DirectoryInfo(Root)
                .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(Recent)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<PlanHeadroom?>.Fail(
                $"Could not read Codex's history at {Root}: {ex.Message}");
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            // Newest file first, and within a file the last reading wins, so
            // the first file that has one has the freshest.
            if (await ReadAsync(file, ct).ConfigureAwait(false) is { } found)
            {
                return OperationResult<PlanHeadroom?>.Ok(found);
            }
        }

        return OperationResult<PlanHeadroom?>.Ok(null);
    }

    private static async Task<PlanHeadroom?> ReadAsync(FileInfo file, CancellationToken ct)
    {
        PlanHeadroom? latest = null;

        try
        {
            using var reader = new StreamReader(file.OpenRead());

            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                ct.ThrowIfCancellationRequested();

                if (!line.Contains("\"rate_limits\"", StringComparison.Ordinal))
                {
                    continue;
                }

                JsonDocument document;

                try
                {
                    document = JsonDocument.Parse(line);
                }
                catch (JsonException)
                {
                    continue;
                }

                using (document)
                {
                    latest = Parse(document.RootElement) ?? latest;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return latest;
    }

    /// <summary>One reading, or null when the line carries no usable one.</summary>
    private static PlanHeadroom? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("payload", out var payload)
            || payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("rate_limits", out var limits)
            || limits.ValueKind != JsonValueKind.Object

            // Present but empty is ordinary here rather than a fault.
            || !limits.TryGetProperty("primary", out var primary)
            || primary.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!primary.TryGetProperty("used_percent", out var used)
            || used.ValueKind != JsonValueKind.Number
            || !used.TryGetDouble(out var percent))
        {
            return null;
        }

        var minutes = primary.TryGetProperty("window_minutes", out var window)
            && window.ValueKind == JsonValueKind.Number
            && window.TryGetDouble(out var value)
            ? value
            : 0;

        DateTimeOffset? resets = primary.TryGetProperty("resets_at", out var at)
            && at.ValueKind == JsonValueKind.Number
            && at.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

        var plan = limits.TryGetProperty("plan_type", out var named)
            && named.ValueKind == JsonValueKind.String
            ? named.GetString()
            : null;

        var observed = root.TryGetProperty("timestamp", out var stamp)
            && stamp.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                stamp.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var when)
            ? when
            : DateTimeOffset.UnixEpoch;

        return new PlanHeadroom(
            "codex",
            percent / 100,
            TimeSpan.FromMinutes(minutes),
            resets,
            observed,
            plan);
    }
}
