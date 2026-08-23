using Loadout.Core.Configuration;
using Loadout.Core.Git;
using Loadout.Models;
using Loadout.Models.Configuration;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Workspace;

/// <inheritdoc />
public sealed class WorkspaceManager : IWorkspaceManager
{
    private const string ManifestFileName = "workspace.yaml";
    private const string ProjectFileName = "project.yaml";

    /// <summary>Highest workspace schema this launcher understands (spec section 91).</summary>
    public const int SupportedSchemaVersion = 1;

    private readonly IPlatformPaths _paths;
    private readonly IGitManager _git;
    private readonly YamlStore _yaml;
    private readonly TimeProvider _time;

    public WorkspaceManager(IPlatformPaths paths, IGitManager git, YamlStore yaml, TimeProvider time)
    {
        _paths = paths;
        _git = git;
        _yaml = yaml;
        _time = time;
    }

    /// <inheritdoc />
    public string LocalPath => _paths.Paths.WorkspaceClone;

    /// <inheritdoc />
    public bool IsConfigured(LauncherConfig config) =>
        !string.IsNullOrWhiteSpace(config.Workspace.Remote);

    /// <inheritdoc />
    public bool IsCloned() => Directory.Exists(Path.Combine(LocalPath, ".git"));

    /// <inheritdoc />
    public bool IsAvailable() =>
        Directory.Exists(Path.Combine(LocalPath, "projects"))
        || Directory.Exists(Path.Combine(LocalPath, "registry"))
        || File.Exists(Path.Combine(LocalPath, ManifestFileName));

    /// <inheritdoc />
    public async Task<OperationResult> CloneAsync(LauncherConfig config, CancellationToken ct = default)
    {
        if (!IsConfigured(config))
        {
            return OperationResult.Fail(
                "No central workspace remote is configured. Run the setup wizard first.",
                ExitCode.ConfigurationInvalid);
        }

        if (IsCloned())
        {
            return OperationResult.Fail($"A workspace clone already exists at '{LocalPath}'.");
        }

        // git clone refuses a non-empty destination, and leaving a stale empty
        // directory behind from a failed attempt would block every retry.
        if (Directory.Exists(LocalPath) && Directory.EnumerateFileSystemEntries(LocalPath).Any())
        {
            return OperationResult.Fail(
                $"'{LocalPath}' already exists and is not empty. Move it aside or run workspace repair.");
        }

        var branch = string.IsNullOrWhiteSpace(config.Workspace.Branch) ? null : config.Workspace.Branch;

        return await _git.CloneAsync(config.Workspace.Remote, LocalPath, branch, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<WorkspaceSyncResult>> SyncAsync(
        LauncherConfig config,
        CancellationToken ct = default)
    {
        if (!IsConfigured(config))
        {
            return OperationResult<WorkspaceSyncResult>.Ok(new WorkspaceSyncResult(
                WorkspaceSyncOutcome.NotConfigured,
                "No central workspace is configured; running with local state only.",
                null));
        }

        if (!IsCloned())
        {
            var cloneResult = await CloneAsync(config, ct).ConfigureAwait(false);

            return cloneResult.Succeeded
                ? OperationResult<WorkspaceSyncResult>.Ok(new WorkspaceSyncResult(
                    WorkspaceSyncOutcome.Synced, "Workspace cloned.", DateTimeOffset.UtcNow))
                : OperationResult<WorkspaceSyncResult>.Fail(cloneResult.Error!, cloneResult.ExitCode);
        }

        var cachedAt = GetCacheTimestamp();
        var timeout = TimeSpan.FromSeconds(Math.Max(1, config.Sync.NetworkTimeoutSeconds));

        var fetchResult = await _git.FetchAsync(LocalPath, timeout, ct).ConfigureAwait(false);

        if (fetchResult.Failed)
        {
            // An unreachable remote is the normal offline case, not a failure.
            // Spec section 48 requires the launcher to carry on with the cached
            // workspace and tell the user how old it is.
            return OperationResult<WorkspaceSyncResult>.Ok(new WorkspaceSyncResult(
                WorkspaceSyncOutcome.Offline,
                $"The central workspace could not be reached: {fetchResult.Error}",
                cachedAt));
        }

        var pullResult = await _git.PullFastForwardAsync(LocalPath, ct).ConfigureAwait(false);

        if (pullResult.ExitCode == ExitCode.GitConflict)
        {
            // Local and remote have both moved. Before anything else touches
            // the clone, the local state is labelled with a branch so it can
            // always be recovered: spec section 47 says no data loss is
            // acceptable, and a branch costs nothing.
            var recovery = await CreateRecoveryBranchAsync(ct).ConfigureAwait(false);

            var detail = recovery is null
                ? "Local and remote workspaces have diverged, and a recovery branch could not be "
                  + "created. Resolve it by hand before syncing again."
                : $"Local and remote workspaces have diverged. Local work is preserved on "
                  + $"branch '{recovery}'.";

            return OperationResult<WorkspaceSyncResult>.Ok(new WorkspaceSyncResult(
                WorkspaceSyncOutcome.Conflict, detail, cachedAt, recovery));
        }

        if (pullResult.Failed)
        {
            return OperationResult<WorkspaceSyncResult>.Ok(new WorkspaceSyncResult(
                WorkspaceSyncOutcome.Offline,
                pullResult.Error ?? "The workspace could not be updated.",
                cachedAt));
        }

        return OperationResult<WorkspaceSyncResult>.Ok(new WorkspaceSyncResult(
            WorkspaceSyncOutcome.Synced, "Workspace is up to date.", DateTimeOffset.UtcNow));
    }

    /// <inheritdoc />
    public async Task<OperationResult<WorkspaceManifest>> ReadManifestAsync(
        CancellationToken ct = default)
    {
        var path = Path.Combine(LocalPath, ManifestFileName);

        if (!File.Exists(path))
        {
            return OperationResult<WorkspaceManifest>.Fail(
                $"'{LocalPath}' has no {ManifestFileName}, so it is not an agent workspace. "
                + "Check the remote, or create the structure with: loadout setup",
                ExitCode.ConfigurationInvalid);
        }

        return await _yaml.LoadAsync(path, () => new WorkspaceManifest(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<OperationResult<ProjectRegistry>> ReadRegistryAsync(CancellationToken ct = default) =>
        _yaml.LoadAsync(RegistryPath, () => new ProjectRegistry(), ct);

    /// <inheritdoc />
    public Task<OperationResult> WriteRegistryAsync(ProjectRegistry registry, CancellationToken ct = default) =>
        // The registry is committed to the central repository and reviewed in
        // pull requests, so it stays group-readable rather than owner-only.
        _yaml.SaveAsync(RegistryPath, registry, restrictPermissions: false, ct);

    /// <inheritdoc />
    public async Task<OperationResult<ProjectManifest>> ReadProjectAsync(
        string slug,
        CancellationToken ct = default)
    {
        var path = Path.Combine(ProjectDirectory(slug), ProjectFileName);

        if (!File.Exists(path))
        {
            return OperationResult<ProjectManifest>.Fail(
                $"No project manifest exists for '{slug}'.", ExitCode.ProjectNotFound);
        }

        return await _yaml.LoadAsync(path, () => new ProjectManifest(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<OperationResult> WriteProjectAsync(ProjectManifest manifest, CancellationToken ct = default) =>
        _yaml.SaveAsync(
            Path.Combine(ProjectDirectory(manifest.Slug), ProjectFileName),
            manifest,
            restrictPermissions: false,
            ct);

    /// <inheritdoc />
    public async Task<OperationResult> InitialiseStructureAsync(
        string workspaceName,
        CancellationToken ct = default)
    {
        try
        {
            // The layout of spec section 11. Created up front so a new
            // workspace has somewhere obvious to put each kind of file, and so
            // the structure is identical on every machine that clones it.
            string[] directories =
            [
                "registry",
                Path.Combine("global", "instructions"),
                Path.Combine("global", "agents", "claude"),
                Path.Combine("global", "agents", "codex"),
                Path.Combine("global", "prompts"),
                Path.Combine("global", "skills"),
                Path.Combine("global", "policies"),
                Path.Combine("global", "templates"),
                "projects",
                "policies",
            ];

            foreach (var directory in directories)
            {
                Directory.CreateDirectory(Path.Combine(LocalPath, directory));
            }

            var manifestResult = await _yaml.SaveAsync(
                Path.Combine(LocalPath, ManifestFileName),
                new WorkspaceManifest
                {
                    WorkspaceSchema = SupportedSchemaVersion,
                    MinimumLauncherVersion = "0.1",
                    Name = workspaceName,
                },
                restrictPermissions: false,
                ct).ConfigureAwait(false);

            if (manifestResult.Failed)
            {
                return manifestResult;
            }

            return await WriteRegistryAsync(new ProjectRegistry(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"Could not create the workspace structure: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult> InitialiseRepositoryAsync(
        string defaultBranch,
        CancellationToken ct = default)
    {
        if (IsCloned())
        {
            return OperationResult.Ok();
        }

        var branch = string.IsNullOrWhiteSpace(defaultBranch) ? "main" : defaultBranch;

        var initResult = await _git.InitAsync(LocalPath, branch, ct).ConfigureAwait(false);
        if (initResult.Failed)
        {
            return initResult;
        }

        var ignoreResult = await WriteIgnoreFileAsync(ct).ConfigureAwait(false);
        if (ignoreResult.Failed)
        {
            return ignoreResult;
        }

        var commitResult = await _git
            .CommitAllAsync(LocalPath, "agent-workspace: create workspace", ct)
            .ConfigureAwait(false);

        return commitResult.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(commitResult.Error!, commitResult.ExitCode);
    }

    /// <summary>
    /// Keeps transient material out of the workspace repository.
    /// <para>
    /// Spec section 12 lists what must never be committed — caches, session
    /// histories, logs, scratch data. The launcher does not put any of that
    /// here, but an agent pointed at the workspace might, and one careless
    /// commit is all it takes.
    /// </para>
    /// </summary>
    private async Task<OperationResult> WriteIgnoreFileAsync(CancellationToken ct)
    {
        var content =
            """
            # Written by loadout. Spec section 12: the workspace holds durable
            # context, never transient state.
            *.log
            *.tmp
            .DS_Store
            Thumbs.db

            cache/
            caches/
            logs/
            sessions/
            history/
            runtime/

            # Agent session state, wherever an agent decides to write it.
            **/.codex/sessions/
            **/.claude/projects/

            """;

        // The workspace is shared between machines by design, so line endings
        // have to be settled in the repository rather than left to whatever
        // each machine's Git happens to be configured to do. Without this, an
        // instruction file written on Windows reads as modified on every
        // machine forever: the launcher reports pending changes after every
        // launch for a file nobody edited, and saving commits the churn.
        var attributes =
            """
            # Written by loadout. The workspace is shared between machines, so
            # line endings are decided here rather than by each machine's Git.
            * text=auto eol=lf
            """;

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(LocalPath, ".gitignore"), content, ct).ConfigureAwait(false);

            await File.WriteAllTextAsync(
                Path.Combine(LocalPath, ".gitattributes"), attributes, ct).ConfigureAwait(false);

            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"Could not write the workspace Git files: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<string>>> GetPendingChangesAsync(
        CancellationToken ct = default)
    {
        if (!IsCloned())
        {
            // Without Git there is nothing to commit, so there is nothing
            // pending. Local-only mode is not a failure here.
            return OperationResult<IReadOnlyList<string>>.Ok([]);
        }

        return await _git.ListChangedFilesAsync(LocalPath, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> SaveAsync(
        string projectName,
        string agentName,
        bool push,
        CancellationToken ct = default)
    {
        if (!IsCloned())
        {
            return OperationResult<bool>.Ok(false);
        }

        // The format of spec section 46, so a workspace history reads as a
        // record of which machine did what to which project. A raw string
        // literal keeps the line breaks visible rather than hidden in escapes.
        var message =
            $"""
            agent-workspace: update {projectName} context

            Project: {projectName}
            Agent: {agentName}
            Machine: {_paths.Host.MachineName}
            """;

        var commitResult = await _git.CommitAllAsync(LocalPath, message, ct).ConfigureAwait(false);

        if (commitResult.Failed)
        {
            return OperationResult<bool>.Fail(commitResult.Error!, commitResult.ExitCode);
        }

        if (!commitResult.Value || !push)
        {
            return OperationResult<bool>.Ok(commitResult.Value);
        }

        var pushResult = await _git.PushAsync(LocalPath, ct).ConfigureAwait(false);

        // The commit already happened, so a failed push is not a lost change:
        // the work is safe locally and the next sync will carry it.
        return pushResult.Succeeded
            ? OperationResult<bool>.Ok(true)
            : OperationResult<bool>.Fail(
                $"The workspace was committed locally but could not be pushed: {pushResult.Error}",
                pushResult.ExitCode);
    }

    /// <summary>
    /// Labels the current local state so a divergence can never lose it
    /// (spec section 47). The branch is named for the machine and the moment,
    /// which is what makes it recognisable weeks later.
    /// </summary>
    private async Task<string?> CreateRecoveryBranchAsync(CancellationToken ct)
    {
        var name = $"recovery/{_paths.Host.MachineName}/{_time.GetUtcNow():yyyy-MM-dd-HHmm}";

        var result = await _git.CreateBranchAsync(LocalPath, name, ct).ConfigureAwait(false);

        // A name collision means a branch from an earlier conflict this minute
        // already holds the same commit, so nothing is at risk either way.
        return result.Succeeded ? name : null;
    }

    private string RegistryPath => Path.Combine(LocalPath, "registry", "projects.yaml");

    private string ProjectDirectory(string slug) => Path.Combine(LocalPath, "projects", slug);

    /// <summary>
    /// When the clone last received data, used to tell the user how stale the
    /// cached workspace is when running offline (spec section 48).
    /// </summary>
    private DateTimeOffset? GetCacheTimestamp()
    {
        try
        {
            // FETCH_HEAD is rewritten by every fetch, which makes it a more
            // honest "last contact" marker than the working tree's timestamps.
            var fetchHead = Path.Combine(LocalPath, ".git", "FETCH_HEAD");
            if (File.Exists(fetchHead))
            {
                return new DateTimeOffset(File.GetLastWriteTimeUtc(fetchHead), TimeSpan.Zero);
            }

            var head = Path.Combine(LocalPath, ".git", "HEAD");

            return File.Exists(head)
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(head), TimeSpan.Zero)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
