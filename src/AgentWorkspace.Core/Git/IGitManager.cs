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

    /// <summary>Pushes the current branch to its upstream.</summary>
    Task<OperationResult> PushAsync(string repositoryPath, CancellationToken ct = default);

    /// <summary>Lists the main working tree and any linked worktrees (spec section 71).</summary>
    Task<OperationResult<IReadOnlyList<GitWorktree>>> ListWorktreesAsync(
        string repositoryPath,
        CancellationToken ct = default);

    /// <summary>
    /// Paths matching the given patterns that git is currently tracking.
    /// Backs the repository policy check of spec section 49.
    /// </summary>
    Task<OperationResult<IReadOnlyList<string>>> ListTrackedFilesAsync(
        string repositoryPath,
        IReadOnlyList<string> patterns,
        CancellationToken ct = default);

    /// <summary>Reads a git config value, or null when it is unset.</summary>
    Task<OperationResult<string?>> GetConfigValueAsync(
        string key,
        string? repositoryPath = null,
        CancellationToken ct = default);
}
