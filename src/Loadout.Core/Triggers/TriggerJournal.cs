using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Triggers;

/// <summary>One line of the trigger journal: an event, or a decision about one.</summary>
/// <param name="At">When it was written.</param>
/// <param name="Kind">What it records: one of <see cref="TriggerEntryKinds"/>.</param>
/// <param name="Event">The event it is about, by id.</param>
/// <param name="Rule">The rule that decided, where one did.</param>
/// <param name="Version">That rule's version when it decided.</param>
/// <param name="Subject">What the event was about.</param>
/// <param name="Reason">Why, in words a person can check.</param>
/// <param name="Usd">Money: what was set aside, or what landed.</param>
/// <param name="Reservation">The reservation it opens or closes.</param>
/// <param name="Source">For an event: what produced it.</param>
/// <param name="EventKind">For an event: what happened.</param>
/// <param name="Project">For an event: where.</param>
/// <param name="Fields">For an event: what it carried.</param>
/// <param name="Origins">For an event: the rules whose actions led to it.</param>
/// <param name="Run">For a start: the run it became, once known.</param>
/// <param name="Happened">For an event: when it happened, where that is not when it arrived.</param>
/// <param name="Request">For a change to the rules: the request that asked for it, so a retry is not applied twice.</param>
public sealed record TriggerEntry(
    DateTimeOffset At,
    string Kind,
    string? Event = null,
    string? Rule = null,
    int? Version = null,
    string? Subject = null,
    string? Reason = null,
    decimal? Usd = null,
    string? Reservation = null,
    string? Source = null,
    string? EventKind = null,
    string? Project = null,
    IReadOnlyDictionary<string, string>? Fields = null,
    IReadOnlyList<string>? Origins = null,
    string? Run = null,
    DateTimeOffset? Happened = null,
    string? Request = null)
{
    /// <summary>The event an event entry recorded, as the rules would have seen it.</summary>
    /// <remarks>
    /// What lets a held event be decided later and a rule be tried against
    /// past events: the journal keeps everything a rule can ask of one.
    /// </remarks>
    public TriggerEvent ToEvent() =>
        new(
            Event ?? string.Empty,
            Happened ?? At,
            Source ?? string.Empty,
            EventKind ?? string.Empty,
            Subject ?? string.Empty,
            Project,
            Fields ?? new Dictionary<string, string>(),
            Origins ?? []);

    /// <summary>The entry recording an event as it arrived.</summary>
    public static TriggerEntry Of(TriggerEvent one, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(one);

        return new TriggerEntry(
            at,
            TriggerEntryKinds.Event,
            Event: one.Id,
            Subject: one.Subject,
            Source: one.Source,
            EventKind: one.Kind,
            Project: one.Project,
            Fields: one.Fields,
            Origins: one.Origins.Count > 0 ? one.Origins : null,
            Happened: one.At != at ? one.At : null);
    }
}

/// <summary>What a journal line can record.</summary>
public static class TriggerEntryKinds
{
    /// <summary>An event arrived.</summary>
    public const string Event = "event";

    /// <summary>A rule matched an event and will act.</summary>
    public const string Matched = "matched";

    /// <summary>A rule looked at an event and passed, with why.</summary>
    public const string Skipped = "skipped";

    /// <summary>A newer event about the same subject replaced one still waiting.</summary>
    public const string Superseded = "superseded";

    /// <summary>A match was kept back, by a pause or a lack of headroom.</summary>
    public const string Held = "held";

    /// <summary>A match was refused outright, with why.</summary>
    public const string Refused = "refused";

    /// <summary>Money was set aside for a start.</summary>
    public const string Reserved = "reserved";

    /// <summary>An action was started.</summary>
    public const string Started = "started";

    /// <summary>An action ended, and what it spent; its reservation is released.</summary>
    public const string Landed = "landed";

    /// <summary>A rule stopped acting, having reached a cap.</summary>
    public const string Paused = "paused";

    /// <summary>A person let a paused rule act again; its caps count from here.</summary>
    public const string Resumed = "resumed";

    /// <summary>A rule was saved, new or changed.</summary>
    public const string Edited = "edited";

    /// <summary>A rule was removed.</summary>
    public const string Removed = "removed";

    /// <summary>An action waiting to start was dropped, because its rule changed or went.</summary>
    public const string Withdrawn = "withdrawn";
}

/// <summary>
/// Every event the spine carried and every decision it made, a day to a file.
/// </summary>
/// <remarks>
/// <para>
/// Under <c>&lt;State&gt;/triggers/journal</c>, one <c>yyyy-MM-dd.jsonl</c> a
/// day in UTC, only ever appended to. A day to a file so pruning by range is
/// deleting files, and so the reads that come with every start - what has this
/// rule reserved in its window - scan days, not months.
/// </para>
/// <para>
/// It is the record that survives a restart. The spine keeps its state in
/// memory and rebuilds it from here as it starts, so anything it decided and
/// did not write down did not happen.
/// </para>
/// </remarks>
public sealed class TriggerJournal
{
    private readonly IFilePermissions? _permissions;
    private readonly Lock _gate = new();

    /// <param name="state">The state directory.</param>
    /// <param name="permissions">Restricts the files to their owner, where given.</param>
    public TriggerJournal(string state, IFilePermissions? permissions = null)
    {
        Directory = Path.Combine(state, "triggers", "journal");
        _permissions = permissions;
    }

    /// <summary>Where the day files are.</summary>
    public string Directory { get; }

    /// <summary>The file a moment belongs in.</summary>
    public string DayPath(DateTimeOffset at) =>
        Path.Combine(Directory, at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");

    /// <summary>Adds entries to the end of the day each belongs to.</summary>
    /// <remarks>
    /// Under a lock, because the dispatcher and the workers both write and two
    /// appends interleaving mid-line would cost both lines.
    /// </remarks>
    public void Append(params TriggerEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            System.IO.Directory.CreateDirectory(Directory);

            foreach (var day in entries.GroupBy(one => DayPath(one.At)))
            {
                var text = new StringBuilder();

                // A daemon killed mid-append leaves a line with no end, and
                // the next entry appended to it would be lost with it.
                if (!EndsLine(day.Key))
                {
                    text.Append('\n');
                }

                foreach (var one in day)
                {
                    text.Append(JsonSerializer.Serialize(one, Json)).Append('\n');
                }

                File.AppendAllText(day.Key, text.ToString(), Encoding.UTF8);
                _permissions?.RestrictToCurrentUser(day.Key);
            }
        }
    }

    /// <summary>Every entry from <paramref name="since"/> on, oldest first. A line that will not parse is left out.</summary>
    public IReadOnlyList<TriggerEntry> Read(DateTimeOffset since)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        var first = Path.GetFileName(DayPath(since));
        var entries = new List<TriggerEntry>();

        // Day names sort as the days do, so the files to read are a tail of
        // the sorted list.
        foreach (var file in System.IO.Directory.GetFiles(Directory, "*.jsonl")
                     .Where(one => string.CompareOrdinal(Path.GetFileName(one), first) >= 0)
                     .Order(StringComparer.Ordinal))
        {
            entries.AddRange(ReadFile(file).Where(one => one.At >= since));
        }

        return entries;
    }

    private static IEnumerable<TriggerEntry> ReadFile(string file)
    {
        var entries = new List<TriggerEntry>();

        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (reader.ReadLine() is { } line)
            {
                if (Parse(line) is { } one)
                {
                    entries.Add(one);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // What was read is still true; one unreadable day is not a reason
            // to forget the rest.
        }

        return entries;
    }

    private static TriggerEntry? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TriggerEntry>(line, Json) is { Kind.Length: > 0 } one ? one : null;
        }
        catch (JsonException)
        {
            // Half a line from a daemon killed mid-append.
            return null;
        }
    }

    private static bool EndsLine(string path)
    {
        if (!File.Exists(path))
        {
            return true;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        if (stream.Length == 0)
        {
            return true;
        }

        stream.Seek(-1, SeekOrigin.End);

        return stream.ReadByte() == '\n';
    }

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
