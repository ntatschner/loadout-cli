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
public interface ITaskService
{
    /// <summary>Everything recorded for a project.</summary>
    Task<OperationResult<IReadOnlyList<TaskItem>>> ListAsync(
        string projectSlug,
        CancellationToken ct = default);

    /// <summary>
    /// Records a state for a task, adding it when the id is new.
    /// </summary>
    /// <remarks>
    /// One call for both, because declaring is what a session actually does:
    /// it says where something stands, and whether that thing was already
    /// written down is not a distinction worth making it think about.
    /// </remarks>
    Task<OperationResult<TaskItem>> DeclareAsync(
        string projectSlug,
        string id,
        TaskState state,
        string declaredBy,
        string? title = null,
        string? note = null,
        CancellationToken ct = default);

    /// <summary>Forgets a task.</summary>
    Task<OperationResult> RemoveAsync(
        string projectSlug,
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

    private string PathFor(string slug) =>
        Path.Combine(_workspace.LocalPath, "projects", slug, "tasks.yaml");

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<TaskItem>>> ListAsync(
        string projectSlug,
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
        string projectSlug,
        string id,
        TaskState state,
        string declaredBy,
        string? title = null,
        string? note = null,
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
        string projectSlug,
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
            : OperationResult.Fail($"'{trimmed}' is not a task of {projectSlug}.", ExitCode.InvalidArguments);
    }
}
