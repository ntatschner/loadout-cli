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
    /// <para>
    /// Half the processors, and never below two. Scaled to the machine because
    /// the limit being hit is the desktop heap, which is a property of the host
    /// rather than of the suite: a four-core runner has to be held to two where
    /// a workstation is comfortable at eight.
    /// </para>
    /// <para>
    /// It was <c>Math.Max(4, ProcessorCount)</c>, which on a four-core Windows
    /// arm64 runner permitted four here and two more through the contract
    /// harness's own semaphore, and that run failed forty-three starts. The
    /// number was chosen then to avoid a slowdown that turned out to be
    /// seventeen hundred leaked temp directories rather than the ceiling, so it
    /// was set generously against a cost that was never real.
    /// </para>
    /// </remarks>
    internal static readonly int Ceiling = Math.Max(2, Environment.ProcessorCount / 2);

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
        for (var attempt = 0; ; attempt++)
        {
            await EnterGateAsync(ct).ConfigureAwait(false);

            OperationResult<ProcessOutcome> result;

            try
            {
                result = await _inner.RunAsync(request, timeout, ct).ConfigureAwait(false);
            }
            finally
            {
                Gate.Release();
            }

            if (attempt >= Attempts
                || result.Value is not { } outcome
                || !RefusedToStart(outcome.ExitCode, outcome.StandardOutput, outcome.StandardError))
            {
                return result;
            }

            await BackOffAsync(attempt, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<int>> RunInteractiveAsync(
        ProcessRequest request,
        CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            await EnterGateAsync(ct).ConfigureAwait(false);

            OperationResult<int> result;

            try
            {
                result = await _inner.RunInteractiveAsync(request, ct).ConfigureAwait(false);
            }
            finally
            {
                Gate.Release();
            }

            // No streams to consult on this path, so the exit code is the whole
            // of the evidence. That is weaker, and acceptable: a command under
            // test choosing to return this exact value is not a thing that
            // happens, and the alternative is leaving the launch tests with no
            // defence at all.
            if (attempt >= Attempts
                || result.Failed
                || result.Value != ProcessInitialisationFailed)
            {
                return result;
            }

            await BackOffAsync(attempt, ct).ConfigureAwait(false);
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
    /// Windows' status code for a process that could not finish initialising.
    /// </summary>
    private const int ProcessInitialisationFailed = unchecked((int)0xC0000142);

    /// <summary>How many times a start that never happened is tried again.</summary>
    private const int Attempts = 5;

    /// <summary>
    /// Whether an outcome is Windows refusing the start rather than the command
    /// running and failing.
    /// </summary>
    /// <remarks>
    /// Both halves are required. A command that ran says something, on one
    /// stream or the other; a process that died before its entry point writes
    /// nothing at all. Retrying on the exit code alone would re-run a command
    /// that genuinely returned it, and hide a real failure behind four more
    /// attempts.
    /// </remarks>
    private static bool RefusedToStart(int exitCode, string standardOutput, string standardError) =>
        exitCode == ProcessInitialisationFailed
        && standardOutput.Length == 0
        && standardError.Length == 0;

    /// <summary>
    /// Waits longer after each refusal.
    /// </summary>
    /// <remarks>
    /// The ceiling bounds what this suite contributes to the shortage and can do
    /// nothing about the rest: the desktop heap belongs to the whole window
    /// station, so a browser and thirty consoles draw on the same pool. That is
    /// not a hypothetical — the run that motivated this was on a machine with
    /// three hundred and ninety-one processes on it, and the tests that survived
    /// were exactly the ones that already retried. So a shortage caused from
    /// outside is waited out rather than prevented, because it cannot be
    /// prevented from in here.
    /// </remarks>
    private static Task BackOffAsync(int attempt, CancellationToken ct) =>
        Task.Delay(500 * (attempt + 1), ct);

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
