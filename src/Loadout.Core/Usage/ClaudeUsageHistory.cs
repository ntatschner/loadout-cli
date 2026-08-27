using System.Globalization;
using System.Text.Json;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Usage;

/// <summary>
/// Adds up what Claude Code recorded spending.
/// </summary>
/// <remarks>
/// <para>
/// The counts live on the same transcript lines the resume list is built from,
/// under <c>message.usage</c>. Reading them costs one more pass over files the
/// launcher already opens.
/// </para>
/// <para>
/// The one thing that must not be got wrong is counting a message twice. A
/// transcript writes one line per content block — text, thinking, each tool
/// call — and every one of those lines carries the <em>whole message's</em>
/// usage, not its own share. Lines are also occasionally rewritten, so the same
/// entry can appear again much later in the file. Adding up lines rather than
/// messages inflated the totals on this machine by three quarters, and produced
/// figures that looked entirely reasonable while being wrong. So accounting is
/// keyed on the message identifier, once, across the whole scan.
/// </para>
/// </remarks>
public sealed class ClaudeUsageHistory : IUsageHistory
{
    private readonly IEnvironmentProvider _environment;

    public ClaudeUsageHistory(IEnvironmentProvider environment) => _environment = environment;

    /// <inheritdoc />
    public string Agent => "claude";

    /// <inheritdoc />
    public bool IsAvailable => Directory.Exists(Root);

    private string Root => Path.Combine(_environment.HomeDirectory, ".claude", "projects");

    /// <inheritdoc />
    public async Task<OperationResult<UsageScan>> ScanAsync(
        DateOnly since,
        CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            return OperationResult<UsageScan>.Ok(new UsageScan([], UsageIntegrity.Empty));
        }

        List<FileInfo> files;

        try
        {
            files = new DirectoryInfo(Root)
                .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                // A transcript last written before the window opened cannot
                // hold anything inside it, so it is never opened at all.
                .Where(f => DateOnly.FromDateTime(f.LastWriteTimeUtc) >= since)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<UsageScan>.Fail(
                $"Could not read Claude's history at {Root}: {ex.Message}");
        }

        var buckets = new Dictionary<(string Directory, DateOnly Day, string Model), UsageTotals>();

        // Held across every file, not per file: a resumed or forked session
        // copies earlier messages into a new transcript, and they were paid
        // for once.
        var counted = new HashSet<string>(StringComparer.Ordinal);

        var integrity = UsageIntegrity.Empty;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            integrity += await ReadAsync(file, since, buckets, counted, ct).ConfigureAwait(false);
        }

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

    /// <summary>Folds one transcript into the running totals.</summary>
    private static async Task<UsageIntegrity> ReadAsync(
        FileInfo file,
        DateOnly since,
        Dictionary<(string, DateOnly, string), UsageTotals> buckets,
        HashSet<string> counted,
        CancellationToken ct)
    {
        var read = 0;
        var repeated = 0;
        var unrecognised = 0;

        // The working directory appears on every line, but reading it from the
        // first one that has it and carrying it forward saves touching the
        // property on lines that do not need it.
        var directory = "unknown";

        try
        {
            using var reader = new StreamReader(file.OpenRead());

            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                ct.ThrowIfCancellationRequested();

                // Most lines are user turns and tool results with no accounting
                // on them at all. Testing for the key before parsing skips the
                // JSON cost for the large majority of a transcript.
                if (!line.Contains("\"usage\"", StringComparison.Ordinal))
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
                    // A half-written final line while an agent is running.
                    continue;
                }

                using (document)
                {
                    var root = document.RootElement;

                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (String(root, "cwd") is { Length: > 0 } cwd)
                    {
                        directory = cwd;
                    }

                    if (!root.TryGetProperty("message", out var message)
                        || message.ValueKind != JsonValueKind.Object
                        || !message.TryGetProperty("usage", out var usage)
                        || usage.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    // No identifier means no way to tell a repeat from a new
                    // message, and counting it risks inflating the total. Not
                    // silently: this is exactly the shape change worth hearing
                    // about.
                    if (String(message, "id") is not { Length: > 0 } id)
                    {
                        unrecognised++;
                        continue;
                    }

                    if (!counted.Add(id))
                    {
                        repeated++;
                        continue;
                    }

                    var totals = Read(usage);

                    if (totals is null)
                    {
                        unrecognised++;
                        continue;
                    }

                    read++;

                    var day = Day(root) ?? DateOnly.FromDateTime(file.LastWriteTimeUtc);

                    if (day < since)
                    {
                        continue;
                    }

                    var model = String(message, "model") ?? "unknown";
                    var key = (directory, day, model);

                    buckets[key] = buckets.TryGetValue(key, out var running)
                        ? running + totals.Value
                        : totals.Value;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new UsageIntegrity(FilesSkipped: 1);
        }

        return new UsageIntegrity(
            FilesRead: 1,
            RecordsCounted: read,
            RecordsRepeated: repeated,
            RecordsUnrecognised: unrecognised);
    }

    /// <summary>
    /// Turns one usage object into counts, or null when it holds none of the
    /// fields this build knows.
    /// </summary>
    /// <remarks>
    /// Null rather than zero on purpose. Zero is a number somebody would
    /// believe; null is reported as a record that could not be read, which is
    /// what a renamed field actually means.
    /// </remarks>
    private static UsageTotals? Read(JsonElement usage)
    {
        var found = false;

        var input = Number(usage, "input_tokens", ref found);
        var read = Number(usage, "cache_read_input_tokens", ref found);
        var output = Number(usage, "output_tokens", ref found);

        long write5m = 0;
        long write1h = 0;

        // The lifetimes are recorded separately and are not billed alike, so
        // they are kept apart. When the breakdown is absent the total still is
        // not, and the cheaper lifetime is assumed — which understates cost
        // rather than overstating the saving.
        if (usage.TryGetProperty("cache_creation", out var creation)
            && creation.ValueKind == JsonValueKind.Object)
        {
            write5m = Number(creation, "ephemeral_5m_input_tokens", ref found);
            write1h = Number(creation, "ephemeral_1h_input_tokens", ref found);
        }
        else
        {
            write5m = Number(usage, "cache_creation_input_tokens", ref found);
        }

        long thinking = 0;

        if (usage.TryGetProperty("output_tokens_details", out var details)
            && details.ValueKind == JsonValueKind.Object)
        {
            var ignored = false;
            thinking = Number(details, "thinking_tokens", ref ignored);
        }

        return found
            ? new UsageTotals(input, write5m, write1h, read, output, thinking)
            : null;
    }

    /// <summary>Reads a count, noting whether the field was there at all.</summary>
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

    /// <summary>The UTC day a line was written, or null when it carries no usable timestamp.</summary>
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
