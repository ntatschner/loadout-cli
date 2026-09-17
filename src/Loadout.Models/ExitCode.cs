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
    /// </remarks>
    TerminalRequired = 11,

    /// <summary>
    /// Stopped at the keyboard. 128 plus SIGINT, which is what a shell reports
    /// for an interrupted program and what scripts already test for — a value
    /// of our own here would be a second convention for something that has one.
    /// </summary>
    Interrupted = 130,
}
