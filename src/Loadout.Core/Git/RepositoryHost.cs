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

    /// <inheritdoc />
    public string? Unavailable() => _resolver.Resolve("gh") is null ? "the GitHub CLI is not on PATH" : null;

    /// <inheritdoc />
    public async Task<OperationResult<string>> OpenAsync(string repository, PullRequestOpening opening, CancellationToken ct = default)
    {
        if (_resolver.Resolve("gh") is not { } gh)
        {
            return OperationResult<string>.Fail("the GitHub CLI is not on PATH", ExitCode.AgentUnavailable);
        }

        var ran = await _processes.RunAsync(new ProcessRequest(gh, CreateArguments(opening), repository), Timeout, ct)
            .ConfigureAwait(false);

        if (ran.Failed || ran.Value?.Succeeded != true)
        {
            return OperationResult<string>.Fail(
                "gh could not open it: " + (ran.Value?.StandardError.Trim() is { Length: > 0 } said ? said : ran.Error),
                ExitCode.GeneralFailure);
        }

        return OperationResult<string>.Ok(ran.Value.StandardOutput.Trim().Split('\n').LastOrDefault()?.Trim() ?? string.Empty);
    }
}
