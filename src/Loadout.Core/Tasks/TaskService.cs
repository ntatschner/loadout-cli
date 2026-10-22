using Loadout.Core.Configuration;
using Loadout.Core.Security;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Tasks;

namespace Loadout.Core.Tasks;

/// <summary>Everything a project is working on.</summary>
/// <remarks>
/// A file rather than one per task. There are tens of these, not thousands,
/// and a single list is what makes "what is open" one read instead of a
/// directory scan.
/// </remarks>
public sealed class TaskList
{
    public int SchemaVersion { get; set; } = 1;

    public List<TaskItem> Items { get; set; } = [];
}

/// <summary>What is being worked on, and what the record makes of it.</summary>
/// <remarks>
/// Every method takes the project a list belongs to, or null for the
/// workspace-wide list: work and ideas that belong to no project yet, kept at
/// <c>tasks.yaml</c> in the workspace root so they travel like everything else.
/// </remarks>
public interface ITaskService
{
    /// <summary>Everything recorded for a project, or the workspace-wide list for null.</summary>
    Task<OperationResult<IReadOnlyList<TaskItem>>> ListAsync(
        string? projectSlug,
        CancellationToken ct = default);

    /// <summary>
    /// Records a state for a task, adding it when the id is new.
    /// </summary>
    /// <remarks>
    /// One call for both, because declaring is what a session actually does:
    /// it says where something stands, and whether that thing was already
    /// written down is not a distinction worth making it think about.
    /// <para>
    /// A kind of null leaves the task as it was, so declaring where an idea
    /// stands does not turn it into work; a new task given none is work.
    /// </para>
    /// <para>
    /// A title longer than <see cref="TaskTitles.Length"/> is shortened, and
    /// the whole of it is kept at the start of the detail, so nothing given is
    /// lost. <see cref="TaskTitles.Shortens"/> says beforehand whether it will be.
    /// </para>
    /// </remarks>
    Task<OperationResult<TaskItem>> DeclareAsync(
        string? projectSlug,
        string id,
        TaskState state,
        string declaredBy,
        string? title = null,
        string? note = null,
        CancellationToken ct = default,
        TaskKind? kind = null,
        string? detail = null,
        IReadOnlyList<string>? doneWhen = null,
        string? reason = null);

    /// <summary>
    /// Shortens every title on a list that is longer than
    /// <see cref="TaskTitles.Length"/>, keeping the original in the detail and
    /// saying so in the task's history; or, without <paramref name="apply"/>,
    /// says which it would shorten and writes nothing.
    /// </summary>
    /// <remarks>
    /// For titles written before there was a limit. Running it again finds
    /// nothing to do, since every title it leaves is within the limit.
    /// </remarks>
    Task<OperationResult<IReadOnlyList<TaskShortened>>> ShortenTitlesAsync(
        string? projectSlug,
        string by,
        bool apply,
        CancellationToken ct = default);

    /// <summary>Forgets a task.</summary>
    Task<OperationResult> RemoveAsync(
        string? projectSlug,
        string id,
        CancellationToken ct = default);

    /// <summary>
    /// Moves a task from one list to another, keeping its id, or refuses when
    /// the destination already has one by that id.
    /// </summary>
    Task<OperationResult<TaskItem>> MoveAsync(
        string? fromSlug,
        string? toSlug,
        string id,
        CancellationToken ct = default);

    /// <summary>
    /// Every task on every list that was moved there, and the list it is on now.
    /// </summary>
    /// <remarks>
    /// One read of every list, rather than one per list asked about: a page
    /// showing every list's trace would otherwise read each file once for each
    /// list. Filtering by where they came from is the caller's.
    /// </remarks>
    Task<OperationResult<IReadOnlyList<TaskMoved>>> MovedAsync(CancellationToken ct = default);

    /// <summary>A list, with the revision a change made from it should name.</summary>
    /// <remarks>
    /// The default has no revision to give, which a caller reads as nothing to
    /// check. It is here for the doubles in the tests; the real service has one.
    /// </remarks>
    async Task<OperationResult<TaskListRead>> ReadAsync(string? projectSlug, CancellationToken ct = default)
    {
        var listed = await ListAsync(projectSlug, ct).ConfigureAwait(false);

        return listed.Succeeded
            ? OperationResult<TaskListRead>.Ok(new TaskListRead(projectSlug, listed.Value!, null))
            : OperationResult<TaskListRead>.Fail(listed.Error!, listed.ExitCode);
    }

    /// <summary>
    /// Makes several changes, each list all or nothing, and says what became
    /// of every one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Operations on one list are applied together or not at all, because one
    /// file is what a revision guards and so the only thing that can honestly
    /// be made atomic. A move changes two lists, which then stand or fall
    /// together. Lists with nothing in common stand or fall on their own, and
    /// the outcomes say which.
    /// </para>
    /// <para>
    /// The default makes the changes one at a time through the methods above,
    /// with none of that: it is for the doubles in the tests, and a test pins
    /// that the real service has its own.
    /// </para>
    /// </remarks>
    async Task<OperationResult<TaskBatch>> ApplyAsync(IReadOnlyList<TaskOperation> operations, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var outcomes = new List<TaskOutcome>();

        foreach (var (operation, index) in operations.Select((one, index) => (one, index)))
        {
            var id = operation.Id ?? string.Empty;
            OperationResult<TaskItem> done;

            if (operation.Op == TaskOperation.Remove)
            {
                var removed = await RemoveAsync(operation.Project, id, ct).ConfigureAwait(false);

                done = removed.Succeeded
                    ? OperationResult<TaskItem>.Ok(new TaskItem { Id = id })
                    : OperationResult<TaskItem>.Fail(removed.Error!, removed.ExitCode);
            }
            else
            {
                done = operation.Op == TaskOperation.Move
                    ? await MoveAsync(operation.Project, operation.To, id, ct).ConfigureAwait(false)
                    : await DeclareAsync(
                        operation.Project, id, operation.State ?? TaskState.Open, operation.By,
                        operation.Title, operation.Note, ct, operation.Kind, operation.Detail, operation.DoneWhen,
                        operation.Reason).ConfigureAwait(false);
            }

            outcomes.Add(new TaskOutcome(
                index, operation.Op, operation.Project, id, done.Succeeded ? null : done.Error, done.ExitCode,
                done.Succeeded ? done.Value : null, operation.To));
        }

        return OperationResult<TaskBatch>.Ok(new TaskBatch(outcomes, []));
    }

    /// <summary>Every list's file as it stands, cheap enough to ask for twice a second.</summary>
    /// <remarks>The default knows of no files, so nothing it stands for is watched.</remarks>
    Task<IReadOnlyList<TaskListStamp>> StampsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TaskListStamp>>([]);
}

/// <summary>A title the tidying pass shortened, or would.</summary>
/// <param name="Id">The task.</param>
/// <param name="Before">The title as it was.</param>
/// <param name="After">The title as it is kept.</param>
public sealed record TaskShortened(string Id, string Before, string After);

/// <summary>A task that was moved, and the list it is on now.</summary>
/// <param name="Task">The task, with where it came from in <see cref="TaskItem.Moved"/>.</param>
/// <param name="Where">The project it is on now, or null for the workspace-wide list.</param>
public sealed record TaskMoved(TaskItem Task, string? Where)
{
    /// <summary>The list it left, as a slug, or null for the workspace-wide list.</summary>
    public string? From => Task.Moved?.From is { Length: > 0 } from ? from : null;

    /// <summary>Whether it left this list, a project's slug or null for the workspace-wide one.</summary>
    public bool Left(string? list)
    {
        var named = list is { Length: > 0 } ? list : null;

        return Task.Moved is not null
            && string.Equals(From, named, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Where, named, StringComparison.OrdinalIgnoreCase);
    }
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// Every change, one or a batch, goes through <see cref="ApplyAsync"/>: the
/// lists it touches are locked, read once, changed in memory by the same
/// rules, and written once each. A single declare is a batch of one, so the
/// two can never come to disagree about what a change does.
/// </para>
/// <para>
/// Lists are kept parsed between reads by <see cref="KeptFiles{T}"/>, which
/// reads one again whenever its file says it has changed.
/// </para>
/// </remarks>
internal sealed class TaskService : ITaskService
{
    private const string NoWorkspace = "There is no workspace on this machine, so there is nowhere to keep tasks.";

    private readonly IWorkspaceManager _workspace;
    private readonly YamlStore _yaml;
    private readonly TimeProvider _time;
    private readonly KeptFiles<TaskList> _kept;

    public TaskService(IWorkspaceManager workspace, YamlStore yaml, TimeProvider time)
    {
        _workspace = workspace;
        _yaml = yaml;
        _time = time;
        _kept = new KeptFiles<TaskList>(yaml, () => new TaskList(), Copy);
    }

    private string PathFor(string? slug) => TaskPaths.List(_workspace.LocalPath, slug);

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<TaskItem>>> ListAsync(
        string? projectSlug,
        CancellationToken ct = default)
    {
        var read = await ReadAsync(projectSlug, ct).ConfigureAwait(false);

        return read.Succeeded
            ? OperationResult<IReadOnlyList<TaskItem>>.Ok(read.Value!.Items)
            : OperationResult<IReadOnlyList<TaskItem>>.Fail(read.Error!, read.ExitCode);
    }

    /// <inheritdoc />
    public async Task<OperationResult<TaskListRead>> ReadAsync(string? projectSlug, CancellationToken ct = default)
    {
        if (!_workspace.IsAvailable())
        {
            return OperationResult<TaskListRead>.Fail(NoWorkspace, ExitCode.WorkspaceSyncFailed);
        }

        if (TaskPaths.Rejection(projectSlug) is { } rejected)
        {
            return OperationResult<TaskListRead>.Fail(rejected, ExitCode.InvalidArguments);
        }

        var loaded = await LoadAsync(PathFor(projectSlug), ct).ConfigureAwait(false);

        return loaded.Succeeded
            ? OperationResult<TaskListRead>.Ok(new TaskListRead(
                projectSlug, [.. loaded.Value!.Value.Items.Where(item => item.Id.Length > 0)], loaded.Value.Revision))
            : OperationResult<TaskListRead>.Fail(loaded.Error!, loaded.ExitCode);
    }

    /// <inheritdoc />
    public async Task<OperationResult<TaskItem>> DeclareAsync(
        string? projectSlug,
        string id,
        TaskState state,
        string declaredBy,
        string? title = null,
        string? note = null,
        CancellationToken ct = default,
        TaskKind? kind = null,
        string? detail = null,
        IReadOnlyList<string>? doneWhen = null,
        string? reason = null) =>
        One(await ApplyCoreAsync(
            [new TaskOperation(TaskOperation.Declare, projectSlug, id, state, title, note, kind, declaredBy, Detail: detail, DoneWhen: doneWhen, Reason: reason)],
            false,
            ct).ConfigureAwait(false));

    /// <inheritdoc />
    public async Task<OperationResult> RemoveAsync(
        string? projectSlug,
        string id,
        CancellationToken ct = default)
    {
        var removed = One(await ApplyCoreAsync(
            [new TaskOperation(TaskOperation.Remove, projectSlug, id)], false, ct).ConfigureAwait(false));

        return removed.Succeeded ? OperationResult.Ok() : OperationResult.Fail(removed.Error!, removed.ExitCode);
    }

    /// <inheritdoc />
    public async Task<OperationResult<TaskItem>> MoveAsync(
        string? fromSlug,
        string? toSlug,
        string id,
        CancellationToken ct = default) =>
        One(await ApplyCoreAsync(
            [new TaskOperation(TaskOperation.Move, fromSlug, id, To: toSlug)], false, ct).ConfigureAwait(false));

    /// <inheritdoc />
    public Task<OperationResult<TaskBatch>> ApplyAsync(IReadOnlyList<TaskOperation> operations, CancellationToken ct = default) =>
        ApplyCoreAsync(operations, true, ct);

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<TaskMoved>>> MovedAsync(CancellationToken ct = default)
    {
        if (!_workspace.IsAvailable())
        {
            return OperationResult<IReadOnlyList<TaskMoved>>.Fail(NoWorkspace, ExitCode.WorkspaceSyncFailed);
        }

        var moved = new List<TaskMoved>();

        foreach (var list in Lists())
        {
            if (!File.Exists(PathFor(list)))
            {
                continue;
            }

            // A list that cannot be read is left out rather than failing every
            // other list's trace: the trace is a convenience, and reading the
            // list itself says what is wrong with it.
            var listed = await ListAsync(list, ct).ConfigureAwait(false);

            if (listed.Succeeded)
            {
                moved.AddRange(listed.Value!
                    .Where(item => item.Moved is not null)
                    .Select(item => new TaskMoved(item, list)));
            }
        }

        return OperationResult<IReadOnlyList<TaskMoved>>.Ok(moved);
    }

    /// <inheritdoc />
    /// <remarks>
    /// With each list's revision as well as its time and length, because two
    /// writes inside one tick of a coarse clock, with the same length, have
    /// the same time and length. Always, rather than only while a file is
    /// settling, or the stamp would change again when it settled and say twice
    /// that one write happened. A settled list's revision comes from what is
    /// kept, without reading it.
    /// </remarks>
    public async Task<IReadOnlyList<TaskListStamp>> StampsAsync(CancellationToken ct = default)
    {
        if (!_workspace.IsAvailable())
        {
            return [];
        }

        var stamps = new List<TaskListStamp>();

        foreach (var list in Lists())
        {
            var path = PathFor(list);
            var stamp = FileStamp.Of(path).Text;

            if (stamp.Length > 0 && await LoadAsync(path, ct).ConfigureAwait(false) is { Succeeded: true, Value: { } read })
            {
                stamp += "#" + read.Revision;
            }

            stamps.Add(new TaskListStamp(list, stamp));
        }

        return stamps;
    }

    /// <summary>The workspace-wide list and every project's, whether or not each has a file yet.</summary>
    private IEnumerable<string?> Lists()
    {
        var projects = Path.Combine(_workspace.LocalPath, "projects");

        return Directory.Exists(projects)
            ? [null, .. Directory.EnumerateDirectories(projects).Select(Path.GetFileName).Order(StringComparer.Ordinal)]
            : [null];
    }

    /// <summary>
    /// The batch itself. Single changes come here as a batch of one, without
    /// the checks a batch from outside gets, which they never had: a project's
    /// list has always been made by the first task declared on it, and the
    /// idea service moves a task's record and plan itself before calling the
    /// single move.
    /// </summary>
    private async Task<OperationResult<TaskBatch>> ApplyCoreAsync(
        IReadOnlyList<TaskOperation> operations,
        bool fromOutside,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(operations);

        // An empty name is the workspace-wide list, as it always was, and is
        // made null here so the two never count as two lists.
        operations = [.. operations.Select(one => one with
        {
            Project = one.Project is { Length: > 0 } ? one.Project : null,
            To = one.To is { Length: > 0 } ? one.To : null,
        })];

        var outcomes = new TaskOutcome?[operations.Count];
        var now = _time.GetUtcNow();

        // What can be refused without reading anything, in the order the
        // single-entry methods always refused it: the id, then the workspace,
        // then a credential.
        for (var index = 0; index < operations.Count; index++)
        {
            if (Precheck(operations[index]) is { } refused)
            {
                outcomes[index] = Failed(index, operations[index], refused.Error, refused.Code);
            }
        }

        if (!_workspace.IsAvailable())
        {
            return OperationResult<TaskBatch>.Ok(new TaskBatch(
                [.. operations.Select((one, index) => outcomes[index] ?? Failed(index, one, NoWorkspace, ExitCode.WorkspaceSyncFailed))],
                []));
        }

        for (var index = 0; index < operations.Count; index++)
        {
            if (outcomes[index] is null && Screened(operations[index]) is { } refused)
            {
                outcomes[index] = Failed(index, operations[index], refused, ExitCode.PolicyViolation);
            }
        }

        if (fromOutside)
        {
            for (var index = 0; index < operations.Count; index++)
            {
                var operation = operations[index];

                if (outcomes[index] is not null)
                {
                    continue;
                }

                if (Touches(operation).FirstOrDefault(list => list is not null && !Directory.Exists(
                        Path.Combine(_workspace.LocalPath, "projects", list))) is { } missing)
                {
                    outcomes[index] = Failed(
                        index, operation, $"'{missing}' is not a project this workspace has.", ExitCode.ProjectNotFound);
                }
                else if (operation.Op == TaskOperation.Move && Carried(operation))
                {
                    // Moved here, the record and the plan would stay behind
                    // on the list it left, answering to nothing.
                    outcomes[index] = Failed(
                        index,
                        operation,
                        $"'{operation.Id!.Trim()}' has an idea record or a plan beside it, which a batch does not carry. "
                        + "Move it on its own with 'loadout task move', which takes them with it.",
                        ExitCode.InvalidArguments);
                }
            }
        }

        // Every list a batch names, the ones it will be refused on included,
        // so a refused operation still fails the rest of its list.
        var lists = new Dictionary<string, Group>(StringComparer.Ordinal);

        Group GroupOf(string? slug)
        {
            var key = TaskOperations.Named(slug);

            if (!lists.TryGetValue(key, out var group))
            {
                group = new Group(slug, PathFor(slug));
                lists[key] = group;
            }

            return group;
        }

        for (var index = 0; index < operations.Count; index++)
        {
            // A name that cannot be a list names no file, so nothing is
            // locked or read for it, and its refusal stands alone.
            if (Touches(operations[index]).Any(list => TaskPaths.Rejection(list) is not null))
            {
                continue;
            }

            foreach (var list in Touches(operations[index]))
            {
                GroupOf(list).Operations.Add(index);
            }
        }

        var held = await _yaml.HoldAsync(lists.Values.Select(group => group.Path), ct).ConfigureAwait(false);

        if (held.Failed)
        {
            return OperationResult<TaskBatch>.Ok(new TaskBatch(
                [.. operations.Select((one, index) => outcomes[index] ?? Failed(index, one, held.Error!, held.ExitCode))],
                []));
        }

        using var hold = held.Value!;

        foreach (var group in lists.Values)
        {
            var loaded = await LoadAsync(group.Path, ct).ConfigureAwait(false);

            if (loaded.Failed)
            {
                group.Failure = (loaded.Error!, loaded.ExitCode);

                continue;
            }

            group.List = loaded.Value!.Value;
            group.Revision = loaded.Value.Revision;
        }

        // Applied in order, in memory, so a later operation sees what an
        // earlier one did to the same list: add a task, then move it.
        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];

            if (outcomes[index] is not null)
            {
                continue;
            }

            var groups = Touches(operation).Select(GroupOf).ToList();

            if (groups.FirstOrDefault(group => group.Failure is not null) is { Failure: { } unread })
            {
                outcomes[index] = Failed(index, operation, unread.Error, unread.Code);

                continue;
            }

            if (operation.Revision is { } expected && !string.Equals(expected, groups[0].Revision, StringComparison.Ordinal))
            {
                outcomes[index] = Failed(
                    index,
                    operation,
                    $"{TaskPaths.Describe(operation.Project)} has changed since it was read: this was made from revision "
                    + $"{expected} and it is at {groups[0].Revision} now. Read it again and make the change from what it says.",
                    ExitCode.ChangedSinceRead);

                continue;
            }

            var applied = Change(operation, groups, now);

            outcomes[index] = applied.Failed
                ? Failed(index, operation, applied.Error!, applied.ExitCode)
                : new TaskOutcome(
                    index, operation.Op, operation.Project, applied.Value!.Id,
                    null, ExitCode.Success, Copy(applied.Value), operation.Op == TaskOperation.Move ? operation.To : null);
        }

        // A list with any operation refused is written with none of them,
        // and so is every list a move ties it to, until nothing more falls.
        var failing = new Dictionary<Group, (int Index, TaskOutcome Outcome)>();

        foreach (var group in lists.Values)
        {
            if (group.Operations.FirstOrDefault(index => outcomes[index]!.Error is not null, -1) is var first and >= 0)
            {
                failing[group] = (first, outcomes[first]!);
            }
        }

        for (var spread = true; spread;)
        {
            spread = false;

            foreach (var index in Enumerable.Range(0, operations.Count).Where(one => operations[one].Op == TaskOperation.Move))
            {
                var tied = Touches(operations[index]).Where(list => lists.ContainsKey(TaskOperations.Named(list))).Select(GroupOf).Distinct().ToList();

                if (tied.FirstOrDefault(failing.ContainsKey) is { } fallen)
                {
                    foreach (var group in tied.Where(group => !failing.ContainsKey(group)))
                    {
                        failing[group] = failing[fallen];
                        spread = true;
                    }
                }
            }
        }

        for (var index = 0; index < operations.Count; index++)
        {
            if (outcomes[index]!.Error is not null)
            {
                continue;
            }

            if (Touches(operations[index]).Select(GroupOf).FirstOrDefault(failing.ContainsKey) is { } fallen)
            {
                var (cause, outcome) = failing[fallen];

                outcomes[index] = Failed(
                    index,
                    operations[index],
                    $"Not applied, because operation {cause} could not be ({outcome.Error}) and "
                    + $"{TaskPaths.Describe(fallen.Slug)} is changed all or nothing.",
                    outcome.Code) with { HeldBack = true };
            }
        }

        // Lists a move puts something on are written before the lists it
        // takes it from. Two files cannot be changed at one instant, and a
        // task briefly on both lists is a duplicate somebody can see and
        // remove, where a task on neither is gone with nothing to say so.
        var receiving = new HashSet<Group>(Enumerable.Range(0, operations.Count)
            .Where(index => operations[index].Op == TaskOperation.Move && outcomes[index]!.Error is null)
            .Select(index => GroupOf(operations[index].To)));

        var written = new HashSet<Group>();

        foreach (var group in lists.Values
            .Where(group => group.Changed && !failing.ContainsKey(group))
            .OrderBy(group => receiving.Contains(group) ? 0 : 1))
        {
            var saved = await hold.WriteAsync(group.Path, group.List!, true, ct).ConfigureAwait(false);

            if (saved.Failed)
            {
                foreach (var index in group.Operations.Where(index => outcomes[index]!.Error is null))
                {
                    var operation = operations[index];
                    var landed = operation.Op == TaskOperation.Move && written.Contains(GroupOf(operation.To));

                    outcomes[index] = Failed(
                        index,
                        operation,
                        landed
                            ? $"'{operation.Id?.Trim()}' was added to {TaskPaths.Describe(operation.To)} but could not be taken off "
                                + $"{TaskPaths.Describe(operation.Project)}, so it is in both: {saved.Error}"
                            : saved.Error!,
                        saved.ExitCode);
                }

                continue;
            }

            group.Revision = saved.Value!;
            written.Add(group);
            _kept.Remember(group.Path, group.List!, saved.Value!);
        }

        return OperationResult<TaskBatch>.Ok(new TaskBatch(
            [.. outcomes.Select(one => one!)],
            [.. lists.Values
                .Where(group => group.Revision is not null)
                .Select(group => new TaskListRevision(group.Slug, group.Revision!, written.Contains(group)))]));
    }

    /// <summary>Whether a task being moved has an idea's working record or a plan kept beside its list.</summary>
    private bool Carried(TaskOperation operation)
    {
        var ideas = TaskPaths.Ideas(_workspace.LocalPath, operation.Project);
        var id = operation.Id!.Trim();

        return File.Exists(Path.Combine(ideas, id + ".yaml")) || File.Exists(Path.Combine(ideas, id + ".md"));
    }

    /// <summary>The lists an operation changes: its own, and for a move, where it goes.</summary>
    private static IEnumerable<string?> Touches(TaskOperation operation) =>
        operation.Op == TaskOperation.Move && !SameList(operation.Project, operation.To)
            ? [operation.Project, operation.To]
            : [operation.Project];

    /// <summary>What can be refused about an operation from the operation alone.</summary>
    private static (string Error, ExitCode Code)? Precheck(TaskOperation operation)
    {
        if (!TaskOperation.Ops.Contains(operation.Op))
        {
            return ($"There is nothing called '{operation.Op}' to do to a task. It is one of {string.Join(", ", TaskOperation.Ops)}.",
                ExitCode.InvalidArguments);
        }

        if (TaskPaths.Rejection(operation.Project) is { } project)
        {
            return (project, ExitCode.InvalidArguments);
        }

        if (operation.Op == TaskOperation.Move && TaskPaths.Rejection(operation.To) is { } to)
        {
            return (to, ExitCode.InvalidArguments);
        }

        if (operation.Op == TaskOperation.Add && string.IsNullOrWhiteSpace(operation.Id))
        {
            return string.IsNullOrWhiteSpace(operation.Title)
                ? ("A task needs a title: what is to be done.", ExitCode.InvalidArguments)
                : null;
        }

        return TaskIds.Rejection(operation.Id) is { } rejected ? (rejected, ExitCode.InvalidArguments) : null;
    }

    /// <summary>
    /// Why an operation's words cannot be recorded, or null when they can.
    /// </summary>
    /// <remarks>
    /// Screened here rather than at the call site, so every caller is screened
    /// rather than the one somebody remembered: the command line, an agent
    /// over MCP and the page all reach this. An agent writing a title straight
    /// out of what it was just looking at is how a credential reaches a file
    /// that then gets committed, and this file is in the workspace, which is
    /// exactly the thing that travels.
    /// <para>
    /// The pattern is named, never the value: a refusal that quoted what it
    /// found would put the credential into terminal scrollback and logs, which
    /// is the whole problem.
    /// </para>
    /// </remarks>
    private static string? Screened(TaskOperation operation)
    {
        if (operation.Op is not (TaskOperation.Add or TaskOperation.Declare))
        {
            return null;
        }

        var patterns = SecretScanner.Match(
            string.Join(
                ' ',
                [
                    operation.Title ?? string.Empty,
                    operation.Note ?? string.Empty,
                    operation.Detail ?? string.Empty,
                    .. operation.DoneWhen ?? [],
                    operation.Reason ?? string.Empty,
                ]));

        return patterns.Count > 0
            ? $"That looks like it contains a credential ({string.Join(", ", patterns)}), so "
                + "nothing was recorded. Describe the task without the value."
            : null;
    }

    /// <summary>Makes one operation's change to lists already read, in memory.</summary>
    private static OperationResult<TaskItem> Change(TaskOperation operation, IReadOnlyList<Group> groups, DateTimeOffset now)
    {
        var list = groups[0].List!;

        switch (operation.Op)
        {
            case TaskOperation.Add:
            {
                var id = operation.Id is { Length: > 0 } given
                    ? given.Trim()
                    : TaskIds.From(operation.Title!, list.Items.Select(item => item.Id));

                if (list.Items.Any(item => Same(item.Id, id)))
                {
                    return OperationResult<TaskItem>.Fail(
                        $"{TaskPaths.Describe(operation.Project)} already has a task called '{id}'. Declare it to change it.",
                        ExitCode.InvalidArguments);
                }

                groups[0].Changed = true;

                return OperationResult<TaskItem>.Ok(Declare(list, operation with { Id = id, State = operation.State ?? TaskState.Open }, now));
            }

            case TaskOperation.Declare:
                groups[0].Changed = true;

                return OperationResult<TaskItem>.Ok(Declare(list, operation, now));

            case TaskOperation.Remove:
            {
                var id = operation.Id!.Trim();

                if (list.Items.FirstOrDefault(item => Same(item.Id, id)) is not { } removing)
                {
                    return OperationResult<TaskItem>.Fail(
                        $"'{id}' is not a task of {TaskPaths.Describe(operation.Project)}.", ExitCode.InvalidArguments);
                }

                list.Items.RemoveAll(item => Same(item.Id, id));
                groups[0].Changed = true;

                return OperationResult<TaskItem>.Ok(removing);
            }

            default:
            {
                var id = operation.Id!.Trim();

                if (list.Items.FirstOrDefault(item => Same(item.Id, id)) is not { } moving)
                {
                    return OperationResult<TaskItem>.Fail(
                        $"'{id}' is not a task of {TaskPaths.Describe(operation.Project)}.", ExitCode.InvalidArguments);
                }

                if (groups.Count == 1)
                {
                    // Already where it was asked to go, which is nothing to do
                    // and nothing to write.
                    return OperationResult<TaskItem>.Ok(moving);
                }

                var destination = groups[1].List!;

                if (destination.Items.Any(item => Same(item.Id, id)))
                {
                    return OperationResult<TaskItem>.Fail(
                        $"{TaskPaths.Describe(operation.To)} already has a task called '{id}', so nothing was "
                        + "moved. Remove or rename one of them first.",
                        ExitCode.InvalidArguments);
                }

                // The latest move only. A task moved twice says where it came
                // from last, which is the list somebody looking for it would
                // have seen it on.
                list.Items.RemoveAll(item => Same(item.Id, id));
                moving.Moved = new TaskMove { From = operation.Project ?? string.Empty, Utc = now };
                destination.Items.Add(moving);
                groups[0].Changed = groups[1].Changed = true;

                return OperationResult<TaskItem>.Ok(moving);
            }
        }
    }

    /// <summary>Records a state for a task on a list in memory, adding it when the id is new.</summary>
    private static TaskItem Declare(TaskList list, TaskOperation operation, DateTimeOffset now)
    {
        var id = operation.Id!.Trim();
        var existing = list.Items.FirstOrDefault(item => Same(item.Id, id));

        var added = existing is null;

        if (existing is null)
        {
            existing = new TaskItem { Id = id };
            list.Items.Add(existing);
        }

        Remember(existing);

        var changed = added || (operation.State is { } next && next != existing.State);
        var reason = string.Empty;

        // No state leaves the one it has, so a change of title from the page
        // says nothing about where the work stands; a new task is open.
        if (operation.State is { } state)
        {
            existing.State = state;
        }

        if (operation.Kind is { } given)
        {
            existing.Kind = given;
        }

        existing.DeclaredBy = operation.By.Trim();
        existing.DeclaredUtc = now;

        if (operation.Detail is { } detail)
        {
            existing.Detail = detail.Trim();
        }

        // A title is only replaced when one is given. Declaring a state
        // should not blank the description of the thing.
        if (operation.Title is { Length: > 0 } title)
        {
            existing.Title = TaskTitles.Shorten(title);

            // Said once, when the original first goes into the detail,
            // rather than every time the same long title is given again.
            if (TaskTitles.Shortens(title) && TaskTitles.Keep(title, existing.Detail) is var kept
                && kept != existing.Detail)
            {
                existing.Detail = kept;
                reason = TaskTitles.ShortenedReason;
            }
        }

        if (operation.Note is { } note)
        {
            existing.Note = note.Trim();
        }

        // Replaced as a whole rather than added to: the list is what somebody
        // confirmed, and a condition they took out must not linger. Blank ones
        // are dropped, since a run can never report a verdict on them.
        if (operation.DoneWhen is { } doneWhen)
        {
            existing.DoneWhen = [.. doneWhen.Select(one => one.Trim()).Where(one => one.Length > 0)];
        }

        // Said by whoever declared it, such as the queue saying where a run's
        // pull request is, so it stays with the claim rather than replacing
        // somebody's note.
        if (operation.Reason is { } why && why.Trim().Length > 0)
        {
            reason = reason.Length > 0 ? $"{why.Trim()}; {reason}" : why.Trim();
        }

        // A state said again is not a change, or a task a launcher marks as
        // doing on every launch would fill its history with the same line.
        // What did change is always recorded.
        if (changed || reason.Length > 0)
        {
            existing.History.Add(new TaskEvent
            {
                State = existing.State,
                By = existing.DeclaredBy,
                Utc = existing.DeclaredUtc,
                Reason = reason,
            });
        }

        return existing;
    }

    /// <summary>
    /// Gives a task recorded before there was a history its last claim as the
    /// first entry, so the claim is not lost when the next one is added.
    /// </summary>
    private static void Remember(TaskItem item)
    {
        if (item.History.Count == 0 && item.DeclaredUtc != default)
        {
            item.History.Add(new TaskEvent { State = item.State, By = item.DeclaredBy, Utc = item.DeclaredUtc });
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<TaskShortened>>> ShortenTitlesAsync(
        string? projectSlug,
        string by,
        bool apply,
        CancellationToken ct = default)
    {
        if (!_workspace.IsAvailable())
        {
            return OperationResult<IReadOnlyList<TaskShortened>>.Fail(NoWorkspace, ExitCode.WorkspaceSyncFailed);
        }

        var path = PathFor(projectSlug);

        // Held as a batch is, so a change made meanwhile is not written over.
        var held = await _yaml.HoldAsync([path], ct).ConfigureAwait(false);

        if (held.Failed)
        {
            return OperationResult<IReadOnlyList<TaskShortened>>.Fail(held.Error!, held.ExitCode);
        }

        using var hold = held.Value!;
        var loaded = await LoadAsync(path, ct).ConfigureAwait(false);

        if (loaded.Failed)
        {
            return OperationResult<IReadOnlyList<TaskShortened>>.Fail(loaded.Error!, loaded.ExitCode);
        }

        // A copy, so what is kept between reads is untouched until the file is.
        var list = Copy(loaded.Value!.Value);
        var over = list.Items.Where(item => item.Id.Length > 0 && TaskTitles.Shortens(item.Title)).ToList();
        IReadOnlyList<TaskShortened> found =
            [.. over.Select(item => new TaskShortened(item.Id, item.Title, TaskTitles.Shorten(item.Title)))];

        // Not written when nothing needs it: a list rewritten for no change is
        // a diff in the workspace somebody has to read for nothing.
        if (!apply || found.Count == 0)
        {
            return OperationResult<IReadOnlyList<TaskShortened>>.Ok(found);
        }

        var now = _time.GetUtcNow();

        foreach (var item in over)
        {
            Remember(item);

            item.Detail = TaskTitles.Keep(item.Title, item.Detail);
            item.Title = TaskTitles.Shorten(item.Title);

            // The state and who last declared it are left alone: this changes
            // how the task is written down, not where it stands.
            item.History.Add(new TaskEvent { State = item.State, By = by.Trim(), Utc = now, Reason = TaskTitles.ShortenedReason });
        }

        var saved = await hold.WriteAsync(path, list, true, ct).ConfigureAwait(false);

        if (saved.Failed)
        {
            return OperationResult<IReadOnlyList<TaskShortened>>.Fail(saved.Error!, saved.ExitCode);
        }

        _kept.Remember(path, list, saved.Value!);

        return OperationResult<IReadOnlyList<TaskShortened>>.Ok(found);
    }

    /// <summary>A list as it stands, from what was kept when the file has not changed since.</summary>
    private Task<OperationResult<YamlRead<TaskList>>> LoadAsync(string path, CancellationToken ct) => _kept.ReadAsync(path, ct);

    /// <summary>A list nothing else holds, so a caller changing what it was given changes nothing kept.</summary>
    internal static TaskList Copy(TaskList list) =>
        new() { SchemaVersion = list.SchemaVersion, Items = [.. list.Items.Select(Copy)] };

    /// <summary>A task nothing else holds.</summary>
    /// <remarks>A test reads every property of <see cref="TaskItem"/>, so one added later is not left out of this.</remarks>
    internal static TaskItem Copy(TaskItem item) => new()
    {
        Id = item.Id,
        Title = item.Title,
        State = item.State,
        Kind = item.Kind,
        DeclaredBy = item.DeclaredBy,
        DeclaredUtc = item.DeclaredUtc,
        Note = item.Note,
        Detail = item.Detail,
        DoneWhen = [.. item.DoneWhen],
        History = [.. item.History.Select(one => new TaskEvent { State = one.State, By = one.By, Utc = one.Utc, Reason = one.Reason })],
        Moved = item.Moved is { } moved ? new TaskMove { From = moved.From, Utc = moved.Utc } : null,
    };

    /// <summary>One operation's outcome, as the single-entry methods have always answered.</summary>
    private static OperationResult<TaskItem> One(OperationResult<TaskBatch> batch)
    {
        var outcome = batch.Value!.Outcomes[0];

        return outcome.Applied
            ? OperationResult<TaskItem>.Ok(outcome.Task!)
            : OperationResult<TaskItem>.Fail(outcome.Error!, outcome.Code);
    }

    private static TaskOutcome Failed(int index, TaskOperation operation, string error, ExitCode code) =>
        new(index, operation.Op, operation.Project, operation.Id?.Trim() ?? string.Empty, error, code, null);

    private static bool SameList(string? left, string? right) =>
        string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>One list a batch touches, and what the batch has done to it so far.</summary>
    private sealed class Group(string? slug, string path)
    {
        public string? Slug { get; } = slug;

        public string Path { get; } = path;

        public List<int> Operations { get; } = [];

        public TaskList? List { get; set; }

        public string? Revision { get; set; }

        public (string Error, ExitCode Code)? Failure { get; set; }

        /// <summary>Whether anything applied to it so far has changed it, and so whether it is written.</summary>
        public bool Changed { get; set; }
    }
}

/// <summary>Where task lists and the ideas beside them are kept.</summary>
public static class TaskPaths
{
    /// <summary>A project's list, or the workspace-wide one for null.</summary>
    public static string List(string workspace, string? slug) => slug is { Length: > 0 }
        ? Path.Combine(workspace, "projects", slug, "tasks.yaml")
        : Path.Combine(workspace, "tasks.yaml");

    /// <summary>
    /// Every list that has been written: null for the workspace-wide one first,
    /// then each project's by slug.
    /// </summary>
    public static IReadOnlyList<string?> Lists(string workspace)
    {
        var projects = Path.Combine(workspace, "projects");

        IEnumerable<string?> lists = Directory.Exists(projects)
            ? [null, .. Directory.EnumerateDirectories(projects).Select(Path.GetFileName).Order(StringComparer.Ordinal)]
            : [null];

        return [.. lists.Where(slug => File.Exists(List(workspace, slug)))];
    }

    /// <summary>The directory ideas' working records are kept in, beside their list.</summary>
    public static string Ideas(string workspace, string? slug) => slug is { Length: > 0 }
        ? Path.Combine(workspace, "projects", slug, "ideas")
        : Path.Combine(workspace, "ideas");

    /// <summary>A path within a list's directory, relative to the workspace, as its records spell it.</summary>
    public static string Relative(string? slug, string file) => slug is { Length: > 0 }
        ? $"projects/{slug}/{file}"
        : file;

    /// <summary>Why a project's name cannot name a list, or null when it can, as null always can.</summary>
    /// <remarks>
    /// A name is made into a path under the workspace, so one that climbs out
    /// of it, or reaches into another project's directory, is refused before
    /// anything is read. Names come from the page and from agents as well as
    /// from the project registry now.
    /// </remarks>
    public static string? Rejection(string? slug) =>
        slug is not { Length: > 0 }
        || (slug.Trim() == slug && slug != "." && slug != ".."
            && slug.IndexOfAny(['/', '\\', ':']) < 0 && slug.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            ? null
            : $"'{slug}' cannot be a project's name.";

    /// <summary>How a list is named to a person.</summary>
    public static string Describe(string? slug) => slug is { Length: > 0 }
        ? slug
        : "the workspace-wide list";
}
