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
    /// Stopped at the keyboard. 128 plus SIGINT, which is what a shell reports
    /// for an interrupted program and what scripts already test for — a value
    /// of our own here would be a second convention for something that has one.
    /// </summary>
    Interrupted = 130,
}
