using AgentWorkspace.Models.Configuration;
using AgentWorkspace.Models.Projects;
using AgentWorkspace.Models.Results;

namespace AgentWorkspace.Core.Workspace;

/// <summary>How a synchronisation attempt ended (spec sections 45, 47, 48).</summary>
public enum WorkspaceSyncOutcome
{
    /// <summary>The local clone is up to date with the remote.</summary>
    Synced,

    /// <summary>No central workspace is configured; the launcher is running local-only.</summary>
    NotConfigured,

    /// <summary>The remote was unreachable and the cached clone is being used instead.</summary>
    Offline,

    /// <summary>Local and remote have diverged and need a human decision.</summary>
    Conflict,
}

/// <summary>Result of a synchronisation attempt.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Detail">Explanation suitable for display. Already redacted.</param>
/// <param name="CachedAtUtc">When the local clone was last updated, shown in the offline prompt.</param>
public sealed record WorkspaceSyncResult(
    WorkspaceSyncOutcome Outcome,
    string Detail,
    DateTimeOffset? CachedAtUtc);

/// <summary>
/// Owns the local clone of the central agent-workspaces repository
/// (spec sections 10, 11, 45, 76).
/// <para>
/// The launcher must work with no central workspace at all (spec section 61
/// offers "run without central storage") and must keep working when the
/// central server is unreachable (spec section 48). Neither is an error path:
/// both are ordinary states this interface reports rather than throws on.
/// </para>
/// </summary>
public interface IWorkspaceManager
{
    /// <summary>Absolute path to the local clone, whether or not it exists yet.</summary>
    string LocalPath { get; }

    /// <summary>Whether a central workspace remote has been configured.</summary>
    bool IsConfigured(LauncherConfig config);

    /// <summary>
    /// Whether the local clone is a Git repository. Gates the Git operations
    /// only; it is false in the local-only mode of spec section 61.
    /// </summary>
    bool IsCloned();

    /// <summary>
    /// Whether workspace content exists locally, whether or not it came from
    /// Git.
    /// <para>
    /// This is the check that gates reading manifests and compiling context.
    /// Spec section 61 offers "run without central storage", and in that mode
    /// the launcher still writes a registry and project manifests into the same
    /// directory. Gating context on IsCloned would silently deprive those users
    /// of every profile and instruction file they had written.
    /// </para>
    /// </summary>
    bool IsAvailable();

    /// <summary>Clones the central workspace for the first time.</summary>
    Task<OperationResult> CloneAsync(LauncherConfig config, CancellationToken ct = default);

    /// <summary>
    /// Brings the local clone up to date, degrading to offline rather than
    /// failing when the remote cannot be reached within the configured timeout.
    /// </summary>
    Task<OperationResult<WorkspaceSyncResult>> SyncAsync(
        LauncherConfig config,
        CancellationToken ct = default);

    /// <summary>Validates workspace.yaml and reports schema compatibility (spec section 91).</summary>
    Task<OperationResult<WorkspaceManifest>> ReadManifestAsync(CancellationToken ct = default);

    /// <summary>Reads registry/projects.yaml. Returns an empty registry when absent.</summary>
    Task<OperationResult<ProjectRegistry>> ReadRegistryAsync(CancellationToken ct = default);

    Task<OperationResult> WriteRegistryAsync(ProjectRegistry registry, CancellationToken ct = default);

    /// <summary>Reads one project manifest from projects/&lt;slug&gt;/project.yaml.</summary>
    Task<OperationResult<ProjectManifest>> ReadProjectAsync(string slug, CancellationToken ct = default);

    Task<OperationResult> WriteProjectAsync(ProjectManifest manifest, CancellationToken ct = default);

    /// <summary>Creates the standard directory structure of spec section 11.</summary>
    Task<OperationResult> InitialiseStructureAsync(string workspaceName, CancellationToken ct = default);
}
