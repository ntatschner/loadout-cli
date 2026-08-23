using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Results;

namespace Loadout.Core.Context;

/// <inheritdoc />
public sealed class HandoffService : IHandoffService
{
    private readonly IWorkspaceManager _workspace;
    private readonly TimeProvider _time;

    public HandoffService(IWorkspaceManager workspace, TimeProvider time)
    {
        _workspace = workspace;
        _time = time;
    }

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<HandoffDocument>>> ListAsync(
        string slug,
        CancellationToken ct = default)
    {
        var directory = HandoffDirectory(slug);

        if (!Directory.Exists(directory))
        {
            // No handoffs yet is an ordinary state for a new project, so it is
            // an empty list rather than a failure.
            return Task.FromResult(
                OperationResult<IReadOnlyList<HandoffDocument>>.Ok([]));
        }

        try
        {
            var documents = Directory
                .EnumerateFiles(directory, "*.md")
                .Select(path => new HandoffDocument(
                    Path.GetFileNameWithoutExtension(path),
                    path,
                    new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)))
                .OrderByDescending(d => d.WrittenUtc)
                .ToList();

            return Task.FromResult(
                OperationResult<IReadOnlyList<HandoffDocument>>.Ok(documents));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<HandoffDocument>>.Fail(
                $"Could not read handoffs for '{slug}': {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<HandoffDocument?>> GetLatestAsync(
        string slug,
        CancellationToken ct = default)
    {
        var listResult = await ListAsync(slug, ct).ConfigureAwait(false);

        return listResult.Failed
            ? OperationResult<HandoffDocument?>.Fail(listResult.Error!, listResult.ExitCode)
            : OperationResult<HandoffDocument?>.Ok(listResult.Value!.FirstOrDefault());
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> ReadAsync(
        string slug,
        string? name = null,
        CancellationToken ct = default)
    {
        var document = name is null
            ? (await GetLatestAsync(slug, ct).ConfigureAwait(false)).Value
            : (await ListAsync(slug, ct).ConfigureAwait(false)).Value?
                .FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

        if (document is null)
        {
            return OperationResult<string>.Fail(
                name is null
                    ? $"'{slug}' has no handoffs yet."
                    : $"'{slug}' has no handoff named '{name}'.",
                ExitCode.ProjectNotFound);
        }

        try
        {
            var text = await File.ReadAllTextAsync(document.Path, ct).ConfigureAwait(false);
            return OperationResult<string>.Ok(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<string>.Fail($"Could not read '{document.Path}': {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<HandoffDocument>> CreateAsync(
        string slug,
        string? name = null,
        CancellationToken ct = default)
    {
        var directory = HandoffDirectory(slug);
        var now = _time.GetUtcNow();

        // Timestamped so handoffs sort chronologically in a directory listing
        // and in a pull request, which is how they are usually read.
        var fileName = name is null
            ? $"{now:yyyy-MM-dd-HHmm}.md"
            : SanitiseName(name) + ".md";

        var path = Path.Combine(directory, fileName);

        if (File.Exists(path))
        {
            return OperationResult<HandoffDocument>.Fail(
                $"A handoff named '{Path.GetFileNameWithoutExtension(path)}' already exists.");
        }

        try
        {
            Directory.CreateDirectory(directory);

            // Handoffs are committed to the workspace and reviewed, so they get
            // ordinary permissions rather than the owner-only treatment applied
            // to runtime material.
            await File.WriteAllTextAsync(path, Template(slug, now), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<HandoffDocument>.Fail($"Could not write '{path}': {ex.Message}");
        }

        return OperationResult<HandoffDocument>.Ok(
            new HandoffDocument(Path.GetFileNameWithoutExtension(path), path, now));
    }

    private string HandoffDirectory(string slug) =>
        Path.Combine(_workspace.LocalPath, "projects", slug, "handoffs");

    /// <summary>Strips anything that would be awkward or unsafe in a file name.</summary>
    internal static string SanitiseName(string name)
    {
        var cleaned = new string(name
            .Trim()
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .ToArray());

        return cleaned.Trim('-').ToLowerInvariant();
    }

    /// <summary>
    /// The standard handoff skeleton from spec section 69. Kept as headings
    /// with no filler so an agent asked to complete it has nothing to copy by
    /// accident.
    /// </summary>
    internal static string Template(string slug, DateTimeOffset now) =>
        $"""
        # Development handoff: {slug}

        Written {now:dd MMMM yyyy HH:mm} UTC.

        ## Goal

        ## Completed

        ## Current state

        ## Decisions

        ## Remaining work

        ## Relevant files

        ## Known issues

        """;
}
