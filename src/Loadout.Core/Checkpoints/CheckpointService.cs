using Loadout.Core.Backups;
using Loadout.Core.Configuration;
using Loadout.Core.Context;
using Loadout.Core.Git;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Checkpoints;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Checkpoints;

/// <inheritdoc />
/// <remarks>
/// A binding and nothing else. Every part of a checkpoint already had somewhere
/// to live; what was missing was the record that these four were one moment.
/// That makes this thin on purpose — it captures identifiers and writes them
/// down, and everything hard is still done by whatever owned it before.
/// </remarks>
internal sealed class CheckpointService : ICheckpointService
{
    private readonly IBackupService _backups;
    private readonly IGitManager _git;
    private readonly IHandoffService _handoffs;
    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly IPlatformPaths _paths;
    private readonly YamlStore _yaml;
    private readonly TimeProvider _time;

    public CheckpointService(
        IBackupService backups,
        IGitManager git,
        IHandoffService handoffs,
        IProjectService projects,
        IWorkspaceManager workspace,
        IPlatformPaths paths,
        YamlStore yaml,
        TimeProvider time)
    {
        _backups = backups;
        _git = git;
        _handoffs = handoffs;
        _projects = projects;
        _workspace = workspace;
        _paths = paths;
        _yaml = yaml;
        _time = time;
    }

    private string DirectoryFor(string slug) =>
        Path.Combine(_paths.Paths.State, "checkpoints", slug);

    private string PathFor(string slug, string name) =>
        Path.Combine(DirectoryFor(slug), name + ".yaml");

    /// <inheritdoc />
    public async Task<OperationResult<Checkpoint>> CreateAsync(
        string projectSlug,
        string name,
        string? description = null,
        CancellationToken ct = default)
    {
        if (CheckpointNames.Rejection(name) is { } rejected)
        {
            return OperationResult<Checkpoint>.Fail(rejected, ExitCode.InvalidArguments);
        }

        var trimmed = name.Trim();

        var resolved = await _projects.ResolveAsync(projectSlug, ct).ConfigureAwait(false);

        if (resolved.Failed)
        {
            return OperationResult<Checkpoint>.Fail(resolved.Error!, resolved.ExitCode);
        }

        var project = resolved.Value!;

        if (File.Exists(PathFor(project.Entry.Slug, trimmed)))
        {
            // Refused rather than overwritten. A checkpoint exists to be
            // returned to, and quietly replacing one is the single way this
            // could destroy the thing it was built to protect.
            return OperationResult<Checkpoint>.Fail(
                $"'{trimmed}' already exists. Checkpoints are not overwritten; "
                + "remove it first if that is what you meant.",
                ExitCode.InvalidArguments);
        }

        var localPath = project.LocalPath;

        if (string.IsNullOrWhiteSpace(localPath) || !Directory.Exists(localPath))
        {
            return OperationResult<Checkpoint>.Fail(
                $"'{project.Entry.Slug}' is not on this machine, so there is nothing to mark.",
                ExitCode.RepositoryUnavailable);
        }

        var captured = await _backups
            .CaptureAsync("checkpoint", trimmed, WorkspaceFiles(project.Entry.Slug), ct)
            .ConfigureAwait(false);

        if (captured.Failed)
        {
            return OperationResult<Checkpoint>.Fail(captured.Error!, captured.ExitCode);
        }

        // Best effort from here. A repository that cannot be read and a project
        // with no handoff are both ordinary, and neither is a reason to refuse
        // to mark the moment — a checkpoint with three of its four parts is
        // worth more than none at all, and the missing part is recorded as
        // missing rather than guessed.
        var state = await _git.GetStateAsync(localPath, ct).ConfigureAwait(false);
        var handoff = await _handoffs.GetLatestAsync(project.Entry.Slug, ct).ConfigureAwait(false);

        var checkpoint = new Checkpoint
        {
            Name = trimmed,
            Description = description?.Trim() ?? string.Empty,
            CreatedUtc = _time.GetUtcNow(),
            ProjectSlug = project.Entry.Slug,
            WorkspaceBackupId = captured.Value!.Id,
            RepositoryCommit = state.Value?.HeadCommit,
            RepositoryBranch = state.Value?.Branch,
            RepositoryWasDirty = state.Succeeded && !state.Value!.IsClean,
            HandoffName = handoff.Value?.Name,
        };

        var written = await _yaml
            .SaveAsync(PathFor(project.Entry.Slug, trimmed), checkpoint, true, ct)
            .ConfigureAwait(false);

        return written.Succeeded
            ? OperationResult<Checkpoint>.Ok(checkpoint)
            : OperationResult<Checkpoint>.Fail(written.Error!, written.ExitCode);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<Checkpoint>>> ListAsync(
        string projectSlug,
        CancellationToken ct = default)
    {
        var directory = DirectoryFor(projectSlug);

        if (!Directory.Exists(directory))
        {
            return OperationResult<IReadOnlyList<Checkpoint>>.Ok([]);
        }

        var found = new List<Checkpoint>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.yaml"))
        {
            var loaded = await _yaml
                .LoadAsync<Checkpoint>(file, () => new Checkpoint(), ct)
                .ConfigureAwait(false);

            // One unreadable file costs what it said, not the listing.
            if (loaded.Succeeded && loaded.Value!.Name.Length > 0)
            {
                found.Add(loaded.Value);
            }
        }

        return OperationResult<IReadOnlyList<Checkpoint>>.Ok(
            [.. found
                .OrderByDescending(c => c.CreatedUtc)
                .ThenBy(c => c.Name, StringComparer.Ordinal)]);
    }

    /// <inheritdoc />
    public async Task<OperationResult<Checkpoint>> GetAsync(
        string projectSlug,
        string name,
        CancellationToken ct = default)
    {
        if (CheckpointNames.Rejection(name) is { } rejected)
        {
            return OperationResult<Checkpoint>.Fail(rejected, ExitCode.InvalidArguments);
        }

        var path = PathFor(projectSlug, name.Trim());

        if (!File.Exists(path))
        {
            return OperationResult<Checkpoint>.Fail(
                $"'{name.Trim()}' is not a checkpoint of {projectSlug}.",
                ExitCode.InvalidArguments);
        }

        var loaded = await _yaml
            .LoadAsync<Checkpoint>(path, () => new Checkpoint(), ct)
            .ConfigureAwait(false);

        return loaded.Succeeded
            ? OperationResult<Checkpoint>.Ok(loaded.Value!)
            : OperationResult<Checkpoint>.Fail(loaded.Error!, loaded.ExitCode);
    }

    /// <inheritdoc />
    public async Task<OperationResult<CheckpointRestore>> RestoreAsync(
        string projectSlug,
        string name,
        bool apply,
        CancellationToken ct = default)
    {
        var found = await GetAsync(projectSlug, name, ct).ConfigureAwait(false);

        if (found.Failed)
        {
            return OperationResult<CheckpointRestore>.Fail(found.Error!, found.ExitCode);
        }

        var checkpoint = found.Value!;

        var restored = await _backups
            .RestoreAsync(checkpoint.WorkspaceBackupId, apply, ct)
            .ConfigureAwait(false);

        if (restored.Failed)
        {
            return OperationResult<CheckpointRestore>.Fail(restored.Error!, restored.ExitCode);
        }

        return OperationResult<CheckpointRestore>.Ok(new CheckpointRestore(
            checkpoint,
            restored.Value!.Restored,
            restored.Value.Applied,
            Advice(checkpoint)));
    }

    /// <inheritdoc />
    public async Task<OperationResult> RemoveAsync(
        string projectSlug,
        string name,
        CancellationToken ct = default)
    {
        var found = await GetAsync(projectSlug, name, ct).ConfigureAwait(false);

        if (found.Failed)
        {
            return OperationResult.Fail(found.Error!, found.ExitCode);
        }

        try
        {
            // The marker only. The backup it points at is left alone, because
            // 'backup' owns those and removing one here would take a snapshot
            // out from under anything else that referenced it.
            File.Delete(PathFor(projectSlug, name.Trim()));

            return OperationResult.Ok();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail(
                $"Could not remove '{name.Trim()}': {exception.Message}");
        }
    }

    /// <summary>
    /// What to do about the repository, said rather than done.
    /// </summary>
    /// <remarks>
    /// Checking a commit out can discard work nobody asked to lose, and doing
    /// that because somebody typed a checkpoint name is the surprise that
    /// preview-before-mutation exists to prevent. The commit is named; running
    /// the command is theirs.
    /// </remarks>
    internal static string? Advice(Checkpoint checkpoint)
    {
        if (checkpoint.RepositoryCommit is not { Length: > 0 } commit)
        {
            return null;
        }

        var shortened = commit[..Math.Min(12, commit.Length)];

        var where = checkpoint.RepositoryBranch is { Length: > 0 } branch
            ? $"{shortened} ({branch})"
            : shortened;

        var advice = $"The repository was on {where}. This does not move it: run git checkout "
            + "yourself if that is what you want.";

        return checkpoint.RepositoryWasDirty
            ? advice + " The tree had uncommitted changes at the time, so that commit does not "
                + "describe everything that was on disk."
            : advice;
    }

    /// <summary>
    /// The files a checkpoint captures.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project's own directory in the workspace — its instructions and its
    /// memory — because that is where this launcher keeps the things a session
    /// is composed from. The first version of this captured CLAUDE.md and
    /// AGENTS.md from the repository root and snapshotted nothing at all, which
    /// running the command showed immediately: those files are what setup
    /// migrates *out* of a repository, so on any project that has been set up
    /// they are exactly where they are not.
    /// </para>
    /// <para>
    /// The repository itself is not captured. Git already holds it, far better
    /// than a copy would, and the checkpoint records the commit instead.
    /// </para>
    /// </remarks>
    private IReadOnlyList<string> WorkspaceFiles(string slug)
    {
        if (!_workspace.IsAvailable())
        {
            return [];
        }

        var directory = Path.Combine(_workspace.LocalPath, "projects", slug);

        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return [.. Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)];
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // A checkpoint that captured nothing is still worth its other three
            // parts, and it says so by listing no files rather than by failing.
            return [];
        }
    }
}
