namespace Loadout.Core.Sessions.Hosting;

/// <summary>The last stretch of a session's output, kept for a window that attaches later.</summary>
/// <remarks>
/// <para>
/// A ring rather than a list of chunks, because a session printing a large
/// build log would otherwise move the whole buffer on every chunk once it was
/// full.
/// </para>
/// <para>
/// Once anything has been dropped from the front, what is handed out starts
/// after the first line feed. The oldest bytes left could otherwise begin
/// halfway through an escape sequence, and a terminal fed half a sequence
/// prints the rest of it as text. A line feed never falls inside the control
/// sequences that colour and move the cursor, so starting after one is safe.
/// </para>
/// </remarks>
internal sealed class Scrollback
{
    private const byte LineFeed = 10;

    private readonly byte[] _ring;
    private int _start;
    private int _count;
    private bool _trimmed;

    internal Scrollback(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _ring = new byte[capacity];
    }

    /// <summary>Whether anything has been dropped from the front.</summary>
    internal bool Trimmed => _trimmed;

    internal void Append(ReadOnlySpan<byte> data)
    {
        if (data.Length >= _ring.Length)
        {
            data[^_ring.Length..].CopyTo(_ring);
            _start = 0;
            _count = _ring.Length;
            _trimmed = true;

            return;
        }

        var end = (_start + _count) % _ring.Length;
        var first = Math.Min(data.Length, _ring.Length - end);

        data[..first].CopyTo(_ring.AsSpan(end));
        data[first..].CopyTo(_ring);

        var overflow = _count + data.Length - _ring.Length;

        if (overflow > 0)
        {
            _start = (_start + overflow) % _ring.Length;
            _count = _ring.Length;
            _trimmed = true;
        }
        else
        {
            _count += data.Length;
        }
    }

    internal byte[] Snapshot()
    {
        var all = new byte[_count];
        var first = Math.Min(_count, _ring.Length - _start);

        _ring.AsSpan(_start, first).CopyTo(all);
        _ring.AsSpan(0, _count - first).CopyTo(all.AsSpan(first));

        if (!_trimmed)
        {
            return all;
        }

        var feed = Array.IndexOf(all, LineFeed);

        // No line feed anywhere in the kept stretch: one enormous line. Better
        // to show it than nothing, and a half sequence at its start is the
        // smaller loss.
        return feed < 0 ? all : all[(feed + 1)..];
    }
}
