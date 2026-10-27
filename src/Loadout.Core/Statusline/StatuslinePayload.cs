using System.Text.Json;
using System.Text.Json.Serialization;

namespace Loadout.Core.Statusline;

/// <summary>
/// What Claude Code hands its status line command on stdin.
/// <para>
/// The shape is Claude's, not ours, and it is read rather than assumed: these
/// names were taken from the installed binary's own documentation of the
/// contract. Every member is nullable because a field this launcher does not
/// know about is far likelier than a field it can rely on — Claude may add,
/// rename or drop one in any release, and a status line that throws on an
/// unexpected payload would blank the bottom of the screen with no explanation.
/// </para>
/// </summary>
public sealed class StatuslinePayload
{
    public string? SessionId { get; set; }

    public string? SessionName { get; set; }

    /// <summary>Working directory of the session.</summary>
    public string? Cwd { get; set; }

    public string? Version { get; set; }

    public StatuslineModel? Model { get; set; }

    public StatuslineWorkspace? Workspace { get; set; }

    public StatuslineContextWindow? ContextWindow { get; set; }

    /// <summary>The session's transcript as Claude sent it, which can be the empty string.</summary>
    public string? TranscriptPath { get; set; }

    /// <summary>
    /// The session's transcript, or null where there is none. Claude sends an
    /// empty string rather than leaving the field out when it keeps no
    /// transcript, and an empty path read as a file would be the working
    /// directory.
    /// </summary>
    public string? TranscriptFile => string.IsNullOrEmpty(TranscriptPath) ? null : TranscriptPath;

    public StatuslineCost? Cost { get; set; }

    /// <summary>
    /// The plan's usage limits, sent only to subscribers or behind a gateway
    /// with a spend limit, and only once there has been a response.
    /// </summary>
    public StatuslineRateLimits? RateLimits { get; set; }

    /// <summary>
    /// Reads a payload, returning null rather than throwing on anything
    /// unexpected. The caller has no way to report a parse failure — its
    /// stdout is a single line on somebody's screen — so it must be able to
    /// carry on and print what it does know.
    /// </summary>
    public static StatuslinePayload? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StatuslinePayload>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}

/// <summary>The model answering in this session.</summary>
public sealed class StatuslineModel
{
    public string? Id { get; set; }

    public string? DisplayName { get; set; }
}

/// <summary>Where the session is rooted.</summary>
public sealed class StatuslineWorkspace
{
    public string? CurrentDir { get; set; }

    /// <summary>Project root, which is what the launcher matches against its registry.</summary>
    public string? ProjectDir { get; set; }

    public List<string>? AddedDirs { get; set; }

    /// <summary>Set only when the session sits in a linked worktree.</summary>
    public string? GitWorktree { get; set; }
}

/// <summary>What the session has cost so far.</summary>
public sealed class StatuslineCost
{
    /// <summary>How long the session has been open, in milliseconds.</summary>
    public long? TotalDurationMs { get; set; }

    /// <summary>Claude's own estimate of what the session has cost, in US dollars.</summary>
    public double? TotalCostUsd { get; set; }

    public long? TotalLinesAdded { get; set; }

    public long? TotalLinesRemoved { get; set; }
}

/// <summary>The plan's usage windows, each present only while its reset has not passed.</summary>
public sealed class StatuslineRateLimits
{
    public StatuslineRateWindow? FiveHour { get; set; }

    public StatuslineRateWindow? SevenDay { get; set; }

    /// <summary>Behind a gateway, the fullest spend limit.</summary>
    public StatuslineSpendLimit? SpendLimit { get; set; }
}

/// <summary>One usage window.</summary>
public sealed class StatuslineRateWindow
{
    /// <summary>Share of the window used, 0 to 100.</summary>
    public double? UsedPercentage { get; set; }

    /// <summary>When the window resets, in Unix seconds.</summary>
    public long? ResetsAt { get; set; }
}

/// <summary>A gateway's spend limit.</summary>
public sealed class StatuslineSpendLimit
{
    /// <summary>Share of the limit used, which goes past 100 once it is exceeded.</summary>
    public double? UsedPercentage { get; set; }

    /// <summary>When its period resets, in Unix seconds.</summary>
    public long? ResetsAt { get; set; }

    public double? UsedUsd { get; set; }

    public double? LimitUsd { get; set; }

    /// <summary><c>daily</c>, <c>weekly</c> or <c>monthly</c>.</summary>
    public string? Period { get; set; }
}

/// <summary>What the last request to the model was made of.</summary>
public sealed class StatuslineCurrentUsage
{
    public long? InputTokens { get; set; }

    public long? OutputTokens { get; set; }

    public long? CacheCreationInputTokens { get; set; }

    public long? CacheReadInputTokens { get; set; }
}

/// <summary>How much of the context window the session has spent.</summary>
public sealed class StatuslineContextWindow
{
    public long? TotalInputTokens { get; set; }

    public long? TotalOutputTokens { get; set; }

    public long? ContextWindowSize { get; set; }

    /// <summary>The last request's tokens, which Claude sends as null before the first.</summary>
    public StatuslineCurrentUsage? CurrentUsage { get; set; }

    /// <summary>Claude's own figure for the share of the window in use, 0 to 100, null before the first message.</summary>
    public double? UsedPercentage { get; set; }

    /// <summary>
    /// Spent fraction, or null when either half is missing or nonsensical.
    /// Guarded against a zero window because dividing by it would render
    /// infinity as a percentage.
    /// </summary>
    public double? UsedFraction =>
        TotalInputTokens is > 0 && ContextWindowSize is > 0
            ? (double)TotalInputTokens.Value / ContextWindowSize.Value
            : null;
}
