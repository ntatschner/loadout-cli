using Loadout.Platform.Abstractions;

namespace Loadout.Tests.Fakes;

/// <summary>
/// A process table the test writes.
/// </summary>
/// <remarks>
/// The real inspector answers about processes on the machine running the suite,
/// which makes it useless for the cases worth testing: a session whose process
/// died, and an identifier that has been handed to something else since. Neither
/// can be arranged for real without killing something, and both are exactly what
/// the registry has to get right.
/// </remarks>
public sealed class FakeProcessInspector : IProcessInspector
{
    private readonly Dictionary<int, DateTimeOffset> _live = [];

    /// <inheritdoc />
    public int CurrentProcessId { get; set; } = 4242;

    /// <inheritdoc />
    public DateTimeOffset CurrentProcessStartedAt { get; set; } =
        new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Every question this inspector was asked, in order.
    /// </summary>
    /// <remarks>
    /// Recorded because the answers alone cannot show whether the caller asked
    /// about the identity it wrote down or about some other one it had to hand.
    /// A caller that asked the wrong question would get the right answer here by
    /// luck, and the test would say nothing.
    /// </remarks>
    public List<(int ProcessId, DateTimeOffset StartedAt)> Asked { get; } = [];

    /// <summary>Says that the process this inspector reports as its own is running.</summary>
    public FakeProcessInspector MarkSelfLive()
    {
        _live[CurrentProcessId] = CurrentProcessStartedAt;

        return this;
    }

    /// <summary>Says that a process is running, whoever it belongs to.</summary>
    public FakeProcessInspector MarkLive(int processId, DateTimeOffset startedAt)
    {
        _live[processId] = startedAt;

        return this;
    }

    /// <summary>Says that nothing is running any more.</summary>
    public void KillEverything() => _live.Clear();

    /// <inheritdoc />
    public bool IsRunning(int processId, DateTimeOffset startedAt)
    {
        Asked.Add((processId, startedAt));

        return _live.TryGetValue(processId, out var actual) && actual == startedAt;
    }
}
