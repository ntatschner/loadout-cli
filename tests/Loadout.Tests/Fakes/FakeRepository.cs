using Loadout.Core.Git;
using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Models.Projects;
using Loadout.Models.Results;

namespace Loadout.Tests.Fakes;

/// <summary>
/// One project, already here, for a test that needs a repository to exist
/// rather than to work.
/// </summary>
public sealed class FakeProjects : IProjectService
{
    public FakeProjects(string slug, string localPath)
    {
        Resolution = new ProjectResolution(
            new ProjectRegistryEntry { Id = slug, Slug = slug, Name = slug },
            localPath,
            null,
            0,
            false);
    }

    public ProjectResolution Resolution { get; }

    public Task<OperationResult<ProjectResolution>> ResolveAsync(string handle, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<ProjectResolution>.Ok(Resolution));

    public Task<OperationResult<IReadOnlyList<ProjectResolution>>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult(OperationResult<IReadOnlyList<ProjectResolution>>.Ok([Resolution]));

    public Task<OperationResult<ProjectResolution>> ResolveFromDirectoryAsync(string directory, CancellationToken ct = default) =>
        ResolveAsync(directory, ct);

    public Task<OperationResult<ProjectResolution>> AddAsync(string repositoryPath, string? slug = null, CancellationToken ct = default) =>
        throw new NotSupportedException("a team run adds no project");

    public Task<OperationResult<AddPreview>> ValidateAddAsync(string repositoryPath, string? slug = null, CancellationToken ct = default) =>
        throw new NotSupportedException("a team run adds no project");

    public Task<OperationResult<ProjectRemoval>> RemoveAsync(string handle, bool fromWorkspace, CancellationToken ct = default) =>
        throw new NotSupportedException("a team run removes no project");

    public Task<OperationResult<IReadOnlyList<DiscoveredRepository>>> DiscoverAsync(CancellationToken ct = default) =>
        throw new NotSupportedException("a team run discovers nothing");

    public Task<OperationResult> RecordLaunchAsync(string slug, string agent, CancellationToken ct = default) =>
        Task.FromResult(OperationResult.Ok());

    public Task<OperationResult> RelocateAsync(string handle, string newPath, CancellationToken ct = default) =>
        throw new NotSupportedException("a team run relocates nothing");

    public Task<OperationResult<ProjectResolution>> CloneAsync(string handle, string? destination = null, CancellationToken ct = default) =>
        throw new NotSupportedException("a team run clones nothing");
}

/// <summary>
/// Git as a script: what each merge answers, and what was asked of it.
/// </summary>
/// <remarks>
/// <para>
/// The real thing is covered against real git elsewhere. What a test needs
/// here is the other half - what the runner does with each answer - and that
/// is not reachable through a repository without making a conflict by hand
/// on every run.
/// </para>
/// <para>
/// Anything a team run has no business calling throws rather than answering,
/// so a run that started reaching for it fails loudly instead of passing on
/// a lie.
/// </para>
/// </remarks>
public sealed class FakeGit : IGitManager
{
    private readonly Queue<OperationResult<GitMerge>> _merges = new();

    public FakeGit(string repository, string branch = "main") =>
        State = new GitRepositoryState(repository, branch, null, IsClean: true, "abc1234");

    public GitRepositoryState State { get; set; }

    /// <summary>Every branch a merge was attempted for, in order.</summary>
    public List<string> Merges { get; } = [];

    /// <summary>Every branch deleted after it landed.</summary>
    public List<string> Deleted { get; } = [];

    /// <summary>Every worktree removed.</summary>
    public List<string> Removed { get; } = [];

    /// <summary>What the next merge answers. Answers a conflict-free merge once these run out.</summary>
    public void NextMerge(GitMerge merge) => _merges.Enqueue(OperationResult<GitMerge>.Ok(merge));

    public Task<OperationResult<GitRepositoryState>> GetStateAsync(string repositoryPath, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<GitRepositoryState>.Ok(State));

    public Task<OperationResult<GitMerge>> MergeAsync(string repositoryPath, string branch, CancellationToken ct = default)
    {
        Merges.Add(branch);

        return Task.FromResult(_merges.Count > 0
            ? _merges.Dequeue()
            : OperationResult<GitMerge>.Ok(new GitMerge(Merged: true, FastForward: true, [])));
    }

    public Task<OperationResult> RemoveWorktreeAsync(string repositoryPath, string path, CancellationToken ct = default)
    {
        Removed.Add(path);

        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> DeleteMergedBranchAsync(string repositoryPath, string branch, CancellationToken ct = default)
    {
        Deleted.Add(branch);

        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult<IReadOnlyList<GitWorktree>>> ListWorktreesAsync(string repositoryPath, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<IReadOnlyList<GitWorktree>>.Ok(
            [new GitWorktree(State.Root, State.Branch, IsPrimary: true)]));

    public Task<OperationResult<string>> GetVersionAsync(CancellationToken ct = default) =>
        Task.FromResult(OperationResult<string>.Ok("git version 2.0.0"));

    public Task<OperationResult<string>> FindRepositoryRootAsync(string path, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<string>.Ok(State.Root));

    public Task<OperationResult> InitAsync(string path, string defaultBranch = "main", CancellationToken ct = default) =>
        throw new NotSupportedException("a team run initialises no repository");

    public Task<OperationResult> SetRemoteAsync(string repositoryPath, string name, string url, CancellationToken ct = default) =>
        throw new NotSupportedException("a team run touches no remote");

    public Task<OperationResult> PushWithUpstreamAsync(string repositoryPath, string remote, string branch, CancellationToken ct = default) =>
        throw new NotSupportedException("a team run pushes nothing");

    public Task<OperationResult> CloneAsync(string remote, string destination, string? branch = null, CancellationToken ct = default) =>
        throw new NotSupportedException("a team run clones nothing");

    public Task<OperationResult> FetchAsync(string repositoryPath, TimeSpan timeout, CancellationToken ct = default) =>
        throw new NotSupportedException("a team run fetches nothing");

    public Task<OperationResult> PullFastForwardAsync(string repositoryPath, CancellationToken ct = default) =>
        throw new NotSupportedException("a team run pulls nothing");

    public Task<OperationResult<bool>> CommitAllAsync(string repositoryPath, string message, CancellationToken ct = default) =>
        throw new NotSupportedException("the nodes commit their own work");

    public Task<OperationResult> CreateBranchAsync(string repositoryPath, string branchName, CancellationToken ct = default) =>
        throw new NotSupportedException("a worktree brings its own branch");

    public Task<OperationResult<IReadOnlyList<string>>> ListChangedFilesAsync(string repositoryPath, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<IReadOnlyList<string>>.Ok([]));

    /// <summary>What each commit touched here, when a test has said.</summary>
    public Dictionary<string, IReadOnlyList<GitFileChange>> Touched { get; } = new(StringComparer.Ordinal);

    public Task<OperationResult<IReadOnlyList<GitFileChange>>> ListCommitFilesAsync(
        string repositoryPath,
        string commit,
        CancellationToken ct = default) =>
        Task.FromResult(Touched.TryGetValue(commit, out var files)
            ? OperationResult<IReadOnlyList<GitFileChange>>.Ok(files)
            : OperationResult<IReadOnlyList<GitFileChange>>.Fail(
                $"fatal: bad object {commit}"));

    public Task<OperationResult> PushAsync(string repositoryPath, CancellationToken ct = default) =>
        throw new NotSupportedException("a team run pushes nothing");

    /// <summary>What a branch resolves to here, when a test has said.</summary>
    public Dictionary<string, string> Commits { get; } = new(StringComparer.Ordinal);

    /// <summary>What a diff between two commits looks like here, by "from..to".</summary>
    public Dictionary<string, string> Diffs { get; } = new(StringComparer.Ordinal);

    public Task<OperationResult<string>> ResolveAsync(
        string repositoryPath, string reference, CancellationToken ct = default) =>
        Task.FromResult(Commits.TryGetValue(reference, out var commit)
            ? OperationResult<string>.Ok(commit)
            : OperationResult<string>.Fail($"Nothing in this repository is called '{reference}'."));

    public Task<OperationResult<string>> DiffAsync(
        string repositoryPath, string from, string to, bool summary = false, CancellationToken ct = default) =>
        Task.FromResult(Diffs.TryGetValue($"{from}..{to}" + (summary ? " --stat" : string.Empty), out var diff)
            ? OperationResult<string>.Ok(diff)
            : OperationResult<string>.Ok(string.Empty));

    public Task<OperationResult<IReadOnlyList<CommitSummary>>> ListCommitsAsync(
        string repositoryPath, DateTimeOffset since, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<IReadOnlyList<CommitSummary>>.Ok([]));

    public Task<OperationResult<GitWorktree>> AddWorktreeAsync(
        string repositoryPath, string path, string branch, string? baseRef = null, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<GitWorktree>.Ok(new GitWorktree(path, branch, IsPrimary: false)));

    public Task<OperationResult<IReadOnlyList<string>>> ListFilesAsync(
        string repositoryPath, IReadOnlyList<string> patterns, GitFileSet fileSet, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<IReadOnlyList<string>>.Ok([]));

    public Task<OperationResult> UntrackAsync(string repositoryPath, IReadOnlyList<string> paths, CancellationToken ct = default) =>
        throw new NotSupportedException("a team run untracks nothing");

    public Task<OperationResult> SetGlobalConfigValueAsync(string key, string value, CancellationToken ct = default) =>
        throw new NotSupportedException("a team run writes no git config");

    public Task<OperationResult<string?>> GetGlobalConfigValueAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<string?>.Ok(null));

    public Task<OperationResult> SetLocalConfigValueAsync(string key, string value, string repositoryPath, CancellationToken ct = default) =>
        throw new NotSupportedException("a team run writes no git config");

    public Task<OperationResult> RemoveLocalConfigValueAsync(string key, string repositoryPath, CancellationToken ct = default) =>
        throw new NotSupportedException("a team run writes no git config");

    public Task<OperationResult<string?>> GetConfigValueAsync(string key, string? repositoryPath = null, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<string?>.Ok(null));
}
