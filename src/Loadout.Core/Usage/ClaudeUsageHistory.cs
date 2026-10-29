using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Loadout.Core.Diagnostics;
using Loadout.Core.Transcripts;
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
internal sealed class ClaudeUsageHistory : IUsageHistory
{
    /// <summary>
    /// Every transcript's reading, kept for the life of the process.
    /// </summary>
    /// <remarks>
    /// Static, because in the daemon each command gets services of its own and
    /// a reading kept on one would be thrown away with it; a reading belongs
    /// to the file it was read from, whoever asks. Raise the version whenever
    /// <see cref="ParseAsync"/> or what it keeps changes.
    /// </remarks>
    private static readonly TranscriptReadings<ClaudeTranscript> Readings = new("claude", version: 1);

    private readonly IEnvironmentProvider _environment;
    private readonly string? _kept;

    /// <param name="environment">Where the agent's history is.</param>
    /// <param name="paths">Where readings are kept between runs; none keeps them only in memory.</param>
    public ClaudeUsageHistory(IEnvironmentProvider environment, IPlatformPaths? paths = null)
    {
        _environment = environment;
        _kept = paths is null ? null : TranscriptReadingsDirectory.Under(paths.Paths.State);
    }

    /// <summary>Drops the readings held in memory, as a new process starts with none.</summary>
    internal static void Forget() => Readings.Forget();

    /// <inheritdoc />
    public string Agent => "claude";

    /// <inheritdoc />
    public bool IsAvailable => Directory.Exists(Root);

    private string Root => Agents.AgentHome.ClaudeProjects(_environment);

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
                $"Could not read Claude's history at {Root}: {ex.Message}");
        }

        // A transcript last written before the window opened cannot hold
        // anything inside it, so it is never opened at all.
        var files = listed.Where(f => DateOnly.FromDateTime(f.LastWriteTimeUtc) >= since).ToList();

        var buckets = new Dictionary<(string Directory, DateOnly Day, string Model), UsageTotals>();

        // Held across every file, not per file: a resumed or forked session
        // copies earlier messages into a new transcript, and they were paid
        // for once.
        var counted = new HashSet<string>(StringComparer.Ordinal);

        var integrity = UsageIntegrity.Empty;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            integrity += Fold(
                file,
                await Readings.GetAsync(file, _kept, ParseAsync, ct).ConfigureAwait(false),
                since,
                buckets,
                counted);
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

    /// <summary>Folds one transcript's reading into the running totals.</summary>
    private static UsageIntegrity Fold(
        FileInfo file,
        ClaudeTranscript? reading,
        DateOnly since,
        Dictionary<(string, DateOnly, string), UsageTotals> buckets,
        HashSet<string> counted)
    {
        if (reading is null)
        {
            return new UsageIntegrity(FilesSkipped: 1);
        }

        var read = 0;
        var repeated = 0;
        var unrecognised = reading.Unnamed;

        foreach (var message in reading.Messages)
        {
            if (!counted.Add(message.Id))
            {
                repeated++;
                continue;
            }

            if (message.Totals is not { Length: 6 } counts)
            {
                unrecognised++;
                continue;
            }

            read++;

            var day = message.Day ?? DateOnly.FromDateTime(file.LastWriteTimeUtc);

            if (day < since)
            {
                continue;
            }

            var totals = new UsageTotals(counts[0], counts[1], counts[2], counts[3], counts[4], counts[5]);
            var key = (reading.Directories[message.Directory], day, reading.Models[message.Model]);

            buckets[key] = buckets.TryGetValue(key, out var running)
                ? running + totals
                : totals;
        }

        return new UsageIntegrity(
            FilesRead: 1,
            RecordsCounted: read,
            RecordsRepeated: repeated,
            RecordsUnrecognised: unrecognised);
    }

    /// <summary>
    /// Every message with usage on it in one transcript, in the order written,
    /// or null when the file could not be read.
    /// </summary>
    private static async Task<ClaudeTranscript?> ParseAsync(FileInfo file, CancellationToken ct)
    {
        var messages = new List<ClaudeMessage>();
        var directories = new List<string>();
        var models = new List<string>();
        var unnamed = 0;

        // The working directory appears on every line, but reading it from the
        // first one that has it and carrying it forward saves touching the
        // property on lines that do not need it.
        var directory = "unknown";

        try
        {
            using var trace = ReadLedger.Begin(file.FullName);
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
                        unnamed++;
                        continue;
                    }

                    // Kept even when its counts cannot be read: the message is
                    // still seen, so a later copy of it is still a repeat.
                    messages.Add(new ClaudeMessage(
                        id,
                        IndexOf(directories, directory),
                        IndexOf(models, String(message, "model") ?? "unknown"),
                        Day(root),
                        Read(usage) is { } totals
                            ? [totals.Input, totals.CacheWrite5m, totals.CacheWrite1h, totals.CacheRead, totals.Output, totals.Thinking]
                            : null));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return new ClaudeTranscript(directories, models, messages, unnamed);
    }

    /// <summary>Where a name is in a reading's table of them, added at the end when it is new.</summary>
    /// <remarks>
    /// A transcript names one directory and one or two models across thousands
    /// of messages, so each is kept once and the messages point at it.
    /// </remarks>
    private static int IndexOf(List<string> table, string name)
    {
        // The last one first: a transcript names the same one line after line.
        for (var i = table.Count - 1; i >= 0; i--)
        {
            if (string.Equals(table[i], name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        table.Add(name);

        return table.Count - 1;
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

/// <summary>What one Claude transcript says about usage, on its own.</summary>
/// <remarks>Named short when stored: there is one of these per message, and months of them.</remarks>
/// <param name="Directories">The working directories its messages were written in, each once.</param>
/// <param name="Models">The models that answered, each once.</param>
/// <param name="Messages">Every message with usage on it and an identifier, in the order written.</param>
/// <param name="Unnamed">Lines with usage on them and no message identifier.</param>
internal sealed record ClaudeTranscript(
    [property: JsonPropertyName("d")] IReadOnlyList<string> Directories,
    [property: JsonPropertyName("m")] IReadOnlyList<string> Models,
    [property: JsonPropertyName("r")] IReadOnlyList<ClaudeMessage> Messages,
    [property: JsonPropertyName("u")] int Unnamed);

/// <summary>One message's usage, as its line recorded it.</summary>
/// <param name="Id">The message identifier, the same on every line and every copy of it.</param>
/// <param name="Directory">Where in the reading's directories the working directory carried forward to its line is.</param>
/// <param name="Model">Where in the reading's models the model that answered is.</param>
/// <param name="Day">The UTC day its line was written, when the line says.</param>
/// <param name="Totals">
/// Its counts in the order <see cref="UsageTotals"/> takes them, or null when
/// the usage held no field this build knows.
/// </param>
internal sealed record ClaudeMessage(
    [property: JsonPropertyName("i")] string Id,
    [property: JsonPropertyName("d")] int Directory,
    [property: JsonPropertyName("m")] int Model,
    [property: JsonPropertyName("y")] DateOnly? Day,
    [property: JsonPropertyName("t")] long[]? Totals);
