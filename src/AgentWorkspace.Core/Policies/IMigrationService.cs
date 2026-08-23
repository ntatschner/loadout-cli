using AgentWorkspace.Models.Policies;
using AgentWorkspace.Models.Results;

namespace AgentWorkspace.Core.Policies;

/// <summary>One file or directory the migration would move.</summary>
/// <param name="SourcePath">Absolute path in the application repository.</param>
/// <param name="RepositoryRelativePath">Where it sits inside the repository.</param>
/// <param name="WorkspaceRelativePath">Where it would land in the central workspace.</param>
/// <param name="Kind">Whether Git is tracking it, which decides how it can be removed.</param>
/// <param name="IsDirectory">True when the whole subtree moves.</param>
public sealed record MigrationStep(
    string SourcePath,
    string RepositoryRelativePath,
    string WorkspaceRelativePath,
    PolicyFindingKind Kind,
    bool IsDirectory);

/// <summary>What a migration would do, or did.</summary>
/// <param name="Slug">Project the material belongs to.</param>
/// <param name="Steps">Everything that would move.</param>
/// <param name="Applied">False for a dry run (spec section 27).</param>
/// <param name="TrackedLeftInPlace">
/// Tracked paths that were copied into the workspace but deliberately not
/// deleted from the repository.
/// </param>
public sealed record MigrationPlan(
    string Slug,
    IReadOnlyList<MigrationStep> Steps,
    bool Applied,
    IReadOnlyList<string> TrackedLeftInPlace);

/// <summary>
/// Moves existing agent configuration out of an application repository and into
/// the central workspace (spec sections 27 and 96).
/// <para>
/// This is the on-ramp. Nobody starts with a clean repository; they start with
/// a <c>.claude</c> directory and a CLAUDE.md that already work, and the
/// launcher is only adoptable if moving them is safe and reversible.
/// </para>
/// </summary>
public interface IMigrationService
{
    /// <summary>
    /// Works out what would move, without changing anything.
    /// </summary>
    /// <param name="repositoryPath">Repository to inspect.</param>
    /// <param name="slug">Project the material belongs to.</param>
    /// <param name="includeIgnored">
    /// Whether to move files Git already ignores.
    /// <para>
    /// False by default, and that default matters. An ignored .claude
    /// directory is not in the repository's content and never will be, so the
    /// repository is already compliant with respect to it (spec sections 9 and
    /// 97, where the policy check treats ignored files as the system working).
    /// Moving one anyway would take away a working local setup to solve a
    /// problem that does not exist. It is offered, not assumed.
    /// </para>
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<OperationResult<MigrationPlan>> PlanAsync(
        string repositoryPath,
        string slug,
        bool includeIgnored = false,
        CancellationToken ct = default);

    /// <summary>
    /// Carries out a plan.
    /// <para>
    /// Tracked files are copied into the workspace and left in the repository
    /// (spec section 27: tracked files must not be silently deleted). Removing
    /// them is a separate, visible Git commit the user makes themselves.
    /// </para>
    /// </summary>
    Task<OperationResult<MigrationPlan>> ApplyAsync(
        MigrationPlan plan,
        CancellationToken ct = default);
}
