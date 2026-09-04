using System.Diagnostics;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Common;

/// <summary>
/// Asks the operating system whether a process is still there.
/// </summary>
/// <remarks>
/// <para>
/// One implementation for every platform. .NET reports process identity the same
/// way on all three, and there is nothing here that needs a shell, a signal or a
/// platform tool — which is why this lives in Common rather than being written
/// three times.
/// </para>
/// </remarks>
public sealed class ProcessInspector : IProcessInspector
{
    /// <summary>
    /// How far apart two readings of the same start time may be and still be the
    /// same process.
    /// </summary>
    /// <remarks>
    /// The recorded moment makes a round trip through a file, and the clock the
    /// operating system reports a start time from is not guaranteed to give the
    /// same tick twice. Comparing exactly would report every process as a
    /// different one, which fails in the safe direction but reports nothing
    /// useful ever. A second is far shorter than the gap between two processes
    /// that could plausibly share an identifier.
    /// </remarks>
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public int CurrentProcessId => Environment.ProcessId;

    /// <inheritdoc />
    public DateTimeOffset CurrentProcessStartedAt
    {
        get
        {
            using var current = Process.GetCurrentProcess();

            return current.StartTime.ToUniversalTime();
        }
    }

    /// <inheritdoc />
    public bool IsRunning(int processId, DateTimeOffset startedAt)
    {
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);

            if (process.HasExited)
            {
                return false;
            }

            var actual = process.StartTime.ToUniversalTime();

            // The identifier is live. Whether it is live as the process that was
            // recorded is what the start time settles.
            return (actual - startedAt.UtcDateTime).Duration() <= Tolerance;
        }
        catch (ArgumentException)
        {
            // No process bears that identifier, which is the ordinary answer for
            // a session that has finished.
            return false;
        }
        catch (InvalidOperationException)
        {
            // It exited between being found and being asked about.
            return false;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // The process exists and will not say when it started. Reporting it
            // as gone would drop a session that is very likely running; the
            // start time is a guard against reuse, not the evidence of life.
            return true;
        }
    }
}
