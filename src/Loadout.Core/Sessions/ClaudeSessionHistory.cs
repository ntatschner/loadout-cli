using System.Text.Json;
using Loadout.Core.Diagnostics;
using Loadout.Core.Transcripts;
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
internal sealed class ClaudeSessionHistory : ISessionHistory
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

    /// <summary>
    /// How much of the start of a transcript stands for its head when deciding
    /// whether a file that has grown is the same file with more on the end.
    /// </summary>
    private const int StartBytes = 4096;

    /// <summary>How long a fallback summary may be, so a list stays one line per session.</summary>
    private const int SummaryLength = 60;

    /// <summary>
    /// Each transcript's session, kept while the file is unchanged.
    /// </summary>
    /// <remarks>
    /// A list of twelve reads the newest hundred and twenty transcripts, and a
    /// search five hundred: a head and a tail of each, every time, for files
    /// nearly all of which have not changed since the last list. Static for
    /// the reason <see cref="Usage.ClaudeUsageHistory"/>'s readings are.
    /// Raise the version whenever <see cref="ReadAsync"/> or what it keeps
    /// changes.
    /// </remarks>
    private static readonly TranscriptReadings<Reading> Readings = new("claude-sessions", version: 2);

    private readonly IEnvironmentProvider _environment;
    private readonly string? _kept;

    /// <param name="environment">Where the agent's history is.</param>
    /// <param name="paths">Where readings are kept between runs; none keeps them only in memory.</param>
    public ClaudeSessionHistory(IEnvironmentProvider environment, IPlatformPaths? paths = null)
    {
        _environment = environment;
        _kept = paths is null ? null : TranscriptReadingsDirectory.Under(paths.Paths.State);
    }

    /// <summary>Drops the readings held in memory, as a new process starts with none.</summary>
    internal static void Forget() => Readings.Forget();

    /// <inheritdoc />
    public string Agent => "claude";

    /// <inheritdoc />
    public bool IsAvailable => Directory.Exists(Root);

    private string Root => Agents.AgentHome.ClaudeProjects(_environment);

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
            // One level down and no further. A session's transcript sits
            // directly in its project's folder; anything deeper belongs to the
            // subfolders Claude keeps beside it, and those are not sessions.
            files = new DirectoryInfo(Root)
                .EnumerateDirectories()
                .SelectMany(project => project.EnumerateFiles("*.jsonl", SearchOption.TopDirectoryOnly))
                .Where(file => !IsSubagent(file.Name))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<IReadOnlyList<AgentSession>>.Fail(
                $"Could not read Claude's session history at {Root}: {ex.Message}");
        }

        var sessions = new List<AgentSession>(Math.Min(limit, files.Count));

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            // The limit counts sessions, not files. Applied before the read it
            // was spent on whatever happened to be newest, which after an
            // afternoon of subagents was nothing resumable at all.
            if (sessions.Count >= limit)
            {
                break;
            }

            var reading = await Readings.GetAsync(file, _kept, ReadingAsync, ct).ConfigureAwait(false);

            if (reading?.Session is { } session)
            {
                sessions.Add(session);
            }
        }

        Readings.Keep([.. files.Select(file => file.FullName)], _kept);

        return OperationResult<IReadOnlyList<AgentSession>>.Ok(sessions);
    }

    /// <summary>
    /// Whether a transcript belongs to a subagent rather than to a session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Belt and braces beside the directory rule above, and worth the few
    /// lines: the layout has already moved once. Claude grew a
    /// <c>&lt;session-id&gt;/subagents/</c> folder per session, and because the
    /// listing recursed, thirty-nine subagent transcripts joined thirteen real
    /// sessions in one project — twenty were shown and eighteen of those could
    /// not be resumed, because 'agent-a2feab3bbb1dc3a34' is not a session id.
    /// </para>
    /// <para>
    /// Nothing inside the file says which it is: a subagent transcript has a
    /// working directory and a first prompt like any other. The name is the
    /// only signal, so the name is what is read.
    /// </para>
    /// </remarks>
    private static bool IsSubagent(string fileName) =>
        fileName.StartsWith("agent-", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What one transcript says about its session, or null when it could not
    /// be read and should be tried again.
    /// </summary>
    /// <remarks>
    /// A live session's transcript changes with every turn, and its first four
    /// hundred lines, megabytes of them in a long session, were parsed again
    /// each time to find what they had already said. A transcript only ever
    /// grows, so when the file is longer than when its head was read and
    /// starts the same way, the head is taken from the last reading and only
    /// the end is read, for a newer title. Anything else is read in full.
    /// </remarks>
    private static async Task<Reading?> ReadingAsync(FileInfo file, Reading? previous, CancellationToken ct)
    {
        if (previous?.Head is { } head
            && file.Length >= head.Length
            && await StartsAsAsync(file, head.Start, ct).ConfigureAwait(false))
        {
            var title = await ReadTitleFromEndAsync(file, ct).ConfigureAwait(false) ?? head.Title;

            return new Reading(Session(file, head.Directory, head.Branch, title, head.FirstPrompt), head);
        }

        return await ReadAsync(file, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds one session from its transcript, with no session when the file
    /// does not hold enough to resume from, or null when it could not be read.
    /// </summary>
    private static async Task<Reading?> ReadAsync(FileInfo file, CancellationToken ct)
    {
        var id = Path.GetFileNameWithoutExtension(file.Name);

        if (string.IsNullOrWhiteSpace(id))
        {
            return new Reading(null);
        }

        string? directory = null;
        string? branch = null;
        string? title = null;
        string? firstPrompt = null;

        // Whether the head reached the end of the file, which decides if there
        // is any point looking at the tail.
        var complete = false;

        // Taken before the head is read, so a transcript that grows during the
        // read is never thought to have started longer than it did.
        var length = file.Length;
        string start;

        try
        {
            start = await StartOfAsync(file, ct).ConfigureAwait(false);

            using var trace = ReadLedger.Begin(file.FullName);
            using var stream = file.OpenRead();
            using var reader = new StreamReader(stream);

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

            trace?.Read(stream.Position);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }

        if (directory is null)
        {
            // Without a working directory the session cannot be attributed to a
            // project, and resuming it would start somewhere unexpected.
            return new Reading(null);
        }

        if (complete)
        {
            // A head that reached the end is the whole file, and the next
            // version is read in full: there is nothing to save by keeping it.
            return new Reading(Session(file, directory, branch, title, firstPrompt));
        }

        var head = new Head(directory, branch, title, firstPrompt, length, start);

        // A long transcript may have been renamed past the point the head
        // reached, and the newest name is the one that describes it.
        title = await ReadTitleFromEndAsync(file, ct).ConfigureAwait(false) ?? title;

        return new Reading(Session(file, directory, branch, title, firstPrompt), head);
    }

    private static AgentSession Session(FileInfo file, string directory, string? branch, string? title, string? firstPrompt) =>
        new(
            "claude",
            Path.GetFileNameWithoutExtension(file.Name),
            title ?? Summarise(firstPrompt),
            directory,
            branch is "HEAD" ? null : branch,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
            file.FullName);

    /// <summary>Whether a transcript still starts as it did, and false when it cannot be opened, so it is read in full.</summary>
    private static async Task<bool> StartsAsAsync(FileInfo file, string start, CancellationToken ct)
    {
        try
        {
            return await StartOfAsync(file, ct).ConfigureAwait(false) == start;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A hash of the start of a transcript, which stays the same while the file only grows.</summary>
    private static async Task<string> StartOfAsync(FileInfo file, CancellationToken ct)
    {
        using var trace = ReadLedger.Begin(file.FullName);
        await using var stream = file.OpenRead();

        var buffer = new byte[StartBytes];
        var read = await stream.ReadAtLeastAsync(buffer, StartBytes, throwOnEndOfStream: false, ct).ConfigureAwait(false);

        trace?.Read(read);

        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(buffer.AsSpan(0, read)));
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
            using var trace = ReadLedger.Begin(file.FullName);
            using var stream = file.OpenRead();

            if (stream.Length > TailBytes)
            {
                stream.Seek(-TailBytes, SeekOrigin.End);
            }

            trace?.Read(stream.Length - stream.Position);

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

    /// <summary>One transcript's session, or none when it holds too little to resume from.</summary>
    /// <remarks>
    /// Wrapped, because a transcript with nothing to resume is an answer worth
    /// keeping too, and a reading of nothing would be read again every time.
    /// </remarks>
    internal sealed record Reading(AgentSession? Session, Head? Head = null);

    /// <summary>
    /// What the first lines of a long transcript said, kept so that the next
    /// version of the file, the same with more on the end, need not be parsed
    /// from the start again.
    /// </summary>
    /// <param name="Directory">The working directory the session ran in.</param>
    /// <param name="Branch">The branch, as recorded.</param>
    /// <param name="Title">The newest title the head held.</param>
    /// <param name="FirstPrompt">The first thing a person typed.</param>
    /// <param name="Length">How long the file was when its head was read.</param>
    /// <param name="Start">A hash of its first bytes then.</param>
    internal sealed record Head(string Directory, string? Branch, string? Title, string? FirstPrompt, long Length, string Start);
}
