using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Loadout.Core.Configuration;

/// <summary>What a file parsed to, and the revision of the text it was read from.</summary>
public sealed record YamlRead<T>(T Value, string Revision);

/// <summary>
/// The write locks for a set of files, held until this is disposed.
/// </summary>
/// <remarks>
/// Writing goes through here rather than through the store's own save, which
/// would try to take a lock this already holds and wait out its timeout on
/// itself. Only a file this holds can be written, so a change cannot reach a
/// file somebody else may be in the middle of changing.
/// </remarks>
public sealed class YamlHold : IDisposable
{
    private readonly YamlStore _store;
    private readonly HashSet<string> _locks;
    private readonly List<FileStream> _streams;

    internal YamlHold(YamlStore store, IReadOnlyCollection<string> locks, List<FileStream> streams)
    {
        _store = store;
        _locks = new HashSet<string>(locks, StringComparer.Ordinal);
        _streams = streams;
    }

    /// <summary>Writes a held file, and says the revision it now has.</summary>
    public Task<OperationResult<string>> WriteAsync<T>(
        string path,
        T value,
        bool restrictPermissions = true,
        CancellationToken ct = default)
    {
        if (!_locks.Contains(YamlStore.LockOf(path)))
        {
            throw new InvalidOperationException($"'{path}' is not one of the files this holds.");
        }

        return _store.WriteHeldAsync(path, value, restrictPermissions, ct);
    }

    public void Dispose()
    {
        foreach (var stream in _streams)
        {
            stream.Dispose();
        }

        _streams.Clear();
    }
}

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
        .WithTypeConverter(new TimeOnlyConverter())
        // A workspace written by a newer launcher may carry keys this version
        // does not know. Ignoring them lets an older client keep working
        // instead of refusing the whole file; genuine incompatibility is
        // signalled by workspace.yaml's schema version instead (section 91).
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .WithTypeConverter(new DateTimeOffsetConverter())
        .WithTypeConverter(new TimeOnlyConverter())
        .DisableAliases()
        .Build();

    public YamlStore(IFilePermissions permissions) => _permissions = permissions;

    /// <summary>
    /// Reads YAML handed over as text rather than from a file, with the same
    /// conventions a file gets.
    /// </summary>
    public OperationResult<T> Parse<T>(string text)
        where T : class
    {
        try
        {
            var value = string.IsNullOrWhiteSpace(text) ? null : _deserializer.Deserialize<T>(text);

            return value is null
                ? OperationResult<T>.Fail("There was nothing to read.", ExitCode.ConfigurationInvalid)
                : OperationResult<T>.Ok(value);
        }
        catch (YamlException ex)
        {
            return OperationResult<T>.Fail(
                $"Not valid YAML at line {ex.Start.Line}, column {ex.Start.Column}: {ex.Message}",
                ExitCode.ConfigurationInvalid);
        }
    }

    /// <summary>The text a value would be written as.</summary>
    public string Render<T>(T value) => _serializer.Serialize(value);

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

    /// <summary>
    /// What a file's text stands for, so a change made from an older read can
    /// be told apart from one made from the current one.
    /// </summary>
    /// <remarks>
    /// A hash of the text rather than a time or a counter, so an unchanged file
    /// keeps its revision across a restart, a copy and a <c>git pull</c> that
    /// brought nothing new. A file that is missing reads as an empty one, as it
    /// does everywhere else here.
    /// </remarks>
    public static string Revision(string? text) =>
        Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty)))[..16];

    /// <summary>
    /// Reads a file's text and what it parses to, or the default when it is
    /// missing or empty.
    /// </summary>
    public async Task<OperationResult<YamlRead<T>>> ReadAsync<T>(
        string path,
        Func<T> createDefault,
        CancellationToken ct = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(createDefault);

        string text;

        try
        {
            text = File.Exists(path) ? await File.ReadAllTextAsync(path, ct).ConfigureAwait(false) : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<YamlRead<T>>.Fail($"Could not read '{path}': {ex.Message}", ExitCode.ConfigurationInvalid);
        }

        try
        {
            var value = string.IsNullOrWhiteSpace(text) ? null : _deserializer.Deserialize<T>(text);

            return OperationResult<YamlRead<T>>.Ok(new YamlRead<T>(value ?? createDefault(), Revision(text)));
        }
        catch (YamlException ex)
        {
            return OperationResult<YamlRead<T>>.Fail(
                $"'{path}' is not valid YAML at line {ex.Start.Line}, column {ex.Start.Column}: {ex.Message}",
                ExitCode.ConfigurationInvalid);
        }
    }

    /// <summary>
    /// Takes the write lock for every one of several files, so they can be
    /// read, checked and written as one change.
    /// </summary>
    /// <remarks>
    /// Taken in one fixed order, by the lock's own name, so two processes
    /// holding overlapping sets can never each be waiting on the other. Every
    /// other writer takes one lock at a time and lets it go, so it cannot be
    /// part of a cycle either.
    /// </remarks>
    public async Task<OperationResult<YamlHold>> HoldAsync(IEnumerable<string> paths, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var held = new List<FileStream>();
        var named = paths
            .Select(path => (Path: Path.GetFullPath(path), Lock: LockPathFor(path)))
            .DistinctBy(one => one.Lock, StringComparer.Ordinal)
            .OrderBy(one => one.Lock, StringComparer.Ordinal)
            .ToList();

        try
        {
            foreach (var one in named)
            {
                if (await LockAsync(one.Path, ct).ConfigureAwait(false) is not { } stream)
                {
                    return OperationResult<YamlHold>.Fail(
                        $"Could not update '{one.Path}': another process has been holding it for longer than "
                        + $"{LockWait.TotalSeconds:0.#}s.");
                }

                held.Add(stream);
            }

            var hold = new YamlHold(this, [.. named.Select(one => one.Lock)], held);
            held = [];

            return OperationResult<YamlHold>.Ok(hold);
        }
        finally
        {
            foreach (var stream in held)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Writes a file this store holds the lock for, and says the revision it now has.</summary>
    internal async Task<OperationResult<string>> WriteHeldAsync<T>(
        string path,
        T value,
        bool restrictPermissions,
        CancellationToken ct)
    {
        var written = await WriteAsync(path, value, restrictPermissions, ct).ConfigureAwait(false);

        return written.Succeeded
            ? OperationResult<string>.Ok(Revision(_serializer.Serialize(value)))
            : OperationResult<string>.Fail(written.Error!, written.ExitCode);
    }

    internal static string LockOf(string path) => LockPathFor(path);

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
