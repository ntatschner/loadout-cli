using Loadout.Core.Git;
using Loadout.Models;
using Loadout.Models.Results;

namespace Loadout.Tests.Fakes;

/// <summary>
/// Pull requests held in memory, so nothing reaches GitHub. Each can be
/// edited between calls, the way somebody edits a description.
/// </summary>
public sealed class FakePullRequests : IPullRequestReader
{
    /// <summary>The pull requests, by number. Merged ones are those listed by <see cref="MergedAsync"/>.</summary>
    public Dictionary<int, PullRequestText> Known { get; } = [];

    /// <summary>When set, every read fails with this, as gh does when it is missing or signed out.</summary>
    public string? Unavailable { get; set; }

    /// <summary>How many times anything was read, so a test can say gh was never asked.</summary>
    public int Reads { get; private set; }

    /// <inheritdoc />
    public Task<OperationResult<PullRequestText>> ReadAsync(string repository, int number, CancellationToken ct = default)
    {
        Reads++;

        if (Unavailable is not null)
        {
            return Task.FromResult(OperationResult<PullRequestText>.Fail(Unavailable, ExitCode.AgentUnavailable));
        }

        return Task.FromResult(Known.TryGetValue(number, out var found)
            ? OperationResult<PullRequestText>.Ok(found)
            : OperationResult<PullRequestText>.Fail($"no pull request #{number}", ExitCode.GeneralFailure));
    }

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<PullRequestText>>> MergedAsync(string repository, int limit, CancellationToken ct = default)
    {
        Reads++;

        return Task.FromResult(Unavailable is not null
            ? OperationResult<IReadOnlyList<PullRequestText>>.Fail(Unavailable, ExitCode.AgentUnavailable)
            : OperationResult<IReadOnlyList<PullRequestText>>.Ok(Known.Values.OrderByDescending(p => p.Number).Take(limit).ToList()));
    }
}
