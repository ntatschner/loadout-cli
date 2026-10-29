using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Loadout.Core.Transcripts;

/// <summary>
/// What was read from each transcript, kept while the file is the same length
/// and was last written at the same moment.
/// </summary>
/// <typeparam name="T">One transcript's reading.</typeparam>
/// <remarks>
/// <para>
/// 'usage' read every transcript in the period on every call: about twenty
/// seconds for thirty days on a machine with months of them, which no page can
/// wait for. Almost none of those files had changed since the last read. So a
/// reading is kept per file and reused while both its length and its last
/// write time are unchanged, the rule the status line's transcript cache
/// already relies on; a file that changed in any way is read again in full.
/// </para>
/// <para>
/// A reading is what one file says on its own and nothing else: which records
/// it holds, in order. Everything that depends on more than one file - a
/// message counted once across every transcript, the window asked about, a
/// day taken from when the file was last written - is still worked out from
/// the readings on every scan, exactly as before. That is what makes the
/// totals the same as a full scan's by construction rather than by care.
/// </para>
/// <para>
/// Kept in memory for as long as the process runs, which in the daemon is
/// what makes a page fast, and on disk, one small file per transcript, so the
/// first read after a restart or from the command line is fast too, and a
/// read writes only what changed. A file on disk is only ever a cache: it is
/// used only when the transcript still matches it, and one that cannot be
/// read is read again from the transcript.
/// </para>
/// </remarks>
internal sealed class TranscriptReadings<T>
    where T : class
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _name;
    private readonly int _version;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string Path, long Length, long Written), Lazy<Task<T?>>> _pending = new();

    /// <param name="name">The directory its files are kept in, under the readings directory.</param>
    /// <param name="version">
    /// Raised whenever what a reading holds or how a file is read changes, so
    /// readings made by an older build are never trusted.
    /// </param>
    internal TranscriptReadings(string name, int version)
    {
        _name = name;
        _version = version;
    }

    /// <summary>
    /// The reading of one file, from memory or disk when the file has not
    /// changed, otherwise from reading it.
    /// </summary>
    /// <param name="file">The transcript, as it was found when the scan listed it.</param>
    /// <param name="kept">The readings directory, or null to keep nothing on disk.</param>
    /// <param name="read">Reads it, or returns null when it could not be read.</param>
    /// <param name="ct">Stops the read.</param>
    internal Task<T?> GetAsync(
        FileInfo file, string? kept, Func<FileInfo, CancellationToken, Task<T?>> read, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(read);

        return GetAsync(file, kept, (one, _, token) => read(one, token), ct);
    }

    /// <summary>
    /// The reading of one file, as above, handing the read what was last read
    /// from the file whatever its length then, so a file that has only grown
    /// can be read from what it already said.
    /// </summary>
    /// <param name="file">The transcript, as it was found when the scan listed it.</param>
    /// <param name="kept">The readings directory, or null to keep nothing on disk.</param>
    /// <param name="read">
    /// Reads it, given the last reading of the file or null when there is none,
    /// or returns null when it could not be read. The last reading is of an
    /// older version of the file, so the read has to check it still applies.
    /// </param>
    /// <param name="ct">Stops the read.</param>
    internal async Task<T?> GetAsync(
        FileInfo file, string? kept, Func<FileInfo, T?, CancellationToken, Task<T?>> read, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(read);

        // As listed, not as it is now: a transcript that grows during the read
        // is kept under its older length and read again next time.
        var length = file.Length;
        var written = file.LastWriteTimeUtc.Ticks;

        if (_entries.TryGetValue(file.FullName, out var held) && held.Length == length && held.Written == written)
        {
            return held.Reading;
        }

        // Studio asks for the history and the lost sessions together, and
        // both scans used to read every changed transcript, so each was read
        // twice. A scan arriving while the same file at the same length is
        // being read waits for that read instead. The read is not stopped by
        // the scan that started it giving up, since another may be waiting.
        var key = (file.FullName, length, written);
        var pending = _pending.GetOrAdd(
            key, _ => new Lazy<Task<T?>>(() => ReadOnceAsync(key, file, kept, read)));

        return await pending.Value.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task<T?> ReadOnceAsync(
        (string Path, long Length, long Written) key, FileInfo file, string? kept, Func<FileInfo, T?, CancellationToken, Task<T?>> read)
    {
        try
        {
            return await LoadOrReadAsync(file, key.Length, key.Written, kept, read).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(key, out _);
        }
    }

    private async Task<T?> LoadOrReadAsync(
        FileInfo file, long length, long written, string? kept, Func<FileInfo, T?, CancellationToken, Task<T?>> read)
    {
        var ct = CancellationToken.None;
        var stored = kept is null ? null : PathFor(kept, file.FullName);
        var previous = _entries.TryGetValue(file.FullName, out var held) ? held.Reading : null;

        if (stored is not null && await LoadAsync(stored, ct).ConfigureAwait(false) is { } loaded
            && loaded.Version == _version
            && string.Equals(loaded.Path, file.FullName, StringComparison.Ordinal))
        {
            if (loaded.Length == length && loaded.Written == written)
            {
                _entries[file.FullName] = new Entry(length, written, loaded.Reading);

                return loaded.Reading;
            }

            previous ??= loaded.Reading;
        }

        var reading = await read(file, previous, ct).ConfigureAwait(false);

        // A file that could not be read is not remembered, so it is tried again.
        if (reading is not null)
        {
            _entries[file.FullName] = new Entry(length, written, reading);

            if (stored is not null)
            {
                await SaveAsync(stored, new Stored(_version, file.FullName, length, written, reading), ct).ConfigureAwait(false);
            }
        }

        return reading;
    }

    /// <summary>
    /// Forgets readings of files no longer there, so neither memory nor the
    /// disk grows with every transcript ever written.
    /// </summary>
    /// <param name="listed">Every file the scan found, whatever its age.</param>
    /// <param name="kept">The readings directory, or null when nothing is kept on disk.</param>
    internal void Keep(IReadOnlyCollection<string> listed, string? kept)
    {
        ArgumentNullException.ThrowIfNull(listed);

        var keep = new HashSet<string>(listed, StringComparer.Ordinal);

        foreach (var path in _entries.Keys)
        {
            if (!keep.Contains(path))
            {
                _entries.TryRemove(path, out _);
            }
        }

        if (kept is null || !Directory.Exists(Path.Combine(kept, _name)))
        {
            return;
        }

        var named = new HashSet<string>(keep.Select(path => Path.GetFileName(PathFor(kept, path))), StringComparer.Ordinal);

        try
        {
            foreach (var stored in Directory.EnumerateFiles(Path.Combine(kept, _name), "*.json"))
            {
                if (!named.Contains(Path.GetFileName(stored)))
                {
                    File.Delete(stored);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next scan; a stale reading is never used anyway.
        }
    }

    /// <summary>
    /// Drops everything held in memory, as a new process starts with nothing,
    /// so the next scan reads from disk or from the transcripts.
    /// </summary>
    internal void Forget() => _entries.Clear();

    /// <summary>Where one transcript's reading is kept: named for its path, which a file name cannot hold.</summary>
    private string PathFor(string kept, string transcript) =>
        Path.Combine(
            kept,
            _name,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(transcript)))[..32].ToLowerInvariant() + ".json");

    private static async Task<Stored?> LoadAsync(string path, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            await using var stream = File.OpenRead(path);

            return await JsonSerializer.DeserializeAsync<Stored>(stream, Json, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static async Task SaveAsync(string path, Stored stored, CancellationToken ct)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, stored, Json, ct).ConfigureAwait(false);
            }

            // Replaced whole, so another process reading it never sees half.
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Only a cache: the transcript is read again next time.
            try
            {
                File.Delete(temporary);
            }
            catch (Exception left) when (left is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed record Entry(long Length, long Written, T Reading);

    private sealed record Stored(int Version, string Path, long Length, long Written, T Reading);
}

/// <summary>Where transcript readings are kept between runs.</summary>
internal static class TranscriptReadingsDirectory
{
    /// <summary>The directory under the state directory.</summary>
    internal static string Under(string state) => Path.Combine(state, "usage", "readings");
}
