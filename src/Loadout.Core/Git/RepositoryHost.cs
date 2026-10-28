using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Git;

/// <summary>A pull request to open.</summary>
/// <param name="Head">The branch it is opened from, already pushed.</param>
/// <param name="Title">Its title.</param>
/// <param name="Body">Its description.</param>
/// <param name="Draft">Whether it opens as a draft rather than ready for review.</param>
public sealed record PullRequestOpening(string Head, string Title, string Body, bool Draft);

/// <summary>
/// Where a repository's pull requests live, and the changes Loadout may make there.
/// </summary>
/// <remarks>
/// Writes only. Reading stays with <see cref="IPullRequestReader"/>, so
/// something that only needs to look is never handed a way to change anything.
/// </remarks>
public interface IRepositoryHost
{
    /// <summary>
    /// Why nothing can be opened on this machine, or null when it can - asked
    /// before a branch is pushed, so a branch never reaches the remote for a
    /// pull request that was never going to open.
    /// </summary>
    string? Unavailable();

    /// <summary>Opens a pull request, returning its address.</summary>
    /// <param name="repository">A checkout of the repository, where the host's CLI is run.</param>
    /// <param name="opening">What to open.</param>
    /// <param name="ct">Cancels the call.</param>
    Task<OperationResult<string>> OpenAsync(string repository, PullRequestOpening opening, CancellationToken ct = default);

    /// <summary>Marks a draft pull request ready for review.</summary>
    /// <param name="repository">A checkout of the repository, where the host's CLI is run.</param>
    /// <param name="pullRequest">The pull request's address.</param>
    /// <param name="ct">Cancels the call.</param>
    Task<OperationResult> MarkReadyAsync(string repository, string pullRequest, CancellationToken ct = default);

    /// <summary>
    /// Asks the host to squash-merge a pull request with this message once its
    /// required checks pass, returning what the host said.
    /// </summary>
    /// <remarks>
    /// The host does the merging, so this returns before anything has merged,
    /// and a pull request with nothing left to wait for may be merged at once.
    /// </remarks>
    /// <param name="repository">A checkout of the repository, where the host's CLI is run.</param>
    /// <param name="pullRequest">The pull request's address.</param>
    /// <param name="subject">The squash commit's subject.</param>
    /// <param name="body">The squash commit's body, which may be empty.</param>
    /// <param name="ct">Cancels the call.</param>
    Task<OperationResult<string>> MergeWhenReadyAsync(
        string repository,
        string pullRequest,
        string subject,
        string body,
        CancellationToken ct = default);
}

/// <summary>Changes pull requests on GitHub through the GitHub CLI.</summary>
/// <remarks>
/// Through gh for the reason <see cref="GhPullRequestReader"/> reads through
/// it: the person's own sign-in and host, with no token passing through Loadout.
/// </remarks>
internal sealed class GhRepositoryHost : IRepositoryHost
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    private readonly IProcessLauncher _processes;
    private readonly IExecutableResolver _resolver;

    public GhRepositoryHost(IProcessLauncher processes, IExecutableResolver resolver)
    {
        _processes = processes;
        _resolver = resolver;
    }

    /// <summary>The arguments gh is given to open <paramref name="opening"/>.</summary>
    internal static IReadOnlyList<string> CreateArguments(PullRequestOpening opening)
    {
        ArgumentNullException.ThrowIfNull(opening);

        return
        [
            "pr", "create",
            .. opening.Draft ? new[] { "--draft" } : [],
            "--head", opening.Head,
            "--title", opening.Title,
            "--body", opening.Body,
        ];
    }

    /// <summary>The arguments gh is given to mark <paramref name="pullRequest"/> ready.</summary>
    internal static IReadOnlyList<string> ReadyArguments(string pullRequest) => ["pr", "ready", pullRequest];

    /// <summary>The arguments gh is given to squash-merge <paramref name="pullRequest"/> once its checks pass.</summary>
    /// <remarks>
    /// The body is always passed, empty or not: left out, gh fills the squash
    /// commit's body with the branch's commit messages, which is not the
    /// message the person wrote.
    /// </remarks>
    internal static IReadOnlyList<string> MergeArguments(string pullRequest, string subject, string body) =>
        ["pr", "merge", pullRequest, "--squash", "--auto", "--subject", subject, "--body", body];

    /// <inheritdoc />
    public string? Unavailable() => _resolver.Resolve("gh") is null ? "the GitHub CLI is not on PATH" : null;

    /// <inheritdoc />
    public async Task<OperationResult<string>> OpenAsync(string repository, PullRequestOpening opening, CancellationToken ct = default)
    {
        if (_resolver.Resolve("gh") is not { } gh)
        {
            return OperationResult<string>.Fail("the GitHub CLI is not on PATH", ExitCode.AgentUnavailable);
        }

        var ran = await RunAsync(gh, repository, CreateArguments(opening), "open it", ct).ConfigureAwait(false);

        return ran.Failed
            ? ran
            : OperationResult<string>.Ok(ran.Value!.Split('\n').LastOrDefault()?.Trim() ?? string.Empty);
    }

    /// <inheritdoc />
    public async Task<OperationResult> MarkReadyAsync(string repository, string pullRequest, CancellationToken ct = default)
    {
        if (_resolver.Resolve("gh") is not { } gh)
        {
            return OperationResult.Fail("the GitHub CLI is not on PATH", ExitCode.AgentUnavailable);
        }

        var ran = await RunAsync(gh, repository, ReadyArguments(pullRequest), "mark it ready", ct).ConfigureAwait(false);

        return ran.Failed ? OperationResult.Fail(ran.Error!, ran.ExitCode) : OperationResult.Ok();
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> MergeWhenReadyAsync(
        string repository,
        string pullRequest,
        string subject,
        string body,
        CancellationToken ct = default)
    {
        if (_resolver.Resolve("gh") is not { } gh)
        {
            return OperationResult<string>.Fail("the GitHub CLI is not on PATH", ExitCode.AgentUnavailable);
        }

        return await RunAsync(gh, repository, MergeArguments(pullRequest, subject, body), "merge it", ct).ConfigureAwait(false);
    }

    /// <summary>Runs gh, returning what it printed, or what it said when it refused.</summary>
    private async Task<OperationResult<string>> RunAsync(
        string gh,
        string repository,
        IReadOnlyList<string> arguments,
        string doing,
        CancellationToken ct)
    {
        var ran = await _processes.RunAsync(new ProcessRequest(gh, arguments, repository), Timeout, ct)
            .ConfigureAwait(false);

        if (ran.Failed || ran.Value?.Succeeded != true)
        {
            return OperationResult<string>.Fail(
                $"gh could not {doing}: " + (ran.Value?.StandardError.Trim() is { Length: > 0 } said ? said : ran.Error),
                ExitCode.GeneralFailure);
        }

        return OperationResult<string>.Ok(ran.Value.StandardOutput.Trim());
    }
}
