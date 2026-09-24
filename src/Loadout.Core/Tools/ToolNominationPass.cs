using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Teams;
using Loadout.Core.Workspace;
using Loadout.Models.Instructions;
using Loadout.Models.Results;
using Loadout.Models.Teams;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Tools;

/// <summary>What the daemon runs before starting a schedule that reads the tool catalogue.</summary>
public interface IToolNominationPass
{
    /// <summary>Files what the nominator finds, when this schedule is one that reads it.</summary>
    /// <param name="schedule">The schedule about to start.</param>
    /// <param name="log">Where a line goes for each thing passed over.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<(ToolNomination Nomination, OperationResult<ToolSubmitted>? Filed)>> BeforeAsync(
        TeamSchedule schedule,
        Action<string>? log = null,
        CancellationToken ct = default);
}

/// <summary>
/// The nominator's pass over finished work, run when something is about to
/// look at the tool catalogue.
/// </summary>
/// <remarks>
/// <para>
/// Run before a run-finished schedule starts its team, and before tool-works
/// starts on its own schedule, so the nominations the Creator reads are the
/// ones finished work produced up to that moment. Nothing else calls it: a
/// nomination nobody reads until tomorrow can wait until tomorrow to be filed.
/// </para>
/// <para>
/// The lessons are every lesson topic in the memory of every registered
/// project. A project whose memory cannot be read is passed over rather than
/// failing the pass; the runs and shelves are still worth reading without it.
/// </para>
/// </remarks>
public sealed class ToolNominationPass : IToolNominationPass
{
    private readonly IToolRegistry _registry;
    private readonly IRunJournal _journal;
    private readonly IRemedyBook _remedies;
    private readonly IPlatformPaths _paths;
    private readonly IMemoryService _memory;
    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;

    // One for the life of the pass, so what it has read of a finished run is
    // not read again on every schedule.
    private readonly ToolNominator _nominator;

    public ToolNominationPass(
        IToolRegistry registry,
        IRunJournal journal,
        IRemedyBook remedies,
        IPlatformPaths paths,
        IMemoryService memory,
        IProjectService projects,
        IWorkspaceManager workspace)
    {
        _registry = registry;
        _journal = journal;
        _remedies = remedies;
        _paths = paths;
        _memory = memory;
        _projects = projects;
        _workspace = workspace;
        _nominator = new ToolNominator(registry, journal, remedies, paths);
    }

    /// <summary>Whether starting this schedule should be preceded by a pass.</summary>
    public static bool Precedes(TeamSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        return string.Equals(schedule.On, ScheduleService.RunFinishedEvent, StringComparison.OrdinalIgnoreCase)
            || string.Equals(schedule.Team, ScheduleService.ToolWorksTeam, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Files what the nominator finds, when this schedule is one that reads it.</summary>
    /// <returns>Each nomination found, as <see cref="ToolNominator.Scan" /> gives it; empty when none is due.</returns>
    public async Task<IReadOnlyList<(ToolNomination Nomination, OperationResult<ToolSubmitted>? Filed)>> BeforeAsync(
        TeamSchedule schedule,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        if (!Precedes(schedule))
        {
            return [];
        }

        var lessons = await LessonsAsync(log, ct).ConfigureAwait(false);

        return _nominator.Scan(lessons);
    }

    /// <summary>The text of every lesson topic of every registered project.</summary>
    private async Task<IReadOnlyList<string>> LessonsAsync(Action<string>? log, CancellationToken ct)
    {
        var listed = await _projects.ListAsync(ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            log?.Invoke("nominating without lessons: the projects could not be listed");
            return [];
        }

        var lessons = new List<string>();

        foreach (var project in listed.Value!)
        {
            IReadOnlyList<MemoryTopic> topics;

            // The memory service leaves out a scope it cannot read rather than
            // failing, so what reaches here unreadable arrives as a throw.
            try
            {
                topics = (await _memory.ListAsync(_workspace.LocalPath, project.Entry.Slug, ct).ConfigureAwait(false))
                    .Value ?? [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log?.Invoke($"passed over the lessons of {project.Entry.Slug}: {ex.Message}");
                continue;
            }

            lessons.AddRange(topics
                .Where(one => one.Kind == MemoryKind.Lesson)
                .Select(one => string.Join('\n', [one.Description, .. one.Facts])));
        }

        return lessons;
    }
}
