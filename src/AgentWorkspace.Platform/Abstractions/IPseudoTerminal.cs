using AgentWorkspace.Models.Results;

namespace AgentWorkspace.Platform.Abstractions;

/// <summary>
/// A pseudo-terminal the launcher owns and proxies (spec section 43).
/// <para>
/// Distinct from IProcessLauncher.RunInteractiveAsync, which hands the child
/// the launcher's own terminal. An owned PTY is needed only where there is no
/// terminal to inherit: a desktop launch that must host the session itself, or
/// a future remote mode.
/// </para>
/// <para>
/// Implementations are ConPTY on Windows and forkpty on Linux and macOS.
/// Availability is reported through PlatformCapability.PseudoTerminal so a
/// platform lacking it degrades visibly rather than silently.
/// </para>
/// </summary>
public interface IPseudoTerminal : IDisposable
{
    /// <summary>Allocates the pseudo-terminal and starts the child process in it.</summary>
    Task<OperationResult> StartAsync(ProcessRequest request, int columns, int rows, CancellationToken ct = default);

    /// <summary>Writes to the child's input.</summary>
    Task<OperationResult> WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);

    /// <summary>Reads from the child's output. Returns 0 once the child has exited.</summary>
    Task<OperationResult<int>> ReadAsync(Memory<byte> buffer, CancellationToken ct = default);

    /// <summary>Tells the child the terminal was resized.</summary>
    OperationResult Resize(int columns, int rows);

    /// <summary>Waits for the child and returns its exit status.</summary>
    Task<OperationResult<int>> WaitForExitAsync(CancellationToken ct = default);
}
