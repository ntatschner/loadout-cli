using System.Collections.Concurrent;
using System.Text.Json;
using Loadout.Core.Diagnostics;
using Loadout.Core.Reading;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Sessions;

/// <summary>
/// Reads Codex's recorded sessions.
/// <para>
/// Codex writes one rollout file per session under
/// <c>~/.codex/sessions/YYYY/MM/DD/</c>, whose first line is a
/// <c>session_meta</c> entry carrying the identifier and the working directory.
/// Names live separately, in <c>session_index.jsonl</c>, so the two are read
/// together — the index for what to call a session, the rollout for where it
/// ran.
/// </para>
/// <para>
/// As with Claude, none of this is a published format, so every read is
/// best-effort and a file that cannot be understood is skipped.
/// </para>
/// </summary>
internal sealed class CodexSessionHistory : ISessionHistory
{
    /// <summary>
    /// What each rollout's first line said, by path, with the longest the file
    /// has been seen, for the process.
    /// </summary>
    /// <remarks>
    /// The first line of a rollout never changes once written, and every
    /// history request opened up to five hundred of them to read it again. A
    /// rollout only grows, so one found shorter than it was seen is taken to
    /// have been replaced and is read afresh. Creation time would say so more
    /// directly, but on Linux it often falls back to the last change, which
    /// every append moves. Only a line that parsed is kept: one still being
    /// written is read again next time.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, (long Length, Meta? Meta)> Metas = new(StringComparer.Ordinal);

    private readonly IEnvironmentProvider _environment;

    public CodexSessionHistory(IEnvironmentProvider environment) => _environment = environment;

    /// <inheritdoc />
    public string Agent => "codex";

    /// <inheritdoc />
    public bool IsAvailable => Directory.Exists(SessionsRoot);

    private string Home => Path.Combine(_environment.HomeDirectory, ".codex");

    private string SessionsRoot => Path.Combine(Home, "sessions");

    private string IndexPath => Path.Combine(Home, "session_index.jsonl");

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<AgentSession>>> ListAsync(
        int limit,
        CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            return OperationResult<IReadOnlyList<AgentSession>>.Ok([]);
        }

        List<FileInfo> files;

        try
        {
            files = new DirectoryInfo(SessionsRoot)
                .EnumerateFiles("rollout-*.jsonl", SearchOption.AllDirectories)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(limit)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<IReadOnlyList<AgentSession>>.Fail(
                $"Could not read Codex's session history at {SessionsRoot}: {ex.Message}");
        }

        var names = await ReadIndexAsync(ct).ConfigureAwait(false);

        var sessions = new List<AgentSession>(files.Count);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var session = await ReadAsync(file, names, ct).ConfigureAwait(false);

            if (session is not null)
            {
                sessions.Add(session);
            }
        }

        return OperationResult<IReadOnlyList<AgentSession>>.Ok(sessions);
    }

    /// <summary>
    /// Session names, keyed by identifier. Absent or unreadable simply means
    /// sessions are shown by their directory instead.
    /// </summary>
    /// <remarks>
    /// Read through one reader for the process, which parses only the lines
    /// appended since its last read; the map is built afresh each call, so a
    /// later name for a session still wins.
    /// </remarks>
    private async Task<Dictionary<string, string>> ReadIndexAsync(CancellationToken ct)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var line in await AppendOnlyFile<IndexName>.For(IndexPath, ParseIndexLine).ReadAsync(ct).ConfigureAwait(false) ?? [])
            {
                names[line.Id] = line.Name;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No names; the listing falls back to directories.
        }

        return names;
    }

    /// <summary>One index line's name for a session, or null when it gives none.</summary>
    private static IndexName? ParseIndexLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("id", out var id)
                || id.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return root.TryGetProperty("thread_name", out var name)
                && name.ValueKind == JsonValueKind.String
                && name.GetString() is { Length: > 0 } text
                    ? new IndexName(id.GetString()!, text)
                    : null;
        }
        catch (JsonException)
        {
            // One bad line costs one name, not the index.
            return null;
        }
    }

    /// <summary>Reads the metadata entry that opens a rollout file.</summary>
    /// <remarks>
    /// The name and the time are taken afresh on every call: the index can
    /// rename a session and every turn moves its time, while what the first
    /// line says does not change.
    /// </remarks>
    private static async Task<AgentSession?> ReadAsync(
        FileInfo file,
        Dictionary<string, string> names,
        CancellationToken ct)
    {
        var length = file.Length;
        Meta? meta;

        if (SharedReaders.Enabled && Metas.TryGetValue(file.FullName, out var kept) && length >= kept.Length)
        {
            meta = kept.Meta;
            Metas[file.FullName] = (length, meta);
        }
        else
        {
            try
            {
                meta = await ReadMetaAsync(file, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return null;
            }

            if (SharedReaders.Enabled)
            {
                Metas[file.FullName] = (length, meta);
            }
        }

        return meta is null
            ? null
            : new AgentSession(
                "codex",
                meta.SessionId,
                names.GetValueOrDefault(meta.SessionId),
                meta.Directory,
                Branch: null,
                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
                file.FullName);
    }

    /// <summary>
    /// What a rollout's first line says, or null when it is not a session's
    /// metadata. Throws when the line cannot be read or parsed, so a line still
    /// being written is not taken for an answer.
    /// </summary>
    private static async Task<Meta?> ReadMetaAsync(FileInfo file, CancellationToken ct)
    {
        using var trace = ReadLedger.Begin(file.FullName);
        using var stream = file.OpenRead();
        using var reader = new StreamReader(stream);

        // The metadata is the first entry. Reading further would mean
        // parsing the conversation to build a menu.
        var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);

        trace?.Read(stream.Position);

        if (string.IsNullOrWhiteSpace(line))
        {
            throw new JsonException("The first line is empty.");
        }

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("payload", out var payload)
            || payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!payload.TryGetProperty("session_id", out var id)
            || id.ValueKind != JsonValueKind.String
            || id.GetString() is not { Length: > 0 } sessionId)
        {
            return null;
        }

        if (!payload.TryGetProperty("cwd", out var cwd)
            || cwd.ValueKind != JsonValueKind.String
            || cwd.GetString() is not { Length: > 0 } directory)
        {
            return null;
        }

        return new Meta(sessionId, directory);
    }

    /// <summary>What a rollout's first line says about its session.</summary>
    private sealed record Meta(string SessionId, string Directory);

    /// <summary>A name the index gives a session.</summary>
    private sealed record IndexName(string Id, string Name);
}
