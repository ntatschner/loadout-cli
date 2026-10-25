using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Models.Results;

namespace Loadout.Tests.Fakes;

/// <summary>
/// Run owners the test writes, one reading per run directory.
/// </summary>
/// <remarks>
/// The real one locks files, and a test of a runner or a command wants to say
/// "this run's driver has died" without killing a process to arrange it. A
/// take makes the directory read as held until it is let go, and as free
/// with this process as its last owner afterwards, which is what the real
/// one would say.
/// </remarks>
public sealed class FakeRunOwnership : IRunOwnership
{
    private readonly Dictionary<string, OwnerReading> _readings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The owner a take records, and a release leaves behind.</summary>
    public RunOwner Owner { get; set; } =
        new(4242, new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero), "TEST", new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero));

    /// <summary>Every directory taken, in order.</summary>
    public List<string> Taken { get; } = [];

    /// <summary>Every directory read, in order.</summary>
    public List<string> Asked { get; } = [];

    /// <summary>How many times a hold was touched.</summary>
    public int Touches { get; private set; }

    /// <summary>Directories whose take is refused, as if another process owned them.</summary>
    public HashSet<string> Refused { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Directories whose take fails for a reason of its own, such as a lock
    /// file that could not be opened, with the error and exit code it gives.
    /// </summary>
    public Dictionary<string, (string Error, ExitCode Code)> Failing { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Says what reading a run directory finds, until something takes it.</summary>
    public FakeRunOwnership Script(string runDirectory, OwnerReading reading)
    {
        _readings[runDirectory] = reading;

        return this;
    }

    /// <inheritdoc />
    public OperationResult<IDisposable> Take(string runDirectory)
    {
        Taken.Add(runDirectory);

        if (Failing.TryGetValue(runDirectory, out var failing))
        {
            return OperationResult<IDisposable>.Fail(failing.Error, failing.Code);
        }

        if (Refused.Contains(runDirectory)
            || (_readings.TryGetValue(runDirectory, out var now) && now is OwnerReading.Held))
        {
            return OperationResult<IDisposable>.Fail(
                $"Another process is driving the run at {runDirectory}, so this one will not.",
                ExitCode.PolicyViolation);
        }

        _readings[runDirectory] = new OwnerReading.Held();

        return OperationResult<IDisposable>.Ok(new Hold(this, runDirectory));
    }

    /// <inheritdoc />
    public OwnerReading Read(string runDirectory)
    {
        Asked.Add(runDirectory);

        return _readings.TryGetValue(runDirectory, out var reading) ? reading : new OwnerReading.NoRecord();
    }

    /// <inheritdoc />
    public void Touch(IDisposable held)
    {
        if (held is not Hold)
        {
            throw new ArgumentException("Only a hold this returned can be touched.", nameof(held));
        }

        Touches++;
    }

    private sealed class Hold(FakeRunOwnership owner, string runDirectory) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done)
            {
                return;
            }

            _done = true;
            owner._readings[runDirectory] = new OwnerReading.Free(owner.Owner);
        }
    }
}
