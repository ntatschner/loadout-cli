using System.Text.Json;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Tasks;

namespace Loadout.Core.Tasks;

/// <summary>One change to a task, as a batch carries it.</summary>
/// <remarks>
/// <para>
/// The same four changes the single-entry methods make, so a batch and a
/// single change run the same rules: <c>add</c> names a new task from its
/// title, <c>declare</c> says where one stands (adding it when the id is new),
/// <c>remove</c> forgets one and <c>move</c> takes one to another list.
/// </para>
/// <para>
/// A project of null is the workspace-wide list, everywhere here, as it is in
/// <see cref="ITaskService"/>.
/// </para>
/// </remarks>
/// <param name="Op">add, declare, remove or move.</param>
/// <param name="Project">The list the task is on, or null for the workspace-wide one.</param>
/// <param name="Id">The task. For add, an id to use, or null for one made from the title.</param>
/// <param name="State">For declare, the state to record. For add, open unless given.</param>
/// <param name="Title">What the task is. Left as it was when null or empty.</param>
/// <param name="Note">A note. Left as it was when null; an empty one clears it.</param>
/// <param name="Kind">Task or idea. Left as it was when null; a new task given none is work.</param>
/// <param name="By">Who is saying so.</param>
/// <param name="To">For move, the project it goes to, or null for the workspace-wide list.</param>
/// <param name="Revision">The revision of the list this was made from, or null to not check.</param>
public sealed record TaskOperation(
    string Op,
    string? Project = null,
    string? Id = null,
    TaskState? State = null,
    string? Title = null,
    string? Note = null,
    TaskKind? Kind = null,
    string By = "",
    string? To = null,
    string? Revision = null)
{
    public const string Add = "add";
    public const string Declare = "declare";
    public const string Remove = "remove";
    public const string Move = "move";

    /// <summary>Every op a batch takes, in the order they are documented.</summary>
    public static IReadOnlyList<string> Ops => [Add, Declare, Remove, Move];
}

/// <summary>What became of one operation in a batch.</summary>
/// <param name="Index">Where it was in the batch, from zero.</param>
/// <param name="Op">What it was.</param>
/// <param name="Project">The list it was made on, or null for the workspace-wide one.</param>
/// <param name="Id">The task, as it was named or, for add, as it was named for it.</param>
/// <param name="Error">Why it was not applied, or null when it was.</param>
/// <param name="Code">The exit code a command making this change alone would have ended with.</param>
/// <param name="Task">The task as it now stands, or as it stood before a remove; null when it was not applied.</param>
/// <param name="To">For a move, the list it went to.</param>
public sealed record TaskOutcome(
    int Index,
    string Op,
    string? Project,
    string Id,
    string? Error,
    ExitCode Code,
    TaskItem? Task,
    string? To = null)
{
    public bool Applied => Error is null;
}

/// <summary>A list's revision after a batch.</summary>
/// <param name="Project">The list, or null for the workspace-wide one.</param>
/// <param name="Revision">The revision it has now.</param>
/// <param name="Written">Whether this batch changed it.</param>
public sealed record TaskListRevision(string? Project, string Revision, bool Written);

/// <summary>What a batch did: one outcome for every operation, and every list it touched.</summary>
public sealed record TaskBatch(IReadOnlyList<TaskOutcome> Outcomes, IReadOnlyList<TaskListRevision> Lists)
{
    public bool Applied => Outcomes.All(one => one.Applied);
}

/// <summary>A list as it was read, with the revision a change made from it should name.</summary>
public sealed record TaskListRead(string? Project, IReadOnlyList<TaskItem> Items, string? Revision);

/// <summary>A list's file as it stands on disk: enough to tell, cheaply, that it changed.</summary>
/// <param name="Project">The list, or null for the workspace-wide one.</param>
/// <param name="Stamp">Its modification time and length, or empty when it does not exist.</param>
public sealed record TaskListStamp(string? Project, string Stamp);

/// <summary>
/// Reads a batch from the JSON every surface takes it in: the CLI's
/// <c>task apply</c>, the MCP tool and the HTTP route.
/// </summary>
/// <remarks>
/// One reader for all three, so a batch that works through one works through
/// the others. What it refuses is a request that is not a batch at all; an
/// operation that is a batch but cannot be applied is the service's to
/// refuse, beside the others, with its reason.
/// </remarks>
public static class TaskOperations
{
    /// <summary>The project field's value for the workspace-wide list.</summary>
    public const string WorkspaceWide = "-";

    /// <summary>The longest batch taken in one go.</summary>
    public const int Most = 200;

    /// <summary>
    /// Reads a JSON array of operations, each as
    /// <c>{"op", "project", "id", "state", "title", "note", "kind", "to", "revision"}</c>.
    /// </summary>
    /// <param name="json">The array.</param>
    /// <param name="project">The list an operation naming none is made on, or null for the workspace-wide one.</param>
    /// <param name="by">Who an operation naming nobody is said by.</param>
    public static OperationResult<IReadOnlyList<TaskOperation>> Parse(string json, string? project, string by)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return Refused($"That is not JSON: {ex.Message}");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Refused("A batch is a JSON array of operations, such as [{\"op\":\"declare\",\"id\":\"fix-login\",\"state\":\"done\"}].");
            }

            var items = document.RootElement.EnumerateArray().ToList();

            if (items.Count > Most)
            {
                return Refused($"That is {items.Count} operations, and a batch takes at most {Most}. Send it in parts.");
            }

            var operations = new List<TaskOperation>();

            for (var index = 0; index < items.Count; index++)
            {
                var read = One(items[index], index, project, by);

                if (read.Failed)
                {
                    return Refused(read.Error!);
                }

                operations.Add(read.Value!);
            }

            return OperationResult<IReadOnlyList<TaskOperation>>.Ok(operations);
        }
    }

    /// <summary>A list named on the wire: a project's slug, or <c>-</c> or empty for the workspace-wide one.</summary>
    public static string? List(string? named) =>
        named is { Length: > 0 } given && given.Trim() is { Length: > 0 } trimmed && trimmed != WorkspaceWide
            ? trimmed
            : null;

    /// <summary>A list as the wire names it.</summary>
    public static string Named(string? project) => project ?? WorkspaceWide;

    /// <summary>An outcome as every surface writes it.</summary>
    public static object Json(TaskOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new
        {
            index = outcome.Index,
            op = outcome.Op,
            project = outcome.Project,
            id = outcome.Id,
            applied = outcome.Applied,
            error = outcome.Error,
            code = (int)outcome.Code,
            to = outcome.Op == TaskOperation.Move && outcome.Applied ? Named(outcome.To) : null,
            task = outcome.Task is { } task ? Json(task) : null,
        };
    }

    /// <summary>A batch as every surface writes it.</summary>
    public static object Json(TaskBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        return new
        {
            applied = batch.Applied,
            results = batch.Outcomes.Select(Json),
            lists = batch.Lists.Select(one => new { project = one.Project, revision = one.Revision, written = one.Written }),
        };
    }

    /// <summary>A task as every surface writes it.</summary>
    public static object Json(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return new
        {
            id = task.Id,
            title = task.Title,
            state = task.State.ToString().ToLowerInvariant(),
            kind = task.Kind.ToString().ToLowerInvariant(),
            by = task.DeclaredBy,
            declared = task.DeclaredUtc,
            note = task.Note,
            moved = task.Moved is { } moved
                ? new { from = moved.From.Length > 0 ? moved.From : null, at = moved.Utc }
                : null,
        };
    }

    private static OperationResult<TaskOperation> One(JsonElement item, int index, string? project, string by)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return OperationResult<TaskOperation>.Fail($"Operation {index} is not an object.", ExitCode.InvalidArguments);
        }

        string? Text(string name) =>
            item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        bool Has(string name) => item.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null;

        foreach (var property in item.EnumerateObject())
        {
            if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                return OperationResult<TaskOperation>.Fail(
                    $"Operation {index} has '{property.Name}' as something other than text.", ExitCode.InvalidArguments);
            }

            if (!Known.Contains(property.Name))
            {
                return OperationResult<TaskOperation>.Fail(
                    $"Operation {index} has '{property.Name}', which is not something an operation takes. "
                    + $"They are {string.Join(", ", Known)}.",
                    ExitCode.InvalidArguments);
            }
        }

        var op = Text("op")?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!TaskOperation.Ops.Contains(op))
        {
            return OperationResult<TaskOperation>.Fail(
                $"Operation {index} has op '{op}'. It should be one of {string.Join(", ", TaskOperation.Ops)}.",
                ExitCode.InvalidArguments);
        }

        TaskState? state = null;

        if (Text("state") is { Length: > 0 } stated)
        {
            if (!Enum.TryParse<TaskState>(stated, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed) || int.TryParse(stated, out _))
            {
                return OperationResult<TaskOperation>.Fail(
                    $"Operation {index} has state '{stated}'. It should be one of open, doing, blocked, done or dropped.",
                    ExitCode.InvalidArguments);
            }

            state = parsed;
        }
        else if (op == TaskOperation.Declare)
        {
            return OperationResult<TaskOperation>.Fail(
                $"Operation {index} declares no state. Say where the task stands: open, doing, blocked, done or dropped.",
                ExitCode.InvalidArguments);
        }

        TaskKind? kind = null;

        if (Text("kind") is { Length: > 0 } kinded)
        {
            if (!Enum.TryParse<TaskKind>(kinded, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed) || int.TryParse(kinded, out _))
            {
                return OperationResult<TaskOperation>.Fail(
                    $"Operation {index} has kind '{kinded}'. It should be task or idea.", ExitCode.InvalidArguments);
            }

            kind = parsed;
        }

        if (op == TaskOperation.Move && !Has("to"))
        {
            return OperationResult<TaskOperation>.Fail(
                $"Operation {index} moves a task without saying where to. Give 'to' a project, or '-' for the workspace-wide list.",
                ExitCode.InvalidArguments);
        }

        return OperationResult<TaskOperation>.Ok(new TaskOperation(
            op,
            Has("project") ? List(Text("project")) : project,
            Text("id"),
            state,
            Text("title"),
            Text("note"),
            kind,
            Text("by") is { Length: > 0 } named ? named : by,
            List(Text("to")),
            Text("revision") is { Length: > 0 } revision ? revision : null));
    }

    private static readonly string[] Known = ["op", "project", "id", "state", "title", "note", "kind", "by", "to", "revision"];

    private static OperationResult<IReadOnlyList<TaskOperation>> Refused(string why) =>
        OperationResult<IReadOnlyList<TaskOperation>>.Fail(why, ExitCode.InvalidArguments);
}
