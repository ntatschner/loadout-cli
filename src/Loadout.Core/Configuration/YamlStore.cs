using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Loadout.Core.Configuration;

/// <summary>
/// Reads and writes the launcher's YAML files.
/// <para>
/// Snake case is used throughout because that is the convention the spec's own
/// examples use, and the central workspace files are meant to be hand-edited
/// and reviewed in a pull request.
/// </para>
/// </summary>
public sealed class YamlStore
{
    private readonly IFilePermissions _permissions;

    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .WithTypeConverter(new DateTimeOffsetConverter())
        // A workspace written by a newer launcher may carry keys this version
        // does not know. Ignoring them lets an older client keep working
        // instead of refusing the whole file; genuine incompatibility is
        // signalled by workspace.yaml's schema version instead (section 91).
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .WithTypeConverter(new DateTimeOffsetConverter())
        .DisableAliases()
        .Build();

    public YamlStore(IFilePermissions permissions) => _permissions = permissions;

    /// <summary>
    /// Loads a YAML file, returning the supplied default when it does not
    /// exist. A missing config file is a first-run condition, not an error.
    /// </summary>
    public async Task<OperationResult<T>> LoadAsync<T>(
        string path,
        Func<T> createDefault,
        CancellationToken ct = default)
        where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                return OperationResult<T>.Ok(createDefault());
            }

            var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(text))
            {
                return OperationResult<T>.Ok(createDefault());
            }

            var value = _deserializer.Deserialize<T>(text);

            return value is null
                ? OperationResult<T>.Ok(createDefault())
                : OperationResult<T>.Ok(value);
        }
        catch (YamlException ex)
        {
            // The line and column matter: these files are hand-edited, so the
            // user needs to know where to look.
            return OperationResult<T>.Fail(
                $"'{path}' is not valid YAML at line {ex.Start.Line}, column {ex.Start.Column}: {ex.Message}",
                ExitCode.ConfigurationInvalid);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<T>.Fail(
                $"Could not read '{path}': {ex.Message}",
                ExitCode.ConfigurationInvalid);
        }
    }

    /// <summary>
    /// Writes a YAML file, creating parent directories as needed. Pass
    /// restrictPermissions for files that hold secret references or machine
    /// layout, which then get owner-only permissions (spec section 82).
    /// </summary>
    public async Task<OperationResult> SaveAsync<T>(
        string path,
        T value,
        bool restrictPermissions = true,
        CancellationToken ct = default)
    {
        using var held = await LockAsync(path, ct).ConfigureAwait(false);

        if (held is null)
        {
            return OperationResult.Fail(
                $"Could not write '{path}': another process has been holding it for longer than "
                + $"{LockWait.TotalSeconds:0.#}s.");
        }

        return await WriteAsync(path, value, restrictPermissions, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads, changes and writes a file without anybody else getting in
    /// between.
    /// </summary>
    /// <remarks>
    /// The load and the save have to be inside one lock. Two launchers each
    /// doing 'config set' would otherwise both read the old file, each write
    /// their own change over it, and one of the two changes would be gone with
    /// nothing to say so — the file is valid, the command reported success, and
    /// the setting is simply not there. Several sessions on one machine is the
    /// ordinary case here.
    /// </remarks>
    public async Task<OperationResult<T>> UpdateAsync<T>(
        string path,
        Func<T> createDefault,
        Action<T> change,
        bool restrictPermissions = true,
        CancellationToken ct = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(change);

        using var held = await LockAsync(path, ct).ConfigureAwait(false);

        if (held is null)
        {
            return OperationResult<T>.Fail(
                $"Could not update '{path}': another process has been holding it for longer than "
                + $"{LockWait.TotalSeconds:0.#}s.");
        }

        var loaded = await LoadAsync(path, createDefault, ct).ConfigureAwait(false);

        if (loaded.Failed)
        {
            return OperationResult<T>.Fail(loaded.Error!, loaded.ExitCode);
        }

        var value = loaded.Value!;

        change(value);

        var written = await WriteAsync(path, value, restrictPermissions, ct).ConfigureAwait(false);

        return written.Succeeded
            ? OperationResult<T>.Ok(value)
            : OperationResult<T>.Fail(written.Error!, written.ExitCode);
    }

    /// <summary>How long to wait for another process to finish writing.</summary>
    /// <remarks>
    /// Long enough that an ordinary overlap never surfaces, short enough that a
    /// crashed launcher holding nothing does not hang a command line for ever.
    /// A lock nobody can take is reported rather than waited on.
    /// </remarks>
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Takes the write lock for a file, or gives up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A lock file opened with no sharing, which is the one mechanism that
    /// works the same for two processes on Windows and on Unix without a
    /// dependency. It is never deleted: removing it would race with another
    /// process about to open it, and an empty file costs nothing.
    /// </para>
    /// <para>
    /// Kept well away from the file it guards. Beside it was the obvious
    /// placement and the wrong one: these files are written into the workspace,
    /// which is a Git repository, so the locks would have been committed — and
    /// into the state directory, where a restore enumerates everything under a
    /// backup's id and would have picked one up as a payload. The one that
    /// caught it was a test asserting a tampered backup fails to restore, which
    /// passed a lock file to the tamperer instead and then restored perfectly.
    /// </para>
    /// </remarks>
    private static async Task<FileStream?> LockAsync(string path, CancellationToken ct)
    {
        var lockPath = LockPathFor(path);

        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        var deadline = DateTime.UtcNow + LockWait;
        var wait = TimeSpan.FromMilliseconds(10);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(
                    lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    return null;
                }

                await Task.Delay(wait, ct).ConfigureAwait(false);

                // Backed off rather than spun, so a dozen launchers waiting on
                // one file do not spend the wait fighting each other for it.
                wait = TimeSpan.FromMilliseconds(Math.Min(wait.TotalMilliseconds * 2, 250));
            }
        }
    }

    /// <summary>
    /// Where the lock for a file lives: one directory, named by the full path
    /// it stands for.
    /// </summary>
    /// <remarks>
    /// Hashed rather than mangled, because the name has to be stable across
    /// processes and short enough to be a filename whatever the path was.
    /// Case is normalised on Windows only, matching how that filesystem
    /// compares paths — doing it everywhere would let two genuinely different
    /// files on Unix share one lock.
    /// </remarks>
    private static string LockPathFor(string path)
    {
        var full = Path.GetFullPath(path);

        if (OperatingSystem.IsWindows())
        {
            full = full.ToUpperInvariant();
        }

        var name = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(full)))[..32];

        return Path.Combine(Path.GetTempPath(), "loadout-locks", name + ".lock");
    }

    private async Task<OperationResult> WriteAsync<T>(
        string path,
        T value,
        bool restrictPermissions,
        CancellationToken ct)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var text = _serializer.Serialize(value);

            // Written to a temporary file and moved into place so an
            // interrupted write cannot leave a half-serialised config behind.
            //
            // One fixed name is enough because SaveAsync and UpdateAsync are
            // the only writers and both hold the lock above, so two of these
            // are never in flight for one file at once. Giving each write its
            // own name was tried and removed: with the lock there is nothing
            // left for it to prevent, and a mutation restoring the shared name
            // failed no test, which is the definition of code that is not
            // earning its place.
            var temporary = path + ".tmp";

            await File.WriteAllTextAsync(temporary, text, ct).ConfigureAwait(false);

            if (restrictPermissions)
            {
                // Applied before the move so the file is never briefly readable
                // by others at its final name.
                _permissions.RestrictToCurrentUser(temporary);
            }

            File.Move(temporary, path, overwrite: true);

            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or YamlException)
        {
            return OperationResult.Fail($"Could not write '{path}': {ex.Message}");
        }
    }
}
