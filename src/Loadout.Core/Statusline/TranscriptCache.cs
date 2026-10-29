using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Loadout.Core.Diagnostics;

namespace Loadout.Core.Statusline;

/// <summary>
/// Keeps the last reading of each transcript, so the status line reads an
/// unchanged transcript once rather than on every redraw.
/// </summary>
/// <remarks>
/// <para>
/// A reading is reused only while the transcript's length and last-write time
/// are both what they were when it was read. Either alone can miss a change:
/// a rewrite can keep the length, and a file system with coarse times can
/// leave the time where it was.
/// </para>
/// <para>
/// The cache holds times, not decisions, so what is outstanding is still
/// worked out against the moment of asking. A cache file that cannot be read
/// is a miss and never an error: the status line has nowhere to report one.
/// </para>
/// </remarks>
public static class TranscriptCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Where cached readings are kept, under the launcher's state directory.</summary>
    public static string Directory(string state) => Path.Combine(state, "statusline", "transcripts");

    /// <summary>Reads a transcript, from the cache where it has not changed since it was last read.</summary>
    /// <param name="state">The launcher's state directory.</param>
    /// <param name="path">The transcript.</param>
    /// <param name="silentAfter">As for <see cref="TranscriptTimes.Read"/>.</param>
    public static TranscriptReading? Read(string state, string? path, TimeSpan? silentAfter = null)
    {
        if (path is null)
        {
            return null;
        }

        FileInfo file;

        try
        {
            // Looked at before it is read: a transcript that grows during the
            // read is then cached under its older length and read again next time.
            file = new FileInfo(path);

            if (!file.Exists)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        var length = file.Length;
        var ticks = file.LastWriteTimeUtc.Ticks;
        var cache = Path.Combine(Directory(state), Key(file.FullName) + ".json");

        if (Load(cache) is { } kept
            && kept.Length == length
            && kept.LastWriteUtcTicks == ticks
            && kept.SilentAfterTicks == silentAfter?.Ticks)
        {
            return kept.Reading;
        }

        var reading = TranscriptTimes.Read(path, silentAfter);

        if (reading is not null)
        {
            Save(cache, new Entry(length, ticks, silentAfter?.Ticks, reading));
        }

        return reading;
    }

    private static string Key(string fullPath) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)))[..16];

    private static Entry? Load(string cache)
    {
        try
        {
            using var trace = ReadLedger.Begin(cache);
            return File.Exists(cache) ? JsonSerializer.Deserialize<Entry>(File.ReadAllText(cache), Json) is { Reading: not null } entry ? entry : null : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static void Save(string cache, Entry entry)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(cache)!);

            // Written aside and moved, so a status line drawn at the same
            // moment never reads half a file.
            var temporary = cache + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";

            File.WriteAllText(temporary, JsonSerializer.Serialize(entry, Json));
            File.Move(temporary, cache, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not kept, so read again next time.
        }
    }

    private sealed record Entry(long Length, long LastWriteUtcTicks, long? SilentAfterTicks, TranscriptReading Reading);
}
