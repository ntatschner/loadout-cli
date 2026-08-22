namespace AgentWorkspace.Models;

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
}
