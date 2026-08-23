using Loadout.Models.Results;

namespace Loadout.Platform.Abstractions;

/// <summary>What a child process produced.</summary>
/// <param name="ExitCode">The child's exit status.</param>
/// <param name="StandardOutput">Captured stdout. Empty for interactive launches.</param>
/// <param name="StandardError">Captured stderr. Empty for interactive launches.</param>
public sealed record ProcessOutcome(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>Describes a process to start.</summary>
/// <param name="Executable">Absolute path, or a name to resolve on PATH.</param>
/// <param name="Arguments">
/// Arguments as a list, never a pre-joined string. Joining is what breaks paths
/// containing spaces, which spec section 84 requires to work everywhere.
/// </param>
/// <param name="WorkingDirectory">Directory to start in, or null to inherit.</param>
/// <param name="Environment">
/// Variables added or overridden for the child only. Spec section 32 requires
/// CODEX_HOME to reach the child without altering the launcher's own environment.
/// </param>
/// <param name="StandardInput">
/// Text piped to the child's stdin, then closed. This is how secret values
/// reach a credential tool: passing one as a command-line argument would
/// expose it to any process listing, which spec section 55 forbids in spirit
/// even though it names shell history specifically. Ignored by interactive
/// launches, which give the child the real terminal instead.
/// </param>
public sealed record ProcessRequest(
    string Executable,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? StandardInput = null);

/// <summary>Starts child processes (spec section 8).</summary>
public interface IProcessLauncher
{
    /// <summary>
    /// Runs a process to completion capturing its output. Used for short
    /// non-interactive work such as a git query or a version probe.
    /// </summary>
    Task<OperationResult<ProcessOutcome>> RunAsync(
        ProcessRequest request,
        TimeSpan? timeout = null,
        CancellationToken ct = default);

    /// <summary>
    /// Runs a process attached to the launcher's own terminal and waits for it.
    /// <para>
    /// Because the child inherits the real terminal it gets correct stdin,
    /// stdout, stderr, Ctrl+C, SIGINT, SIGTERM, window resize and exit status
    /// with the launcher brokering none of it, which is what spec section 43
    /// asks for on the common path. An owned pseudo-terminal is needed only
    /// where there is no terminal to inherit.
    /// </para>
    /// </summary>
    Task<OperationResult<int>> RunInteractiveAsync(
        ProcessRequest request,
        CancellationToken ct = default);
}
