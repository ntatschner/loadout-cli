using Loadout.Models.Results;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;

namespace Loadout.Tests.Fakes;

/// <summary>
/// A real process launcher with a ceiling on how many processes the suite may
/// be starting at once.
/// </summary>
/// <remarks>
/// <para>
/// Windows fails a process start with <c>0xC0000142</c>
/// (<c>STATUS_DLL_INIT_FAILED</c>, surfacing as exit code -1073741502) when too
/// many are asked for together: the desktop heap runs out, and the process
/// never reaches its entry point. Nothing about the code under test is wrong
/// when this happens, which is what makes it expensive — it produces a
/// scattering of failures in tests that pass alone and pass on the next run,
/// and it has been diagnosed as a race and as a disturbed binary before now.
/// </para>
/// <para>
/// Serialising the contract collection covered the processes that collection
/// could see, and said so in its own comment: the integration tests spawn real
/// agents and real Git alongside it and it cannot see those. Raising that to
/// "serialise everything that starts a process" would have put most of the
/// suite on one thread to fix a resource limit.
/// </para>
/// <para>
/// So the ceiling is here instead, where every start passes through one gate
/// whatever xUnit happens to be scheduling. It bounds the peak rather than the
/// ordering, which is the thing Windows actually objects to, and it leaves
/// tests that merely ask Git a question free to run beside each other.
/// </para>
/// <para>
/// Static, and deliberately: a per-instance ceiling would be no ceiling at all,
/// because each test class builds its own launcher.
/// </para>
/// </remarks>
public sealed class ThrottledProcessLauncher : IProcessLauncher
{
    /// <summary>
    /// How many processes the whole assembly may have starting at once.
    /// </summary>
    /// <remarks>
    /// Generous on purpose. The failure is a burst of simultaneous starts, and
    /// what has to be prevented is the pathological peak, not ordinary
    /// concurrency — a tight ceiling took this suite from 2m11s to 12m17s on
    /// one machine, which is a worse bargain than the flake it was bought to
    /// stop. Scaled to the machine so a small CI runner is held tighter than a
    /// workstation, which is also where the failures happen.
    /// </remarks>
    private static readonly int Ceiling = Math.Max(4, Environment.ProcessorCount);

    private static readonly SemaphoreSlim Gate = new(Ceiling, Ceiling);

    private readonly ProcessLauncher _inner = new();

    /// <summary>
    /// Holds a place while a process starts, for a caller that starts one
    /// itself rather than through this launcher.
    /// </summary>
    public static async Task<IDisposable> EnterAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);

        return new Slot();
    }

    /// <inheritdoc />
    public async Task<OperationResult<ProcessOutcome>> RunAsync(
        ProcessRequest request,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        await EnterGateAsync(ct).ConfigureAwait(false);

        try
        {
            return await _inner.RunAsync(request, timeout, ct).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<int>> RunInteractiveAsync(
        ProcessRequest request,
        CancellationToken ct = default)
    {
        await EnterGateAsync(ct).ConfigureAwait(false);

        try
        {
            return await _inner.RunInteractiveAsync(request, ct).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <inheritdoc />
    public OperationResult StartDetached(ProcessRequest request) =>
        // Ungated deliberately. A blocking wait here would hold one of the
        // runner's two threads while the holders of the gate need a thread to
        // finish, which is a deadlock rather than a slow test. Detached starts
        // are rare and are stubbed in every test that reaches one.
        _inner.StartDetached(request);

    /// <summary>
    /// Takes a place, without an await when one is free.
    /// </summary>
    /// <remarks>
    /// The synchronous attempt first because the await is not free: every
    /// process start in the suite passes through here, and a continuation
    /// apiece — queued behind a runner limited to two threads — is paid
    /// thousands of times whether or not the gate was ever contended.
    /// </remarks>
    private static async Task EnterGateAsync(CancellationToken ct)
    {
        if (Gate.Wait(0, ct))
        {
            return;
        }

        await Gate.WaitAsync(ct).ConfigureAwait(false);
    }

    private sealed class Slot : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            Gate.Release();
        }
    }
}
