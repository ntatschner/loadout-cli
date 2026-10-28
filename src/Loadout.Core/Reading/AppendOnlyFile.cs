using System.Runtime.CompilerServices;
using System.Text;
using Loadout.Core.Diagnostics;

namespace Loadout.Core.Reading;

/// <summary>Whether shared readers are in use in this process.</summary>
/// <remarks>
/// On unless <see cref="Variable"/> is "0". Off, every reader goes to the file
/// on every call exactly as it did before shared readers existed, which is the
/// way to tell whether a wrong answer comes from the sharing or from the source.
/// </remarks>
public static class SharedReaders
{
    /// <summary>The variable that turns shared readers off.</summary>
    public const string Variable = "LOADOUT_SHARED_READERS";

    /// <summary>Whether this process may keep readings between calls.</summary>
    public static bool Enabled => Environment.GetEnvironmentVariable(Variable) != "0";
}

/// <summary>
/// An append-only file of lines, read once and then only from where the last
/// reading stopped.
/// </summary>
/// <remarks>
/// <para>
/// Every call looks at the file's length and last-write time first, so a
/// caller sees a change no later than it would by reading the whole file
/// itself: nothing here waits to be told. An unchanged file is not opened. A
/// longer one is read from the end of its last complete line, after checking
/// that the bytes before that point are the ones read last time, so a file
/// rewritten to something longer is read whole rather than taken for an
/// append. A shorter one, or one the same length with a new time, was not
/// appended to and is read whole.
/// </para>
/// <para>
/// A last line with no newline yet is parsed on every call that finds it and
/// never kept, so a line half-written when it was read is read again once
/// finished, as the whole-file reader always did.
/// </para>
/// <para>
/// The check is the 1 KB before the old end, not the whole prefix: a rewrite
/// that grows the file and leaves those bytes as they were looks like an
/// append. Nothing that writes these files rewrites them, so that is a limit
/// stated rather than one met.
/// </para>
/// </remarks>
/// <typeparam name="T">What each line becomes.</typeparam>
internal sealed class AppendOnlyFile<T>
    where T : class
{
    private const int GuardLength = 1024;

    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private readonly string _path;
    private readonly Func<string, T?> _parse;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Kept? _kept;

    /// <param name="path">The file.</param>
    /// <param name="parse">Turns one line into an item, or null to skip it.</param>
    public AppendOnlyFile(string path, Func<string, T?> parse)
    {
        _path = path;
        _parse = parse;
    }

    /// <summary>
    /// Every item in the file, in the order written, or null when there is no
    /// file. Throws what reading the file throws.
    /// </summary>
    public async Task<IReadOnlyList<T>?> ReadAsync(
        CancellationToken ct = default,
        [CallerFilePath] string site = "",
        [CallerMemberName] string member = "")
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            // Looked at before it is read, as TranscriptCache does: a file that
            // grows during the read is kept under its older length and read
            // from where this read stopped next time.
            var file = new FileInfo(_path);

            if (!file.Exists)
            {
                _kept = null;
                return null;
            }

            var length = file.Length;
            var ticks = file.LastWriteTimeUtc.Ticks;
            var kept = _kept;

            if (kept is not null && kept.Length == length && kept.Ticks == ticks)
            {
                return kept.Result;
            }

            _kept = null;

            using var trace = ReadLedger.Begin(_path, site, member);
            await using var stream = File.OpenRead(_path);

            var appended = kept is not null && length > kept.Length && await ContinuesAsync(stream, kept, ct).ConfigureAwait(false);
            var from = appended ? kept!.Offset : 0;

            stream.Position = from;

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);

            var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
            var complete = bytes.LastIndexOf((byte)'\n') + 1;

            var items = appended ? new List<T>(kept!.Items) : [];
            Parse(bytes[..complete], from == 0, items);

            var tail = new List<T>(1);
            Parse(bytes[complete..], from == 0 && complete == 0, tail);

            trace?.Read(bytes.Length + (appended ? kept!.Guard.Length : 0));

            var guard = Guard(appended ? kept!.Guard : [], bytes[..complete]);
            var result = tail.Count == 0 ? items.ToArray() : [.. items, .. tail];

            _kept = new Kept(length, ticks, from + complete, guard, items.ToArray(), result);

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<bool> ContinuesAsync(FileStream stream, Kept kept, CancellationToken ct)
    {
        if (kept.Guard.Length == 0)
        {
            return true;
        }

        var seen = new byte[kept.Guard.Length];

        stream.Position = kept.Offset - seen.Length;
        await stream.ReadExactlyAsync(seen, ct).ConfigureAwait(false);

        return seen.AsSpan().SequenceEqual(kept.Guard);
    }

    private static byte[] Guard(byte[] before, ReadOnlySpan<byte> read)
    {
        if (read.Length >= GuardLength)
        {
            return read[^GuardLength..].ToArray();
        }

        var joined = new byte[before.Length + read.Length];
        before.CopyTo(joined, 0);
        read.CopyTo(joined.AsSpan(before.Length));

        return joined.Length > GuardLength ? joined[^GuardLength..] : joined;
    }

    private void Parse(ReadOnlySpan<byte> bytes, bool atStart, List<T> into)
    {
        if (atStart && bytes.StartsWith(Bom))
        {
            bytes = bytes[Bom.Length..];
        }

        while (bytes.Length > 0)
        {
            var end = bytes.IndexOf((byte)'\n');
            var line = end < 0 ? bytes : bytes[..end];

            bytes = end < 0 ? [] : bytes[(end + 1)..];

            var text = Encoding.UTF8.GetString(line).TrimEnd('\r');

            if (!string.IsNullOrWhiteSpace(text) && _parse(text) is { } item)
            {
                into.Add(item);
            }
        }
    }

    /// <summary>The last reading.</summary>
    /// <param name="Length">The file's length when it was looked at.</param>
    /// <param name="Ticks">Its last-write time then.</param>
    /// <param name="Offset">Where the last complete line ended.</param>
    /// <param name="Guard">The bytes just before <paramref name="Offset"/>.</param>
    /// <param name="Items">What the complete lines held.</param>
    /// <param name="Result">Those and an unfinished last line, as returned.</param>
    private sealed record Kept(long Length, long Ticks, long Offset, byte[] Guard, T[] Items, T[] Result);
}
