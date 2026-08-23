namespace Loadout.Models.Backups;

/// <summary>One file captured in a backup set.</summary>
public sealed class BackupEntry
{
    /// <summary>
    /// Required by the YAML deserialiser, which constructs the object before
    /// it has any values to give it. A positional record reads better but
    /// cannot be rehydrated from a manifest, and a manifest that cannot be read
    /// back is a backup that silently does not exist.
    /// </summary>
    public BackupEntry()
    {
    }

    public BackupEntry(string originalPath, string storedName, string sha256, long bytes, bool existed)
    {
        OriginalPath = originalPath;
        StoredName = storedName;
        Sha256 = sha256;
        Bytes = bytes;
        Existed = existed;
    }

    /// <summary>Absolute path the file was taken from.</summary>
    public string OriginalPath { get; set; } = string.Empty;

    /// <summary>File name inside the set that holds the copy.</summary>
    public string StoredName { get; set; } = string.Empty;

    /// <summary>Lowercase hex digest of the captured content.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Size of the captured content.</summary>
    public long Bytes { get; set; }

    /// <summary>
    /// False when the path did not exist at capture time.
    /// <para>
    /// Recorded rather than skipped, because restoring has to be able to put
    /// things back exactly: a file the operation created must be deleted on
    /// rollback, not left behind as a leftover nobody expected.
    /// </para>
    /// </summary>
    public bool Existed { get; set; }
}

/// <summary>
/// A timestamped snapshot taken before a mutating operation.
/// <para>
/// Every operation that changes a file writes one of these first and prints the
/// command that undoes it. Without that, a migration or a policy change is a
/// one-way door, and the launcher is asking people to trust it with their
/// repositories on the strength of a dry run alone.
/// </para>
/// </summary>
public sealed class BackupSet
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Identifier used to address this set on the command line.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>What produced it, for example "migrate" or "protect".</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>Free-text detail, such as the project slug it applied to.</summary>
    public string Detail { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public string MachineName { get; set; } = string.Empty;

    public List<BackupEntry> Entries { get; set; } = [];
}

/// <summary>What happened to one key when a structured file was restored.</summary>
public enum KeyDriftKind
{
    /// <summary>The key exists now and would not survive the restore.</summary>
    Dropped,

    /// <summary>The key is in the backup and is absent now, so the restore brings it back.</summary>
    Added,

    /// <summary>The key exists in both and holds something different.</summary>
    Changed,
}

/// <summary>
/// One key path a restore would change in a structured file.
/// <para>
/// The path only, never the value. A settings file can hold a credential or a
/// command line containing one, and this is printed to a terminal.
/// </para>
/// </summary>
/// <param name="File">The file the key lives in.</param>
/// <param name="KeyPath">Dotted path to the key.</param>
/// <param name="Kind">What the restore would do to it.</param>
public sealed record KeyDrift(string File, string KeyPath, KeyDriftKind Kind);

/// <summary>What a restore did, or would do.</summary>
/// <param name="Set">The set that was restored.</param>
/// <param name="Restored">Paths written back.</param>
/// <param name="Removed">Paths deleted because they did not exist before the operation.</param>
/// <param name="Skipped">Paths left alone, with the reason.</param>
/// <param name="Applied">False for a dry run.</param>
/// <param name="Drift">
/// Keys a restore of a structured file would drop, add or change.
/// <para>
/// The point of reporting these is that a whole-file restore silently discards
/// every key written since the snapshot. The digests all match, the restore
/// reports success, and a setting somebody turned on last week is simply gone
/// with nothing to show it ever existed.
/// </para>
/// </param>
public sealed record RestoreReport(
    BackupSet Set,
    IReadOnlyList<string> Restored,
    IReadOnlyList<string> Removed,
    IReadOnlyDictionary<string, string> Skipped,
    bool Applied,
    IReadOnlyList<KeyDrift> Drift)
{
    /// <summary>Keys that exist now and would not survive, which is the loss worth seeing.</summary>
    public IEnumerable<KeyDrift> Dropped => Drift.Where(d => d.Kind == KeyDriftKind.Dropped);
}
