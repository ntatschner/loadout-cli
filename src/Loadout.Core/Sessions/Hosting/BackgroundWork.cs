using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Loadout.Core.Diagnostics;

namespace Loadout.Core.Sessions.Hosting;

/// <summary>What the end of a Claude transcript says about work the agent left running.</summary>
/// <param name="Outstanding">Whether anything it started in the background has not reported back.</param>
/// <param name="WakeupDue">When a wakeup it scheduled is due, where one is still to come.</param>
public sealed record BackgroundReading(bool Outstanding, DateTimeOffset? WakeupDue);

/// <summary>What a transcript leaves open, before it is weighed against a time.</summary>
/// <param name="OpenUntil">When each task still out stops counting, or null for one that never does.</param>
/// <param name="Wakeup">The last wakeup scheduled, whether or not it has fired.</param>
/// <param name="LastEntry">When the last message was written.</param>
internal sealed record BackgroundTally(IReadOnlyList<DateTimeOffset?> OpenUntil, DateTimeOffset? Wakeup, DateTimeOffset? LastEntry)
{
    /// <summary>The wakeup, where it is still to come: one due before the last thing written has already fired.</summary>
    public DateTimeOffset? Due => Wakeup is { } w && (LastEntry is null || w > LastEntry) ? Wakeup : null;
}

/// <summary>
/// Tells an agent that has stopped to wait on its own background work apart
/// from one that has stopped to wait for its person, from the tail of its
/// transcript.
/// </summary>
/// <remarks>
/// <para>
/// Claude says it is idle about a minute after any turn ends, whether the
/// turn ended on a question or on "I'll carry on when the tests finish". The
/// difference is in the transcript: a background launch (a shell, an agent,
/// a Monitor, a Workflow) leaves a task id in its tool result, and its report
/// comes back as a task notification naming that id. A launch with no report
/// is work still out; a wakeup scheduled for later than anything since is a
/// turn still to come.
/// </para>
/// <para>
/// Only ids, times and the shape of entries are read. Nothing from the text
/// of the conversation is kept or returned: the caller gets a yes or no and,
/// for a wakeup, a time.
/// </para>
/// <para>
/// Tied to the shape Claude Code writes its transcripts in, which it can
/// change. A shape this does not recognise reads as nothing outstanding, so
/// the session shows as waiting for you, which is what it showed before.
/// </para>
/// </remarks>
public static partial class BackgroundWork
{
    /// <summary>How much of the end of a transcript is read. Launches older than this are taken as finished.</summary>
    public const int Tail = 8 * 1024 * 1024;

    /// <summary>
    /// Reads the tail of a transcript file, or null where it cannot be read
    /// or is not a transcript, which the caller treats as waiting for you.
    /// </summary>
    /// <param name="path">The transcript.</param>
    /// <param name="now">When the agent said it was idle.</param>
    /// <param name="silentAfter">
    /// How long work with no time of its own may go unreported and still
    /// count, or null for as long as it takes. Something started an hour ago
    /// and never heard from is far more often a server the agent left running
    /// for you to look at than work it is waiting on.
    /// </param>
    public static BackgroundReading? Read(string? path, DateTimeOffset now, TimeSpan? silentAfter = null) =>
        ReadTail(path) is { } lines ? Scan(lines, now, silentAfter) : null;

    /// <summary>
    /// The lines of the last <see cref="Tail"/> bytes of a transcript, oldest
    /// first, or null where it cannot be read or is not a transcript.
    /// </summary>
    internal static IReadOnlyList<string>? ReadTail(string? path)
    {
        if (path is null || !path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var trace = ReadLedger.Begin(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var skipFirst = stream.Length > Tail;

            trace?.Read(Math.Min(stream.Length, Tail));

            if (skipFirst)
            {
                stream.Seek(-Tail, SeekOrigin.End);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var lines = new List<string>();

            // The first line of a tail is cut part way through.
            if (skipFirst)
            {
                reader.ReadLine();
            }

            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }

            return lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Reads transcript lines, oldest first, or null where none of them is an entry.</summary>
    /// <param name="lines">The lines.</param>
    /// <param name="now">When the agent said it was idle.</param>
    /// <param name="silentAfter">As for <see cref="Read"/>.</param>
    public static BackgroundReading? Scan(IEnumerable<string> lines, DateTimeOffset now, TimeSpan? silentAfter = null)
    {
        if (Tally(lines, silentAfter) is not { } tally)
        {
            return null;
        }

        var outstanding = tally.OpenUntil.Any(expires => expires is null || expires > now);

        return new BackgroundReading(outstanding || tally.Due is not null, tally.Due);
    }

    /// <summary>
    /// What is still out at the end of transcript lines, without deciding
    /// against a time: when each open task stops counting, the last wakeup
    /// and the last entry. Null where none of the lines is an entry.
    /// </summary>
    /// <param name="lines">The lines, oldest first.</param>
    /// <param name="silentAfter">As for <see cref="Read"/>.</param>
    internal static BackgroundTally? Tally(IEnumerable<string> lines, TimeSpan? silentAfter)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var tools = new Dictionary<string, string>(StringComparer.Ordinal);
        var open = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);
        DateTimeOffset? wakeup = null;
        DateTimeOffset? last = null;
        var entries = 0;

        foreach (var line in lines)
        {
            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                // A line still being written, or one this does not know.
                continue;
            }

            using (document)
            {
                var entry = document.RootElement;

                if (entry.ValueKind != JsonValueKind.Object
                    || entry.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                // A report that arrives while the agent is mid-turn is queued
                // rather than written as a message of its own, and only these
                // say so.
                switch (Text(entry, "type"))
                {
                    case "queue-operation" when Text(entry, "operation") == "enqueue":
                        Ended(Text(entry, "content"), open);

                        continue;

                    case "attachment" when entry.TryGetProperty("attachment", out var attached) && attached.ValueKind == JsonValueKind.Object:
                        Attached(attached, open);

                        continue;

                    case "user" or "assistant":
                        break;

                    default:
                        continue;
                }

                entries++;

                if (Text(entry, "timestamp") is { } stamp
                    && DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
                {
                    last = at;
                }

                var content = entry.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object
                    && message.TryGetProperty("content", out var c) ? c : default;

                if (Text(entry, "type") == "assistant")
                {
                    foreach (var part in Parts(content))
                    {
                        if (Text(part, "type") == "tool_use" && Text(part, "id") is { } id && Text(part, "name") is { } name)
                        {
                            tools[id] = name;
                        }
                    }

                    continue;
                }

                Launched(entry, content, tools, open, ref wakeup, last, silentAfter);
                Reported(entry, content, open);
            }
        }

        if (entries == 0)
        {
            return null;
        }

        return new BackgroundTally([.. open.Values], wakeup, last);
    }

    /// <summary>A tool result that started something in the background, stopped it, or set a wakeup.</summary>
    private static void Launched(
        JsonElement entry,
        JsonElement content,
        Dictionary<string, string> tools,
        Dictionary<string, DateTimeOffset?> open,
        ref DateTimeOffset? wakeup,
        DateTimeOffset? at,
        TimeSpan? silentAfter)
    {
        if (!entry.TryGetProperty("toolUseResult", out var result))
        {
            return;
        }

        var tool = Parts(content)
            .Where(part => Text(part, "type") == "tool_result")
            .Select(part => Text(part, "tool_use_id") is { } id && tools.TryGetValue(id, out var name) ? name : null)
            .FirstOrDefault(name => name is not null);

        if (tool == "ScheduleWakeup")
        {
            // A wakeup replaces the one before it, and a loop told to stop has none.
            wakeup = result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("scheduledFor", out var scheduled)
                && scheduled.TryGetInt64(out var ms)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                    : null;

            return;
        }

        if (result.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (tool == "TaskStop")
        {
            if (Text(result, "task_id") is { } stopped)
            {
                open.Remove(stopped);
            }

            return;
        }

        if (Text(result, "backgroundTaskId") is { } shell)
        {
            open[shell] = at + silentAfter;
        }
        else if (result.TryGetProperty("isAsync", out var async) && async.ValueKind == JsonValueKind.True
            && Text(result, "agentId") is { } agent)
        {
            open[agent] = at + silentAfter;
        }
        else if (Text(result, "taskId") is { } task)
        {
            // A Monitor ends on its own when its time is up, unless it was
            // made to last; a Workflow has no time.
            var persistent = result.TryGetProperty("persistent", out var p) && p.ValueKind == JsonValueKind.True;

            open[task] = !persistent && at is { } started
                && result.TryGetProperty("timeoutMs", out var timeout) && timeout.TryGetInt64(out var limit)
                    ? started.AddMilliseconds(limit)
                    : at + silentAfter;
        }
    }

    /// <summary>
    /// A report from background work delivered as a message of its own: a
    /// task notification, a subagent's hand-back, or what TaskOutput read.
    /// </summary>
    private static void Reported(JsonElement entry, JsonElement content, Dictionary<string, DateTimeOffset?> open)
    {
        if (entry.TryGetProperty("toolUseResult", out _))
        {
            // TaskOutput says whether the task it read has finished.
            foreach (var part in Parts(content).Where(part => Text(part, "type") == "tool_result"))
            {
                Ended(part.TryGetProperty("content", out var result) ? Flatten(result) : null, open);
            }

            return;
        }

        if (!entry.TryGetProperty("origin", out var origin) || origin.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        switch (Text(origin, "kind"))
        {
            case "peer" when Text(origin, "senderTaskId") is { } sender:
                open.Remove(sender);

                break;

            case "task-notification":
                Ended(Flatten(content), open);

                break;
        }
    }

    /// <summary>What the agent was handed while it was busy: a queued report, or a task's status.</summary>
    private static void Attached(JsonElement attached, Dictionary<string, DateTimeOffset?> open)
    {
        switch (Text(attached, "type"))
        {
            case "queued_command":
                Ended(Text(attached, "prompt"), open);

                break;

            case "task_status" when Text(attached, "taskId") is { } task && Text(attached, "status") is { } status && !StillGoing(status):
                open.Remove(task);

                break;
        }
    }

    /// <summary>
    /// Ends every task a report names, where it says the task has stopped:
    /// any status but running, or a subagent handing back its final report.
    /// </summary>
    /// <remarks>
    /// A Monitor's events carry no status, and it keeps watching, so an event
    /// ends nothing.
    /// </remarks>
    private static void Ended(string? text, Dictionary<string, DateTimeOffset?> open)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (text.Contains(HandBack, StringComparison.Ordinal))
        {
            foreach (Match from in HandedBackBy().Matches(text))
            {
                open.Remove(from.Groups[1].Value);
            }
        }

        if (!Status().Matches(text).Any(status => !StillGoing(status.Groups[1].Value)))
        {
            return;
        }

        foreach (Match id in TaskId().Matches(text))
        {
            open.Remove(id.Groups[1].Value);
        }
    }

    private const string HandBack = "[Subagent hand-back]";

    private static bool StillGoing(string status) => status is "running" or "pending";

    private static string Flatten(JsonElement content) =>
        content.ValueKind == JsonValueKind.String
            ? content.GetString() ?? string.Empty
            : string.Concat(Parts(content).Select(part => Text(part, "text")));

    private static IEnumerable<JsonElement> Parts(JsonElement content) =>
        content.ValueKind == JsonValueKind.Array
            ? content.EnumerateArray().Where(part => part.ValueKind == JsonValueKind.Object)
            : [];

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [GeneratedRegex("<status>([a-z_]+)</status>")]
    private static partial Regex Status();

    // A notification says task-id; what TaskOutput reads says task_id.
    [GeneratedRegex("<task[-_]id>([A-Za-z0-9_-]{1,64})</task[-_]id>")]
    private static partial Regex TaskId();

    [GeneratedRegex("<agent-message from=\"([A-Za-z0-9_-]{1,64})\"")]
    private static partial Regex HandedBackBy();
}
