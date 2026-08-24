using System.Text.Json;
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
public sealed class CodexSessionHistory : ISessionHistory
{
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
    private async Task<Dictionary<string, string>> ReadIndexAsync(CancellationToken ct)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(IndexPath))
        {
            return names;
        }

        try
        {
            using var reader = new StreamReader(IndexPath);

            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;

                    if (root.ValueKind != JsonValueKind.Object
                        || !root.TryGetProperty("id", out var id)
                        || id.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    if (root.TryGetProperty("thread_name", out var name)
                        && name.ValueKind == JsonValueKind.String
                        && name.GetString() is { Length: > 0 } text)
                    {
                        names[id.GetString()!] = text;
                    }
                }
                catch (JsonException)
                {
                    // One bad line costs one name, not the index.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No names; the listing falls back to directories.
        }

        return names;
    }

    /// <summary>Reads the metadata entry that opens a rollout file.</summary>
    private static async Task<AgentSession?> ReadAsync(
        FileInfo file,
        Dictionary<string, string> names,
        CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(file.OpenRead());

            // The metadata is the first entry. Reading further would mean
            // parsing the conversation to build a menu.
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
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

            return new AgentSession(
                "codex",
                sessionId,
                names.GetValueOrDefault(sessionId),
                directory,
                Branch: null,
                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
                file.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
