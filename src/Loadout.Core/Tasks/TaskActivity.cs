using Loadout.Core.Sessions;
using Loadout.Core.Teams;
using Loadout.Models.Agents;

namespace Loadout.Core.Tasks;

/// <summary>How much has been started on each of a project's tasks: team runs, and sessions launched on it.</summary>
/// <param name="Runs">Team runs started for each task, by its id.</param>
/// <param name="Launches">Sessions launched on each task, by its id.</param>
public sealed record TaskActivity(
    IReadOnlyDictionary<string, int> Runs,
    IReadOnlyDictionary<string, int> Launches)
{
    /// <summary>Nothing counted: every task reads zero.</summary>
    public static readonly TaskActivity None = new(
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Team runs started for the task.</summary>
    public int RunsOf(string id) => Runs.GetValueOrDefault(id);

    /// <summary>Sessions launched on the task.</summary>
    public int LaunchesOf(string id) => Launches.GetValueOrDefault(id);

    /// <summary>
    /// What was started on each of this project's tasks. A run or launch with
    /// no task, or on another project, counts for nothing here.
    /// </summary>
    public static TaskActivity Count(string slug, IEnumerable<RunSummary> runs, IEnumerable<LaunchRecord> launches)
    {
        ArgumentNullException.ThrowIfNull(slug);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(launches);

        return new TaskActivity(
            Tally(runs.Where(run => string.Equals(run.Project, slug, StringComparison.OrdinalIgnoreCase)).Select(run => run.Task)),
            Tally(launches.Where(launch => string.Equals(launch.ProjectSlug, slug, StringComparison.OrdinalIgnoreCase)).Select(launch => launch.TaskId)));
    }

    private static Dictionary<string, int> Tally(IEnumerable<string?> ids)
    {
        var counted = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in ids)
        {
            if (id is { Length: > 0 })
            {
                counted[id] = counted.GetValueOrDefault(id) + 1;
            }
        }

        return counted;
    }
}

/// <summary>Counts what has been started on a project's tasks.</summary>
public interface ITaskActivityReader
{
    /// <summary>What was started on each task of the project, or nothing counted when neither record could be read.</summary>
    Task<TaskActivity> ReadAsync(string slug, CancellationToken ct = default);
}

/// <summary>
/// Reads the run journal and the launch ledger whole, so it is asked only
/// when a list is sorted or filtered on what it counts.
/// </summary>
internal sealed class TaskActivityReader : ITaskActivityReader
{
    private readonly IRunJournal _runs;
    private readonly ILaunchLedger _ledger;

    public TaskActivityReader(IRunJournal runs, ILaunchLedger ledger)
    {
        _runs = runs;
        _ledger = ledger;
    }

    /// <inheritdoc />
    public async Task<TaskActivity> ReadAsync(string slug, CancellationToken ct = default)
    {
        var runs = _runs.List(int.MaxValue)
            .Select(_runs.Summarise)
            .Where(result => result.Succeeded)
            .Select(result => result.Value!);

        var launches = await _ledger.ReadAsync(DateTimeOffset.MinValue, ct).ConfigureAwait(false);

        return TaskActivity.Count(slug, runs, launches.Value ?? []);
    }
}
