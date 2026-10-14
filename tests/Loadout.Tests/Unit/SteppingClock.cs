namespace Loadout.Tests.Unit;

/// <summary>
/// A clock that starts at a fixed instant and moves on one millisecond each
/// time it is read, so a run's times say what order things happened in and
/// nothing about how fast the machine was.
/// </summary>
/// <remarks>
/// Timers are the base class's, which are the system's: the run's watcher
/// still polls on real time, and only what the run writes down is stepped.
/// </remarks>
internal sealed class SteppingClock : TimeProvider
{
    /// <summary>Where every stepping clock starts.</summary>
    public static readonly DateTimeOffset Start = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>How far past <see cref="Start"/> a time can be and still be one of this clock's.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private long _steps;

    public override DateTimeOffset GetUtcNow() =>
        Start + TimeSpan.FromMilliseconds(Interlocked.Increment(ref _steps));
}
