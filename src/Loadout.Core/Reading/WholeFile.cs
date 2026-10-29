using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Loadout.Core.Configuration;
using Loadout.Core.Diagnostics;

namespace Loadout.Core.Reading;

/// <summary>
/// A file that is rewritten whole rather than added to, such as a memory
/// topic, read again only when it has changed.
/// </summary>
/// <remarks>
/// <para>
/// The text-only sibling of <see cref="KeptFiles{T}"/>, by the same rule:
/// every call looks at the file's <see cref="FileStamp"/> first, so a caller
/// sees a change no later than it would by reading the file itself, and a
/// file changed in the last couple of seconds is always read again, because a
/// rewrite to the same length can land inside one tick of the clock.
/// </para>
/// <para>
/// The text is kept, not what it parses to, so nobody can change a reading
/// another caller is holding. A file over a megabyte is read every time, so a
/// directory of large files cannot fill the process with copies. The readers
/// are kept for the process, not for a service: inside the daemon every
/// request gets services of its own, and a reading kept on one would last a
/// single call.
/// </para>
/// </remarks>
internal sealed class WholeFile
{
    private const int KeptAtMost = 1024 * 1024;

    private static readonly ConcurrentDictionary<string, WholeFile> Shared = new(StringComparer.Ordinal);

    private readonly string _path;
    private readonly object _gate = new();
    private (string Stamp, string Text)? _kept;

    /// <param name="path">The file.</param>
    public WholeFile(string path) => _path = path;

    /// <summary>The one reader of <paramref name="path"/> in this process.</summary>
    public static WholeFile For(string path) =>
        Shared.GetOrAdd(Path.GetFullPath(path), full => new WholeFile(full));

    /// <summary>
    /// The file's text. Throws what reading the file throws, a missing file
    /// included, exactly as reading it directly would.
    /// </summary>
    public async Task<string> ReadTextAsync(
        CancellationToken ct = default,
        [CallerFilePath] string site = "",
        [CallerMemberName] string member = "")
    {
        var stamp = FileStamp.Of(_path);

        lock (_gate)
        {
            if (stamp.Settled && _kept is { } kept && kept.Stamp == stamp.Text)
            {
                return kept.Text;
            }

            _kept = null;
        }

        string text;

        using (ReadLedger.Begin(_path, site, member))
        {
            text = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
        }

        // Kept only when the file is as it was before it was read, so a write
        // landing in between is never remembered under the older stamp.
        if (stamp.Exists && stamp.Settled && text.Length <= KeptAtMost && FileStamp.Of(_path).Text == stamp.Text)
        {
            lock (_gate)
            {
                _kept = (stamp.Text, text);
            }
        }

        return text;
    }
}
