using System.Text;
using System.Text.Json;
using Loadout.Core.Diagnostics;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Events;

/// <summary>Which events somebody has read.</summary>
/// <param name="Before">Everything at or before this has been read: what "mark all read" moves.</param>
/// <param name="Read">Events after <paramref name="Before"/> read one at a time.</param>
/// <remarks>
/// A watermark and a short list rather than a flag per event, so marking
/// everything read is one moment written down instead of thousands of ids,
/// and so the log itself is never rewritten to say what has been seen.
/// </remarks>
public sealed record EventMarks(DateTimeOffset? Before, IReadOnlyList<string> Read)
{
    /// <summary>Nothing read.</summary>
    public static EventMarks None { get; } = new(null, []);

    /// <summary>Whether this one has been read.</summary>
    public bool Has(UserEvent one)
    {
        ArgumentNullException.ThrowIfNull(one);

        return (Before is { } before && one.At <= before) || Read.Contains(one.Id, StringComparer.Ordinal);
    }
}

/// <summary>
/// Where the event history is kept: the events, what has been read, and
/// where the recorder had got to.
/// </summary>
/// <remarks>
/// <para>
/// Three files under <c>&lt;State&gt;/events</c>. <c>feed.jsonl</c> is one
/// event a line and only ever appended to, by the daemon's recorder alone, so
/// nothing interleaves; it is rewritten only when it is pruned, which the
/// daemon does as it starts, before the recorder runs. <c>read.json</c> is
/// written by the page and by <c>loadout events --mark-read</c>, aside and
/// moved so neither ever reads half of it. <c>recorder.json</c> is what the
/// recorder saw last, so a daemon that restarts does not take everything still
/// outstanding for something new.
/// </para>
/// <para>
/// Called feed rather than events because a separate log of what Loadout did
/// for you, for the report of whether it is earning its keep, is designed to
/// sit beside it as <c>events.jsonl</c>.
/// </para>
/// <para>
/// Kept for <see cref="DefaultDays"/> days, or what <c>events-days</c> says,
/// and never past <see cref="MaximumBytes"/>: a run stuck in a loop can say a
/// great deal inside a month, and every read scans the whole file.
/// </para>
/// </remarks>
public sealed class EventStore
{
    /// <summary>How long events are kept where nothing is set.</summary>
    public const int DefaultDays = 30;

    /// <summary>The most the feed may hold before its oldest events go, whatever their age.</summary>
    public const long MaximumBytes = 8L * 1024 * 1024;

    /// <summary>What pruning cuts an oversized feed back to, so it is not pruned again on the next start.</summary>
    public const long PrunedBytes = MaximumBytes * 3 / 4;

    /// <summary>The most single read marks kept after the watermark; older ones go first.</summary>
    public const int MaximumMarks = 2000;

    private readonly IFilePermissions? _permissions;

    /// <param name="state">The state directory.</param>
    /// <param name="permissions">Restricts the files to their owner, where given: what needed you is yours.</param>
    public EventStore(string state, IFilePermissions? permissions = null)
    {
        Directory = System.IO.Path.Combine(state, "events");
        _permissions = permissions;
    }

    /// <summary>The directory the three files are in.</summary>
    public string Directory { get; }

    /// <summary>The events, one a line.</summary>
    public string FeedPath => System.IO.Path.Combine(Directory, "feed.jsonl");

    /// <summary>What has been read.</summary>
    public string MarksPath => System.IO.Path.Combine(Directory, "read.json");

    /// <summary>What the recorder saw last.</summary>
    public string RecorderPath => System.IO.Path.Combine(Directory, "recorder.json");

    /// <summary>The configured retention, or the default for nothing or nonsense. Zero keeps everything the size allows.</summary>
    public static int Days(int? configured) => configured is >= 0 ? configured.Value : DefaultDays;

    /// <summary>Adds events to the end of the feed.</summary>
    public void Append(IReadOnlyList<UserEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0)
        {
            return;
        }

        Ready();

        var text = new StringBuilder();

        // A daemon killed mid-append leaves a line with no end, and the next
        // event appended to it would be lost with it.
        if (!EndsLine(FeedPath))
        {
            text.Append('\n');
        }

        foreach (var one in events)
        {
            text.Append(JsonSerializer.Serialize(one, Json)).Append('\n');
        }

        File.AppendAllText(FeedPath, text.ToString(), Encoding.UTF8);
        _permissions?.RestrictToCurrentUser(FeedPath);
    }

    /// <summary>Every event in the feed, oldest first. A line that will not parse is left out.</summary>
    /// <remarks>
    /// A line can be half written if the daemon was killed mid-append, and one
    /// bad line must not hide a month of good ones.
    /// </remarks>
    public IReadOnlyList<UserEvent> Read()
    {
        if (!File.Exists(FeedPath))
        {
            return [];
        }

        var events = new List<UserEvent>();

        try
        {
            using var trace = ReadLedger.Begin(FeedPath);
            using var stream = new FileStream(FeedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (reader.ReadLine() is { } line)
            {
                if (Parse(line) is { } one)
                {
                    events.Add(one);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return events;
        }

        return events;
    }

    /// <summary>
    /// Drops what is older than <paramref name="days"/>, where that is more
    /// than zero, then the oldest of what is left while the feed is over
    /// <see cref="MaximumBytes"/>.
    /// </summary>
    /// <returns>How many events went.</returns>
    /// <remarks>
    /// A <c>cleared</c> event goes with the event it clears, and is kept while
    /// that is, so what remains always folds the same way it did.
    /// </remarks>
    public int Prune(DateTimeOffset now, int days)
    {
        if (!File.Exists(FeedPath))
        {
            return 0;
        }

        var all = Read();
        var kept = days > 0
            ? all.Where(one => one.At >= now - TimeSpan.FromDays(days)).ToList()
            : [.. all];
        var bytes = kept.Sum(Size);

        if (new FileInfo(FeedPath).Length > MaximumBytes || bytes > MaximumBytes)
        {
            var from = 0;

            while (from < kept.Count && bytes > PrunedBytes)
            {
                bytes -= Size(kept[from]);
                from++;
            }

            kept = kept.Skip(from).ToList();
        }

        var ids = kept.Select(one => one.Id).ToHashSet(StringComparer.Ordinal);

        // A clearing whose event has gone is a sentence about nothing.
        kept = kept.Where(one => one.Clears is null || ids.Contains(one.Clears)).ToList();

        var gone = all.Count - kept.Count;

        if (gone == 0)
        {
            return 0;
        }

        Replace(FeedPath, string.Concat(kept.Select(one => JsonSerializer.Serialize(one, Json) + "\n")));

        // And the single marks for events that are no longer there.
        var marks = Marks();

        if (marks.Read.Any(id => !ids.Contains(id)))
        {
            WriteMarks(marks with { Read = [.. marks.Read.Where(ids.Contains)] });
        }

        return gone;
    }

    /// <summary>What has been read, or nothing for no file or one that will not parse.</summary>
    public EventMarks Marks()
    {
        try
        {
            using var trace = ReadLedger.Begin(MarksPath);

            return File.Exists(MarksPath)
                ? JsonSerializer.Deserialize<EventMarks>(File.ReadAllText(MarksPath), Json) is { } marks
                    ? marks with { Read = marks.Read ?? [] }
                    : EventMarks.None
                : EventMarks.None;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return EventMarks.None;
        }
    }

    /// <summary>Marks these read, keeping the newest <see cref="MaximumMarks"/> single marks.</summary>
    public EventMarks MarkRead(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var marks = Marks();
        var read = marks.Read.Concat(ids.Where(id => id is { Length: > 0 })).Distinct(StringComparer.Ordinal).ToList();

        return WriteMarks(marks with { Read = read.Count > MaximumMarks ? read[^MaximumMarks..] : read });
    }

    /// <summary>Marks everything up to this moment read, which empties the single marks it covers.</summary>
    public EventMarks MarkAllRead(DateTimeOffset upTo)
    {
        var events = Read().ToDictionary(one => one.Id, one => one.At, StringComparer.Ordinal);
        var marks = Marks();

        return WriteMarks(new EventMarks(
            marks.Before is { } before && before > upTo ? before : upTo,
            [.. marks.Read.Where(id => events.TryGetValue(id, out var at) && at > upTo)]));
    }

    /// <summary>What the recorder saw last, or nothing for a first start or a file it cannot read.</summary>
    public RecorderState Recorded()
    {
        try
        {
            using var trace = ReadLedger.Begin(RecorderPath);

            return File.Exists(RecorderPath)
                ? JsonSerializer.Deserialize<RecorderState>(File.ReadAllText(RecorderPath), Json) ?? RecorderState.Empty
                : RecorderState.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return RecorderState.Empty;
        }
    }

    /// <summary>Keeps what the recorder saw, for the next pass and the next start.</summary>
    public void Record(RecorderState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Replace(RecorderPath, JsonSerializer.Serialize(state, Json));
    }

    /// <summary>When the feed or the marks last changed, for the page's change stream to notice.</summary>
    public string Stamp()
    {
        static string Of(string path)
        {
            var file = new FileInfo(path);

            return file.Exists ? $"{file.Length}@{file.LastWriteTimeUtc.Ticks}" : "-";
        }

        return Of(FeedPath) + "/" + Of(MarksPath);
    }

    private EventMarks WriteMarks(EventMarks marks)
    {
        Replace(MarksPath, JsonSerializer.Serialize(marks, Json));

        return marks;
    }

    /// <summary>Written aside and moved, so nothing reads half of it; each writer has its own aside.</summary>
    private void Replace(string path, string text)
    {
        Ready();

        var aside = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";

        File.WriteAllText(aside, text, Encoding.UTF8);
        File.Move(aside, path, overwrite: true);
        _permissions?.RestrictToCurrentUser(path);
    }

    private void Ready()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.CreateDirectory(Directory);
            _permissions?.RestrictDirectoryToCurrentUser(Directory);
        }
    }

    private static UserEvent? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<UserEvent>(line, Json) is { Id.Length: > 0, Title: not null, Kind: not null } one ? one : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool EndsLine(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length == 0)
            {
                return true;
            }

            stream.Seek(-1, SeekOrigin.End);

            return stream.ReadByte() == '\n';
        }
        catch (FileNotFoundException)
        {
            return true;
        }
    }

    private static long Size(UserEvent one) => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(one, Json)) + 1;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
