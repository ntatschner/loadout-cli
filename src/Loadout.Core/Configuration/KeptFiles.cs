using System.Collections.Concurrent;
using System.Globalization;
using Loadout.Models.Results;

namespace Loadout.Core.Configuration;

/// <summary>A file's modification time and length, and whether it changed long enough ago to go by them.</summary>
/// <param name="Text">The time and length together, or empty when the file does not exist.</param>
/// <param name="Settled">Whether it last changed long enough ago for the time and length alone to be trusted.</param>
public readonly record struct FileStamp(string Text, bool Settled)
{
    /// <summary>How recently a file must have changed for its stamp not to be trusted alone.</summary>
    public static readonly TimeSpan Settling = TimeSpan.FromSeconds(2);

    public bool Exists => Text.Length > 0;

    public static FileStamp Of(string path)
    {
        var file = new FileInfo(path);

        if (!file.Exists)
        {
            return new FileStamp(string.Empty, false);
        }

        var written = file.LastWriteTimeUtc;

        return new FileStamp(
            written.Ticks.ToString(CultureInfo.InvariantCulture) + ":" + file.Length.ToString(CultureInfo.InvariantCulture),
            DateTime.UtcNow - written > Settling);
    }
}

/// <summary>
/// YAML files kept parsed between reads, read again whenever the file says it
/// has changed.
/// </summary>
/// <remarks>
/// <para>
/// Checked against the file's modification time and length on every read, so
/// a file changed by another process, by hand or by a pull is read again the
/// next time it is asked for whether or not anything noticed it change. A
/// file changed in the last couple of seconds is always read again: a clock
/// that ticks coarser than two writes would otherwise give both the same
/// stamp, which is the rule Git keeps its index by.
/// </para>
/// <para>
/// What is handed out is a copy, made by the function given, so a caller
/// changing what it was given changes nothing kept.
/// </para>
/// </remarks>
/// <typeparam name="T">What each file holds.</typeparam>
public sealed class KeptFiles<T>
    where T : class
{
    private readonly YamlStore _yaml;
    private readonly Func<T> _empty;
    private readonly Func<T, T> _copy;
    private readonly ConcurrentDictionary<string, (string Stamp, string Revision, T Value)> _kept = new(StringComparer.Ordinal);

    public KeptFiles(YamlStore yaml, Func<T> empty, Func<T, T> copy)
    {
        _yaml = yaml;
        _empty = empty;
        _copy = copy;
    }

    /// <summary>A file as it stands, from what was kept when it has not changed since.</summary>
    public async Task<OperationResult<YamlRead<T>>> ReadAsync(string path, CancellationToken ct)
    {
        var stamp = FileStamp.Of(path);

        if (stamp.Settled && _kept.TryGetValue(path, out var kept) && kept.Stamp == stamp.Text)
        {
            return OperationResult<YamlRead<T>>.Ok(new YamlRead<T>(_copy(kept.Value), kept.Revision));
        }

        var read = await _yaml.ReadAsync(path, _empty, ct).ConfigureAwait(false);

        // Kept only when the file is as it was before it was read, so a write
        // landing in between is never remembered under the older stamp.
        if (read.Succeeded && stamp.Exists && FileStamp.Of(path).Text == stamp.Text)
        {
            _kept[path] = (stamp.Text, read.Value!.Revision, _copy(read.Value.Value));
        }

        return read;
    }

    /// <summary>Keeps what was just written, so the next read need not parse it.</summary>
    public void Remember(string path, T value, string revision)
    {
        var stamp = FileStamp.Of(path);

        if (stamp.Exists)
        {
            _kept[path] = (stamp.Text, revision, _copy(value));
        }
        else
        {
            _kept.TryRemove(path, out _);
        }
    }
}
