using Loadout.Core.Configuration;
using Loadout.Core.Projects;
using Loadout.Models.Projects;
using Loadout.Core.Tasks;
using Loadout.Core.Teams;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Capabilities;
using Loadout.Models.Results;
using Loadout.Models.Tasks;
using Loadout.Models.Teams;

namespace Loadout.Core.Capabilities;

/// <summary>One gap on one piece of work, and what can be done about it.</summary>
/// <param name="Check">The check, by its id.</param>
/// <param name="Name">What it is, in a few words.</param>
/// <param name="State">What the profile found: a gap, or for release possibly manual release.</param>
/// <param name="Evidence">The one line it found.</param>
/// <param name="CanOffer">Whether a shipped template can fill it. A specialist's check cannot.</param>
/// <param name="Stacks">What the stack key cycles through, the default first. Empty when stacks do not apply.</param>
/// <param name="Environments">The names to fill in, for the environments template.</param>
/// <param name="Prerequisite">An open task already made for this check, or null.</param>
/// <param name="Linked">Whether this work already requires that task.</param>
public sealed record CapabilityOffer(
    string Check,
    string Name,
    CapabilityState State,
    string Evidence,
    bool CanOffer,
    IReadOnlyList<string> Stacks,
    IReadOnlyList<string> Environments,
    string? Prerequisite,
    bool Linked);

/// <summary>A check that matters to the work and could not be looked at, and what would let it be.</summary>
public sealed record CapabilityUnseen(string Check, CapabilityState State, string Hint);

/// <summary>A link from a piece of work to a prerequisite made from a template.</summary>
/// <param name="Check">The check the prerequisite exists to fix.</param>
/// <param name="Prerequisite">The prerequisite, by id.</param>
/// <param name="Outgrown">Whether the work's type no longer depends on that check, so the link may be left from an old type.</param>
public sealed record CapabilityLink(string Check, string Prerequisite, bool Outgrown);

/// <summary>What is missing before one piece of work, as of a profile.</summary>
/// <param name="Project">The project, by slug.</param>
/// <param name="Task">The waiting work, by id.</param>
/// <param name="Built">When the profile was built.</param>
/// <param name="GitHub">How GitHub was read for it.</param>
/// <param name="Offers">The gaps, in catalogue order.</param>
/// <param name="Unseen">Checks that could not be looked at. Never an offer, never a hold.</param>
/// <param name="NotNeeded">Checks this project has said it will do without.</param>
/// <param name="Notes">Worth knowing and never a gap, such as a stack no workflow appears to build.</param>
/// <param name="Links">Its links to open prerequisites made from a template.</param>
public sealed record OfferSheet(
    string Project,
    string Task,
    DateTimeOffset Built,
    string GitHub,
    IReadOnlyList<CapabilityOffer> Offers,
    IReadOnlyList<CapabilityUnseen> Unseen,
    IReadOnlyList<string> NotNeeded,
    IReadOnlyList<string> Notes,
    IReadOnlyList<CapabilityLink>? Links = null);

/// <summary>What the dashboard shows beside one piece of work.</summary>
/// <param name="Seen">Gaps the profile has actually seen.</param>
/// <param name="NoOffer">How many of those no shipped template can fill.</param>
/// <param name="Unseen">Checks that could not be looked at, which are never counted as gaps.</param>
public sealed record GapCount(int Seen, int NoOffer, int Unseen);

/// <summary>What accepting an offer did.</summary>
/// <param name="Prerequisite">The task the work now requires.</param>
/// <param name="Created">False when an open one for this check was reused.</param>
public sealed record OfferAccepted(TaskItem Prerequisite, bool Created);

/// <summary>What a fresh profile did to one prerequisite.</summary>
/// <param name="Task">The prerequisite, by id.</param>
/// <param name="Closed">Whether it was closed. False when a run still owns it and only its dependants were released.</param>
/// <param name="Released">The tasks whose link to it was taken off because a run still owns it.</param>
/// <param name="Reason">The dated line that says why.</param>
public sealed record PrerequisiteSettled(string Task, bool Closed, IReadOnlyList<TaskLink> Released, string Reason);

/// <summary>An open prerequisite written for something that has since changed.</summary>
/// <param name="Task">The prerequisite, by id.</param>
/// <param name="Check">The check it fixes.</param>
/// <param name="Why">What changed, as somebody would say it.</param>
public sealed record StalePrerequisite(string Task, string Check, string Why);

/// <summary>What a 'Not needed' answer touched.</summary>
/// <param name="Prerequisite">The open prerequisite for the check, or null.</param>
/// <param name="Waiting">The tasks that require it.</param>
/// <param name="Released">Whether those links were taken off and the prerequisite dropped.</param>
public sealed record NotNeededOutcome(string? Prerequisite, IReadOnlyList<TaskLink> Waiting, bool Released);

/// <summary>
/// Turns a gap a profile has seen into a frozen prerequisite task joined to the
/// waiting work, and closes those tasks once the capability is really there.
/// </summary>
public interface ICapabilityOffers
{
    /// <summary>What is missing before a task, from the profile last kept, or the repository's files when none is.</summary>
    Task<OperationResult<OfferSheet>> ReadAsync(ProjectResolution project, string taskId, CancellationToken ct = default);

    /// <summary>What is missing before a task on a project named by its slug.</summary>
    Task<OperationResult<OfferSheet>> ReadAsync(string slug, string taskId, CancellationToken ct = default);

    /// <summary>What is missing before every open item on a project's list, looking at the repository once.</summary>
    Task<IReadOnlyDictionary<string, OfferSheet>> SheetsAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Takes off the links a task's type has outgrown, leaving the prerequisites
    /// themselves, which other work may still wait for.
    /// </summary>
    /// <returns>The prerequisites it stopped waiting for.</returns>
    Task<OperationResult<IReadOnlyList<string>>> ReleaseOutgrownAsync(string slug, string taskId, CancellationToken ct = default);

    /// <summary>The links a task's type has outgrown.</summary>
    Task<IReadOnlyList<CapabilityLink>> OutgrownAsync(string slug, string taskId, CancellationToken ct = default);

    /// <summary>
    /// A profile from the repository's files alone, for telling afterwards what
    /// a run's time took away. Null when it cannot be had: this is for a note,
    /// and nothing about a run may fail because of it.
    /// </summary>
    Task<CapabilityProfile?> LookAsync(string slug, CancellationToken ct = default);

    /// <summary>The counts for every open item on a project's list, from the kept profile. Empty when none is kept.</summary>
    Task<IReadOnlyDictionary<string, GapCount>> GapCountsAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Sets up a project made from an idea, which has no repository to look at
    /// yet: a prerequisite for each template not skipped, required by the task,
    /// and 'Not needed' remembered for each skipped.
    /// </summary>
    Task<OperationResult<IReadOnlyList<TaskItem>>> SetUpNewProjectAsync(
        string slug, string taskId, IReadOnlyList<string> skipped, string by, CancellationToken ct = default);

    /// <summary>The answers given for a project.</summary>
    Task<CapabilityAnswers> AnswersAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Accepts an offer: builds the profile afresh, makes or reuses the
    /// prerequisite, and makes the waiting task require it.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="taskId">The waiting task.</param>
    /// <param name="check">The check being filled.</param>
    /// <param name="stack">A stack choice, or null for the default.</param>
    /// <param name="environments">Environment names, or null for the remembered ones.</param>
    /// <param name="by">Who accepted it.</param>
    /// <param name="ct">Cancels it.</param>
    Task<OperationResult<OfferAccepted>> AcceptAsync(
        ProjectResolution project,
        string taskId,
        string check,
        string? stack,
        IReadOnlyList<string>? environments,
        string by,
        CancellationToken ct = default);

    /// <summary>Records that a project will do without a check.</summary>
    /// <param name="project">The project.</param>
    /// <param name="check">The check.</param>
    /// <param name="releaseLinks">Also take off any links holding work on its open prerequisite, and drop it. A person's confirmed choice.</param>
    /// <param name="by">Who said so.</param>
    /// <param name="ct">Cancels it.</param>
    Task<OperationResult<NotNeededOutcome>> NotNeededAsync(ProjectResolution project, string check, bool releaseLinks, string by, CancellationToken ct = default);

    /// <summary>Undoes a 'Not needed' answer, and says which open work the gap now touches.</summary>
    Task<OperationResult<IReadOnlyList<string>>> NeededAsync(ProjectResolution project, string check, CancellationToken ct = default);

    /// <summary>
    /// Looks at the project's open prerequisites against a fresh profile and
    /// closes those it now sees met. With none open it does nothing, GitHub
    /// not asked.
    /// </summary>
    Task<OperationResult<IReadOnlyList<PrerequisiteSettled>>> SettleAsync(ProjectResolution project, string by, CancellationToken ct = default);

    /// <summary>Open prerequisites made from an older template, or for stacks that no longer match.</summary>
    Task<IReadOnlyList<StalePrerequisite>> StaleAsync(string slug, CapabilityProfile profile, CancellationToken ct = default);

    /// <summary>Rewrites an open prerequisite to the current template and stacks. Refused while a run owns it.</summary>
    Task<OperationResult<TaskItem>> RenewAsync(ProjectResolution project, string taskId, string by, CancellationToken ct = default);

    /// <summary>Ticks the step only a person can do on a prerequisite.</summary>
    Task<OperationResult<TaskItem>> TickAsync(string slug, string taskId, string by, CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// A link is only ever made by somebody choosing to, and only a gap the profile
/// has seen can be offered: unknown and no permission are never gaps, so a
/// missing token can never make work wait.
/// </para>
/// <para>
/// Everything that decides builds the profile again rather than reading the one
/// kept. The kept one is for showing.
/// </para>
/// </remarks>
public sealed class CapabilityOffers : ICapabilityOffers
{
    private readonly ICapabilityProfiles _profiles;
    private readonly IProjectService _projects;
    private readonly ITaskService _tasks;
    private readonly IRunJournal _journal;
    private readonly IWorkspaceManager _workspace;
    private readonly YamlStore _yaml;
    private readonly TimeProvider _time;

    public CapabilityOffers(
        ICapabilityProfiles profiles,
        IProjectService projects,
        ITaskService tasks,
        IRunJournal journal,
        IWorkspaceManager workspace,
        YamlStore yaml,
        TimeProvider time)
    {
        _profiles = profiles;
        _projects = projects;
        _tasks = tasks;
        _journal = journal;
        _workspace = workspace;
        _yaml = yaml;
        _time = time;
    }

    /// <summary>Where a project's answers are kept: beside its profile, in the workspace.</summary>
    public static string AnswersPath(string workspace, string slug) =>
        Path.Combine(workspace, "projects", slug, "capability-answers.yaml");

    /// <summary>
    /// The checks that matter to a task and that this profile has seen missing,
    /// and for release a manual release, which offers the template without being a gap.
    /// </summary>
    /// <remarks>
    /// A task's type picks the checks. A done-when only CI can meet raises CI on
    /// any type, because it is the person's own words. A prerequisite skips only
    /// the check it exists to fix, so release automation on a project with no CI
    /// is still told about CI.
    /// </remarks>
    public static IReadOnlyList<CapabilityResult> Relevant(TaskItem task, CapabilityProfile profile, CapabilityAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(answers);

        var needsCi = task.DoneWhen.Any(criterion => CriterionTags.Of(criterion, null).Needs == CriterionNeeds.Ci);

        return
        [
            .. profile.Checks.Where(check =>
                ((task.Type is { } type && CapabilityChecks.Matters(check, type)) || (needsCi && check.Id == CapabilityChecks.Ci))
                && !string.Equals(task.Prerequisite?.Check, check.Id, StringComparison.Ordinal)
                && !answers.NotNeeded.Any(answer => string.Equals(answer.Check, check.Id, StringComparison.Ordinal))),
        ];
    }

    /// <summary>How many gaps a task has that the profile has actually seen, the number a dashboard may show.</summary>
    /// <remarks>Manual release offers a template but is not a gap, and a stack note is never one.</remarks>
    public static int SeenGaps(TaskItem task, CapabilityProfile profile, CapabilityAnswers answers) =>
        Relevant(task, profile, answers).Count(check => check.State == CapabilityState.Gap);

    /// <summary>How many checks matter to a task and could not be looked at.</summary>
    public static int UnseenChecks(TaskItem task, CapabilityProfile profile, CapabilityAnswers answers) =>
        Relevant(task, profile, answers).Count(check => check.State is CapabilityState.Unknown or CapabilityState.NoPermission);

    /// <summary>What the stack key cycles through for a check, the default first.</summary>
    /// <remarks>
    /// CI gets one job per stack, so its default is all of them. Release covers
    /// one stack, so its default is the one last chosen if still detected, else
    /// the one nearest the root.
    /// </remarks>
    public static IReadOnlyList<string> StackChoices(string check, IReadOnlyList<CapabilityStack> stacks, CapabilityAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(stacks);
        ArgumentNullException.ThrowIfNull(answers);

        var names = stacks.Select(stack => stack.Name).ToList();

        switch (check)
        {
            case CapabilityChecks.Ci:
                return [.. names.Count > 1 ? [PrerequisiteTemplates.AllDetected] : Array.Empty<string>(), .. names, PrerequisiteTemplates.Generic];

            case CapabilityChecks.Release:
                var first = names.Contains(answers.ReleaseStack, StringComparer.Ordinal) ? answers.ReleaseStack : null;

                return
                [
                    .. first is null ? Array.Empty<string>() : [first],
                    .. names.Where(name => name != first),
                    PrerequisiteTemplates.Generic,
                ];

            default:
                return [];
        }
    }

    /// <summary>A task's links to open prerequisites, each saying whether its type has outgrown it.</summary>
    /// <remarks>
    /// A task with no type outgrows nothing: handing a type back to the agent
    /// says nothing about what the work depends on. A hand-made link records an
    /// order rather than a capability, so a type never touches one.
    /// </remarks>
    public static IReadOnlyList<CapabilityLink> LinksOf(TaskItem task, string slug, IReadOnlyList<TaskItem> list)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(list);

        var needsCi = task.DoneWhen.Any(criterion => CriterionTags.Of(criterion, null).Needs == CriterionNeeds.Ci);
        var links = new List<CapabilityLink>();

        foreach (var link in task.Requires.Where(one => string.Equals(one.Project, slug, StringComparison.OrdinalIgnoreCase)))
        {
            if (TaskRuns.Find(link.Id, list) is not { Prerequisite: { } prerequisite, State: not (TaskState.Done or TaskState.Dropped) } target)
            {
                continue;
            }

            var depends = task.Type is not { } type
                || (CapabilityChecks.Find(prerequisite.Check)?.Types.Contains(type) ?? true)
                || (needsCi && prerequisite.Check == CapabilityChecks.Ci);

            links.Add(new CapabilityLink(prerequisite.Check, target.Id, !depends));
        }

        return links;
    }

    /// <summary>
    /// What a run's time took away, as lines for its report: checks that were
    /// there when it started and that were seen to be gaps as it ended.
    /// </summary>
    /// <remarks>
    /// Only ever described. Pausing a run over something it may not even touch
    /// costs more than the gap, and the next run to start looks again.
    /// </remarks>
    public static IReadOnlyList<string> Appeared(CapabilityProfile? before, CapabilityProfile? after)
    {
        if (before is null || after is null)
        {
            return [];
        }

        return
        [
            .. after.Checks
                .Where(check => check.State == CapabilityState.Gap
                    && before.Checks.Any(was => was.Id == check.Id && was.State is CapabilityState.Present or CapabilityState.Delegated))
                .Select(check => $"While this run was going, {check.Id} stopped being there: {check.Evidence}"),
        ];
    }

    /// <summary>What to tell somebody about a check that could not be looked at.</summary>
    public static string Hint(CapabilityResult check)
    {
        ArgumentNullException.ThrowIfNull(check);

        return check.State == CapabilityState.NoPermission
            ? $"The token needs {check.Scope} for this."
            : check.Evidence;
    }

    /// <inheritdoc />
    public async Task<OperationResult<OfferSheet>> ReadAsync(ProjectResolution project, string taskId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var slug = project.Entry.Slug;
        var listed = await _tasks.ListAsync(slug, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<OfferSheet>.Fail(listed.Error!, listed.ExitCode);
        }

        if (TaskRuns.Find(taskId, listed.Value!) is not { } task)
        {
            return OperationResult<OfferSheet>.Fail($"'{taskId.Trim()}' is not a task of {slug}.", ExitCode.InvalidArguments);
        }

        var profile = await _profiles.CachedAsync(slug, ct).ConfigureAwait(false)
            ?? await _profiles.BuildAsync(project, askGitHub: false, ct).ConfigureAwait(false);
        var answers = await AnswersAsync(slug, ct).ConfigureAwait(false);

        return OperationResult<OfferSheet>.Ok(Sheet(slug, task, listed.Value!, profile, answers));
    }

    /// <inheritdoc />
    public async Task<OperationResult<OfferSheet>> ReadAsync(string slug, string taskId, CancellationToken ct = default)
    {
        var project = await _projects.ResolveAsync(slug, ct).ConfigureAwait(false);

        return project.Failed
            ? OperationResult<OfferSheet>.Fail(project.Error!, project.ExitCode)
            : await ReadAsync(project.Value!, taskId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, OfferSheet>> SheetsAsync(string slug, CancellationToken ct = default)
    {
        var sheets = new Dictionary<string, OfferSheet>(StringComparer.OrdinalIgnoreCase);
        var project = await _projects.ResolveAsync(slug, ct).ConfigureAwait(false);
        var listed = await _tasks.ListAsync(slug, ct).ConfigureAwait(false);

        if (project.Failed || listed.Failed)
        {
            return sheets;
        }

        var profile = await _profiles.CachedAsync(slug, ct).ConfigureAwait(false)
            ?? await _profiles.BuildAsync(project.Value!, askGitHub: false, ct).ConfigureAwait(false);
        var answers = await AnswersAsync(slug, ct).ConfigureAwait(false);

        foreach (var task in listed.Value!.Where(one => one.State is not (TaskState.Done or TaskState.Dropped)))
        {
            sheets[task.Id] = Sheet(slug, task, listed.Value!, profile, answers);
        }

        return sheets;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CapabilityLink>> OutgrownAsync(string slug, string taskId, CancellationToken ct = default)
    {
        var listed = await _tasks.ListAsync(slug, ct).ConfigureAwait(false);

        return listed.Succeeded && TaskRuns.Find(taskId, listed.Value!) is { } task
            ? [.. LinksOf(task, slug, listed.Value!).Where(link => link.Outgrown)]
            : [];
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<string>>> ReleaseOutgrownAsync(string slug, string taskId, CancellationToken ct = default)
    {
        var outgrown = await OutgrownAsync(slug, taskId, ct).ConfigureAwait(false);

        if (outgrown.Count == 0)
        {
            return OperationResult<IReadOnlyList<string>>.Ok([]);
        }

        var removed = await _tasks.RequireAsync(
            slug,
            taskId,
            [],
            [.. outgrown.Select(link => new TaskLink { Project = slug, Id = link.Prerequisite })],
            ct).ConfigureAwait(false);

        return removed.Failed
            ? OperationResult<IReadOnlyList<string>>.Fail(removed.Error!, removed.ExitCode)
            : OperationResult<IReadOnlyList<string>>.Ok([.. outgrown.Select(link => link.Prerequisite)]);
    }

    /// <inheritdoc />
    public async Task<CapabilityProfile?> LookAsync(string slug, CancellationToken ct = default)
    {
        var project = await _projects.ResolveAsync(slug, ct).ConfigureAwait(false);

        if (project.Failed)
        {
            return null;
        }

        try
        {
            return await _profiles.BuildAsync(project.Value!, askGitHub: false, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, GapCount>> GapCountsAsync(string slug, CancellationToken ct = default)
    {
        var counts = new Dictionary<string, GapCount>(StringComparer.OrdinalIgnoreCase);

        // The kept profile only. A list is polled, and walking a repository's
        // files on every poll is not worth a count somebody can refresh.
        if (await _profiles.CachedAsync(slug, ct).ConfigureAwait(false) is not { } profile)
        {
            return counts;
        }

        var listed = await _tasks.ListAsync(slug, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return counts;
        }

        var answers = await AnswersAsync(slug, ct).ConfigureAwait(false);

        foreach (var task in listed.Value!.Where(one => one.State is not (TaskState.Done or TaskState.Dropped)))
        {
            var relevant = Relevant(task, profile, answers);

            counts[task.Id] = new GapCount(
                relevant.Count(check => check.State == CapabilityState.Gap),
                relevant.Count(check => check.State == CapabilityState.Gap && !PrerequisiteTemplates.Has(check.Id)),
                relevant.Count(check => check.State is CapabilityState.Unknown or CapabilityState.NoPermission));
        }

        return counts;
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<TaskItem>>> SetUpNewProjectAsync(
        string slug, string taskId, IReadOnlyList<string> skipped, string by, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(skipped);

        var listed = await _tasks.ListAsync(slug, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<IReadOnlyList<TaskItem>>.Fail(listed.Error!, listed.ExitCode);
        }

        var answers = await AnswersAsync(slug, ct).ConfigureAwait(false);
        var empty = new CapabilityProfile { Project = slug };
        var made = new List<TaskItem>();

        foreach (var check in (string[])[CapabilityChecks.Ci, CapabilityChecks.Release, CapabilityChecks.Environments])
        {
            if (skipped.Contains(check, StringComparer.OrdinalIgnoreCase))
            {
                answers.NotNeeded.Add(new CapabilityNotNeeded { Check = check, By = by, Utc = _time.GetUtcNow() });

                continue;
            }

            // Generic text: there is no repository for stacks to be found in.
            var task = await MakeAsync(slug, check, PrerequisiteTemplates.Generic, null, empty, answers, listed.Value!, by, ct).ConfigureAwait(false);

            if (task.Failed)
            {
                return OperationResult<IReadOnlyList<TaskItem>>.Fail(task.Error!, task.ExitCode);
            }

            var linked = await _tasks.RequireAsync(slug, taskId, [new TaskLink { Project = slug, Id = task.Value!.Id }], [], ct).ConfigureAwait(false);

            if (linked.Failed)
            {
                return OperationResult<IReadOnlyList<TaskItem>>.Fail(linked.Error!, linked.ExitCode);
            }

            made.Add(task.Value);
        }

        var saved = await SaveAnswersAsync(slug, answers, ct).ConfigureAwait(false);

        return saved.Failed
            ? OperationResult<IReadOnlyList<TaskItem>>.Fail(saved.Error!, saved.ExitCode)
            : OperationResult<IReadOnlyList<TaskItem>>.Ok(made);
    }

    /// <inheritdoc />
    public async Task<CapabilityAnswers> AnswersAsync(string slug, CancellationToken ct = default)
    {
        var loaded = await _yaml.LoadAsync(AnswersPath(_workspace.LocalPath, slug), () => new CapabilityAnswers(), ct).ConfigureAwait(false);

        return loaded.Succeeded ? loaded.Value! : new CapabilityAnswers();
    }

    /// <inheritdoc />
    public async Task<OperationResult<OfferAccepted>> AcceptAsync(
        ProjectResolution project,
        string taskId,
        string check,
        string? stack,
        IReadOnlyList<string>? environments,
        string by,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var slug = project.Entry.Slug;
        var id = (check ?? string.Empty).Trim().ToLowerInvariant();

        if (!PrerequisiteTemplates.Has(id))
        {
            return OperationResult<OfferAccepted>.Fail(
                $"'{check}' has no template to offer. Only {CapabilityChecks.Ci}, {CapabilityChecks.Release} and {CapabilityChecks.Environments} do; "
                + "anything else can be linked by hand with loadout task requires.",
                ExitCode.InvalidArguments);
        }

        var listed = await _tasks.ListAsync(slug, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<OfferAccepted>.Fail(listed.Error!, listed.ExitCode);
        }

        if (TaskRuns.Find(taskId, listed.Value!) is not { } task)
        {
            return OperationResult<OfferAccepted>.Fail($"'{taskId.Trim()}' is not a task of {slug}.", ExitCode.InvalidArguments);
        }

        // An offer decides something, so it is judged on a profile built now.
        var profile = await _profiles.BuildAsync(project, askGitHub: true, ct).ConfigureAwait(false);
        var seen = profile.Checks.FirstOrDefault(one => one.Id == id);

        if (seen is not { State: CapabilityState.Gap } && !(id == CapabilityChecks.Release && seen is { State: CapabilityState.ManualRelease }))
        {
            return OperationResult<OfferAccepted>.Fail(
                $"There is nothing to offer for {id}: {seen?.Evidence ?? "the profile has no such check."}", ExitCode.InvalidArguments);
        }

        var answers = await AnswersAsync(slug, ct).ConfigureAwait(false);
        var existing = listed.Value!.FirstOrDefault(one =>
            one.Prerequisite?.Check == id && one.State is not (TaskState.Done or TaskState.Dropped));
        var created = existing is null;
        TaskItem prerequisite;

        if (existing is not null)
        {
            prerequisite = existing;
        }
        else
        {
            var made = await MakeAsync(slug, id, stack, environments, profile, answers, listed.Value!, by, ct).ConfigureAwait(false);

            if (made.Failed)
            {
                return OperationResult<OfferAccepted>.Fail(made.Error!, made.ExitCode);
            }

            prerequisite = made.Value!;
        }

        var linked = await _tasks.RequireAsync(
            slug,
            task.Id,
            [new TaskLink { Project = slug, Id = prerequisite.Id }],
            [],
            ct).ConfigureAwait(false);

        if (linked.Failed)
        {
            return OperationResult<OfferAccepted>.Fail(linked.Error!, linked.ExitCode);
        }

        return OperationResult<OfferAccepted>.Ok(new OfferAccepted(prerequisite, created));
    }

    /// <inheritdoc />
    public async Task<OperationResult<NotNeededOutcome>> NotNeededAsync(
        ProjectResolution project, string check, bool releaseLinks, string by, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var slug = project.Entry.Slug;
        var id = (check ?? string.Empty).Trim().ToLowerInvariant();
        var answers = await AnswersAsync(slug, ct).ConfigureAwait(false);

        if (!answers.NotNeeded.Any(answer => answer.Check == id))
        {
            answers.NotNeeded.Add(new CapabilityNotNeeded { Check = id, By = by, Utc = _time.GetUtcNow() });

            var saved = await SaveAnswersAsync(slug, answers, ct).ConfigureAwait(false);

            if (saved.Failed)
            {
                return OperationResult<NotNeededOutcome>.Fail(saved.Error!, saved.ExitCode);
            }
        }

        var every = await _tasks.EveryListAsync(ct).ConfigureAwait(false);

        if (every.Failed)
        {
            return OperationResult<NotNeededOutcome>.Fail(every.Error!, every.ExitCode);
        }

        var open = every.Value!.TryGetValue(slug, out var list)
            ? list.FirstOrDefault(one => one.Prerequisite?.Check == id && one.State is not (TaskState.Done or TaskState.Dropped))
            : null;

        if (open is null)
        {
            return OperationResult<NotNeededOutcome>.Ok(new NotNeededOutcome(null, [], false));
        }

        var waiting = Waiting(new TaskLink { Project = slug, Id = open.Id }, every.Value!);

        if (!releaseLinks)
        {
            return OperationResult<NotNeededOutcome>.Ok(new NotNeededOutcome(open.Id, waiting, false));
        }

        foreach (var link in waiting)
        {
            var removed = await _tasks.RequireAsync(
                link.Project.Length > 0 ? link.Project : null,
                link.Id,
                [],
                [new TaskLink { Project = slug, Id = open.Id }],
                ct).ConfigureAwait(false);

            if (removed.Failed)
            {
                return OperationResult<NotNeededOutcome>.Fail(removed.Error!, removed.ExitCode);
            }
        }

        var dropped = await _tasks.DeclareAsync(
            slug, open.Id, TaskState.Dropped, by, reason: $"{id} marked not needed for this project", ct: ct).ConfigureAwait(false);

        return dropped.Failed
            ? OperationResult<NotNeededOutcome>.Fail(dropped.Error!, dropped.ExitCode)
            : OperationResult<NotNeededOutcome>.Ok(new NotNeededOutcome(open.Id, waiting, true));
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<string>>> NeededAsync(ProjectResolution project, string check, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var slug = project.Entry.Slug;
        var id = (check ?? string.Empty).Trim().ToLowerInvariant();
        var answers = await AnswersAsync(slug, ct).ConfigureAwait(false);

        if (answers.NotNeeded.RemoveAll(answer => answer.Check == id) == 0)
        {
            return OperationResult<IReadOnlyList<string>>.Fail($"{slug} has no 'Not needed' answer for {id}.", ExitCode.InvalidArguments);
        }

        var saved = await SaveAnswersAsync(slug, answers, ct).ConfigureAwait(false);

        if (saved.Failed)
        {
            return OperationResult<IReadOnlyList<string>>.Fail(saved.Error!, saved.ExitCode);
        }

        var profile = await _profiles.CachedAsync(slug, ct).ConfigureAwait(false)
            ?? await _profiles.BuildAsync(project, askGitHub: false, ct).ConfigureAwait(false);
        var listed = await _tasks.ListAsync(slug, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<IReadOnlyList<string>>.Fail(listed.Error!, listed.ExitCode);
        }

        return OperationResult<IReadOnlyList<string>>.Ok(
        [
            .. listed.Value!
                .Where(task => task.State is TaskState.Open or TaskState.Doing or TaskState.Blocked)
                .Where(task => Relevant(task, profile, answers).Any(one => one.Id == id && Offerable(one)))
                .Select(task => task.Id),
        ]);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<PrerequisiteSettled>>> SettleAsync(
        ProjectResolution project, string by, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var slug = project.Entry.Slug;
        var every = await _tasks.EveryListAsync(ct).ConfigureAwait(false);

        if (every.Failed)
        {
            return OperationResult<IReadOnlyList<PrerequisiteSettled>>.Fail(every.Error!, every.ExitCode);
        }

        var open = every.Value!.TryGetValue(slug, out var list)
            ? list.Where(one => one.Prerequisite is not null && one.State is not (TaskState.Done or TaskState.Dropped)).ToList()
            : [];

        if (open.Count == 0)
        {
            return OperationResult<IReadOnlyList<PrerequisiteSettled>>.Ok([]);
        }

        var profile = await _profiles.BuildAsync(project, askGitHub: true, ct).ConfigureAwait(false);
        var settled = new List<PrerequisiteSettled>();
        var date = _time.GetUtcNow().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        foreach (var task in open)
        {
            var prerequisite = task.Prerequisite!;
            var seen = profile.Checks.FirstOrDefault(one => one.Id == prerequisite.Check);

            if (seen is not { State: CapabilityState.Present or CapabilityState.Delegated })
            {
                continue;
            }

            // The file does not prove the secret exists, and the person's tick
            // is the only evidence there is. An environment GitHub lists is the
            // very thing its step creates, so that one is met by being seen.
            if (prerequisite.Check == CapabilityChecks.Release && prerequisite.PersonStepUtc is null)
            {
                continue;
            }

            var reason = $"{prerequisite.Check} seen {date}: {seen.Evidence}";
            var link = new TaskLink { Project = slug, Id = task.Id };

            if (RunOwns(slug, task.Id))
            {
                // Waiting work goes now, because the capability is real; the
                // run keeps its own task, which closes once the run has ended.
                var released = Waiting(link, every.Value!);

                foreach (var waiting in released)
                {
                    var off = await _tasks.RequireAsync(
                        waiting.Project.Length > 0 ? waiting.Project : null, waiting.Id, [], [link], ct).ConfigureAwait(false);

                    if (off.Failed)
                    {
                        return OperationResult<IReadOnlyList<PrerequisiteSettled>>.Fail(off.Error!, off.ExitCode);
                    }
                }

                if (released.Count > 0)
                {
                    settled.Add(new PrerequisiteSettled(task.Id, false, released, reason));
                }

                continue;
            }

            var closed = await _tasks.ApplyAsync(
            [
                new TaskOperation(
                    TaskOperation.Declare,
                    slug,
                    task.Id,
                    TaskState.Done,
                    By: by,
                    Reason: reason,
                    Prerequisite: prerequisite.Check == CapabilityChecks.Environments && prerequisite.PersonStepUtc is null
                        ? Ticked(prerequisite, by)
                        : null),
            ], ct).ConfigureAwait(false);

            if (closed.Failed || !closed.Value!.Outcomes[0].Applied)
            {
                return OperationResult<IReadOnlyList<PrerequisiteSettled>>.Fail(
                    closed.Error ?? closed.Value!.Outcomes[0].Error ?? $"{task.Id} could not be closed.");
            }

            settled.Add(new PrerequisiteSettled(task.Id, true, [], reason));
        }

        return OperationResult<IReadOnlyList<PrerequisiteSettled>>.Ok(settled);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StalePrerequisite>> StaleAsync(string slug, CapabilityProfile profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var listed = await _tasks.ListAsync(slug, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return [];
        }

        var detected = profile.Stacks.Select(stack => stack.Name).ToHashSet(StringComparer.Ordinal);
        var stale = new List<StalePrerequisite>();

        foreach (var task in listed.Value!.Where(one => one.Prerequisite is not null && one.State is not (TaskState.Done or TaskState.Dropped)))
        {
            var prerequisite = task.Prerequisite!;

            if (prerequisite.TemplateVersion < PrerequisiteTemplates.Version)
            {
                stale.Add(new StalePrerequisite(
                    task.Id, prerequisite.Check, $"made from template version {prerequisite.TemplateVersion}; the current one is {PrerequisiteTemplates.Version}"));
            }
            else if (prerequisite.Check != CapabilityChecks.Environments && !detected.SetEquals(prerequisite.Detected))
            {
                stale.Add(new StalePrerequisite(
                    task.Id,
                    prerequisite.Check,
                    $"written when the stacks were {Said(prerequisite.Detected)}; they are now {Said(detected)}"));
            }
        }

        return stale;
    }

    /// <inheritdoc />
    public async Task<OperationResult<TaskItem>> RenewAsync(ProjectResolution project, string taskId, string by, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var slug = project.Entry.Slug;
        var listed = await _tasks.ListAsync(slug, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<TaskItem>.Fail(listed.Error!, listed.ExitCode);
        }

        if (TaskRuns.Find(taskId, listed.Value!) is not { Prerequisite: { } old } task)
        {
            return OperationResult<TaskItem>.Fail($"'{taskId.Trim()}' is not a prerequisite made from a template.", ExitCode.InvalidArguments);
        }

        if (RunOwns(slug, task.Id))
        {
            return OperationResult<TaskItem>.Fail(
                $"A run is working on {task.Id}, and nothing an unattended run reads changes underneath it. Renew it once the run has ended.",
                ExitCode.InvalidArguments);
        }

        var profile = await _profiles.BuildAsync(project, askGitHub: false, ct).ConfigureAwait(false);
        var detected = profile.Stacks.Select(stack => stack.Name).ToList();
        var everyStack = old.Stacks.Count > 0 && old.Stacks.ToHashSet(StringComparer.Ordinal).SetEquals(old.Detected);
        IReadOnlyList<string> stacks = old.Check switch
        {
            CapabilityChecks.Ci when everyStack || old.Stacks.Count == 0 && old.Detected.Count == 0 => detected,
            CapabilityChecks.Ci => [.. old.Stacks.Where(detected.Contains)],
            CapabilityChecks.Release when old.Stacks.Count > 0 && detected.Contains(old.Stacks[0]) => [old.Stacks[0]],
            CapabilityChecks.Release => detected.Take(1).ToList(),
            _ => [],
        };
        var rendered = PrerequisiteTemplates.Render(old.Check, stacks, old.Environments);

        if (rendered is null)
        {
            return OperationResult<TaskItem>.Fail($"{old.Check} has no template any more.", ExitCode.InvalidArguments);
        }

        // A tick said the secret for that stack was set; for another stack it says nothing.
        var clearTick = old.Check == CapabilityChecks.Release && !old.Stacks.SequenceEqual(stacks);
        var renewed = new TaskPrerequisite
        {
            Check = old.Check,
            TemplateVersion = PrerequisiteTemplates.Version,
            Stacks = [.. stacks],
            Detected = detected,
            Environments = [.. old.Environments],
            PersonStepBy = clearTick ? string.Empty : old.PersonStepBy,
            PersonStepUtc = clearTick ? null : old.PersonStepUtc,
        };

        return Applied(await _tasks.ApplyAsync(
        [
            new TaskOperation(
                TaskOperation.Declare,
                slug,
                task.Id,
                task.State,
                rendered.Title,
                By: by,
                Detail: rendered.Detail,
                DoneWhen: rendered.DoneWhen,
                Reason: $"renewed from template version {old.TemplateVersion} to {PrerequisiteTemplates.Version}, for stacks {Said(stacks)}",
                Prerequisite: renewed),
        ], ct).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<OperationResult<TaskItem>> TickAsync(string slug, string taskId, string by, CancellationToken ct = default)
    {
        var listed = await _tasks.ListAsync(slug, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<TaskItem>.Fail(listed.Error!, listed.ExitCode);
        }

        if (TaskRuns.Find(taskId, listed.Value!) is not { Prerequisite: { } prerequisite } task
            || !PrerequisiteTemplates.HasPersonStep(prerequisite.Check))
        {
            return OperationResult<TaskItem>.Fail($"'{taskId.Trim()}' has no step for a person to tick.", ExitCode.InvalidArguments);
        }

        return Applied(await _tasks.ApplyAsync(
        [
            new TaskOperation(
                TaskOperation.Declare,
                slug,
                task.Id,
                task.State,
                By: by,
                Reason: "the step only a person can do was ticked",
                Prerequisite: Ticked(prerequisite, by)),
        ], ct).ConfigureAwait(false));
    }

    private static bool Offerable(CapabilityResult check) =>
        check.State == CapabilityState.Gap || (check.Id == CapabilityChecks.Release && check.State == CapabilityState.ManualRelease);

    private static OfferSheet Sheet(string slug, TaskItem task, IReadOnlyList<TaskItem> list, CapabilityProfile profile, CapabilityAnswers answers)
    {
        var offers = new List<CapabilityOffer>();
        var unseen = new List<CapabilityUnseen>();

        foreach (var check in Relevant(task, profile, answers))
        {
            if (check.State is CapabilityState.Unknown or CapabilityState.NoPermission)
            {
                unseen.Add(new CapabilityUnseen(check.Id, check.State, Hint(check)));
            }
            else if (Offerable(check))
            {
                var open = list.FirstOrDefault(one => one.Prerequisite?.Check == check.Id && one.State is not (TaskState.Done or TaskState.Dropped));

                offers.Add(new CapabilityOffer(
                    check.Id,
                    check.Name.Length > 0 ? check.Name : CapabilityChecks.Find(check.Id)?.Name ?? check.Id,
                    check.State,
                    check.Evidence,
                    PrerequisiteTemplates.Has(check.Id),
                    StackChoices(check.Id, profile.Stacks, answers),
                    check.Id == CapabilityChecks.Environments ? EnvironmentsOf(answers) : [],
                    open?.Id,
                    open is not null && task.Requires.Any(link => TaskRequires.Same(link, new TaskLink { Project = slug, Id = open.Id }))));
            }
        }

        return new OfferSheet(
            slug,
            task.Id,
            profile.BuiltUtc,
            profile.GitHub,
            offers,
            unseen,
            [.. answers.NotNeeded.Select(answer => answer.Check)],
            profile.Notes,
            LinksOf(task, slug, list));
    }

    private static IReadOnlyList<string> EnvironmentsOf(CapabilityAnswers answers) =>
        answers.Environments.Count > 0 ? answers.Environments : PrerequisiteTemplates.DefaultEnvironments;

    private TaskPrerequisite Ticked(TaskPrerequisite prerequisite, string by) => new()
    {
        Check = prerequisite.Check,
        TemplateVersion = prerequisite.TemplateVersion,
        Stacks = [.. prerequisite.Stacks],
        Detected = [.. prerequisite.Detected],
        Environments = [.. prerequisite.Environments],
        PersonStepBy = by,
        PersonStepUtc = _time.GetUtcNow(),
    };

    private static string Said(IEnumerable<string> stacks)
    {
        var named = stacks.Order(StringComparer.Ordinal).ToList();

        return named.Count > 0 ? string.Join(", ", named) : "none";
    }

    private static OperationResult<TaskItem> Applied(OperationResult<TaskBatch> batch)
    {
        if (batch.Failed)
        {
            return OperationResult<TaskItem>.Fail(batch.Error!, batch.ExitCode);
        }

        var outcome = batch.Value!.Outcomes[0];

        return outcome.Applied ? OperationResult<TaskItem>.Ok(outcome.Task!) : OperationResult<TaskItem>.Fail(outcome.Error!, outcome.Code);
    }

    /// <summary>Every task, on any list, that requires this one and has not finished.</summary>
    private static IReadOnlyList<TaskLink> Waiting(TaskLink target, IReadOnlyDictionary<string, IReadOnlyList<TaskItem>> every) =>
    [
        .. every.SelectMany(list => list.Value
            .Where(task => task.State is not (TaskState.Done or TaskState.Dropped)
                && task.Requires.Any(link => TaskRequires.Same(link, target)))
            .Select(task => new TaskLink { Project = list.Key, Id = task.Id })),
    ];

    private async Task<OperationResult<TaskItem>> MakeAsync(
        string slug,
        string check,
        string? stack,
        IReadOnlyList<string>? environments,
        CapabilityProfile profile,
        CapabilityAnswers answers,
        IReadOnlyList<TaskItem> list,
        string by,
        CancellationToken ct)
    {
        var detected = profile.Stacks.Select(one => one.Name).ToList();
        var choices = StackChoices(check, profile.Stacks, answers);
        var chosen = string.IsNullOrWhiteSpace(stack) ? choices.FirstOrDefault() : stack.Trim().ToLowerInvariant();

        if (choices.Count > 0 && !choices.Contains(chosen!, StringComparer.Ordinal))
        {
            return OperationResult<TaskItem>.Fail(
                $"'{stack}' is not a stack choice for {check}. The choices are: {string.Join(", ", choices)}.", ExitCode.InvalidArguments);
        }

        IReadOnlyList<string> stacks = chosen switch
        {
            null or PrerequisiteTemplates.Generic => [],
            PrerequisiteTemplates.AllDetected => detected,
            _ => [chosen],
        };

        var names = environments is { Count: > 0 }
            ? [.. environments.Select(name => name.Trim()).Where(name => name.Length > 0)]
            : EnvironmentsOf(answers);
        var rendered = PrerequisiteTemplates.Render(check, stacks, names)!;

        // Remembered only when the task is made from them, so a refused offer leaves nothing behind.
        var id = rendered.Id;

        for (var number = 2; list.Any(one => string.Equals(one.Id, id, StringComparison.OrdinalIgnoreCase)); number++)
        {
            id = $"{rendered.Id}-{number}";
        }

        var made = Applied(await _tasks.ApplyAsync(
        [
            new TaskOperation(
                TaskOperation.Add,
                slug,
                id,
                TaskState.Open,
                rendered.Title,
                By: by,
                Detail: rendered.Detail,
                DoneWhen: rendered.DoneWhen,
                Reason: $"accepted the {check} offer, template version {PrerequisiteTemplates.Version}",
                Type: rendered.Type,
                TypeBy: TaskTypeSource.Rule,
                Prerequisite: new TaskPrerequisite
                {
                    Check = check,
                    TemplateVersion = PrerequisiteTemplates.Version,
                    Stacks = [.. stacks],
                    Detected = detected,
                    Environments = check == CapabilityChecks.Environments ? [.. names] : [],
                }),
        ], ct).ConfigureAwait(false));

        if (made.Succeeded)
        {
            if (check == CapabilityChecks.Environments)
            {
                answers.Environments = [.. names];
            }
            else if (check == CapabilityChecks.Release && stacks.Count > 0)
            {
                answers.ReleaseStack = stacks[0];
            }

            await SaveAnswersAsync(slug, answers, ct).ConfigureAwait(false);
        }

        return made;
    }

    private Task<OperationResult> SaveAnswersAsync(string slug, CapabilityAnswers answers, CancellationToken ct) =>
        _yaml.SaveAsync(AnswersPath(_workspace.LocalPath, slug), answers, true, ct);

    /// <summary>Whether a run still going was started for this task.</summary>
    private bool RunOwns(string slug, string taskId)
    {
        foreach (var runId in _journal.List(50))
        {
            var run = _journal.Summarise(runId);

            if (run.Succeeded
                && run.Value!.Ended is null
                && string.Equals(run.Value.Task, taskId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(run.Value.Project, slug, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
