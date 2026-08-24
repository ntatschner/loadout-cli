using System.Text.Json;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Sessions;

/// <summary>
/// Reads Claude Code's own conversation transcripts.
/// <para>
/// Claude keeps one directory per working directory under
/// <c>~/.claude/projects</c>, and one JSON-lines file per session named after
/// the session identifier. The directory name is the working directory with its
/// separators replaced by dashes, which is lossy — a folder with a dash in its
/// name is indistinguishable from a nested one — so the working directory is
/// read out of the transcript rather than decoded from the directory name.
/// </para>
/// <para>
/// None of this is a published format. It is read defensively: a line that does
/// not parse is skipped, a file that cannot be opened is skipped, and the
/// listing still returns everything else.
/// </para>
/// </summary>
public sealed class ClaudeSessionHistory : ISessionHistory
{
    /// <summary>
    /// How far into a transcript to look for the metadata. The working
    /// directory appears on the first real entry; reading whole files would
    /// mean parsing megabytes to build a menu.
    /// </summary>
    private const int HeadLines = 400;

    /// <summary>
    /// How much of the end to read for the title. Claude names a session after
    /// the conversation has started and may rename it later, so the newest
    /// title is at the end rather than the beginning.
    /// </summary>
    private const int TailBytes = 128 * 1024;

    /// <summary>How long a fallback summary may be, so a list stays one line per session.</summary>
    private const int SummaryLength = 60;

    private readonly IEnvironmentProvider _environment;

    public ClaudeSessionHistory(IEnvironmentProvider environment) => _environment = environment;

    /// <inheritdoc />
    public string Agent => "claude";

    /// <inheritdoc />
    public bool IsAvailable => Directory.Exists(Root);

    private string Root => Path.Combine(_environment.HomeDirectory, ".claude", "projects");

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
            files = new DirectoryInfo(Root)
                .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(limit)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<IReadOnlyList<AgentSession>>.Fail(
                $"Could not read Claude's session history at {Root}: {ex.Message}");
        }

        var sessions = new List<AgentSession>(files.Count);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var session = await ReadAsync(file, ct).ConfigureAwait(false);

            if (session is not null)
            {
                sessions.Add(session);
            }
        }

        return OperationResult<IReadOnlyList<AgentSession>>.Ok(sessions);
    }

    /// <summary>
    /// Builds one session from its transcript, or null when the file does not
    /// hold enough to resume from.
    /// </summary>
    private static async Task<AgentSession?> ReadAsync(FileInfo file, CancellationToken ct)
    {
        var id = Path.GetFileNameWithoutExtension(file.Name);

        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        string? directory = null;
        string? branch = null;
        string? title = null;
        string? firstPrompt = null;

        // Whether the head reached the end of the file, which decides if there
        // is any point looking at the tail.
        var complete = false;

        try
        {
            using var reader = new StreamReader(file.OpenRead());

            for (var i = 0; i < HeadLines; i++)
            {
                ct.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);

                if (line is null)
                {
                    complete = true;
                    break;
                }

                // Read the whole head rather than stopping at the first title.
                // Claude renames a session as the conversation turns out to be
                // about something else, and stopping early pins the name to
                // its first guess.
                Absorb(line, ref directory, ref branch, ref title, ref firstPrompt);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }

        if (directory is null)
        {
            // Without a working directory the session cannot be attributed to a
            // project, and resuming it would start somewhere unexpected.
            return null;
        }

        if (!complete)
        {
            // A long transcript may have been renamed past the point the head
            // reached, and the newest name is the one that describes it.
            title = await ReadTitleFromEndAsync(file, ct).ConfigureAwait(false) ?? title;
        }

        return new AgentSession(
            "claude",
            id,
            title ?? Summarise(firstPrompt),
            directory,
            branch is "HEAD" ? null : branch,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
            file.FullName);
    }

    /// <summary>Takes whatever a transcript line has to offer, ignoring the rest.</summary>
    private static void Absorb(
        string line,
        ref string? directory,
        ref string? branch,
        ref string? title,
        ref string? firstPrompt)
    {
        JsonElement root;

        try
        {
            using var document = JsonDocument.Parse(line);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (directory is null
            && root.TryGetProperty("cwd", out var cwd)
            && cwd.ValueKind == JsonValueKind.String)
        {
            directory = cwd.GetString();
        }

        if (branch is null
            && root.TryGetProperty("gitBranch", out var gitBranch)
            && gitBranch.ValueKind == JsonValueKind.String)
        {
            branch = gitBranch.GetString();
        }

        if (root.TryGetProperty("aiTitle", out var aiTitle)
            && aiTitle.ValueKind == JsonValueKind.String)
        {
            // Later titles replace earlier ones: Claude renames a session as
            // the conversation turns out to be about something else.
            title = aiTitle.GetString();
        }

        if (firstPrompt is null
            && root.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && type.GetString() == "user")
        {
            var text = TextOf(root);

            // Only what a person actually typed. The opening entries of a
            // transcript are often injected by the tooling — command output,
            // caveats, reminders — and naming a session after one of those
            // tells the person scanning the list nothing about it.
            if (LooksTyped(text))
            {
                firstPrompt = text;
            }
        }
    }

    /// <summary>
    /// Whether a user entry reads like something a person typed rather than
    /// something the tooling put there.
    /// </summary>
    private static bool LooksTyped(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.TrimStart();

        // Injected blocks are markup, and the caveat is a fixed preamble the
        // CLI adds ahead of the real conversation.
        return !trimmed.StartsWith('<')
            && !trimmed.StartsWith("Caveat:", StringComparison.Ordinal);
    }

    /// <summary>Pulls the text out of a user entry, whichever shape it took.</summary>
    private static string? TextOf(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object
                && block.TryGetProperty("type", out var kind)
                && kind.ValueKind == JsonValueKind.String
                && kind.GetString() == "text"
                && block.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String)
            {
                return text.GetString();
            }
        }

        return null;
    }

    /// <summary>
    /// Looks for a title near the end of a long transcript, where a rename
    /// would have been recorded.
    /// </summary>
    private static async Task<string?> ReadTitleFromEndAsync(FileInfo file, CancellationToken ct)
    {
        try
        {
            using var stream = file.OpenRead();

            if (stream.Length > TailBytes)
            {
                stream.Seek(-TailBytes, SeekOrigin.End);
            }

            using var reader = new StreamReader(stream);

            // The first line after seeking is almost certainly cut in half.
            if (stream.Position > 0)
            {
                await reader.ReadLineAsync(ct).ConfigureAwait(false);
            }

            string? title = null;
            string? ignored = null;

            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                Absorb(line, ref ignored, ref ignored, ref title, ref ignored);
            }

            return title;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Turns an opening prompt into something that fits in a menu. Used only
    /// when the agent recorded no title of its own.
    /// </summary>
    private static string? Summarise(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        var flattened = string.Join(
            ' ',
            prompt.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        // A pasted specification opens with a heading; the hash marks are noise
        // in a one-line summary.
        flattened = flattened.TrimStart('#', ' ');

        return flattened.Length <= SummaryLength
            ? flattened
            : flattened[..(SummaryLength - 1)] + "…";
    }
}
