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
    /// </remarks>
    Task<OperationResult<TaskItem>> DeclareAsync(
        string? projectSlug,
        string id,
        TaskState state,
        string declaredBy,
        string? title = null,
        string? note = null,
        CancellationToken ct = default,
        TaskKind? kind = null);

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
        TaskKind? kind = null)
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
        var patterns = SecretScanner.Match(string.Join(' ', title ?? string.Empty, note ?? string.Empty));

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

                if (existing is null)
                {
                    existing = new TaskItem { Id = trimmed };
                    list.Items.Add(existing);
                }

                existing.State = state;

                if (kind is { } given)
                {
                    existing.Kind = given;
                }

                existing.DeclaredBy = declaredBy.Trim();
                existing.DeclaredUtc = _time.GetUtcNow();

                // A title is only replaced when one is given. Declaring a state
                // should not blank the description of the thing.
                if (title is { Length: > 0 })
                {
                    existing.Title = title.Trim();
                }

                if (note is not null)
                {
                    existing.Note = note.Trim();
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
