namespace Loadout.Models.Checkpoints;

/// <summary>
/// A named marker binding everything that made a moment what it was.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is new. The backups already hold the workspace, Git already
/// holds the commit, the handoffs already hold what somebody wrote down, and
/// the ledger already holds the session. What none of them held is the fact
/// that these four belong together, and that is the whole of this type: an
/// identifier for each, taken at one moment, under a name a person chose.
/// </para>
/// <para>
/// References rather than copies, apart from the workspace. Copying a commit is
/// not a thing anybody can do, and copying a handoff would leave two of them to
/// drift apart. A reference that no longer resolves is reported as missing,
/// which is honest, where a stale copy would look fine and be wrong.
/// </para>
/// </remarks>
public sealed class Checkpoint
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>The name somebody gave it.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Why it was taken, in their words. Empty is allowed.</summary>
    public string Description { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public string ProjectSlug { get; set; } = string.Empty;

    /// <summary>The backup set holding the workspace files as they were.</summary>
    public string WorkspaceBackupId { get; set; } = string.Empty;

    /// <summary>The commit the repository was on. Null when it could not be read.</summary>
    public string? RepositoryCommit { get; set; }

    public string? RepositoryBranch { get; set; }

    /// <summary>
    /// Whether the tree had uncommitted changes when this was taken.
    /// </summary>
    /// <remarks>
    /// Recorded because it decides what the commit is worth. A checkpoint taken
    /// on a dirty tree names a commit that does not describe what was on disk,
    /// and somebody returning to it should be told that rather than left to
    /// discover it.
    /// </remarks>
    public bool RepositoryWasDirty { get; set; }

    /// <summary>The handoff that was current, by name. Null when there was none.</summary>
    public string? HandoffName { get; set; }

    /// <summary>The launch this was taken during, by ledger id. Null outside a session.</summary>
    public string? SessionId { get; set; }
}
