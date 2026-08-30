using Loadout.Models.Projects;
using Loadout.Models.Results;

namespace Loadout.Core.Projects;

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

/// <summary>What removing a project registration did, and what it did not.</summary>
/// <param name="Slug">The project that was removed.</param>
/// <param name="FromWorkspace">Whether the shared registry row went too.</param>
/// <param name="DefinitionPath">
/// Where the project's definition still sits in the workspace, or null when
/// there is none. It is never deleted: it holds instructions, rules and the
/// memory an agent accumulated, and losing that to a command whose stated job
/// is removing a registration would be the kind of surprise there is no
/// recovering from.
/// </param>
/// <param name="DefinitionFiles">How many files remain there.</param>
public sealed record ProjectRemoval(
    string Slug,
    bool FromWorkspace,
    string? DefinitionPath,
    int DefinitionFiles);

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
    /// Git configuration key a repository uses to name the project it belongs
    /// to. Read by <see cref="ResolveFromDirectoryAsync"/> and written whenever
    /// a project is mapped to a directory.
    /// </summary>
    const string ProjectMarker = "loadout.project";

    /// <summary>
    /// What the key was called before the tool was renamed.
    /// <para>
    /// Still read, because it was written into repositories that exist. A rename
    /// on this side does not reach into somebody's clones, and quietly failing
    /// to recognise a repository the launcher itself had marked would be a
    /// self-inflicted regression.
    /// </para>
    /// </summary>
    const string LegacyProjectMarker = "agentctl.project";

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
    /// <para>
    /// Nor the project's definition in the workspace, which holds its
    /// instructions, rules and memory. The result says what was left behind so
    /// the caller can be honest about it rather than implying more was removed
    /// than was.
    /// </para>
    /// </summary>
    Task<OperationResult<ProjectRemoval>> RemoveAsync(
        string handle,
        bool fromWorkspace,
        CancellationToken ct = default);

    /// <summary>Scans only the configured discovery roots (spec sections 64 and 85).</summary>
    Task<OperationResult<IReadOnlyList<DiscoveredRepository>>> DiscoverAsync(CancellationToken ct = default);

    /// <summary>Records a launch so the recent-projects ordering stays useful.</summary>
    Task<OperationResult> RecordLaunchAsync(string slug, string agent, CancellationToken ct = default);

    /// <summary>Points a project at a different local path on this machine (spec section 75).</summary>
    Task<OperationResult> RelocateAsync(string handle, string newPath, CancellationToken ct = default);

    /// <summary>
    /// Clones a project that is registered centrally but absent here
    /// (spec sections 28 and 75), then maps it locally.
    /// <para>
    /// This is the other half of cross-machine work: a project definition
    /// travels through the workspace, but the source has to arrive somehow, and
    /// making the user find the remote URL themselves defeats the point.
    /// </para>
    /// </summary>
    /// <param name="handle">Slug, alias or name of the project.</param>
    /// <param name="destination">
    /// Where to clone to. Defaults to the machine's configured clone root plus
    /// the project slug.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<OperationResult<ProjectResolution>> CloneAsync(
        string handle,
        string? destination = null,
        CancellationToken ct = default);
}
