using System.Text.Json;
using System.Text.Json.Serialization;
using Loadout.Core.Git;
using Loadout.Models.Results;

namespace Loadout.Core.Instructions;

/// <summary>One symbol that answered a lookup, and how well.</summary>
/// <param name="Symbol">What was found.</param>
/// <param name="Exact">Whether the name matched whole rather than as a fragment.</param>
public sealed record SymbolMatch(Symbol Symbol, bool Exact);

/// <summary>What a lookup found, and where the index it searched came from.</summary>
/// <param name="Matches">The symbols, best first.</param>
/// <param name="Indexed">How many symbols the index holds.</param>
/// <param name="Head">The commit the index was built at, or null outside a repository.</param>
/// <param name="FromCache">
/// Whether the index was read back rather than built. A cached index is
/// re-read from disk for every file it names in the answer, so a hit is right
/// about the file as it is now; what it cannot see is a file added since.
/// </param>
/// <param name="Rebuilt">
/// Whether a cached index found nothing and the repository was scanned again
/// before answering. The cache only ever makes a hit faster; a miss is
/// checked against the tree before it is reported.
/// </param>
public sealed record SymbolLookup(
    IReadOnlyList<SymbolMatch> Matches,
    int Indexed,
    string? Head,
    bool FromCache,
    bool Rebuilt);

/// <summary>Finds where a name is declared.</summary>
public interface ISymbolIndexService
{
    /// <summary>The symbols whose name matches, best first.</summary>
    /// <param name="repositoryPath">The repository to look in.</param>
    /// <param name="slug">The project, which names the cache.</param>
    /// <param name="query">A type or member name, whole or in part.</param>
    /// <param name="limit">How many to return.</param>
    /// <param name="rescan">Read the tree again rather than the cache, whatever the commit.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<OperationResult<SymbolLookup>> FindAsync(
        string repositoryPath,
        string slug,
        string query,
        int limit = 10,
        bool rescan = false,
        CancellationToken ct = default);

    /// <summary>The map of the code: one line per directory, naming the types it holds.</summary>
    /// <param name="repositoryPath">The repository to map.</param>
    /// <param name="slug">The project, which names the cache.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<OperationResult<SymbolDigest>> DigestAsync(
        string repositoryPath,
        string slug,
        CancellationToken ct = default);

    /// <summary>Brings the cached index up to date for the files named, and only those.</summary>
    /// <param name="repositoryPath">The repository the files are in.</param>
    /// <param name="slug">The project, which names the cache.</param>
    /// <param name="files">Paths, absolute or relative to the repository.</param>
    /// <param name="dryRun">Say what would change and write nothing.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<OperationResult<SymbolRefresh>> RefreshAsync(
        string repositoryPath,
        string slug,
        IReadOnlyList<string> files,
        bool dryRun = false,
        CancellationToken ct = default);
}

/// <summary>What a refresh did to the index.</summary>
/// <param name="Files">Each file asked about, with what it held before and holds now.</param>
/// <param name="Indexed">How many symbols the index holds afterwards.</param>
/// <param name="Head">The commit the index is keyed by, or null outside a repository.</param>
/// <param name="Built">
/// Whether there was no index to patch and one was built instead. A refresh
/// that finds nothing to refresh still leaves the next lookup fast.
/// </param>
/// <param name="MapChanges">
/// The directories whose line on the map changed: a type added, removed or
/// renamed. Empty for an edit inside a method, which is most of them, so a
/// hook that reports these has nothing to say most of the time.
/// </param>
public sealed record SymbolRefresh(
    IReadOnlyList<SymbolRefreshedFile> Files,
    int Indexed,
    string? Head,
    bool Built,
    IReadOnlyList<SymbolMapChange>? MapChanges = null)
{
    /// <summary>The map lines that changed, never null.</summary>
    public IReadOnlyList<SymbolMapChange> Changes => MapChanges ?? [];
}

/// <summary>One directory's line on the map, before and after a refresh.</summary>
/// <param name="Directory">Repository-relative, or <c>(root)</c>.</param>
/// <param name="Before">Its line before, or null when the directory held no symbols.</param>
/// <param name="After">Its line now, or null when it holds none any more.</param>
public sealed record SymbolMapChange(string Directory, string? Before, string? After);

/// <summary>One file's entries before and after a refresh.</summary>
/// <param name="File">Repository-relative, forward-slashed.</param>
/// <param name="Before">How many symbols the index held for it.</param>
/// <param name="After">How many it holds now. Zero for a file that is gone or in no language read.</param>
public sealed record SymbolRefreshedFile(string File, int Before, int After);

/// <summary>A map of where the code is, small enough to inline.</summary>
/// <param name="Text">One Markdown list line per directory.</param>
/// <param name="Indexed">How many symbols the map was drawn from.</param>
/// <param name="Head">The commit the index was built at, or null outside a repository.</param>
/// <param name="Languages">The names of the languages the symbols came from, most frequent first.</param>
public sealed record SymbolDigest(
    string Text,
    int Indexed,
    string? Head,
    IReadOnlyList<string> Languages)
{
    /// <summary>
    /// Draws the map from a set of symbols.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The directory is the closest thing to a module a lexical scan can see,
    /// and in practice it is what people mean anyway: a namespace that does not
    /// match its folder is rare, and where it happens the folder is still where
    /// somebody would go looking.
    /// </para>
    /// <para>
    /// Nested types repeat: every command in this codebase carries its own
    /// Settings, so a raw list reads "Settings, ThingCommand, Settings,
    /// OtherCommand, Settings". The repetition says nothing and costs the
    /// tokens this digest exists to save, so names are listed once, and only
    /// the first dozen: past that a line stops being a map and starts being
    /// the index it is meant to save reading.
    /// </para>
    /// </remarks>
    public static string Modules(IReadOnlyList<Symbol> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        var text = new System.Text.StringBuilder();

        foreach (var module in ByModule(symbols))
        {
            text.AppendLine(Line(module.Key, [.. module]));
        }

        return text.ToString();
    }

    /// <summary>One directory's line on the map.</summary>
    public static string Line(string directory, IReadOnlyList<Symbol> symbols)
    {
        const int MostNames = 12;

        var names = symbols
            .Where(symbol => symbol.Kind == SymbolKind.Type)
            .Select(symbol => symbol.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return $"- `{directory}` — {names.Count} type(s): "
            + string.Join(", ", names.Take(MostNames))
            + (names.Count > MostNames ? ", and more" : string.Empty);
    }

    /// <summary>The directory a repository-relative file sits in, or <c>(root)</c>.</summary>
    public static string DirectoryOf(string file)
    {
        var directory = Path.GetDirectoryName(file)?.Replace('\\', '/');

        return directory is { Length: > 0 } ? directory : "(root)";
    }

    /// <summary>Symbols grouped by the directory that holds them, in path order.</summary>
    public static IEnumerable<IGrouping<string, Symbol>> ByModule(IReadOnlyList<Symbol> symbols) =>
        symbols
            .GroupBy(symbol => DirectoryOf(symbol.File), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

    /// <summary>The languages present, by how much of the code is in each.</summary>
    public static IReadOnlyList<string> LanguagesOf(IReadOnlyList<Symbol> symbols) =>
    [
        .. symbols
            .GroupBy(symbol => symbol.Language, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => SymbolLanguages.All.FirstOrDefault(l => l.Id == group.Key)?.Name)
            .Where(name => name is not null)
            .Select(name => name!),
    ];
}

/// <summary>
/// Ranks symbols against a name.
/// </summary>
/// <remarks>
/// <para>
/// Names rather than meanings, and said so. A lookup here answers "where is
/// <c>PreflightService</c>", which is the question an agent asks a dozen times
/// a session and otherwise answers with a search across the tree. It is not a
/// search for what a thing does; the memory index is for that.
/// </para>
/// <para>
/// A whole-name match comes first, then a name the query begins, then one it
/// merely appears in. Types outrank members within a rank, because a member
/// called <c>Scan</c> exists in twenty places and the type is what somebody
/// meant when the query is ambiguous. Case is ignored: a lookup typed from
/// memory gets the casing wrong as often as right, and nothing in a codebase
/// distinguishes two names by case alone.
/// </para>
/// </remarks>
public static class SymbolSearch
{
    /// <summary>The symbols matching a name, best first, at most <paramref name="limit"/>.</summary>
    public static IReadOnlyList<SymbolMatch> Find(
        IReadOnlyList<Symbol> symbols,
        string query,
        int limit = 10)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        var name = query?.Trim() ?? string.Empty;

        if (name.Length == 0 || limit <= 0)
        {
            return [];
        }

        return
        [
            .. symbols
                .Select(symbol => (Symbol: symbol, Rank: Rank(symbol.Name, name)))
                .Where(entry => entry.Rank >= 0)
                .OrderBy(entry => entry.Rank)
                .ThenBy(entry => entry.Symbol.Kind == SymbolKind.Type ? 0 : 1)
                .ThenBy(entry => entry.Symbol.File, StringComparer.Ordinal)
                .ThenBy(entry => entry.Symbol.Line)
                .Take(limit)
                .Select(entry => new SymbolMatch(entry.Symbol, entry.Rank == 0)),
        ];
    }

    /// <summary>0 for a whole match, 1 for a prefix, 2 for a fragment, -1 for none.</summary>
    private static int Rank(string candidate, string query)
    {
        if (candidate.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (candidate.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return candidate.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2 : -1;
    }
}

/// <summary>
/// Keeps one scanned index per project, keyed by the commit it was built at.
/// </summary>
/// <remarks>
/// <para>
/// Under the cache root and nowhere else. This is derived from the tree and
/// true only of one checkout at one commit, which is everything memory is not:
/// memory travels with the workspace and is audited for facts that rot, and an
/// index that changes on every commit would fail that audit on the day it was
/// written. A cache can be deleted at any moment and the only cost is one
/// scan.
/// </para>
/// <para>
/// A file that cannot be read, or was written for another commit, reads as no
/// cache. Both mean the same thing to the caller — scan again — and telling
/// them apart would be a distinction nothing acts on.
/// </para>
/// </remarks>
public sealed class SymbolIndexCache
{
    /// <summary>
    /// The shape of the file. Bumped when what a scan finds, or how it is
    /// stored, changes.
    /// </summary>
    /// <remarks>
    /// The commit keys the index to a tree; this keys it to a scanner. An
    /// upgraded launcher that reads another language, or fixes a pattern,
    /// would otherwise keep answering from the old scan until the next commit
    /// happened to move.
    /// </remarks>
    internal const int Format = 2;

    private readonly string _root;

    /// <param name="cacheRoot">The machine's cache directory. The index sits in a folder under it.</param>
    public SymbolIndexCache(string cacheRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);

        _root = Path.Combine(cacheRoot, "symbols");
    }

    /// <summary>Where a project's index for one working tree lives.</summary>
    /// <remarks>
    /// One file per tree rather than per project, because a linked worktree
    /// and the primary checkout are the same project at different commits,
    /// and one file between them would be rebuilt on every other lookup.
    /// </remarks>
    public string PathFor(string slug, string repositoryPath) =>
        Path.Combine(_root, slug + "-" + TreeKey(repositoryPath) + ".json");

    /// <summary>The index built at this commit, or null when there is none.</summary>
    public async Task<IReadOnlyList<Symbol>?> ReadAsync(
        string slug,
        string repositoryPath,
        string head,
        CancellationToken ct = default)
    {
        var path = PathFor(slug, repositoryPath);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);

            var stored = await JsonSerializer
                .DeserializeAsync(stream, SymbolIndexJson.Default.Stored, ct)
                .ConfigureAwait(false);

            if (stored is not { Symbols: not null }
                || stored.Format != Format
                || !string.Equals(stored.Head, head, StringComparison.Ordinal))
            {
                return null;
            }

            return
            [
                .. stored.Symbols.Select(entry => new Symbol(
                    entry.Kind, entry.Name, string.Empty, entry.File, entry.Line,
                    entry.Summary ?? string.Empty, string.Empty, entry.Language ?? string.Empty)),
            ];
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Records the index built at this commit, replacing whatever was there.</summary>
    /// <remarks>
    /// Written beside under a name of its own and moved into place, so a
    /// write interrupted half-way leaves the previous index rather than a
    /// torn file, and two writers at once do not fight over one staging
    /// file. A torn file would read as no cache and cost a scan, which is
    /// survivable; this is cheaper.
    /// </remarks>
    public async Task WriteAsync(
        string slug,
        string repositoryPath,
        string head,
        IReadOnlyList<Symbol> symbols,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        var path = PathFor(slug, repositoryPath);
        var staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            Directory.CreateDirectory(_root);

            // Only what a lookup shows or ranks on. The signature and the
            // remarks paragraph are for the exported documents, which scan
            // afresh; carrying them here made the file a third larger and
            // every read that much slower for nothing anybody read.
            var stored = new Stored(
                Format,
                head,
                [.. symbols.Select(symbol => new Entry(
                    symbol.Kind, symbol.Name, symbol.File, symbol.Line,
                    symbol.Summary.Length > 0 ? symbol.Summary : null,
                    symbol.Language.Length > 0 ? symbol.Language : null))]);

            await using (var stream = File.Create(staging))
            {
                await JsonSerializer
                    .SerializeAsync(stream, stored, SymbolIndexJson.Default.Stored, ct)
                    .ConfigureAwait(false);
            }

            File.Move(staging, path, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // A cache that cannot be written is a cache that is not there. The
            // lookup already has its answer; the next one pays for a scan.
            TryDelete(staging);
        }
    }

    /// <summary>
    /// Holds the index for one tree against other processes until disposed.
    /// </summary>
    /// <remarks>
    /// An agent's parallel edits run their hooks in parallel, and two
    /// read-patch-write cycles interleaved lose one edit's patch. The lock
    /// is a file opened exclusively, which every platform honours across
    /// processes, waited for briefly rather than forever: a hook that cannot
    /// get in within a couple of seconds gives up on the refresh, which costs
    /// the next lookup a rescan and nothing more.
    /// </remarks>
    public async Task<IDisposable?> LockAsync(
        string slug,
        string repositoryPath,
        CancellationToken ct = default)
    {
        var path = PathFor(slug, repositoryPath) + ".lock";
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);

        Directory.CreateDirectory(_root);

        while (true)
        {
            try
            {
                return new FileStream(
                    path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    bufferSize: 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// A short, stable name for a working tree, so two trees of one project
    /// keep separate files.
    /// </summary>
    private static string TreeKey(string repositoryPath)
    {
        var full = Path.GetFullPath(repositoryPath).TrimEnd(Path.DirectorySeparatorChar);

        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            full = full.ToLowerInvariant();
        }

        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(full));

        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Left behind is untidy and harmless.
        }
    }

    /// <summary>The file's shape. Internal so the serializer can be generated for it.</summary>
    internal sealed record Stored(int Format, string Head, List<Entry> Symbols);

    /// <summary>One symbol as stored: the fields a lookup uses and no others.</summary>
    internal sealed record Entry(
        [property: JsonConverter(typeof(JsonStringEnumConverter<SymbolKind>))] SymbolKind Kind,
        string Name,
        string File,
        int Line,
        string? Summary,
        string? Language);
}

/// <summary>
/// The serializer for the cache, generated at build time.
/// </summary>
/// <remarks>
/// Generated rather than reflected because this runs in a process that starts
/// to answer one question and exits: the reflection-based serializer spends
/// its first call building what the generator has already built, and that
/// first call is the only call.
/// </remarks>
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(SymbolIndexCache.Stored))]
internal sealed partial class SymbolIndexJson : JsonSerializerContext
{
}

/// <summary>
/// Answers "where is this declared" from an index kept in step with the tree.
/// </summary>
/// <remarks>
/// <para>
/// The scan behind this reads every C# file in the repository, which is fast
/// enough to do on demand and slow enough to be worth not doing on every
/// question. So the result is cached against the commit it was built at, and
/// thrown away when the commit moves.
/// </para>
/// <para>
/// A commit is a coarse key: the tree an agent is looking at has its own
/// uncommitted edits, and a line number from the cache is wrong the moment a
/// method above it grows. Two rules keep the answer honest anyway. Every file
/// a cached hit names is read again before it is reported, so the line is the
/// line now. And a cached miss is checked against a fresh scan before it is
/// reported as a miss, because the one thing a cache cannot know about is a
/// file added since. The cache can make a hit faster; it cannot make an answer
/// wrong.
/// </para>
/// </remarks>
public sealed class SymbolIndexService : ISymbolIndexService
{
    private readonly Func<string, CancellationToken, Task<string?>> _head;
    private readonly SymbolIndexCache _cache;

    /// <param name="git">Where the commit comes from.</param>
    /// <param name="cacheRoot">The machine's cache directory.</param>
    public SymbolIndexService(IGitManager git, string cacheRoot)
        : this(HeadFrom(git), new SymbolIndexCache(cacheRoot))
    {
    }

    /// <summary>For tests, which have no git to ask.</summary>
    /// <param name="fallback">Answers when the repository's own files cannot.</param>
    /// <param name="cache">Where the index is kept.</param>
    internal SymbolIndexService(
        Func<string, CancellationToken, Task<string?>> fallback,
        SymbolIndexCache cache)
    {
        // The file first, the fallback only when the file cannot be read with
        // confidence. Git costs a process; the file costs a read, and a lookup
        // that runs after every edit pays whichever this picks.
        _head = async (path, ct) => GitHead.Read(path) ?? await fallback(path, ct).ConfigureAwait(false);
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<OperationResult<SymbolLookup>> FindAsync(
        string repositoryPath,
        string slug,
        string query,
        int limit = 10,
        bool rescan = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return OperationResult<SymbolLookup>.Fail(
                "Say what name to look for.", Models.ExitCode.InvalidArguments);
        }

        if (!Directory.Exists(repositoryPath))
        {
            return OperationResult<SymbolLookup>.Fail(
                $"'{repositoryPath}' is not on this machine, so there is nothing to look in.",
                Models.ExitCode.RepositoryUnavailable);
        }

        var head = await _head(repositoryPath, ct).ConfigureAwait(false);

        // No commit means no key to cache under. Outside a repository every
        // lookup is a scan, which is correct and merely slower.
        var symbols = head is not null && !rescan
            ? await _cache.ReadAsync(slug, repositoryPath, head, ct).ConfigureAwait(false)
            : null;

        var fromCache = symbols is not null;
        var rebuilt = false;

        if (symbols is null)
        {
            symbols = await ScanAsync(repositoryPath, slug, head, ct).ConfigureAwait(false);
        }

        var matches = SymbolSearch.Find(symbols, query, limit);

        if (fromCache)
        {
            var hadExact = matches.Any(match => match.Exact);

            matches = Refresh(matches, repositoryPath, query, limit);

            // A miss, or an exact answer that the files no longer bear out. The
            // second is the type moved to a new file this session: the old
            // file no longer declares it, a fragment elsewhere still matches,
            // and answering with the fragment would report the type gone.
            if (matches.Count == 0 || (hadExact && !matches.Any(match => match.Exact)))
            {
                symbols = await ScanAsync(repositoryPath, slug, head, ct).ConfigureAwait(false);
                matches = SymbolSearch.Find(symbols, query, limit);
                rebuilt = true;
            }
        }

        return OperationResult<SymbolLookup>.Ok(
            new SymbolLookup(matches, symbols.Count, head, fromCache, rebuilt));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Drawn from the cached index when there is one for this commit, and
    /// otherwise from a scan that is then cached, so a launch that inlines the
    /// map and a lookup a minute later share one scan between them. Unlike a
    /// lookup, nothing is re-read: a directory's list of types moves far less
    /// than a line number, and a map is read for its shape rather than its
    /// precision.
    /// </remarks>
    public async Task<OperationResult<SymbolDigest>> DigestAsync(
        string repositoryPath,
        string slug,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(repositoryPath))
        {
            return OperationResult<SymbolDigest>.Fail(
                $"'{repositoryPath}' is not on this machine, so there is nothing to map.",
                Models.ExitCode.RepositoryUnavailable);
        }

        var head = await _head(repositoryPath, ct).ConfigureAwait(false);

        var symbols = head is not null
            ? await _cache.ReadAsync(slug, repositoryPath, head, ct).ConfigureAwait(false)
            : null;

        symbols ??= await ScanAsync(repositoryPath, slug, head, ct).ConfigureAwait(false);

        return OperationResult<SymbolDigest>.Ok(new SymbolDigest(
            SymbolDigest.Modules(symbols),
            symbols.Count,
            head,
            SymbolDigest.LanguagesOf(symbols)));
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Made for a hook that fires after every edit, so it has to cost what one
    /// file costs: the cached index is read, the entries for the named files
    /// are replaced with what those files hold now, and the index is written
    /// back under the same commit. A file that is gone, or in no language the
    /// scan reads, ends with no entries, which is the truth about it.
    /// </para>
    /// <para>
    /// With no index for this commit there is nothing to patch, so one is
    /// built. That is one full scan on the first edit of a session and a
    /// file's worth on every edit after, which is the right way round: the
    /// index is warm by the time the agent asks.
    /// </para>
    /// </remarks>
    public async Task<OperationResult<SymbolRefresh>> RefreshAsync(
        string repositoryPath,
        string slug,
        IReadOnlyList<string> files,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (!Directory.Exists(repositoryPath))
        {
            return OperationResult<SymbolRefresh>.Fail(
                $"'{repositoryPath}' is not on this machine, so there is nothing to refresh.",
                Models.ExitCode.RepositoryUnavailable);
        }

        var root = Path.GetFullPath(repositoryPath);
        var relatives = new List<string>();

        foreach (var file in files)
        {
            var full = Path.GetFullPath(Path.IsPathRooted(file) ? file : Path.Combine(root, file));
            var relative = Path.GetRelativePath(root, full).Replace('\\', '/');

            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                return OperationResult<SymbolRefresh>.Fail(
                    $"'{file}' is outside the repository, so it has no place in its index.",
                    Models.ExitCode.InvalidArguments);
            }

            relatives.Add(Canonical(root, relative));
        }

        var head = await _head(repositoryPath, ct).ConfigureAwait(false);

        if (head is null)
        {
            // Outside a repository every lookup is a scan, and there is no
            // index to keep current.
            return OperationResult<SymbolRefresh>.Ok(new SymbolRefresh([], 0, null, false));
        }

        // Held across the read and the write. An agent's parallel edits run
        // their hooks in parallel, and two patches interleaved lose one.
        using var held = dryRun ? null : await _cache.LockAsync(slug, repositoryPath, ct).ConfigureAwait(false);

        if (!dryRun && held is null)
        {
            return OperationResult<SymbolRefresh>.Fail(
                "The index is being written by another process and did not free up in time; "
                + "the next lookup will read the tree instead.");
        }

        var cached = await _cache.ReadAsync(slug, repositoryPath, head, ct).ConfigureAwait(false);

        if (cached is null)
        {
            var built = dryRun
                ? SymbolScan.Scan(repositoryPath, ct)
                : await ScanAsync(repositoryPath, slug, head, ct).ConfigureAwait(false);

            return OperationResult<SymbolRefresh>.Ok(new SymbolRefresh(
                [.. relatives.Select(relative => new SymbolRefreshedFile(
                    relative, 0, built.Count(symbol => symbol.File == relative)))],
                built.Count,
                head,
                Built: true));
        }

        var kept = cached.Where(symbol => !relatives.Contains(symbol.File, StringComparer.Ordinal)).ToList();
        var refreshed = new List<SymbolRefreshedFile>();

        foreach (var relative in relatives.Distinct(StringComparer.Ordinal))
        {
            var before = cached.Count(symbol => symbol.File == relative);
            var now = Current(root, relative);

            kept.AddRange(now);
            refreshed.Add(new SymbolRefreshedFile(relative, before, now.Count));
        }

        var symbols = kept
            .OrderBy(symbol => symbol.File, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Line)
            .ToList();

        if (!dryRun)
        {
            await _cache.WriteAsync(slug, repositoryPath, head, symbols, ct).ConfigureAwait(false);
        }

        return OperationResult<SymbolRefresh>.Ok(new SymbolRefresh(
            refreshed, symbols.Count, head, Built: false, MapChanged(cached, symbols, relatives)));
    }

    /// <summary>
    /// The map lines that read differently now, for the directories touched.
    /// </summary>
    /// <remarks>
    /// Only the directories of the files refreshed are compared, because
    /// nothing else can have moved. A line is its directory's entry in the
    /// digest, so the comparison is exactly what a session would see change
    /// on its map — a type added, removed or renamed — and nothing that it
    /// would not.
    /// </remarks>
    private static IReadOnlyList<SymbolMapChange> MapChanged(
        IReadOnlyList<Symbol> before,
        IReadOnlyList<Symbol> after,
        IReadOnlyList<string> files)
    {
        var directories = files
            .Select(SymbolDigest.DirectoryOf)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        var was = Lines(before);
        var now = Lines(after);
        var changed = new List<SymbolMapChange>();

        foreach (var directory in directories)
        {
            was.TryGetValue(directory, out var old);
            now.TryGetValue(directory, out var line);

            if (!string.Equals(old, line, StringComparison.Ordinal))
            {
                changed.Add(new SymbolMapChange(directory, old, line));
            }
        }

        return changed;
    }

    private static Dictionary<string, string> Lines(IReadOnlyList<Symbol> symbols) =>
        SymbolDigest.ByModule(symbols)
            .ToDictionary(
                module => module.Key,
                module => SymbolDigest.Line(module.Key, [.. module]),
                StringComparer.Ordinal);

    /// <summary>
    /// The path as the disk spells it.
    /// </summary>
    /// <remarks>
    /// The index stores paths as the directory listing gave them; a hook
    /// hands over the path as the agent typed it, which on Windows and macOS
    /// may differ in case and still be the same file. Matched by the typed
    /// spelling, the old entries would survive beside the new ones. Each
    /// segment is looked up in its directory, which is case-insensitive
    /// where the file system is, so the spelling that comes back is the one
    /// the scan would have used. A segment that does not exist — the file
    /// has been deleted — is kept as typed.
    /// </remarks>
    private static string Canonical(string root, string relative)
    {
        var current = root;
        var spelled = new List<string>();

        foreach (var segment in relative.Split('/'))
        {
            string? actual = null;

            try
            {
                actual = Directory.Exists(current)
                    ? Directory.EnumerateFileSystemEntries(current, segment)
                        .Select(Path.GetFileName)
                        .FirstOrDefault(name => string.Equals(name, segment, StringComparison.OrdinalIgnoreCase))
                    : null;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // Kept as typed.
            }

            spelled.Add(actual ?? segment);
            current = Path.Combine(current, actual ?? segment);
        }

        return string.Join('/', spelled);
    }

    /// <summary>What one file declares now, or nothing when it is gone or unreadable.</summary>
    private static List<Symbol> Current(string root, string relative)
    {
        var path = Path.Combine(root, relative);

        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return [.. SymbolScan.InFile(File.ReadAllLines(path), relative)];
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private async Task<IReadOnlyList<Symbol>> ScanAsync(
        string repositoryPath,
        string slug,
        string? head,
        CancellationToken ct)
    {
        var symbols = SymbolScan.Scan(repositoryPath, ct);

        if (head is not null)
        {
            await _cache.WriteAsync(slug, repositoryPath, head, symbols, ct).ConfigureAwait(false);
        }

        return symbols;
    }

    /// <summary>
    /// The same matches, as the files declaring them stand now.
    /// </summary>
    /// <remarks>
    /// Re-reads only the files the cached answer named. That is a handful of
    /// files against a scan of thousands, and it is where the cache goes wrong
    /// in practice: the agent is editing exactly the files it asks about. A
    /// file that is gone contributes nothing; a symbol that has moved within
    /// its file is reported at its new line; one renamed away is dropped.
    /// </remarks>
    private static IReadOnlyList<SymbolMatch> Refresh(
        IReadOnlyList<SymbolMatch> matches,
        string repositoryPath,
        string query,
        int limit)
    {
        var current = new List<Symbol>();

        foreach (var file in matches.Select(match => match.Symbol.File).Distinct(StringComparer.Ordinal))
        {
            var path = Path.Combine(repositoryPath, file);

            if (!File.Exists(path))
            {
                continue;
            }

            string[] lines;

            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            current.AddRange(SymbolScan.InFile(lines, file));
        }

        return SymbolSearch.Find(current, query, limit);
    }

    private static Func<string, CancellationToken, Task<string?>> HeadFrom(IGitManager git)
    {
        ArgumentNullException.ThrowIfNull(git);

        return async (path, ct) =>
        {
            var state = await git.GetStateAsync(path, ct).ConfigureAwait(false);

            return state.Succeeded ? state.Value!.HeadCommit : null;
        };
    }
}
