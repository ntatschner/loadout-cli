using Loadout.Models.Checkpoints;
using Loadout.Models.Results;

namespace Loadout.Core.Checkpoints;

/// <summary>What restoring a checkpoint would do, or did.</summary>
/// <param name="Checkpoint">The checkpoint in question.</param>
/// <param name="Files">Files the workspace restore would put back.</param>
/// <param name="Applied">False for a preview, true when it happened.</param>
/// <param name="RepositoryAdvice">
/// What to do about the repository, in words, because this never does it.
/// </param>
public sealed record CheckpointRestore(
    Checkpoint Checkpoint,
    IReadOnlyList<string> Files,
    bool Applied,
    string? RepositoryAdvice);

/// <summary>Named markers binding a workspace, a commit, a handoff and a session.</summary>
public interface ICheckpointService
{
    /// <summary>Takes a checkpoint of where a project stands now.</summary>
    Task<OperationResult<Checkpoint>> CreateAsync(
        string projectSlug,
        string name,
        string? description = null,
        CancellationToken ct = default);

    /// <summary>Checkpoints for a project, newest first.</summary>
    Task<OperationResult<IReadOnlyList<Checkpoint>>> ListAsync(
        string projectSlug,
        CancellationToken ct = default);

    /// <summary>One checkpoint by name.</summary>
    Task<OperationResult<Checkpoint>> GetAsync(
        string projectSlug,
        string name,
        CancellationToken ct = default);

    /// <summary>
    /// Forgets a checkpoint, leaving the snapshot it pointed at alone.
    /// </summary>
    Task<OperationResult> RemoveAsync(
        string projectSlug,
        string name,
        CancellationToken ct = default);

    /// <summary>
    /// Puts the workspace back as it was, and says what to do about the rest.
    /// </summary>
    /// <remarks>
    /// The repository is described, never moved. Checking a commit out can
    /// discard work nobody asked to lose, and doing that on somebody's behalf
    /// because they typed a checkpoint name is exactly the kind of surprise a
    /// preview-before-mutation rule exists to prevent. The commit is named and
    /// the command is theirs to run.
    /// </remarks>
    Task<OperationResult<CheckpointRestore>> RestoreAsync(
        string projectSlug,
        string name,
        bool apply,
        CancellationToken ct = default);
}
