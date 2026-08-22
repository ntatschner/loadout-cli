using AgentWorkspace.Models.Projects;
using AgentWorkspace.Models.Results;

namespace AgentWorkspace.Core.Projects;

/// <summary>A repository found by scanning the configured roots (spec section 64).</summary>
/// <param name="Path">Absolute path to the repository root.</param>
/// <param name="Name">Directory name, offered as the default slug.</param>
/// <param name="RemoteUrl">Origin remote, or null when the repository has none.</param>
/// <param name="IsRegistered">True when this repository already maps to a registered project.</param>
/// <param name="MatchedSlug">Slug it matched, when it is already registered.</param>
public sealed record DiscoveredRepository(
    string Path,
    string Name,
    string? RemoteUrl,
    bool IsRegistered,
    string? MatchedSlug);

/// <summary>
/// Registers, resolves and lists projects (spec sections 29, 64, 70, 75).
/// <para>
/// Every operation joins two sources: the shared registry in the central
/// workspace, and this machine's local path mappings. Keeping them apart is
/// what lets one project definition serve a Windows desktop, a Linux
/// workstation and a Mac without carrying any machine's absolute paths into
/// the shared repository (spec section 15).
/// </para>
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// Every registered project joined to its local state, ordered for the
    /// recent-projects list: pinned first, then most recently launched, then
    /// most frequently launched (spec section 23).
    /// </summary>
    Task<OperationResult<IReadOnlyList<ProjectResolution>>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Resolves a slug, alias or display name to a project. Matching is
    /// case-insensitive on the handle itself, which is a user-typed identifier
    /// rather than a filesystem path.
    /// </summary>
    Task<OperationResult<ProjectResolution>> ResolveAsync(string handle, CancellationToken ct = default);

    /// <summary>
    /// Identifies the project containing a directory, for the "here" command
    /// (spec section 24). Matches on local path first, then on canonical
    /// remote so a second clone of the same repository still resolves.
    /// </summary>
    Task<OperationResult<ProjectResolution>> ResolveFromDirectoryAsync(
        string directory,
        CancellationToken ct = default);

    /// <summary>
    /// Registers an existing local repository, writing the shared definition to
    /// the workspace and the local path to this machine's config.
    /// </summary>
    Task<OperationResult<ProjectResolution>> AddAsync(
        string repositoryPath,
        string? slug = null,
        CancellationToken ct = default);

    /// <summary>
    /// Removes a project registration. Never touches the source repository:
    /// spec section 75 requires deleting code to be a separate, explicit act.
    /// </summary>
    Task<OperationResult> RemoveAsync(string handle, bool fromWorkspace, CancellationToken ct = default);

    /// <summary>Scans only the configured discovery roots (spec sections 64 and 85).</summary>
    Task<OperationResult<IReadOnlyList<DiscoveredRepository>>> DiscoverAsync(CancellationToken ct = default);

    /// <summary>Records a launch so the recent-projects ordering stays useful.</summary>
    Task<OperationResult> RecordLaunchAsync(string slug, string agent, CancellationToken ct = default);

    /// <summary>Points a project at a different local path on this machine (spec section 75).</summary>
    Task<OperationResult> RelocateAsync(string handle, string newPath, CancellationToken ct = default);
}
