using Loadout.Core.Configuration;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Teams;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Teams;

/// <summary>Each team's queue of fully specified tasks, kept on this machine.</summary>
public interface IQueueService
{
    /// <summary>A team's queue, empty when it has never had one.</summary>
    Task<OperationResult<TeamQueue>> LoadAsync(string team, CancellationToken ct = default);

    /// <summary>
    /// Changes a team's queue under its lock, writing it only when the change succeeds.
    /// </summary>
    /// <param name="team">The team.</param>
    /// <param name="change">The change, returning why it was refused, or null when it was made.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The queue as written, or the refusal.</returns>
    Task<OperationResult<TeamQueue>> UpdateAsync(
        string team,
        Func<TeamQueue, string?> change,
        CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// One file per team, under <c>teams/queues</c> beside the schedules, so one
/// team's queue being worked through never holds another's lock. It holds the
/// record and nothing else: deciding what runs is <see cref="QueueRules"/>'s,
/// and starting it is the same command a person would type.
/// </remarks>
public sealed class QueueService : IQueueService
{
    private readonly IPlatformPaths _paths;
    private readonly YamlStore _yaml;

    public QueueService(IPlatformPaths paths, YamlStore yaml)
    {
        _paths = paths;
        _yaml = yaml;
    }

    /// <summary>Why a team's name cannot name a queue file, or null when it can.</summary>
    /// <remarks>Refused rather than cleaned, so two teams can never end up sharing a file.</remarks>
    public static string? Rejection(string team) =>
        string.IsNullOrWhiteSpace(team)
        || team.StartsWith('.')
        || !team.All(one => char.IsAsciiLetterOrDigit(one) || one is '-' or '_' or '.')
            ? $"'{team}' can't name a queue: a team's name is letters, digits, '-', '_' and '.'."
            : null;

    private string PathFor(string team) =>
        Path.Combine(_paths.Paths.State, "teams", "queues", team.Trim().ToLowerInvariant() + ".yaml");

    /// <inheritdoc />
    public async Task<OperationResult<TeamQueue>> LoadAsync(string team, CancellationToken ct = default)
    {
        if (Rejection(team) is { } wrong)
        {
            return OperationResult<TeamQueue>.Fail(wrong, ExitCode.InvalidArguments);
        }

        return await _yaml.LoadAsync(PathFor(team), () => new TeamQueue(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<TeamQueue>> UpdateAsync(
        string team,
        Func<TeamQueue, string?> change,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (Rejection(team) is { } wrong)
        {
            return OperationResult<TeamQueue>.Fail(wrong, ExitCode.InvalidArguments);
        }

        string? refused = null;
        TeamQueue? kept = null;

        OperationResult<TeamQueue> written;

        try
        {
            written = await _yaml.UpdateAsync<TeamQueue>(
                PathFor(team),
                () => new TeamQueue(),
                queue =>
                {
                    refused = change(queue);

                    if (refused is not null)
                    {
                        // Thrown so the store writes nothing: a refused change
                        // must leave the file exactly as it was.
                        throw new QueueRefused(refused);
                    }

                    if (queue.Runs.Count > QueueRules.Kept)
                    {
                        queue.Runs.RemoveRange(0, queue.Runs.Count - QueueRules.Kept);
                    }

                    kept = queue;
                },
                true,
                ct).ConfigureAwait(false);
        }
        catch (QueueRefused)
        {
            return OperationResult<TeamQueue>.Fail(refused!, ExitCode.InvalidArguments);
        }

        return written.Succeeded && kept is not null
            ? OperationResult<TeamQueue>.Ok(kept)
            : OperationResult<TeamQueue>.Fail(written.Error ?? "The queue could not be written.", written.ExitCode);
    }

    private sealed class QueueRefused(string why) : Exception(why);
}
