using Loadout.Core.Configuration;
using Loadout.Models;
using Loadout.Models.Delivery;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Delivery;

/// <summary>Each project's landings, kept on this machine.</summary>
public interface ILandingService
{
    /// <summary>A project's landings, empty when it has none.</summary>
    Task<OperationResult<ProjectLandings>> LoadAsync(string project, CancellationToken ct = default);

    /// <summary>Every project that has landings on this machine, by the name they are kept under.</summary>
    IReadOnlyList<string> Projects();

    /// <summary>
    /// Records landings in a project, each replacing the one already kept for
    /// its branch where the move from one state to the other is allowed.
    /// </summary>
    /// <returns>The branches that were not recorded, each with why.</returns>
    Task<OperationResult<IReadOnlyList<string>>> RecordAsync(
        string project,
        IReadOnlyList<BranchLanding> landings,
        CancellationToken ct = default);
}

/// <summary>Which moves between landing states are allowed.</summary>
/// <remarks>
/// Forwards only, with two ways back: a draft or ready pull request that is
/// closed unmerged leaves its branch pending again, and an abandoned branch
/// can be picked up again. Nothing leaves merged, so a late write from a run
/// that had not heard can never undo one.
/// </remarks>
public static class LandingMoves
{
    /// <summary>Whether a landing may go from one state to another. Staying put is always allowed.</summary>
    public static bool Allowed(LandingState from, LandingState to) =>
        from == to || (from, to) switch
        {
            (LandingState.Pending, _) => true,
            (LandingState.Draft, LandingState.Ready or LandingState.Merged or LandingState.Abandoned or LandingState.Pending) => true,
            (LandingState.Ready, LandingState.Merged or LandingState.Abandoned or LandingState.Pending) => true,
            (LandingState.Abandoned, LandingState.Pending) => true,
            _ => false,
        };

    /// <summary>The word a state is written as.</summary>
    public static string Word(LandingState state) => state.ToString().ToLowerInvariant();
}

/// <inheritdoc />
/// <remarks>
/// One file per project under <c>delivery</c>, so one project's runs landing
/// never hold another's lock.
/// </remarks>
public sealed class LandingService : ILandingService
{
    /// <summary>
    /// How many merged or abandoned landings a project keeps. Those still
    /// waiting are always kept, however many there are.
    /// </summary>
    public const int KeptFinished = 100;

    private readonly IPlatformPaths _paths;
    private readonly YamlStore _yaml;
    private readonly TimeProvider _time;

    public LandingService(IPlatformPaths paths, YamlStore yaml, TimeProvider time)
    {
        _paths = paths;
        _yaml = yaml;
        _time = time;
    }

    /// <summary>Why a project's name cannot name a landings file, or null when it can.</summary>
    /// <remarks>Refused rather than cleaned, so two projects can never end up sharing a file.</remarks>
    public static string? Rejection(string project) =>
        string.IsNullOrWhiteSpace(project)
        || project.StartsWith('.')
        || !project.All(one => char.IsAsciiLetterOrDigit(one) || one is '-' or '_' or '.')
            ? $"'{project}' can't name a project's landings: a project's name is letters, digits, '-', '_' and '.'."
            : null;

    private string Directory => Path.Combine(_paths.Paths.State, "delivery");

    private string PathFor(string project) => Path.Combine(Directory, project.Trim().ToLowerInvariant() + ".yaml");

    /// <inheritdoc />
    public IReadOnlyList<string> Projects() =>
        System.IO.Directory.Exists(Directory)
            ? [.. System.IO.Directory.EnumerateFiles(Directory, "*.yaml")
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Where(name => Rejection(name) is null)
                .Order(StringComparer.Ordinal)]
            : [];

    /// <inheritdoc />
    public async Task<OperationResult<ProjectLandings>> LoadAsync(string project, CancellationToken ct = default)
    {
        if (Rejection(project) is { } wrong)
        {
            return OperationResult<ProjectLandings>.Fail(wrong, ExitCode.InvalidArguments);
        }

        return await _yaml.LoadAsync(PathFor(project), () => new ProjectLandings(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<string>>> RecordAsync(
        string project,
        IReadOnlyList<BranchLanding> landings,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(landings);

        if (Rejection(project) is { } wrong)
        {
            return OperationResult<IReadOnlyList<string>>.Fail(wrong, ExitCode.InvalidArguments);
        }

        var now = _time.GetUtcNow();
        var skipped = new List<string>();

        var written = await _yaml.UpdateAsync<ProjectLandings>(
            PathFor(project),
            () => new ProjectLandings(),
            kept =>
            {
                foreach (var landing in landings.Where(one => one.Branch.Length > 0))
                {
                    var index = kept.Landings.FindIndex(one => string.Equals(one.Branch, landing.Branch, StringComparison.Ordinal));

                    if (index < 0)
                    {
                        landing.CreatedUtc = landing.CreatedUtc == default ? now : landing.CreatedUtc;
                        landing.UpdatedUtc = now;
                        kept.Landings.Add(landing);

                        continue;
                    }

                    var was = kept.Landings[index];

                    // Told again that it merged, as the queue's merge check
                    // tells it every time it looks: nothing has changed.
                    if (was.State == landing.State && was.State is LandingState.Merged or LandingState.Abandoned)
                    {
                        continue;
                    }

                    if (!LandingMoves.Allowed(was.State, landing.State))
                    {
                        skipped.Add(
                            $"{landing.Branch} is {LandingMoves.Word(was.State)}, and can't go back to {LandingMoves.Word(landing.State)}");

                        continue;
                    }

                    landing.CreatedUtc = was.CreatedUtc;
                    landing.UpdatedUtc = now;
                    kept.Landings[index] = landing;
                }

                var finished = kept.Landings
                    .Where(one => one.State is LandingState.Merged or LandingState.Abandoned)
                    .OrderBy(one => one.UpdatedUtc)
                    .ToList();

                foreach (var old in finished.Take(Math.Max(0, finished.Count - KeptFinished)))
                {
                    kept.Landings.Remove(old);
                }
            },
            true,
            ct).ConfigureAwait(false);

        return written.Succeeded
            ? OperationResult<IReadOnlyList<string>>.Ok(skipped)
            : OperationResult<IReadOnlyList<string>>.Fail(written.Error!, written.ExitCode);
    }
}
