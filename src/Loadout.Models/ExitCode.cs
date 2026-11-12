namespace Loadout.Models;

/// <summary>
/// Stable process exit codes (spec section 40). These are a public contract:
/// automation depends on them, so values are never reordered or reused.
/// </summary>
public enum ExitCode
{
    Success = 0,
    GeneralFailure = 1,
    InvalidArguments = 2,
    ProjectNotFound = 3,
    RepositoryUnavailable = 4,
    AgentUnavailable = 5,
    WorkspaceSyncFailed = 6,
    ConfigurationInvalid = 7,
    AuthenticationRequired = 8,
    PolicyViolation = 9,
    GitConflict = 10,

    /// <summary>
    /// The launcher was asked for something that needs a terminal, and there
    /// is not one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="InvalidArguments"/>, which is what this used to
    /// return and is not what happened: running <c>loadout</c> with no
    /// arguments is the documented way to open the launcher, and the arguments
    /// were fine. What was missing was somewhere to draw.
    /// </para>
    /// <para>
    /// Its own value because 2 is also <c>ERROR_FILE_NOT_FOUND</c>, and
    /// anything reading a process exit code as a Win32 one says "the system
    /// cannot find the file specified" about a launcher that started, printed
    /// and exited perfectly. A validator did exactly that, and reported a
    /// healthy package as a missing file.
    /// </para>
    /// <para>
    /// No longer what <c>loadout</c> with no arguments returns: that now
    /// succeeds, having said where to go instead, because a validator reads
    /// this code as a failure too. It stays for commands that were asked to
    /// do something only a person can, such as a manual team run.
    /// </para>
    /// </remarks>
    TerminalRequired = 11,

    /// <summary>
    /// What was to be changed has changed since it was read, so nothing was
    /// written. Read it again and redo the change on what is there now.
    /// </summary>
    /// <remarks>
    /// Not <see cref="GitConflict"/>, which is two histories that will not
    /// merge. This is one file somebody else wrote first.
    /// </remarks>
    ChangedSinceRead = 12,

    /// <summary>
    /// What was asked for has already happened, so nothing was changed: a
    /// run asked to close that had already finished.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not <see cref="InvalidArguments"/>, which is what this used to return.
    /// The arguments were fine, and that code is also what a close returns when
    /// it wants a confirmation, so a caller reading the code asked somebody to
    /// confirm closing a run that had nothing left to close.
    /// </para>
    /// <para>
    /// Not <see cref="ChangedSinceRead"/> either: a run typed by hand on the
    /// command line was never read first, so nothing changed under anybody.
    /// </para>
    /// </remarks>
    AlreadyDone = 13,

    /// <summary>
    /// Stopped at the keyboard. 128 plus SIGINT, which is what a shell reports
    /// for an interrupted program and what scripts already test for — a value
    /// of our own here would be a second convention for something that has one.
    /// </summary>
    Interrupted = 130,
}
