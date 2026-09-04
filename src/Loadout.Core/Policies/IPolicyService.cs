using Loadout.Models.Policies;
using Loadout.Models.Results;

namespace Loadout.Core.Policies;

/// <summary>What a clone has by way of the launcher-managed pre-commit hook.</summary>
/// <param name="Installed">Whether the launcher's own hook is there.</param>
/// <param name="NeedsUpgrade">
/// Whether it is there but written by an older version. Protected in practice,
/// and still worth replacing.
/// </param>
public sealed record HookState(bool Installed, bool NeedsUpgrade);

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
    /// <summary>
    /// Whether this clone carries the launcher's pre-commit hook, without
    /// scanning the repository for anything else.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="CheckAsync"/> because the answers cost
    /// differently. A full check walks the working tree for agent files, which
    /// is right for a report somebody asked for and wrong on the way into every
    /// launch; this reads one file. Hooks live in <c>.git/hooks</c> and never
    /// travel, so a fresh clone or a new worktree has none until somebody
    /// notices — which is the moment worth catching.
    /// </remarks>
    /// <returns>
    /// Null when it cannot be told from here — a working tree keeps its hooks in
    /// the repository it was made from, and reporting "not installed" would be a
    /// warning somebody would act on and be wrong about.
    /// </returns>
    HookState? InspectHook(string repositoryPath);

    Task<OperationResult> InstallHookAsync(string repositoryPath, CancellationToken ct = default);

    /// <summary>Removes a hook the launcher installed. Leaves a foreign hook alone.</summary>
    Task<OperationResult> RemoveHookAsync(string repositoryPath, CancellationToken ct = default);
}
