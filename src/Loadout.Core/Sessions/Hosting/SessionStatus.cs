using System.Text.Json;
using Loadout.Core.Statusline;

namespace Loadout.Core.Sessions.Hosting;

/// <summary>How much of the context window a session is using, as Claude counted it.</summary>
/// <param name="UsedPercent">Share of the window in use, 0 to 100: Claude's own figure where it sent one.</param>
/// <param name="InputTokens">Tokens in the window, cache reads and writes included.</param>
/// <param name="WindowSize">The model's window.</param>
/// <param name="UncachedTokens">The last request's input that was neither read from nor written to the cache.</param>
/// <param name="CacheReadTokens">The last request's input read from the cache.</param>
/// <param name="CacheWriteTokens">The last request's input written to the cache.</param>
/// <param name="OutputTokens">The last response's output.</param>
public sealed record StatusContext(
    double? UsedPercent,
    long? InputTokens,
    long? WindowSize,
    long? UncachedTokens,
    long? CacheReadTokens,
    long? CacheWriteTokens,
    long? OutputTokens);

/// <summary>What the session has cost and changed so far.</summary>
public sealed record StatusCost(double? Usd, long? DurationMs, long? LinesAdded, long? LinesRemoved);

/// <summary>One usage window and when it resets.</summary>
public sealed record StatusWindow(double UsedPercent, DateTimeOffset? ResetsAt);

/// <summary>A gateway's spend limit.</summary>
public sealed record StatusSpend(double? UsedPercent, DateTimeOffset? ResetsAt, double? UsedUsd, double? LimitUsd, string? Period);

/// <summary>The plan's usage limits, each absent where Claude did not send it.</summary>
public sealed record StatusLimits(StatusWindow? FiveHour, StatusWindow? SevenDay, StatusSpend? Spend);

/// <summary>What a hosted session's status line was last given, for Studio to draw.</summary>
/// <param name="Version">The shape's version, so a page can tell a snapshot it does not know.</param>
/// <param name="At">When the status line ran.</param>
/// <param name="RefreshSeconds">How often Claude was asked to redraw while idle, or null for only on its own events.</param>
/// <param name="Terminal">What the terminal's own line was set to show: <c>off</c>, <c>minimal</c> or <c>full</c>.</param>
/// <param name="Wrapping">Whether the terminal shows the person's own status line command, wrapped.</param>
/// <param name="Model">The model's display name.</param>
/// <param name="Context">The context window, or null where Claude sent none.</param>
/// <param name="Cost">What the session has cost, or null where Claude sent none.</param>
/// <param name="Limits">The plan's usage limits, or null where Claude sent none, as it does not on an API key.</param>
/// <param name="Project">The registered project, where the session is in one.</param>
/// <param name="Specialists">The specialists composed into the project's last launch, by id.</param>
/// <remarks>
/// Every figure is Claude's, read from what it sent and never assumed: a field
/// it left out is null here, not zero, so Studio can say it does not know
/// rather than draw an empty meter as a fact.
/// </remarks>
public sealed record SessionStatusSnapshot(
    int Version,
    DateTimeOffset At,
    int? RefreshSeconds,
    string Terminal,
    bool Wrapping,
    string? Model,
    StatusContext? Context,
    StatusCost? Cost,
    StatusLimits? Limits,
    string? Project,
    IReadOnlyList<string>? Specialists)
{
    /// <summary>The version this build writes.</summary>
    public const int Current = 1;

    /// <summary>A snapshot of what Claude sent, with the launcher's own facts beside it.</summary>
    public static SessionStatusSnapshot From(
        StatuslinePayload? payload,
        DateTimeOffset at,
        int? refreshSeconds,
        string terminal,
        bool wrapping,
        string? project,
        IReadOnlyList<string>? specialists)
    {
        var window = payload?.ContextWindow;
        var usage = window?.CurrentUsage;

        var context = window is null
            ? null
            : new StatusContext(
                UsedPercent: window.UsedPercentage ?? window.UsedFraction * 100,
                InputTokens: window.TotalInputTokens,
                WindowSize: window.ContextWindowSize,
                UncachedTokens: usage?.InputTokens,
                CacheReadTokens: usage?.CacheReadInputTokens,
                CacheWriteTokens: usage?.CacheCreationInputTokens,
                OutputTokens: usage?.OutputTokens);

        var cost = payload?.Cost is { } spent
            ? new StatusCost(spent.TotalCostUsd, spent.TotalDurationMs, spent.TotalLinesAdded, spent.TotalLinesRemoved)
            : null;

        var limits = payload?.RateLimits is { } rate
            ? new StatusLimits(
                Window(rate.FiveHour),
                Window(rate.SevenDay),
                rate.SpendLimit is { } spend
                    ? new StatusSpend(spend.UsedPercentage, Moment(spend.ResetsAt), spend.UsedUsd, spend.LimitUsd, spend.Period)
                    : null)
            : null;

        if (limits is { FiveHour: null, SevenDay: null, Spend: null })
        {
            limits = null;
        }

        return new SessionStatusSnapshot(
            Current,
            at,
            refreshSeconds,
            terminal,
            wrapping,
            payload?.Model?.DisplayName ?? payload?.Model?.Id,
            context,
            cost,
            limits,
            project,
            specialists);
    }

    private static StatusWindow? Window(StatuslineRateWindow? window) =>
        window?.UsedPercentage is { } used ? new StatusWindow(used, Moment(window.ResetsAt)) : null;

    private static DateTimeOffset? Moment(long? unixSeconds)
    {
        if (unixSeconds is not { } seconds)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}

/// <summary>
/// Where a hosted session's status line leaves what it was given, and where
/// the daemon reads it for Studio.
/// </summary>
/// <remarks>
/// <para>
/// The same arrangement as <see cref="SessionSignals"/>, for the same reasons:
/// the status line runs inside the agent, which holds no sign-in, so it writes
/// a file in the owner-only state directory, named by the id the daemon gave
/// the session in <see cref="SessionSignals.Variable"/>.
/// </para>
/// <para>
/// Unlike a signal, a snapshot is no use once its session has gone, so it is
/// removed when the session is forgotten and anything left behind by a daemon
/// that stopped is pruned after a day.
/// </para>
/// </remarks>
public static class SessionStatus
{
    /// <summary>How long a snapshot left by a session nobody hosts is kept.</summary>
    public static readonly TimeSpan Kept = TimeSpan.FromDays(1);

    /// <summary>The directory the snapshots live in, under the state directory.</summary>
    public static string Directory(string state) => Path.Combine(state, "studio", "status");

    /// <summary>Records a snapshot, written aside and moved so the daemon never reads half of one.</summary>
    /// <returns>False where the id is not one the daemon could have given.</returns>
    public static bool Write(string directory, string id, SessionStatusSnapshot snapshot)
    {
        if (!SessionSignals.IsSessionId(id))
        {
            return false;
        }

        System.IO.Directory.CreateDirectory(directory);

        var path = PathOf(directory, id);
        var temporary = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";

        File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, Json));
        File.Move(temporary, path, overwrite: true);

        return true;
    }

    /// <summary>The last snapshot for a session, or null for none, or none this build can read.</summary>
    public static SessionStatusSnapshot? Read(string directory, string id)
    {
        if (!SessionSignals.IsSessionId(id))
        {
            return null;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<SessionStatusSnapshot>(File.ReadAllText(PathOf(directory, id)), Json);

            // A newer launcher's shape is left unread rather than half read.
            return snapshot is { Version: SessionStatusSnapshot.Current } ? snapshot : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>When the session's snapshot was last written, without reading it, or null for none.</summary>
    /// <remarks>Cheap enough for the change stream to ask about every session on every turn.</remarks>
    public static DateTime? Stamp(string directory, string id)
    {
        if (!SessionSignals.IsSessionId(id))
        {
            return null;
        }

        var file = new FileInfo(PathOf(directory, id));

        return file.Exists ? file.LastWriteTimeUtc : null;
    }

    /// <summary>Forgets a session's snapshot.</summary>
    public static void Remove(string directory, string id)
    {
        if (!SessionSignals.IsSessionId(id))
        {
            return;
        }

        try
        {
            File.Delete(PathOf(directory, id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pruned later; nothing reads a snapshot for a session that has gone.
        }
    }

    /// <summary>Removes snapshots older than <see cref="Kept"/>, and asides a writer left behind.</summary>
    public static void Prune(string directory, DateTimeOffset now)
    {
        if (!System.IO.Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in System.IO.Directory.EnumerateFiles(directory))
        {
            try
            {
                if (now - File.GetLastWriteTimeUtc(file) > Kept)
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Tried again on the next start.
            }
        }
    }

    private static string PathOf(string directory, string id) => Path.Combine(directory, id + ".json");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
