namespace Loadout.Core.Teams.Daemon;

/// <summary>
/// Something read on a clock, read once however many event streams ask for it.
/// </summary>
/// <remarks>
/// Every open Studio tab holds its own event stream, and each asks for the
/// task lists' stamps twice a second. Without this, two tabs stat every list
/// twice as often as one does; with it, the cost stays where one tab puts it,
/// and an answer is at most <c>fresh</c> old.
/// </remarks>
/// <typeparam name="T">What is read.</typeparam>
internal sealed class SharedPoll<T>
{
    private readonly Func<CancellationToken, Task<T>> _read;
    private readonly TimeSpan _fresh;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (T Value, DateTimeOffset At)? _last;

    public SharedPoll(Func<CancellationToken, Task<T>> read, TimeSpan fresh, TimeProvider? time = null)
    {
        _read = read;
        _fresh = fresh;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>How many times it has actually been read, for the test that says it is shared.</summary>
    public int Reads { get; private set; }

    public async Task<T> GetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (_last is { } last && _time.GetUtcNow() - last.At < _fresh)
            {
                return last.Value;
            }

            var value = await _read(ct).ConfigureAwait(false);

            Reads++;
            _last = (value, _time.GetUtcNow());

            return value;
        }
        finally
        {
            _gate.Release();
        }
    }
}
