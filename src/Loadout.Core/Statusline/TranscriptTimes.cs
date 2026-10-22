using System.Globalization;
using System.Text.Json;
using Loadout.Core.Sessions.Hosting;

namespace Loadout.Core.Statusline;

/// <summary>When things happened at the end of a Claude transcript, for the time segments of the status line.</summary>
/// <param name="TurnStartedAt">When the current or last turn was asked for, where the tail holds it.</param>
/// <param name="TurnEndedAt">When the last turn finished, or null while one is still going.</param>
/// <param name="LastEntryAt">When the last message was written.</param>
/// <param name="PendingToolSince">When the earliest tool call still waiting on its result was made.</param>
/// <param name="LastWriteAt">When the last file edit that succeeded was made.</param>
/// <param name="OpenUntil">When each piece of background work still out stops counting, or null for one that never does.</param>
/// <param name="WakeupDue">When a wakeup still to come is due.</param>
/// <remarks>
/// Times only, kept rather than a yes or no, because whether background work
/// is still out depends on when you ask: <see cref="Outstanding"/> is worked
/// out at the moment of asking, so a cached reading does not go stale.
/// </remarks>
public sealed record TranscriptReading(
    DateTimeOffset? TurnStartedAt,
    DateTimeOffset? TurnEndedAt,
    DateTimeOffset? LastEntryAt,
    DateTimeOffset? PendingToolSince,
    DateTimeOffset? LastWriteAt,
    IReadOnlyList<DateTimeOffset?> OpenUntil,
    DateTimeOffset? WakeupDue)
{
    /// <summary>Whether anything started in the background has not reported back, as <see cref="BackgroundWork.Scan"/> decides it.</summary>
    public bool Outstanding(DateTimeOffset now) => OpenUntil.Any(until => until is null || until > now) || WakeupDue is not null;

    /// <summary>
    /// Whether a hosted session's signal speaks for the transcript's present:
    /// one written before the last entry is about a moment that has passed.
    /// </summary>
    /// <remarks>
    /// A signal written at the same instant as the last entry counts, since
    /// the hook that writes an approval fires as the tool call is made.
    /// </remarks>
    public static bool SignalCounts(SessionSignal? signal, DateTimeOffset? lastEntry) =>
        signal is not null && (lastEntry is null || signal.At >= lastEntry);
}

/// <summary>Reads the times the status line shows from the tail of a Claude transcript.</summary>
/// <remarks>
/// <para>
/// Only ids, times and the shape of entries are read, as in
/// <see cref="BackgroundWork"/>; nothing from the text of the conversation.
/// </para>
/// <para>
/// A finished turn is an assistant entry whose <c>message.stop_reason</c> is
/// set and is not <c>tool_use</c>. Checked against real transcripts written by
/// Claude Code 2.1: an assistant message is written as one entry per content
/// block, and every one of them carries the message's stop reason, so a
/// thinking block that ends a turn already says <c>end_turn</c> and one that
/// precedes a tool call says <c>tool_use</c>. Interactive sessions end on
/// <c>end_turn</c> and then write a <c>system</c> entry of subtype
/// <c>stop_hook_summary</c> and one of <c>turn_duration</c> about a second
/// later; a headless session ended on <c>stop_sequence</c> and wrote neither,
/// which is why the stop reason is the marker and the system entries are not.
/// </para>
/// </remarks>
public static class TranscriptTimes
{
    private static readonly HashSet<string> Writes = new(StringComparer.Ordinal) { "Write", "Edit", "MultiEdit", "NotebookEdit" };

    /// <summary>Reads a transcript, or null where it cannot be read or holds no entry.</summary>
    /// <param name="path">The transcript.</param>
    /// <param name="silentAfter">As for <see cref="BackgroundWork.Read"/>.</param>
    public static TranscriptReading? Read(string? path, TimeSpan? silentAfter = null) =>
        BackgroundWork.ReadTail(path) is { } lines ? Scan(lines, silentAfter) : null;

    /// <summary>Reads transcript lines, oldest first, or null where none of them is an entry.</summary>
    internal static TranscriptReading? Scan(IReadOnlyList<string> lines, TimeSpan? silentAfter)
    {
        // One read from disk feeds both passes, so the background reading
        // cannot disagree with the one the dashboard makes.
        if (BackgroundWork.Tally(lines, silentAfter) is not { } tally)
        {
            return null;
        }

        DateTimeOffset? started = null;
        DateTimeOffset? ended = null;
        DateTimeOffset? written = null;
        var pending = new Dictionary<string, (string Name, DateTimeOffset? At)>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
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
                var entry = document.RootElement;

                if (entry.ValueKind != JsonValueKind.Object
                    || entry.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                var type = Text(entry, "type");

                if (type is not ("user" or "assistant"))
                {
                    continue;
                }

                DateTimeOffset? at = Text(entry, "timestamp") is { } stamp
                    && DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                        ? parsed
                        : null;

                var message = entry.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object ? m : default;
                var content = message.ValueKind == JsonValueKind.Object && message.TryGetProperty("content", out var c) ? c : default;

                if (type == "assistant")
                {
                    foreach (var part in Parts(content).Where(part => Text(part, "type") == "tool_use"))
                    {
                        if (Text(part, "id") is { } id)
                        {
                            pending[id] = (Text(part, "name") ?? string.Empty, at);
                        }
                    }

                    ended = Text(message, "stop_reason") is { } stop && stop != "tool_use" ? at : null;

                    continue;
                }

                var results = Parts(content).Where(part => Text(part, "type") == "tool_result").ToList();

                if (results.Count == 0)
                {
                    // A prompt, or a task notification, which wakes Claude
                    // just the same; a meta entry is said on the person's
                    // behalf and starts nothing.
                    if (entry.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True)
                    {
                        continue;
                    }

                    started = at;
                    ended = null;
                    pending.Clear();

                    continue;
                }

                foreach (var result in results)
                {
                    if (Text(result, "tool_use_id") is not { } id || !pending.Remove(id, out var call))
                    {
                        continue;
                    }

                    var failed = result.TryGetProperty("is_error", out var error) && error.ValueKind == JsonValueKind.True;

                    if (!failed && Writes.Contains(call.Name) && call.At is { } made && (written is null || made > written))
                    {
                        written = made;
                    }
                }
            }
        }

        var since = pending.Values.Select(call => call.At).Where(made => made is not null).Min();

        return new TranscriptReading(started, ended, tally.LastEntry, ended is null ? since : null, written, tally.OpenUntil, tally.Due);
    }

    private static IEnumerable<JsonElement> Parts(JsonElement content) =>
        content.ValueKind == JsonValueKind.Array
            ? content.EnumerateArray().Where(part => part.ValueKind == JsonValueKind.Object)
            : [];

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
