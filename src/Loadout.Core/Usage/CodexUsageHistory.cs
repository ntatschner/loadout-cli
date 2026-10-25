using System.Globalization;
using System.Text.Json;
using Loadout.Core.Transcripts;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Usage;

/// <summary>
/// Adds up what Codex recorded spending.
/// </summary>
/// <remarks>
/// <para>
/// Codex keeps rollout files under <c>~/.codex/sessions</c>, laid out by date,
/// and reports its accounting in <c>token_count</c> events.
/// </para>
/// <para>
/// Those events are <em>running totals</em>, not amounts spent since the last
/// one, and they are emitted more than once per turn. Adding them up is
/// therefore wrong twice over: on one session here it gave 717 million tokens
/// where 8.5 million had been spent, eighty-four times the truth. Adding the
/// per-turn figures instead is also wrong, because the repeats repeat those
/// too. Only the last running total in a file is right, so that is what is
/// taken — one row per session, no arithmetic at all.
/// </para>
/// <para>
/// Codex counts cached and written tokens <em>inside</em> its input figure
/// rather than beside it, which was checked against real sessions rather than
/// assumed: <c>input_tokens + output_tokens</c> comes to exactly
/// <c>total_tokens</c> on every one. They are subtracted back out here so both
/// agents' numbers mean the same thing.
/// </para>
/// </remarks>
internal sealed class CodexUsageHistory : IUsageHistory
{
    /// <summary>Every rollout's reading, kept for the life of the process.</summary>
    /// <remarks>
    /// Static for the reason <see cref="ClaudeUsageHistory"/>'s is. Raise the
    /// version whenever <see cref="ParseAsync"/> or what it keeps changes.
    /// </remarks>
    private static readonly TranscriptReadings<CodexSession> Readings = new("codex", version: 1);

    private readonly IEnvironmentProvider _environment;
    private readonly string? _kept;

    /// <param name="environment">Where the agent's history is.</param>
    /// <param name="paths">Where readings are kept between runs; none keeps them only in memory.</param>
    public CodexUsageHistory(IEnvironmentProvider environment, IPlatformPaths? paths = null)
    {
        _environment = environment;
        _kept = paths is null ? null : TranscriptReadingsDirectory.Under(paths.Paths.State);
    }

    /// <summary>Drops the readings held in memory, as a new process starts with none.</summary>
    internal static void Forget() => Readings.Forget();

    /// <inheritdoc />
    public string Agent => "codex";

    /// <inheritdoc />
    public bool IsAvailable => Directory.Exists(Root);

    private string Root => Path.Combine(_environment.HomeDirectory, ".codex", "sessions");

    /// <inheritdoc />
    public async Task<OperationResult<UsageScan>> ScanAsync(
        DateOnly since,
        CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            return OperationResult<UsageScan>.Ok(new UsageScan([], UsageIntegrity.Empty));
        }

        List<FileInfo> listed;

        try
        {
            listed = [.. new DirectoryInfo(Root).EnumerateFiles("*.jsonl", SearchOption.AllDirectories)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<UsageScan>.Fail(
                $"Could not read Codex's history at {Root}: {ex.Message}");
        }

        var files = listed.Where(f => DateOnly.FromDateTime(f.LastWriteTimeUtc) >= since).ToList();

        var buckets = new Dictionary<(string Directory, DateOnly Day, string Model), UsageTotals>();

        var integrity = UsageIntegrity.Empty;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            integrity += Fold(file, await Readings.GetAsync(file, _kept, ParseAsync, ct).ConfigureAwait(false), since, buckets);
        }

        Readings.Keep([.. listed.Select(file => file.FullName)], _kept);

        var rows = buckets
            .Select(entry => new UsageBucket(
                Agent,
                entry.Key.Directory,
                entry.Key.Day,
                entry.Key.Model,
                entry.Value))
            .ToList();

        return OperationResult<UsageScan>.Ok(new UsageScan(rows, integrity));
    }

    /// <summary>Folds one session's final total into the running counts.</summary>
    private static UsageIntegrity Fold(
        FileInfo file,
        CodexSession? session,
        DateOnly since,
        Dictionary<(string, DateOnly, string), UsageTotals> buckets)
    {
        if (session is null)
        {
            return new UsageIntegrity(FilesSkipped: 1);
        }

        if (session.Latest is not { } latest)
        {
            // A session that never reported. Read successfully, nothing to add.
            return new UsageIntegrity(FilesRead: 1, RecordsUnrecognised: session.Unrecognised);
        }

        var when = session.Day ?? DateOnly.FromDateTime(file.LastWriteTimeUtc);

        if (when >= since)
        {
            var key = (session.Directory, when, session.Model);

            buckets[key] = buckets.TryGetValue(key, out var running)
                ? running + latest
                : latest;
        }

        return new UsageIntegrity(
            FilesRead: 1,
            RecordsCounted: 1,
            RecordsRepeated: session.Repeated,
            RecordsUnrecognised: session.Unrecognised);
    }

    /// <summary>What one rollout says about its session, or null when it could not be read.</summary>
    private static async Task<CodexSession?> ParseAsync(FileInfo file, CancellationToken ct)
    {
        var directory = "unknown";
        var model = "unknown";
        DateOnly? day = null;
        UsageTotals? latest = null;

        var repeated = 0;
        var unrecognised = 0;

        try
        {
            using var reader = new StreamReader(file.OpenRead());

            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                ct.ThrowIfCancellationRequested();

                if (!line.Contains("\"cwd\"", StringComparison.Ordinal)
                    && !line.Contains("\"token_count\"", StringComparison.Ordinal))
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
                    var root = document.RootElement;

                    if (root.ValueKind != JsonValueKind.Object
                        || !root.TryGetProperty("payload", out var payload)
                        || payload.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    // Both the opening metadata and every turn carry these, and
                    // the newest wins: a session can be told to work somewhere
                    // else, or switched to another model, part way through.
                    if (String(payload, "cwd") is { Length: > 0 } cwd)
                    {
                        directory = cwd;
                    }

                    if (String(payload, "model") is { Length: > 0 } named)
                    {
                        model = named;
                    }

                    if (String(payload, "type") != "token_count")
                    {
                        continue;
                    }

                    // Some of these arrive with nothing in them at all, which
                    // is ordinary rather than a fault.
                    if (!payload.TryGetProperty("info", out var info)
                        || info.ValueKind != JsonValueKind.Object
                        || !info.TryGetProperty("total_token_usage", out var total)
                        || total.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var totals = Read(total);

                    if (totals is null)
                    {
                        unrecognised++;
                        continue;
                    }

                    if (latest is not null)
                    {
                        // Every event after the first supersedes one already
                        // seen. Counted so the figure is explainable rather
                        // than mysterious.
                        repeated++;
                    }

                    latest = totals;
                    day = Day(root) ?? day;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return new CodexSession(directory, model, day, latest, repeated, unrecognised);
    }

    /// <summary>
    /// Turns one running total into counts, or null when it holds none of the
    /// fields this build knows.
    /// </summary>
    private static UsageTotals? Read(JsonElement total)
    {
        var found = false;

        var input = Number(total, "input_tokens", ref found);
        var cached = Number(total, "cached_input_tokens", ref found);
        var written = Number(total, "cache_write_input_tokens", ref found);
        var output = Number(total, "output_tokens", ref found);

        var ignored = false;
        var reasoning = Number(total, "reasoning_output_tokens", ref ignored);

        if (!found)
        {
            return null;
        }

        // Cached and written tokens are inside the input figure, so the
        // uncached remainder is what is left. Clamped because a format that
        // stopped nesting them would otherwise produce a negative count, and a
        // negative token is worse than a slightly wrong one.
        var uncached = Math.Max(0, input - cached - written);

        // Codex does not record which cache lifetime it bought. The cheaper one
        // is assumed, which understates cost rather than overstating the saving.
        return new UsageTotals(uncached, written, 0, cached, output, reasoning);
    }

    private static long Number(JsonElement parent, string name, ref bool found)
    {
        if (!parent.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var number))
        {
            return 0;
        }

        found = true;

        return number;
    }

    private static string? String(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateOnly? Day(JsonElement root)
    {
        if (String(root, "timestamp") is not { Length: > 0 } stamp)
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            stamp,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? DateOnly.FromDateTime(parsed.UtcDateTime)
            : null;
    }
}

/// <summary>What one Codex rollout says about its session, on its own.</summary>
/// <param name="Directory">The newest working directory it named.</param>
/// <param name="Model">The newest model it named.</param>
/// <param name="Day">The UTC day of its last reported total, when its line says.</param>
/// <param name="Latest">Its last running total, or null for a session that never reported.</param>
/// <param name="Repeated">Running totals superseded by a later one.</param>
/// <param name="Unrecognised">Totals that held no field this build knows.</param>
internal sealed record CodexSession(
    string Directory, string Model, DateOnly? Day, UsageTotals? Latest, int Repeated, int Unrecognised);
