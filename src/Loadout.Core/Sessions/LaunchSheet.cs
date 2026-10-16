using Loadout.Core.Context;
using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Core.Workspace;
using Loadout.Models.Instructions;
using Loadout.Models.Projects;
using Loadout.Models.Tasks;

namespace Loadout.Core.Sessions;

/// <summary>A task the project has on record, as the sheet offers it.</summary>
/// <param name="Id">What <c>--task-id</c> takes.</param>
/// <param name="Title">What is shown, and what chooses specialists.</param>
/// <param name="State">Doing, open or blocked; nothing finished is offered.</param>
public sealed record DeclaredTask(string Id, string Title, TaskState State);

/// <summary>One thing that can be picked, and what picking it means.</summary>
/// <param name="Label">What is shown.</param>
/// <param name="Value">
/// What reaches the launch, or null for "the default", which a launch spells
/// as the absence of the option.
/// </param>
public sealed record LaunchChoice(string Label, string? Value);

/// <summary>
/// What a project offers a launch to choose between, beyond agents and modes.
/// </summary>
/// <param name="Profiles">Context profiles the chosen agent can use, the default first.</param>
/// <param name="Worktrees">Working trees of the repository, the main one first.</param>
/// <param name="Declared">
/// What this project has recorded itself as working on: doing, then open,
/// then blocked, newest first within each.
/// <para>
/// The task line decides which specialists a session is given, and it was
/// filled in twice in a hundred and twenty-two launches: a blank field asking
/// for a sentence is answered by starting work instead. What a project has
/// already declared is the one description of the work that exists before the
/// sheet opens, so the sheet offers it rather than asking again.
/// </para>
/// </param>
public sealed record LaunchChoices(
    IReadOnlyList<LaunchChoice> Profiles,
    IReadOnlyList<LaunchChoice> Worktrees,
    IReadOnlyList<DeclaredTask> Declared)
{
    /// <summary>A project with nothing to choose: one profile, one working tree.</summary>
    public static readonly LaunchChoices None = new(
        [new LaunchChoice("default", null)],
        [new LaunchChoice("main working tree", null)],
        []);
}

/// <summary>What the preview is asked about: the launch as it stands.</summary>
public sealed record LaunchPreviewRequest(
    ProjectResolution Project,
    string Agent,
    string? Profile,
    string? Task,
    string? Mode);

/// <summary>
/// What a launch sheet offers and what it would load: the one answer the
/// terminal launcher's sheet and Studio's New session sheet both show.
/// </summary>
/// <remarks>
/// It lived in the terminal launcher, which was the only sheet there was.
/// A second sheet asking a second implementation would be one that could
/// describe a different session from the one it starts, so both ask here,
/// and here asks the same resolver the launch itself reads.
/// </remarks>
public sealed class LaunchSheet
{
    private readonly IWorkspaceManager _workspace;
    private readonly IContextCompiler _compiler;
    private readonly IInstructionService _instructions;
    private readonly IGitManager _git;
    private readonly ITaskService _tasks;

    public LaunchSheet(
        IWorkspaceManager workspace,
        IContextCompiler compiler,
        IInstructionService instructions,
        IGitManager git,
        ITaskService tasks)
    {
        _workspace = workspace;
        _compiler = compiler;
        _instructions = instructions;
        _git = git;
        _tasks = tasks;
    }

    /// <summary>
    /// The profiles the agent can use and the working trees the repository
    /// has, for the sheet to offer.
    /// </summary>
    public async Task<LaunchChoices> ChoicesAsync(
        ProjectResolution project,
        string agentName,
        CancellationToken ct)
    {
        var profiles = LaunchChoices.None.Profiles;
        var worktrees = LaunchChoices.None.Worktrees;

        var manifest = await _workspace.ReadProjectAsync(project.Entry.Slug, ct).ConfigureAwait(false);

        if (manifest.Succeeded)
        {
            // The default profile is the absence of one, which is how the
            // launch spells it; the rest go by name.
            profiles = _compiler.ListProfiles(manifest.Value!, agentName)
                .Select((name, index) => new LaunchChoice(
                    manifest.Value!.Profiles.TryGetValue(name, out var profile)
                        && !string.IsNullOrWhiteSpace(profile.Description)
                        ? $"{name}  ({profile.Description})"
                        : name,
                    index == 0 ? null : name))
                .ToList();
        }

        if (project.LocalPath is { Length: > 0 } path)
        {
            var listed = await _git.ListWorktreesAsync(path, ct).ConfigureAwait(false);

            if (listed.Succeeded && listed.Value!.Count > 0)
            {
                // Named the way --worktree expects: by branch, or by directory
                // where there is no branch. The primary is the main working
                // tree, which a launch spells as no worktree at all.
                worktrees = listed.Value
                    .OrderByDescending(w => w.IsPrimary)
                    .Select(w => new LaunchChoice(
                        w.IsPrimary
                            ? $"{w.Branch ?? Path.GetFileName(w.Path)}  (main working tree)"
                            : w.Branch ?? Path.GetFileName(w.Path),
                        w.IsPrimary ? null : w.Branch ?? Path.GetFileName(w.Path)))
                    .ToList();
            }
        }

        // What the project says it is working on, in the order somebody would
        // pick from: being worked on first, then not started, then waiting.
        // Finished and abandoned work is left out — it describes what a session
        // is about to do, and those two no longer describe anything.
        // Every one, not the first eight: the sheet's list scrolls, and a task
        // left off it is one that can only be reached by typing its title.
        var declared = new List<DeclaredTask>();
        var tasks = await _tasks.ListAsync(project.Entry.Slug, ct).ConfigureAwait(false);

        if (tasks.Succeeded)
        {
            declared = tasks.Value!
                .Where(item => item.Kind == TaskKind.Task)
                .Where(item => item.State is TaskState.Doing or TaskState.Open or TaskState.Blocked)
                .OrderBy(item => item.State switch
                {
                    TaskState.Doing => 0,
                    TaskState.Open => 1,
                    _ => 2,
                })
                .ThenByDescending(item => item.DeclaredUtc)
                .Where(item => !string.IsNullOrWhiteSpace(item.Title))
                .Select(item => new DeclaredTask(item.Id, item.Title, item.State))
                .ToList();
        }

        return new LaunchChoices(profiles, worktrees, declared);
    }

    /// <summary>
    /// What a launch as described would load: the same question the launch
    /// asks, put to the same resolver.
    /// </summary>
    public async Task<EffectiveInstructions?> PreviewAsync(
        LaunchPreviewRequest request,
        CancellationToken ct)
    {
        var manifest = await _workspace
            .ReadProjectAsync(request.Project.Entry.Slug, ct)
            .ConfigureAwait(false);

        var tasks = await _tasks.ListAsync(request.Project.Entry.Slug, ct).ConfigureAwait(false);

        var (task, mode, named) = PreviewedSession(
            request, tasks.Succeeded && ProjectOnboardingTask.Pending(tasks.Value!));

        var resolved = await _instructions.ResolveAsync(
            new InstructionRequest(
                manifest.Succeeded ? manifest.Value : null,
                RepositoryPath: request.Project.LocalPath,
                WorkspacePath: _workspace.LocalPath,
                AgentName: request.Agent,
                ProfileName: request.Profile,
                Task: task,
                Explicit: named,
                Mode: mode),
            ct).ConfigureAwait(false);

        if (resolved.Failed)
        {
            throw new InvalidOperationException(resolved.Error);
        }

        return resolved.Value;
    }

    /// <summary>
    /// The task, mode and named specialists the preview resolves for, which
    /// are the ones the launch will use.
    /// </summary>
    /// <remarks>
    /// The launch turns a session with no task of its own into the project's
    /// onboarding when one is pending, so the preview has to as well, or the
    /// sheet would describe a session that is not the one about to start.
    /// </remarks>
    public static (string? Task, string? Mode, IReadOnlyList<string>? Named) PreviewedSession(
        LaunchPreviewRequest request,
        bool onboardingPending)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ProjectOnboardingTask.TurnFor(onboardingPending, request.Task, attended: true)
            != OnboardingTurn.Onboard)
        {
            return (request.Task, request.Mode, null);
        }

        var (task, mode, named) = ProjectOnboardingTask.Onboarding(request.Mode, null);

        return (task, mode, named);
    }
}
