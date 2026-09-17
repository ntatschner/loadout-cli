using Loadout.Core.Diagnostics;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models.Diagnostics;
using Loadout.Models.Tasks;

namespace Loadout.Core.Tasks;

/// <summary>
/// Finds projects writing down what they are working on and showing it to
/// nobody.
/// <para>
/// A project carries its open tasks into every session only when it is asked
/// to, and most projects are never asked. So a session records where something
/// stands, the next session is not shown it, and the record quietly becomes a
/// place work goes to be forgotten. Nothing about either session looks wrong,
/// which is why this needs saying out loud rather than being left to be
/// noticed.
/// </para>
/// <para>
/// Evidence, not advice. A project with nothing recorded is not mentioned,
/// because "you could turn this on" about an empty record is the kind of
/// suggestion that teaches people to skim past suggestions. This only speaks
/// when there is something being written down that nobody is reading.
/// </para>
/// <para>
/// Reported as a suggestion rather than a warning. The verdict is the worst
/// severity in the report, and a project that has simply not opted into an
/// optional feature has nothing wrong with it.
/// </para>
/// </summary>
internal sealed class TaskContextDiagnosticContributor : IDiagnosticContributor
{
    private const string Category = "Instructions";

    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly ITaskService _tasks;

    public TaskContextDiagnosticContributor(
        IProjectService projects,
        IWorkspaceManager workspace,
        ITaskService tasks)
    {
        _projects = projects;
        _workspace = workspace;
        _tasks = tasks;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiagnosticCheck>> ContributeAsync(CancellationToken ct = default)
    {
        if (!_workspace.IsAvailable())
        {
            return [];
        }

        var listed = await _projects.ListAsync(ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            // The workspace and registry are reported on by the doctor already.
            return [];
        }

        var checks = new List<DiagnosticCheck>();

        foreach (var project in listed.Value!)
        {
            ct.ThrowIfCancellationRequested();

            var slug = project.Entry.Slug;

            var manifest = await _workspace.ReadProjectAsync(slug, ct).ConfigureAwait(false);

            if (manifest.Failed || manifest.Value!.Context.Tasks)
            {
                continue;
            }

            var tasks = await _tasks.ListAsync(slug, ct).ConfigureAwait(false);

            if (tasks.Failed)
            {
                continue;
            }

            if (Suggestion(slug, tasks.Value!) is { } check)
            {
                checks.Add(check);
            }
        }

        return checks;
    }

    /// <summary>
    /// The suggestion for one project whose sessions are not shown its tasks,
    /// or null when it has nothing worth showing them.
    /// </summary>
    /// <remarks>
    /// Separated from the walk over projects so the decision can be tested
    /// without standing up a workspace, a registry and a task store to reach
    /// it. What is worth asserting is which records earn a suggestion, and that
    /// is the whole of this method.
    /// </remarks>
    internal static DiagnosticCheck? Suggestion(string slug, IReadOnlyList<TaskItem> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        // Finished and dropped work is not waiting on anybody, so a project
        // whose record holds only those is not being ignored — it is done.
        var waiting = tasks
            .Count(task => task.State is TaskState.Open or TaskState.Doing or TaskState.Blocked);

        return waiting == 0
            ? null
            : DiagnosticCheck.Suggest(
                Category,
                $"Tasks nobody is shown: {slug}",
                $"{waiting} task(s) are recorded for {slug} and its sessions are not shown any of "
                + "them. Carrying them costs a heading and a line each.",
                new Remedy(
                    RemedyKind.CarryProjectContext,
                    $"Carry {slug}'s open tasks into every session.",
                    $"{slug}=tasks"));
    }
}
