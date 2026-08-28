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
/// <param name="RemoveEnvironmentPrefixes">
/// Variable names beginning with any of these are withheld from the child.
/// <para>
/// Launching an application from inside a terminal that application owns hands
/// it a copy of its own private environment, and some of that is poison.
/// VS Code sets ELECTRON_RUN_AS_NODE=1 for its command line shim, so a VS Code
/// started from a VS Code terminal runs as Node, reads the folder it was asked
/// to open as a module path, and comes up as a blank window with no workbench
/// in it. The arguments were right the whole way down; the environment was not.
/// </para>
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
    string? StandardInput = null,
    IReadOnlyList<string>? RemoveEnvironmentPrefixes = null);

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

    /// <summary>
    /// Starts a process and returns without waiting for it, giving it neither
    /// a captured output stream nor a suppressed window. For opening a
    /// graphical application.
    /// </summary>
    /// <remarks>
    /// Opening an editor through <see cref="RunAsync"/> looked reasonable and
    /// was not: that method captures output and suppresses the window, because
    /// it exists for short questions like a git query. VS Code launched that
    /// way came up as a blank frame with no workbench in it, and the same call
    /// with no redirection and no suppressed window opened the folder.
    ///
    /// Waiting is wrong here too. An editor outlives the launcher that started
    /// it, and there is no exit code worth having.
    /// </remarks>
    OperationResult StartDetached(ProcessRequest request);
}
