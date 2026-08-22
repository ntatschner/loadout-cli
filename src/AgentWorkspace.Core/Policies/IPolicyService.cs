using AgentWorkspace.Models.Policies;
using AgentWorkspace.Models.Results;

namespace AgentWorkspace.Core.Policies;

/// <summary>
/// Checks and enforces repository cleanliness (spec sections 49 to 51 and 97).
/// <para>
/// This is the component that makes the launcher's central promise verifiable
/// rather than aspirational: application repositories hold application source,
/// and agent state lives elsewhere. Without a check that anyone can run, the
/// separation lasts exactly as long as everybody remembers it.
/// </para>
/// </summary>
public interface IPolicyService
{
    /// <summary>
    /// Loads the workspace policy, falling back to the built-in defaults when
    /// the workspace defines none.
    /// </summary>
    Task<OperationResult<RepositoryPolicy>> LoadPolicyAsync(CancellationToken ct = default);

    /// <summary>Checks one repository against the policy (spec section 49).</summary>
    Task<OperationResult<PolicyReport>> CheckAsync(
        string repositoryPath,
        CancellationToken ct = default);

    /// <summary>
    /// Configures a global Git exclude file covering the forbidden patterns
    /// (spec section 50), so no application repository needs its .gitignore
    /// polluted.
    /// </summary>
    Task<OperationResult<string>> InstallGlobalExcludesAsync(CancellationToken ct = default);

    /// <summary>
    /// Installs an untracked pre-commit hook that blocks agent files
    /// (spec section 51).
    /// </summary>
    Task<OperationResult> InstallHookAsync(string repositoryPath, CancellationToken ct = default);

    /// <summary>Removes a hook the launcher installed. Leaves a foreign hook alone.</summary>
    Task<OperationResult> RemoveHookAsync(string repositoryPath, CancellationToken ct = default);
}
