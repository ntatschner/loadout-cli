using System.Security.Cryptography;
using AgentWorkspace.Core.Configuration;
using AgentWorkspace.Models;
using AgentWorkspace.Models.Backups;
using AgentWorkspace.Models.Results;
using AgentWorkspace.Platform.Abstractions;

namespace AgentWorkspace.Core.Backups;

/// <inheritdoc />
public sealed class BackupService : IBackupService
{
    private const string ManifestFileName = "manifest.yaml";
    private const string PayloadDirectoryName = "files";

    /// <summary>
    /// Refuses to capture anything implausibly large. A backup exists to make an
    /// operation reversible, not to become a second copy of a repository, and
    /// filling the disk while trying to be safe would be its own failure.
    /// </summary>
    private const long MaximumEntryBytes = 64L * 1024 * 1024;

    private readonly IPlatformPaths _paths;
    private readonly IFilePermissions _permissions;
    private readonly YamlStore _yaml;
    private readonly TimeProvider _time;

    public BackupService(
        IPlatformPaths paths,
        IFilePermissions permissions,
        YamlStore yaml,
        TimeProvider time)
    {
        _paths = paths;
        _permissions = permissions;
        _yaml = yaml;
        _time = time;
    }

    /// <summary>
    /// Backups live in state, not cache. The system is free to reclaim a cache
    /// directory whenever it likes, and a rollback that had silently stopped
    /// being possible would be worse than never offering one.
    /// </summary>
    private string Root => Path.Combine(_paths.Paths.State, "backups");

    /// <inheritdoc />
    public async Task<OperationResult<BackupSet>> CaptureAsync(
        string operation,
        string detail,
        IReadOnlyList<string> paths,
        CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        var id = $"{now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..22];

        var setDirectory = Path.Combine(Root, id);
        var payloadDirectory = Path.Combine(setDirectory, PayloadDirectoryName);

        var set = new BackupSet
        {
            Id = id,
            Operation = operation,
            Detail = detail,
            CreatedUtc = now,
            MachineName = _paths.Host.MachineName,
        };

        try
        {
            Directory.CreateDirectory(payloadDirectory);
            _permissions.RestrictDirectoryToCurrentUser(setDirectory);

            var index = 0;

            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();

                var existed = false;

                foreach (var file in ExpandToFiles(path))
                {
                    existed = true;

                    var entry = await CaptureFileAsync(file, payloadDirectory, index++, ct)
                        .ConfigureAwait(false);

                    if (entry.Failed)
                    {
                        return OperationResult<BackupSet>.Fail(entry.Error!, entry.ExitCode);
                    }

                    set.Entries.Add(entry.Value!);
                }

                if (!existed)
                {
                    // Recorded rather than skipped: the operation is about to
                    // create this, and rolling back means removing it again.
                    set.Entries.Add(new BackupEntry(
                        Path.GetFullPath(path), string.Empty, string.Empty, 0, false));
                }
            }

            var manifest = await _yaml
                .SaveAsync(Path.Combine(setDirectory, ManifestFileName), set, true, ct)
                .ConfigureAwait(false);

            return manifest.Succeeded
                ? OperationResult<BackupSet>.Ok(set)
                : OperationResult<BackupSet>.Fail(manifest.Error!, manifest.ExitCode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<BackupSet>.Fail($"The backup could not be written: {ex.Message}");
        }
    }

    private static async Task<OperationResult<BackupEntry>> CaptureFileAsync(
        string file,
        string payloadDirectory,
        int index,
        CancellationToken ct)
    {
        var info = new FileInfo(file);

        if (info.Length > MaximumEntryBytes)
        {
            return OperationResult<BackupEntry>.Fail(
                $"'{file}' is {info.Length / (1024 * 1024)}MB, larger than this tool will copy into "
                + "a backup. Move it aside yourself before continuing.");
        }

        // Positional rather than derived from the original name: two files can
        // share a name in different directories, and flattening a path into a
        // file name is a collision waiting to happen.
        var storedName = index.ToString("D5", System.Globalization.CultureInfo.InvariantCulture)
            + Path.GetExtension(file);

        File.Copy(file, Path.Combine(payloadDirectory, storedName), overwrite: true);

        await using var stream = File.OpenRead(file);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);

        return OperationResult<BackupEntry>.Ok(new BackupEntry(
            Path.GetFullPath(file),
            storedName,
            Convert.ToHexStringLower(hash),
            info.Length,
            true));
    }

    private static IEnumerable<string> ExpandToFiles(string path)
    {
        if (File.Exists(path))
        {
            return [path];
        }

        return Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            : [];
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<BackupSet>>> ListAsync(
        CancellationToken ct = default)
    {
        if (!Directory.Exists(Root))
        {
            return OperationResult<IReadOnlyList<BackupSet>>.Ok([]);
        }

        var sets = new List<BackupSet>();

        foreach (var directory in Directory.EnumerateDirectories(Root))
        {
            var manifest = Path.Combine(directory, ManifestFileName);

            if (!File.Exists(manifest))
            {
                continue;
            }

            var loaded = await _yaml.LoadAsync(manifest, () => new BackupSet(), ct)
                .ConfigureAwait(false);

            if (loaded.Failed || string.IsNullOrEmpty(loaded.Value!.Id))
            {
                // Loudly, not silently. A set whose manifest cannot be read is
                // a rollback point that has quietly stopped existing, and the
                // one moment somebody discovers that must not be the moment
                // they are trying to undo something.
                return OperationResult<IReadOnlyList<BackupSet>>.Fail(
                    $"The backup manifest at '{manifest}' could not be read"
                    + (loaded.Failed ? ": " + loaded.Error : " and names no set.")
                    + " Move that directory aside to continue.");
            }

            sets.Add(loaded.Value);
        }

        return OperationResult<IReadOnlyList<BackupSet>>.Ok(
            sets.OrderByDescending(s => s.CreatedUtc).ToList());
    }

    /// <inheritdoc />
    public async Task<OperationResult<BackupSet>> GetAsync(string id, CancellationToken ct = default)
    {
        var all = await ListAsync(ct).ConfigureAwait(false);

        if (all.Failed)
        {
            return OperationResult<BackupSet>.Fail(all.Error!, all.ExitCode);
        }

        var exact = all.Value!.FirstOrDefault(s => s.Id == id);

        if (exact is not null)
        {
            return OperationResult<BackupSet>.Ok(exact);
        }

        // A prefix is enough while it stays unambiguous. Requiring somebody to
        // type twenty-two characters to undo something is its own deterrent.
        var matches = all.Value!.Where(s => s.Id.StartsWith(id, StringComparison.Ordinal)).ToList();

        return matches.Count switch
        {
            1 => OperationResult<BackupSet>.Ok(matches[0]),

            0 => OperationResult<BackupSet>.Fail(
                $"No backup set matches '{id}'.", ExitCode.InvalidArguments),

            _ => OperationResult<BackupSet>.Fail(
                $"'{id}' matches {matches.Count} backup sets. Use more characters.",
                ExitCode.InvalidArguments),
        };
    }

    /// <inheritdoc />
    public async Task<OperationResult<RestoreReport>> RestoreAsync(
        string id,
        bool apply,
        CancellationToken ct = default)
    {
        var setResult = await GetAsync(id, ct).ConfigureAwait(false);

        if (setResult.Failed)
        {
            return OperationResult<RestoreReport>.Fail(setResult.Error!, setResult.ExitCode);
        }

        var set = setResult.Value!;
        var payloadDirectory = Path.Combine(Root, set.Id, PayloadDirectoryName);

        var verified = await VerifyAsync(set, payloadDirectory, ct).ConfigureAwait(false);

        if (verified.Failed)
        {
            return OperationResult<RestoreReport>.Fail(verified.Error!, verified.ExitCode);
        }

        if (apply)
        {
            // Taken before anything is written, so undoing this restore is
            // possible too.
            var snapshot = await CaptureAsync(
                "restore",
                $"state before restoring {set.Id}",
                set.Entries.Select(e => e.OriginalPath).Distinct().ToList(),
                ct).ConfigureAwait(false);

            if (snapshot.Failed)
            {
                return OperationResult<RestoreReport>.Fail(
                    "A snapshot of the current state could not be taken, so the restore was not "
                    + $"attempted: {snapshot.Error}");
            }
        }

        var restored = new List<string>();
        var removed = new List<string>();
        var skipped = new Dictionary<string, string>(StringComparer.Ordinal);

        // Computed before anything is written, because it compares what is on
        // disk now against what is about to replace it.
        var drift = CollectDrift(set, payloadDirectory);

        foreach (var entry in set.Entries)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (!entry.Existed)
                {
                    if (File.Exists(entry.OriginalPath))
                    {
                        if (apply)
                        {
                            File.Delete(entry.OriginalPath);
                        }

                        removed.Add(entry.OriginalPath);
                    }

                    continue;
                }

                if (apply)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(entry.OriginalPath)!);

                    File.Copy(
                        Path.Combine(payloadDirectory, entry.StoredName),
                        entry.OriginalPath,
                        overwrite: true);
                }

                restored.Add(entry.OriginalPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped[entry.OriginalPath] = ex.Message;
            }
        }

        return OperationResult<RestoreReport>.Ok(
            new RestoreReport(set, restored, removed, skipped, apply, drift));
    }

    /// <summary>
    /// Compares the current shape of each structured file against the shape the
    /// restore would put back.
    /// <para>
    /// This is the failure a file-level backup cannot otherwise see: every
    /// digest matches, the restore succeeds, and keys written after the
    /// snapshot are gone without a word.
    /// </para>
    /// </summary>
    private static List<KeyDrift> CollectDrift(BackupSet set, string payloadDirectory)
    {
        var drift = new List<KeyDrift>();

        foreach (var entry in set.Entries.Where(e => e.Existed))
        {
            if (!StructuredShape.IsStructured(entry.OriginalPath))
            {
                continue;
            }

            var current = StructuredShape.Read(entry.OriginalPath);
            var stored = StructuredShape.Read(Path.Combine(payloadDirectory, entry.StoredName));

            // Either side unreadable means no comparison is possible. Reporting
            // every key as dropped because a file failed to parse would be
            // worse than saying nothing.
            if (current is null || stored is null)
            {
                continue;
            }

            foreach (var key in current.Keys.Where(k => k.Length > 0 && !stored.ContainsKey(k)))
            {
                drift.Add(new KeyDrift(entry.OriginalPath, key, KeyDriftKind.Dropped));
            }

            foreach (var key in stored.Keys.Where(k => k.Length > 0 && !current.ContainsKey(k)))
            {
                drift.Add(new KeyDrift(entry.OriginalPath, key, KeyDriftKind.Added));
            }

            foreach (var pair in current.Where(p => p.Key.Length > 0
                && stored.TryGetValue(p.Key, out var other)
                && !string.Equals(other, p.Value, StringComparison.Ordinal)))
            {
                drift.Add(new KeyDrift(entry.OriginalPath, pair.Key, KeyDriftKind.Changed));
            }
        }

        return CollapseParents(drift);
    }

    /// <summary>
    /// Drops a key when its parent is already reported.
    /// <para>
    /// Removing one object would otherwise report the object and every field
    /// inside it, burying the one line that says what actually happened.
    /// </para>
    /// </summary>
    private static List<KeyDrift> CollapseParents(List<KeyDrift> drift)
    {
        var reported = drift
            .Where(d => d.Kind != KeyDriftKind.Changed)
            .Select(d => (d.File, d.KeyPath))
            .ToHashSet();

        return drift
            .Where(d => !HasReportedParent(d, reported))
            .OrderBy(d => d.File, StringComparer.Ordinal)
            .ThenBy(d => d.KeyPath, StringComparer.Ordinal)
            .ToList();
    }

    private static bool HasReportedParent(
        KeyDrift drift,
        HashSet<(string File, string KeyPath)> reported)
    {
        var path = drift.KeyPath;

        while (true)
        {
            var separator = path.LastIndexOf('.');

            if (separator <= 0)
            {
                return false;
            }

            path = path[..separator];

            if (reported.Contains((drift.File, path)))
            {
                return true;
            }
        }
    }

    /// <summary>
    /// Checks every payload before anything is written.
    /// <para>
    /// A set with one bad payload must not leave the tree half restored, which
    /// is a worse state than either the before or the after.
    /// </para>
    /// </summary>
    private static async Task<OperationResult> VerifyAsync(
        BackupSet set,
        string payloadDirectory,
        CancellationToken ct)
    {
        foreach (var entry in set.Entries.Where(e => e.Existed))
        {
            ct.ThrowIfCancellationRequested();

            var stored = Path.Combine(payloadDirectory, entry.StoredName);

            if (!File.Exists(stored))
            {
                return OperationResult.Fail(
                    $"Backup set '{set.Id}' is missing its copy of '{entry.OriginalPath}'. "
                    + "Nothing was restored.",
                    ExitCode.PolicyViolation);
            }

            await using var stream = File.OpenRead(stored);
            var hash = Convert.ToHexStringLower(
                await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));

            if (!string.Equals(hash, entry.Sha256, StringComparison.Ordinal))
            {
                return OperationResult.Fail(
                    $"The stored copy of '{entry.OriginalPath}' does not match its recorded digest. "
                    + "The backup set has been altered or corrupted, so nothing was restored.",
                    ExitCode.PolicyViolation);
            }
        }

        return OperationResult.Ok();
    }

    /// <inheritdoc />
    public async Task<OperationResult> RemoveAsync(string id, CancellationToken ct = default)
    {
        var setResult = await GetAsync(id, ct).ConfigureAwait(false);

        if (setResult.Failed)
        {
            return OperationResult.Fail(setResult.Error!, setResult.ExitCode);
        }

        try
        {
            Directory.Delete(Path.Combine(Root, setResult.Value!.Id), recursive: true);
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"The backup set could not be removed: {ex.Message}");
        }
    }
}
