using Loadout.Agents.Ideas;
using Loadout.Core.Ideas;
using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Tasks;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents.Tasks;

/// <summary>What to propose done-when for.</summary>
/// <param name="Project">The task's project, or null for the workspace-wide list.</param>
/// <param name="Task">The task's id.</param>
/// <param name="AgentName">The agent to ask, or null for the default.</param>
/// <param name="Model">The model to ask for, or null for the agent's own.</param>
/// <param name="DryRun">Say what would be started, and start nothing.</param>
public sealed record DoneWhenRequest(
    string? Project,
    string Task,
    string? AgentName = null,
    string? Model = null,
    bool DryRun = false);

/// <summary>What was proposed, and what it took.</summary>
/// <param name="Task">The task as it was when asked.</param>
/// <param name="Proposal">What the agent proposed, or null for a dry run.</param>
/// <param name="Plan">How the agent was, or would have been, started.</param>
/// <param name="Warnings">Anything the launch had to say.</param>
/// <param name="CostUsd">What the round cost.</param>
public sealed record DoneWhenOutcome(
    TaskItem Task,
    DoneWhenProposal? Proposal,
    LaunchPlan Plan,
    IReadOnlyList<string> Warnings,
    decimal CostUsd);

/// <summary>Asks an agent what a task's done-when should be. Records nothing.</summary>
public interface IDoneWhenProposer
{
    Task<OperationResult<DoneWhenOutcome>> ProposeAsync(DoneWhenRequest request, CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// One round, through the same headless start the idea refiner uses, with the
/// same read-only tools, in the project's own checkout: a proposal that names
/// the real test or command is one a lead can check, where a guess at one is
/// not.
/// </para>
/// <para>
/// The task's accepted plan, when it has one, is in the prompt as it was
/// written, because the plan is where the person agreed what the work is.
/// </para>
/// </remarks>
internal sealed class DoneWhenProposer : IDoneWhenProposer
{
    private readonly ITaskService _tasks;
    private readonly IIdeaService _ideas;
    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly IDetachedLauncher _launcher;
    private readonly IPlatformPaths _paths;

    public DoneWhenProposer(
        ITaskService tasks,
        IIdeaService ideas,
        IProjectService projects,
        IWorkspaceManager workspace,
        IDetachedLauncher launcher,
        IPlatformPaths paths)
    {
        _tasks = tasks;
        _ideas = ideas;
        _projects = projects;
        _workspace = workspace;
        _launcher = launcher;
        _paths = paths;
    }

    /// <summary>Why a done-when cannot be proposed for a task, or null when it can.</summary>
    public static string? Rejection(TaskItem? task, string id, string? project) => task switch
    {
        null => $"There is no task '{id}' on {TaskPaths.Describe(project)}.",
        { Kind: TaskKind.Idea } =>
            $"'{task.Id}' is an idea. Refine it, and its plan proposes done-when that accepting it confirms.",
        { State: TaskState.Done or TaskState.Dropped } =>
            $"'{task.Id}' is {task.State.ToString().ToLowerInvariant()}, so there is no finish line left to set.",
        _ => null,
    };

    /// <inheritdoc />
    public async Task<OperationResult<DoneWhenOutcome>> ProposeAsync(DoneWhenRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var listed = await _tasks.ListAsync(request.Project, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<DoneWhenOutcome>.Fail(listed.Error!, listed.ExitCode);
        }

        var task = TaskRuns.Find(request.Task, listed.Value!);

        if (Rejection(task, request.Task, request.Project) is { } refused)
        {
            return OperationResult<DoneWhenOutcome>.Fail(refused, ExitCode.InvalidArguments);
        }

        var plan = await PlanAsync(request.Project, task!, ct).ConfigureAwait(false);

        // In the project's checkout when it has one, so the agent can read the
        // code it is setting a finish line for; otherwise somewhere that is
        // sure to exist.
        var directory = request.Project is { } slug
            && (await _projects.ResolveAsync(slug, ct).ConfigureAwait(false)).Value?.LocalPath is { Length: > 0 } checkout
                ? checkout
                : _paths.Paths.State;

        var asked = await SchemaRound.AskAsync(
            _launcher,
            new DetachedLaunchRequest(
                directory, $"Proposing a done-when for '{task!.Id}'", request.AgentName, request.Model, request.DryRun),
            IdeaRefiner.Allowed,
            DoneWhenSchema.Version1,
            DoneWhenSchema.Version,
            DoneWhenWork.Prompt(task, request.Project, plan),
            DoneWhenWork.Read,
            null,
            ct).ConfigureAwait(false);

        if (asked.Failed)
        {
            return OperationResult<DoneWhenOutcome>.Fail(asked.Error!, asked.ExitCode);
        }

        var round = asked.Value!;

        if (!round.Started)
        {
            return OperationResult<DoneWhenOutcome>.Ok(new DoneWhenOutcome(task, null, round.Plan, round.Warnings, 0m));
        }

        return round.Answer.Succeeded
            ? OperationResult<DoneWhenOutcome>.Ok(
                new DoneWhenOutcome(task, round.Answer.Value!, round.Plan, round.Warnings, round.CostUsd))
            : OperationResult<DoneWhenOutcome>.Fail(round.Answer.Error!, ExitCode.GeneralFailure);
    }

    /// <summary>The accepted plan a task came from, as written, or null.</summary>
    private async Task<string?> PlanAsync(string? project, TaskItem task, CancellationToken ct)
    {
        var record = await _ideas.ReadAsync(new IdeaPlace(project, task.Id), ct).ConfigureAwait(false);

        if (record.Failed || record.Value!.Accepted is not { PlanPath.Length: > 0 } accepted || !_workspace.IsAvailable())
        {
            return null;
        }

        var path = Path.Combine(_workspace.LocalPath, accepted.PlanPath);

        try
        {
            return File.Exists(path) ? await File.ReadAllTextAsync(path, ct).ConfigureAwait(false) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
