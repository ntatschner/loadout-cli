using AgentWorkspace.Models.Results;

namespace AgentWorkspace.Core.Git;

/// <summary>State of a working tree, as far as the launcher needs to know it.</summary>
/// <param name="Root">Absolute path to the repository root.</param>
/// <param name="Branch">Current branch name, or null when the head is detached.</param>
/// <param name="RemoteUrl">URL of the origin remote, or null when there is none.</param>
/// <param name="IsClean">True when there are no staged, unstaged or untracked changes.</param>
/// <param name="HeadCommit">Full SHA of HEAD, recorded in audit metadata (spec section 81).</param>
public sealed record GitRepositoryState(
    string Root,
    string? Branch,
    string? RemoteUrl,
    bool IsClean,
    string? HeadCommit);

/// <summary>A linked working tree (spec section 71).</summary>
/// <param name="Path">Absolute path to the worktree.</param>
/// <param name="Branch">Branch checked out there, or null when detached.</param>
/// <param name="IsPrimary">True for the main working tree rather than a linked one.</param>
public sealed record GitWorktree(string Path, string? Branch, bool IsPrimary);

/// <summary>Which paths a listing should return.</summary>
public enum GitFileSet
{
    /// <summary>Files git is tracking.</summary>
    Tracked,

    /// <summary>Files present but untracked and not ignored.</summary>
    UntrackedAndVisible,

    /// <summary>Files present, untracked and ignored.</summary>
    Ignored,
}

/// <summary>
/// Git operations, performed by invoking the user's own git binary.
/// <para>
/// Shelling out rather than linking a Git library is a deliberate choice. It
/// is the only way to honour spec sections 56 and 17: the user's ssh config,
/// SSH agent, and configured credential helper — osxkeychain, libsecret,
/// manager — all apply automatically, and the launcher never has to reinvent
/// Git authentication or ask for credentials it should not hold.
/// </para>
/// </summary>
public interface IGitManager
{
    /// <summary>Whether a usable git binary was found, and which version.</summary>
    Task<OperationResult<string>> GetVersionAsync(CancellationToken ct = default);

    /// <summary>
    /// Finds the repository root containing a path (spec section 24).
    /// Returns a failure when the path is not inside a repository.
    /// </summary>
    Task<OperationResult<string>> FindRepositoryRootAsync(string path, CancellationToken ct = default);

    /// <summary>Reads branch, remote, cleanliness and HEAD in one pass.</summary>
    Task<OperationResult<GitRepositoryState>> GetStateAsync(string repositoryPath, CancellationToken ct = default);

    /// <summary>
    /// Initialises a repository, so a freshly created workspace is something
    /// that can actually be committed and pushed rather than a bare directory.
    /// </summary>
    Task<OperationResult> InitAsync(
        string path,
        string defaultBranch = "main",
        CancellationToken ct = default);

    /// <summary>Points a repository at a remote, replacing any existing one of that name.</summary>
    Task<OperationResult> SetRemoteAsync(
        string repositoryPath,
        string name,
        string url,
        CancellationToken ct = default);

    /// <summary>Pushes a branch and sets it to track the remote.</summary>
    Task<OperationResult> PushWithUpstreamAsync(
        string repositoryPath,
        string remote,
        string branch,
        CancellationToken ct = default);

    /// <summary>Clones a repository. Progress is inherited by the caller's terminal.</summary>
    Task<OperationResult> CloneAsync(
        string remote,
        string destination,
        string? branch = null,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches from the default remote. The timeout is bounded by the caller so
    /// a launch-time sync cannot hang on an unreachable server; exceeding it is
    /// what puts the launcher into offline mode (spec sections 45 and 48).
    /// </summary>
    Task<OperationResult> FetchAsync(
        string repositoryPath,
        TimeSpan timeout,
        CancellationToken ct = default);

    /// <summary>
    /// Fast-forwards the current branch to its upstream. Refuses rather than
    /// merging or rebasing, so a divergence surfaces as the conflict flow of
    /// spec section 47 instead of silently rewriting the user's work.
    /// </summary>
    Task<OperationResult> PullFastForwardAsync(string repositoryPath, CancellationToken ct = default);

    /// <summary>Stages everything and commits. Returns false when there was nothing to commit.</summary>
    Task<OperationResult<bool>> CommitAllAsync(
        string repositoryPath,
        string message,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a branch at the current HEAD without switching to it.
    /// <para>
    /// Used to preserve local work before a divergence is resolved
    /// (spec section 47). Not switching matters: the user keeps the working
    /// tree they had, and the branch is simply a label they can return to.
    /// </para>
    /// </summary>
    Task<OperationResult> CreateBranchAsync(
        string repositoryPath,
        string branchName,
        CancellationToken ct = default);

    /// <summary>
    /// Paths with uncommitted changes, staged or not, including untracked ones.
    /// </summary>
    Task<OperationResult<IReadOnlyList<string>>> ListChangedFilesAsync(
        string repositoryPath,
        CancellationToken ct = default);

    /// <summary>Pushes the current branch to its upstream.</summary>
    Task<OperationResult> PushAsync(string repositoryPath, CancellationToken ct = default);

    /// <summary>Lists the main working tree and any linked worktrees (spec section 71).</summary>
    Task<OperationResult<IReadOnlyList<GitWorktree>>> ListWorktreesAsync(
        string repositoryPath,
        CancellationToken ct = default);

    /// <summary>Which set of paths to ask git about.</summary>
    /// <remarks>
    /// The distinction is the whole substance of the policy check: a tracked
    /// agent file is a violation, an untracked but visible one is a near miss,
    /// and an ignored one is the system working.
    /// </remarks>
    Task<OperationResult<IReadOnlyList<string>>> ListFilesAsync(
        string repositoryPath,
        IReadOnlyList<string> patterns,
        GitFileSet fileSet,
        CancellationToken ct = default);

    /// <summary>
    /// Writes a value to the user's global Git configuration.
    /// <para>
    /// Global rather than system or repository scope: spec section 50 wants one
    /// rule covering every repository this user works in, without needing root
    /// and without touching any repository's own .gitignore.
    /// </para>
    /// </summary>
    Task<OperationResult> SetGlobalConfigValueAsync(
        string key,
        string value,
        CancellationToken ct = default);

    /// <summary>
    /// Reads a value from the user's global Git configuration specifically.
    /// <para>
    /// Distinct from the plain read, which resolves through whatever repository
    /// the process happens to be standing in. For something like the committer
    /// identity that difference decides correctness: a local identity in an
    /// unrelated repository must not be mistaken for one the workspace can use.
    /// </para>
    /// </summary>
    Task<OperationResult<string?>> GetGlobalConfigValueAsync(
        string key,
        CancellationToken ct = default);

    /// <summary>
    /// Writes a value to one repository's own configuration.
    /// <para>
    /// This is where a repository records which project it belongs to. The
    /// file it lands in, <c>.git/config</c>, is per-clone and is never
    /// committed, so the mark leaves no trace in the repository's contents:
    /// spec section 9's rule that application repositories hold application
    /// source only is about what gets committed, and this does not.
    /// </para>
    /// </summary>
    Task<OperationResult> SetLocalConfigValueAsync(
        string key,
        string value,
        string repositoryPath,
        CancellationToken ct = default);

    /// <summary>Reads a git config value, or null when it is unset.</summary>
    Task<OperationResult<string?>> GetConfigValueAsync(
        string key,
        string? repositoryPath = null,
        CancellationToken ct = default);
}
