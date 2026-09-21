namespace Loadout.Platform.Abstractions;

/// <summary>
/// A child process the launcher is talking to while it runs: its input held
/// open for writing, its output and error streams read as they arrive.
/// </summary>
/// <remarks>
/// <para>
/// The two existing ways of running a process cover a question and a
/// conversation with the user. <see cref="IProcessLauncher.RunAsync"/> writes
/// a single string to stdin, closes it and collects everything until exit,
/// which is right for a version probe and wrong for an agent that answers a
/// message, waits for the next, and must be told when there are no more.
/// <see cref="IProcessLauncher.RunInteractiveAsync"/> hands the child the real
/// terminal and steps out of the loop, which is right for a person at the
/// keyboard and wrong for a coordinator that has to read every event. This
/// is the third shape: a pipe the launcher holds both ends of.
/// </para>
/// <para>
/// Ownership is the caller's. The process is not killed when this is disposed
/// unless it is still running, because an agent that has already exited has
/// nothing to kill and one that is still running was the caller's to stop
/// deliberately. Disposal releases the handles and nothing else.
/// </para>
/// </remarks>
public interface IPipedProcess : IAsyncDisposable
{
    /// <summary>The operating system's identifier for the child.</summary>
    int ProcessId { get; }

    /// <summary>When the child was started, for telling a reused identifier from the process that had it.</summary>
    DateTimeOffset StartedAt { get; }

    /// <summary>The child's standard input, held open until <see cref="CloseInputAsync"/>.</summary>
    TextWriter Input { get; }

    /// <summary>The child's standard output, line by line as it is written.</summary>
    TextReader Output { get; }

    /// <summary>The child's standard error, line by line as it is written.</summary>
    TextReader Error { get; }

    /// <summary>Completes with the exit code once the child has exited.</summary>
    Task<int> Exited { get; }

    /// <summary>
    /// Closes the child's standard input. For an agent reading messages this
    /// is how it learns there will be no more, and most exit cleanly on it.
    /// </summary>
    Task CloseInputAsync();

    /// <summary>
    /// Stops the child and everything it started. Used when it has overrun a
    /// budget, stalled, or the run was cancelled; never as the ordinary way
    /// to end a conversation, which is <see cref="CloseInputAsync"/>.
    /// </summary>
    void Kill();
}
