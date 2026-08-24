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

/// <summary>How much of the context window the session has spent.</summary>
public sealed class StatuslineContextWindow
{
    public long? TotalInputTokens { get; set; }

    public long? TotalOutputTokens { get; set; }

    public long? ContextWindowSize { get; set; }

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
