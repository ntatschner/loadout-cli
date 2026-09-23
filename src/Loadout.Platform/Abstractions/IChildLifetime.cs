namespace Loadout.Platform.Abstractions;

/// <summary>
/// Ties a child process to this one, so that nothing the launcher started
/// carries on after the launcher has gone.
/// </summary>
/// <remarks>
/// <para>
/// A headless agent takes its instructions from the process that started it
/// and from nowhere else. When that process dies, the agent is left running
/// with a session nobody is reading, spending money on turns nobody asked
/// for and editing a repository nobody is watching. It has happened: a team
/// run whose coordinator was killed left its lead and its verifier running
/// until they were found and stopped by hand.
/// </para>
/// <para>
/// Only children the launcher talks to are tied this way. A person's own
/// session inherits the terminal and ends with it, and an editor opened by
/// <see cref="IProcessLauncher.StartDetached"/> is meant to outlive the
/// command that opened it.
/// </para>
/// </remarks>
public interface IChildLifetime
{
    /// <summary>
    /// Whether the operating system enforces this even when the launcher is
    /// killed outright rather than asked to stop.
    /// </summary>
    /// <remarks>
    /// The difference is the whole substance: an exit handler covers Ctrl+C
    /// and an ordinary shutdown, and covers nothing at all when a process is
    /// terminated. Where the answer is false, a run can still be orphaned,
    /// and that is worth saying rather than assuming.
    /// </remarks>
    bool IsEnforced { get; }

    /// <summary>How the tie is made here, for the diagnostics to report.</summary>
    string Detail { get; }

    /// <summary>
    /// Takes responsibility for a child. Doing this to one that has already
    /// exited, or twice to the same one, does nothing and is not an error.
    /// </summary>
    void Adopt(int processId);
}
