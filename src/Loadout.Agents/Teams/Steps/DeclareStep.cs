using Loadout.Core.Tasks;
using Loadout.Models.Tasks;
using Loadout.Models.Teams;

namespace Loadout.Agents.Teams.Steps;

/// <summary>Says where a run has got to on the project's task list.</summary>
internal sealed class DeclareStep
{
    private readonly ITaskService? _tasks;

    public DeclareStep(ITaskService? tasks)
    {
        _tasks = tasks;
    }

    /// <summary>
    /// Puts the run in the project's task list, so what a team is doing shows
    /// up beside everything else the project is working on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One task for the run, not one per node. The list is a shared file that
    /// travels with the workspace and is meant to hold tens of things; a
    /// row per node would put hundreds there within a few runs, and the
    /// per-node view already exists in <c>team status</c>, which reads the
    /// run's own journal.
    /// </para>
    /// <para>
    /// A run started for somebody's task moves that task instead, and leaves
    /// its title and note as they were: they are the person's description of
    /// the work, and a run that overwrote them with its own status line would
    /// lose the one thing nobody can get back from the journal. Which run has
    /// it is said in who declared it, and <c>team status</c> has the rest.
    /// </para>
    /// <para>
    /// A failure here is not the run's failure. The task list is a
    /// convenience beside the journal, which is the record, so this reports
    /// nothing and stops nothing. It writes nothing to the journal either:
    /// it is handed the journal only so that every step is called the same way.
    /// </para>
    /// </remarks>
    public async Task RunAsync(
        RunContext run,
        JournalWriter journal,
        string slug,
        string runId,
        string? task,
        TeamDefinition team,
        TaskState state,
        string goal,
        string note,
        CancellationToken ct)
    {
        if (_tasks is null)
        {
            return;
        }

        if (task is { Length: > 0 })
        {
            await _tasks.DeclareAsync(slug, task, state, $"team {team.Name}, run {runId}", ct: ct)
                .ConfigureAwait(false);

            return;
        }

        await _tasks.DeclareAsync(
            slug,
            $"team-{runId}",
            state,
            $"team {team.Name}",
            Cut(goal),
            note,
            ct).ConfigureAwait(false);
    }

    /// <summary>A goal short enough to read in a list.</summary>
    private static string Cut(string goal)
    {
        var trimmed = goal.Trim();

        return trimmed.Length > 120 ? trimmed[..120] + "..." : trimmed;
    }
}
