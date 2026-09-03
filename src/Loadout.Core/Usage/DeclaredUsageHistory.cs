using System.Globalization;
using System.Text.Json;
using Loadout.Core.Transcripts;
using Loadout.Models.Agents;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Usage;

/// <summary>
/// Counts any agent's tokens from a description of its transcripts.
/// </summary>
/// <remarks>
/// <para>
/// The counting counterpart of the described session reader, and it wants the
/// opposite thing from a file it cannot understand. A listing skips a bad
/// transcript and carries on, because one unreadable file must not cost somebody
/// the other forty. Arithmetic cannot: a reader that meets a renamed field does
/// not fail, it counts zero, and returns a total that looks entirely reasonable.
/// </para>
/// <para>
/// So every skipped file and every record that carried an identifier but no
/// numbers is counted and reported. That matters more for a described format
/// than a compiled one: a description is somebody's account of a file they do
/// not own, and the day the agent changes it, the only thing standing between a
/// wrong total and a believed one is the count of what could not be read.
/// </para>
/// </remarks>
internal sealed class DeclaredUsageHistory : IUsageHistory
{
    private readonly IEnvironmentProvider _environment;
    private readonly TranscriptFormat _format;
    private readonly TranscriptUsageFormat _usage;

    public DeclaredUsageHistory(
        string agent,
        TranscriptFormat format,
        IEnvironmentProvider environment)
    {
        Agent = agent;
        _format = format;
        _usage = format.Usage ?? new TranscriptUsageFormat();
        _environment = environment;
    }

    /// <inheritdoc />
    public string Agent { get; }

    /// <inheritdoc />
    public bool IsAvailable => _format.CanCount && Directory.Exists(Root);

    private string Root => TranscriptPaths.Expand(_format.Root, _environment);

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
                .EnumerateFiles(
                    _format.Files,
                    _format.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<UsageScan>.Fail(
                $"Could not read {Agent}'s transcripts at {Root}: {ex.Message}");
        }

        var buckets = new Dictionary<(string Directory, DateOnly Day, string Model), UsageTotals>();

        // Repeats are judged across the whole scan rather than per file, because
        // an agent that resumes a conversation copies earlier records into the
        // new transcript and they were paid for once.
        var counted = new HashSet<string>(StringComparer.Ordinal);
        var integrity = UsageIntegrity.Empty;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            integrity += await ScanFileAsync(file, since, buckets, counted, ct).ConfigureAwait(false);
        }

        return OperationResult<UsageScan>.Ok(new UsageScan(
            buckets
                .Select(entry => new UsageBucket(
                    Agent,
                    entry.Key.Directory,
                    entry.Key.Day,
                    entry.Key.Model,
                    entry.Value))
                .ToList(),
            integrity));
    }

    private async Task<UsageIntegrity> ScanFileAsync(
        FileInfo file,
        DateOnly since,
        Dictionary<(string, DateOnly, string), UsageTotals> buckets,
        HashSet<string> counted,
        CancellationToken ct)
    {
        var read = 0;
        var repeated = 0;
        var unrecognised = 0;

        // Carried between lines: formats that write the working directory once
        // expect it to hold for everything after it.
        var directory = "unknown";

        try
        {
            using var reader = new StreamReader(file.OpenRead());

            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
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
                    // A line that is not JSON is not an accounting record that
                    // could not be read; it is a banner, or a half-written last
                    // line. Counting it as unrecognised would put a caveat on
                    // every report.
                    continue;
                }

                using (document)
                {
                    var root = document.RootElement;

                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (TranscriptPaths.String(root, _usage.Directory) is { Length: > 0 } cwd)
                    {
                        directory = cwd;
                    }

                    var totals = Totals(root);

                    // Nothing here says it is an accounting record, so it is one
                    // of the many lines that are not. Silence is right.
                    if (totals is null)
                    {
                        if (Identifies(root))
                        {
                            // It does look like one, and carried no number this
                            // description knows how to find. That is the shape
                            // change worth hearing about.
                            unrecognised++;
                        }

                        continue;
                    }

                    if (_usage.Id is { Length: > 0 })
                    {
                        if (TranscriptPaths.String(root, _usage.Id) is not { Length: > 0 } id)
                        {
                            unrecognised++;
                            continue;
                        }

                        if (!counted.Add(id))
                        {
                            repeated++;
                            continue;
                        }
                    }

                    read++;

                    var day = Day(root) ?? DateOnly.FromDateTime(file.LastWriteTimeUtc);

                    if (day < since)
                    {
                        continue;
                    }

                    var model = TranscriptPaths.String(root, _usage.Model) ?? "unknown";
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
    /// Whether this line claims to be an accounting record at all.
    /// </summary>
    /// <remarks>
    /// The identifier is the marker when one is described, and the model when it
    /// is not. Without either there is nothing to tell an accounting record from
    /// a message, and every ordinary line would be reported as unreadable.
    /// </remarks>
    private bool Identifies(JsonElement root) =>
        TranscriptPaths.String(root, _usage.Id) is { Length: > 0 }
        || TranscriptPaths.String(root, _usage.Model) is { Length: > 0 };

    /// <summary>The counts on one line, or null when it carries none.</summary>
    private UsageTotals? Totals(JsonElement root)
    {
        var found = false;

        var input = TranscriptPaths.Number(root, _usage.Input, ref found);
        var output = TranscriptPaths.Number(root, _usage.Output, ref found);
        var cacheRead = TranscriptPaths.Number(root, _usage.CacheRead, ref found);
        var write5m = TranscriptPaths.Number(root, _usage.CacheWrite5m, ref found);
        var write1h = TranscriptPaths.Number(root, _usage.CacheWrite1h, ref found);
        var thinking = TranscriptPaths.Number(root, _usage.Thinking, ref found);

        return found
            ? new UsageTotals(input, write5m, write1h, cacheRead, output, thinking)
            : null;
    }

    private DateOnly? Day(JsonElement root) =>
        TranscriptPaths.String(root, _usage.Timestamp) is { Length: > 0 } text
        && DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var when)
            ? DateOnly.FromDateTime(when.UtcDateTime)
            : null;
}
