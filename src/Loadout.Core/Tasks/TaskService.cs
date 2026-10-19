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
        string? detail = null);

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
internal sealed class TaskService : ITaskService
{
    private readonly IWorkspaceManager _workspace;
    private readonly YamlStore _yaml;
    private readonly TimeProvider _time;

    public TaskService(IWorkspaceManager workspace, YamlStore yaml, TimeProvider time)
    {
        _workspace = workspace;
        _yaml = yaml;
        _time = time;
    }

    private string PathFor(string? slug) => TaskPaths.List(_workspace.LocalPath, slug);

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<TaskItem>>> ListAsync(
        string? projectSlug,
        CancellationToken ct = default)
    {
        if (!_workspace.IsAvailable())
        {
            return OperationResult<IReadOnlyList<TaskItem>>.Fail(
                "There is no workspace on this machine, so there is nowhere to keep tasks.",
                ExitCode.WorkspaceSyncFailed);
        }

        var loaded = await _yaml
            .LoadAsync(PathFor(projectSlug), () => new TaskList(), ct)
            .ConfigureAwait(false);

        return loaded.Succeeded
            ? OperationResult<IReadOnlyList<TaskItem>>.Ok(
                [.. loaded.Value!.Items.Where(item => item.Id.Length > 0)])
            : OperationResult<IReadOnlyList<TaskItem>>.Fail(loaded.Error!, loaded.ExitCode);
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
        string? detail = null)
    {
        if (TaskIds.Rejection(id) is { } rejected)
        {
            return OperationResult<TaskItem>.Fail(rejected, ExitCode.InvalidArguments);
        }

        if (!_workspace.IsAvailable())
        {
            return OperationResult<TaskItem>.Fail(
                "There is no workspace on this machine, so there is nowhere to keep tasks.",
                ExitCode.WorkspaceSyncFailed);
        }

        // Screened here rather than at the call site, so every caller is
        // screened rather than the one somebody remembered. An agent writing a
        // title straight out of what it was just looking at is how a credential
        // reaches a file that then gets committed — and this file is in the
        // workspace, which is exactly the thing that travels.
        //
        // The pattern is named, never the value: a refusal that quoted what it
        // found would put the credential into terminal scrollback and logs,
        // which is the whole problem.
        var patterns = SecretScanner.Match(
            string.Join(' ', title ?? string.Empty, note ?? string.Empty, detail ?? string.Empty));

        if (patterns.Count > 0)
        {
            return OperationResult<TaskItem>.Fail(
                $"That looks like it contains a credential ({string.Join(", ", patterns)}), so "
                + "nothing was recorded. Describe the task without the value.",
                ExitCode.PolicyViolation);
        }

        var trimmed = id.Trim();
        TaskItem? declared = null;

        // Read and written under one lock. Two sessions declaring at once would
        // otherwise each read the same list and write their own over it, and
        // one of the two declarations would be gone with the file still valid
        // and both callers told it worked.
        var written = await _yaml.UpdateAsync<TaskList>(
            PathFor(projectSlug),
            () => new TaskList(),
            list =>
            {
                var existing = list.Items.FirstOrDefault(
                    item => string.Equals(item.Id, trimmed, StringComparison.OrdinalIgnoreCase));

                var added = existing is null;

                if (existing is null)
                {
                    existing = new TaskItem { Id = trimmed };
                    list.Items.Add(existing);
                }

                Remember(existing);

                var changed = added || existing.State != state;
                var reason = string.Empty;

                existing.State = state;

                if (kind is { } given)
                {
                    existing.Kind = given;
                }

                existing.DeclaredBy = declaredBy.Trim();
                existing.DeclaredUtc = _time.GetUtcNow();

                if (detail is not null)
                {
                    existing.Detail = detail.Trim();
                }

                // A title is only replaced when one is given. Declaring a state
                // should not blank the description of the thing.
                if (title is { Length: > 0 })
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

                if (note is not null)
                {
                    existing.Note = note.Trim();
                }

                // A state said again is not a change, or a task a launcher
                // marks as doing on every launch would fill its history with
                // the same line. What did change is always recorded.
                if (changed || reason.Length > 0)
                {
                    existing.History.Add(new TaskEvent
                    {
                        State = state,
                        By = existing.DeclaredBy,
                        Utc = existing.DeclaredUtc,
                        Reason = reason,
                    });
                }

                declared = existing;
            },
            true,
            ct).ConfigureAwait(false);

        return written.Succeeded && declared is not null
            ? OperationResult<TaskItem>.Ok(declared)
            : OperationResult<TaskItem>.Fail(
                written.Error ?? "The task could not be recorded.", written.ExitCode);
    }

    /// <inheritdoc />
    public async Task<OperationResult> RemoveAsync(
        string? projectSlug,
        string id,
        CancellationToken ct = default)
    {
        if (TaskIds.Rejection(id) is { } rejected)
        {
            return OperationResult.Fail(rejected, ExitCode.InvalidArguments);
        }

        if (!_workspace.IsAvailable())
        {
            return OperationResult.Fail(
                "There is no workspace on this machine, so there is nowhere to keep tasks.",
                ExitCode.WorkspaceSyncFailed);
        }

        var trimmed = id.Trim();
        var removed = false;

        var written = await _yaml.UpdateAsync<TaskList>(
            PathFor(projectSlug),
            () => new TaskList(),
            list => removed = list.Items.RemoveAll(
                item => string.Equals(item.Id, trimmed, StringComparison.OrdinalIgnoreCase)) > 0,
            true,
            ct).ConfigureAwait(false);

        if (written.Failed)
        {
            return OperationResult.Fail(written.Error!, written.ExitCode);
        }

        return removed
            ? OperationResult.Ok()
            : OperationResult.Fail($"'{trimmed}' is not a task of {TaskPaths.Describe(projectSlug)}.", ExitCode.InvalidArguments);
    }

    /// <inheritdoc />
    public async Task<OperationResult<TaskItem>> MoveAsync(
        string? fromSlug,
        string? toSlug,
        string id,
        CancellationToken ct = default)
    {
        if (TaskIds.Rejection(id) is { } rejected)
        {
            return OperationResult<TaskItem>.Fail(rejected, ExitCode.InvalidArguments);
        }

        if (!_workspace.IsAvailable())
        {
            return OperationResult<TaskItem>.Fail(
                "There is no workspace on this machine, so there is nowhere to keep tasks.",
                ExitCode.WorkspaceSyncFailed);
        }

        var trimmed = id.Trim();

        var source = await ListAsync(fromSlug, ct).ConfigureAwait(false);

        if (source.Failed)
        {
            return OperationResult<TaskItem>.Fail(source.Error!, source.ExitCode);
        }

        if (source.Value!.FirstOrDefault(item => Same(item.Id, trimmed)) is not { } moving)
        {
            return OperationResult<TaskItem>.Fail(
                $"'{trimmed}' is not a task of {TaskPaths.Describe(fromSlug)}.",
                ExitCode.InvalidArguments);
        }

        if (string.Equals(fromSlug ?? string.Empty, toSlug ?? string.Empty, StringComparison.Ordinal))
        {
            return OperationResult<TaskItem>.Ok(moving);
        }

        // Written to the destination first and taken from the source second.
        // Two files cannot be changed under one lock, so one order has to be
        // chosen for the moment between them, and a task briefly in both lists
        // is a duplicate somebody can see and remove, where a task in neither
        // is gone with nothing to say so.
        var clash = false;

        // The latest move only. A task moved twice says where it came from
        // last, which is the list somebody looking for it would have seen it on.
        moving.Moved = new TaskMove { From = fromSlug ?? string.Empty, Utc = _time.GetUtcNow() };

        var written = await _yaml.UpdateAsync<TaskList>(
            PathFor(toSlug),
            () => new TaskList(),
            list =>
            {
                clash = list.Items.Any(item => Same(item.Id, trimmed));

                if (!clash)
                {
                    list.Items.Add(moving);
                }
            },
            true,
            ct).ConfigureAwait(false);

        if (written.Failed)
        {
            return OperationResult<TaskItem>.Fail(written.Error!, written.ExitCode);
        }

        if (clash)
        {
            return OperationResult<TaskItem>.Fail(
                $"{TaskPaths.Describe(toSlug)} already has a task called '{trimmed}', so nothing was "
                + "moved. Remove or rename one of them first.",
                ExitCode.InvalidArguments);
        }

        var taken = await _yaml.UpdateAsync<TaskList>(
            PathFor(fromSlug),
            () => new TaskList(),
            list => list.Items.RemoveAll(item => Same(item.Id, trimmed)),
            true,
            ct).ConfigureAwait(false);

        return taken.Succeeded
            ? OperationResult<TaskItem>.Ok(moving)
            : OperationResult<TaskItem>.Fail(
                $"'{trimmed}' was added to {TaskPaths.Describe(toSlug)} but could not be taken off "
                + $"{TaskPaths.Describe(fromSlug)}, so it is in both: {taken.Error}",
                taken.ExitCode);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<TaskMoved>>> MovedAsync(CancellationToken ct = default)
    {
        if (!_workspace.IsAvailable())
        {
            return OperationResult<IReadOnlyList<TaskMoved>>.Fail(
                "There is no workspace on this machine, so there is nowhere to keep tasks.",
                ExitCode.WorkspaceSyncFailed);
        }

        var moved = new List<TaskMoved>();

        foreach (var list in TaskPaths.Lists(_workspace.LocalPath))
        {
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
    public async Task<OperationResult<IReadOnlyList<TaskShortened>>> ShortenTitlesAsync(
        string? projectSlug,
        string by,
        bool apply,
        CancellationToken ct = default)
    {
        var listed = await ListAsync(projectSlug, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<IReadOnlyList<TaskShortened>>.Fail(listed.Error!, listed.ExitCode);
        }

        IReadOnlyList<TaskShortened> found =
            [.. listed.Value!.Where(item => TaskTitles.Shortens(item.Title)).Select(Shortened)];

        // Not written when nothing needs it: a list rewritten for no change is
        // a diff in the workspace somebody has to read for nothing.
        if (!apply || found.Count == 0)
        {
            return OperationResult<IReadOnlyList<TaskShortened>>.Ok(found);
        }

        var shortened = new List<TaskShortened>();

        var written = await _yaml.UpdateAsync<TaskList>(
            PathFor(projectSlug),
            () => new TaskList(),
            list =>
            {
                foreach (var item in list.Items.Where(item => item.Id.Length > 0 && TaskTitles.Shortens(item.Title)))
                {
                    shortened.Add(Shortened(item));
                    Remember(item);

                    item.Detail = TaskTitles.Keep(item.Title, item.Detail);
                    item.Title = TaskTitles.Shorten(item.Title);

                    // The state and who last declared it are left alone: this
                    // changes how the task is written down, not where it stands.
                    item.History.Add(new TaskEvent
                    {
                        State = item.State,
                        By = by.Trim(),
                        Utc = _time.GetUtcNow(),
                        Reason = TaskTitles.ShortenedReason,
                    });
                }
            },
            true,
            ct).ConfigureAwait(false);

        return written.Succeeded
            ? OperationResult<IReadOnlyList<TaskShortened>>.Ok(shortened)
            : OperationResult<IReadOnlyList<TaskShortened>>.Fail(
                written.Error ?? "The titles could not be shortened.", written.ExitCode);
    }

    private static TaskShortened Shortened(TaskItem item) =>
        new(item.Id, item.Title, TaskTitles.Shorten(item.Title));

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

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>How a list is named to a person.</summary>
    public static string Describe(string? slug) => slug is { Length: > 0 }
        ? slug
        : "the workspace-wide list";
}
