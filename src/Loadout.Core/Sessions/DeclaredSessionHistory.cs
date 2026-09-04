using System.Text.Json;
using Loadout.Core.Transcripts;
using Loadout.Models.Agents;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Sessions;

/// <summary>
/// Reads any agent's sessions from a description of its transcripts.
/// </summary>
/// <remarks>
/// <para>
/// The same job the compiled-in readers do, driven by configuration instead of
/// code. What varies between agents is a directory, a filename pattern and where
/// two or three values sit inside a JSON line; what does not vary is everything
/// around that — ordering by recency, stopping early, skipping what cannot be
/// understood, and never letting one bad file cost the listing.
/// </para>
/// <para>
/// Best-effort by construction, exactly as the compiled readers are. No
/// transcript format here is a published contract, and a description of one is a
/// guess about somebody else's file that is right until they change it. A file
/// that does not parse is skipped rather than reported: for building a menu that
/// is right, and counting has its own reader that says when it skipped something.
/// </para>
/// </remarks>
internal sealed class DeclaredSessionHistory : ISessionHistory
{
    private readonly IEnvironmentProvider _environment;
    private readonly TranscriptFormat _format;

    public DeclaredSessionHistory(
        string agent,
        TranscriptFormat format,
        IEnvironmentProvider environment)
    {
        Agent = agent;
        _format = format;
        _environment = environment;
    }

    /// <inheritdoc />
    public string Agent { get; }

    /// <inheritdoc />
    public bool IsAvailable => _format.IsUsable && Directory.Exists(Root);

    private string Root => TranscriptPaths.Expand(_format.Root, _environment);

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
                .EnumerateFiles(
                    _format.Files,
                    _format.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(limit)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<IReadOnlyList<AgentSession>>.Fail(
                $"Could not read {Agent}'s session history at {Root}: {ex.Message}");
        }

        var sessions = new List<AgentSession>(files.Count);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            if (await ReadAsync(file, ct).ConfigureAwait(false) is { } session)
            {
                sessions.Add(session);
            }
        }

        return OperationResult<IReadOnlyList<AgentSession>>.Ok(sessions);
    }

    private async Task<AgentSession?> ReadAsync(FileInfo file, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(file.OpenRead());

            string? id = null;
            string? directory = null;
            string? title = null;
            string? branch = null;

            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    Read(line, ref id, ref directory, ref title, ref branch);
                }

                // Either the format says everything is on the opening entry, or
                // the file has now given up both of the things a listing cannot
                // do without. Reading on would mean parsing a conversation to
                // build a menu.
                if (_format.Session.FirstLineOnly || (id is not null && directory is not null))
                {
                    break;
                }
            }

            return id is { Length: > 0 } && directory is { Length: > 0 }
                ? new AgentSession(
                    Agent,
                    id,
                    title,
                    directory,
                    branch,
                    new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
                    file.FullName)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Takes from one line whatever it happens to carry.
    /// </summary>
    /// <remarks>
    /// Values already found are kept. A format where the working directory
    /// appears on every line would otherwise have the last line win, and the
    /// earliest is the one that says where the session started.
    /// </remarks>
    private void Read(
        string line,
        ref string? id,
        ref string? directory,
        ref string? title,
        ref string? branch)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            // One line that is not JSON costs that line. Some formats open with
            // a comment or a version banner.
            return;
        }

        using (document)
        {
            id ??= TranscriptPaths.String(document.RootElement, _format.Session.Id);
            directory ??= TranscriptPaths.String(document.RootElement, _format.Session.Directory);
            title ??= TranscriptPaths.String(document.RootElement, _format.Session.Title);
            branch ??= TranscriptPaths.String(document.RootElement, _format.Session.Branch);
        }
    }
}
