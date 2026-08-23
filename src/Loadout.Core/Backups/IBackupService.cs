using Loadout.Models.Backups;
using Loadout.Models.Results;

namespace Loadout.Core.Backups;

/// <summary>
/// Captures files before a mutating operation, and puts them back.
/// <para>
/// The launcher moves files out of repositories, installs hooks and rewrites
/// configuration. Every one of those is something somebody will want to undo at
/// some point, and "restore from your own backup" is not an answer when the tool
/// is the thing that made the change.
/// </para>
/// <para>
/// Each set records a SHA-256 per file, and a restore verifies it before writing.
/// A restore also takes its own snapshot first, so undoing an undo is possible.
/// </para>
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Captures the given paths into a new set. Paths that do not exist are
    /// recorded as absent rather than skipped, so a restore can remove what the
    /// operation created.
    /// </summary>
    Task<OperationResult<BackupSet>> CaptureAsync(
        string operation,
        string detail,
        IReadOnlyList<string> paths,
        CancellationToken ct = default);

    /// <summary>Sets on this machine, newest first.</summary>
    Task<OperationResult<IReadOnlyList<BackupSet>>> ListAsync(CancellationToken ct = default);

    /// <summary>Reads one set by id, or by prefix when it is unambiguous.</summary>
    Task<OperationResult<BackupSet>> GetAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Puts a set back. Verifies every payload's digest before writing anything,
    /// so a corrupted set fails before it can do half a restore.
    /// </summary>
    Task<OperationResult<RestoreReport>> RestoreAsync(
        string id,
        bool apply,
        CancellationToken ct = default);

    /// <summary>Deletes a set permanently.</summary>
    Task<OperationResult> RemoveAsync(string id, CancellationToken ct = default);
}
