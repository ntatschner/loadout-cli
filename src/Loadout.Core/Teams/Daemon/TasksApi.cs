using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Loadout.Core.Lists;
using Loadout.Core.Tasks;
using Loadout.Models;
using Loadout.Models.Tasks;

namespace Loadout.Core.Teams.Daemon;

/// <summary>
/// One task list, or one task, under <c>/api/v1/projects</c>, read and changed
/// through the same service the command line and the agents' tools use.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>GET {list}/tasks</term><description>The list, with its revision as the ETag. <c>?filter=</c> takes the query <c>task list --filter</c> does.</description></item>
/// <item><term>POST {list}/tasks</term><description>Adds one from <c>{"title", "id"?, "note"?, "state"?, "kind"?}</c>: 201 with it.</description></item>
/// <item><term>GET {list}/tasks/{id}</term><description>One task.</description></item>
/// <item><term>PATCH {list}/tasks/{id}</term><description>Changes what <c>{"state", "title", "note", "kind", "to"}</c> names; <c>to</c> moves it after the rest.</description></item>
/// <item><term>DELETE {list}/tasks/{id}</term><description>Forgets it.</description></item>
/// <item><term>POST {list}/tasks:batch</term><description>A JSON array of operations, as <c>task apply</c> takes; one result each.</description></item>
/// </list>
/// <para>
/// The list is a project's slug, or <c>-</c> for the workspace-wide list. A
/// change carrying <c>If-Match</c> is refused with 409 when the list has moved
/// on from that revision, as a memory topic's save is. Every change answers
/// with the task as it now stands and the list's new revision, so a page need
/// not read the list again to show what it did.
/// </para>
/// </remarks>
internal static class TasksApi
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Whether a path under <see cref="MemoryApi.Root"/> is one of these.</summary>
    public static bool Handles(string[] parts) =>
        parts is [_, "tasks", ..] or [_, "tasks:batch"];

    public static async Task HandleAsync(
        HttpListenerContext context,
        ITaskService tasks,
        string[] parts,
        bool watchOnly,
        string by,
        CancellationToken ct)
    {
        var method = context.Request.HttpMethod;

        if (method != "GET" && watchOnly)
        {
            await PlainAsync(context, 403, "This dashboard is only watching, so it cannot change tasks. Use 'loadout task' in a terminal.")
                .ConfigureAwait(false);

            return;
        }

        var list = TaskOperations.List(parts[0]);

        switch (parts)
        {
            case [_, "tasks"] when method == "GET":
                await ListAsync(context, tasks, list, ct).ConfigureAwait(false);
                return;

            case [_, "tasks"] when method == "POST":
                await AddAsync(context, tasks, list, by, ct).ConfigureAwait(false);
                return;

            case [_, "tasks", var id] when method == "GET":
                await OneAsync(context, tasks, list, id, ct).ConfigureAwait(false);
                return;

            case [_, "tasks", var id] when method == "PATCH":
                await ChangeAsync(context, tasks, list, id, by, ct).ConfigureAwait(false);
                return;

            case [_, "tasks", var id] when method == "DELETE":
                await SingleAsync(context, tasks, new TaskOperation(TaskOperation.Remove, list, id, Revision: IfMatch(context)), 200, ct)
                    .ConfigureAwait(false);
                return;

            case [_, "tasks:batch"] when method == "POST":
                await BatchAsync(context, tasks, list, by, ct).ConfigureAwait(false);
                return;

            case [_, "tasks"] or [_, "tasks:batch"]:
                await PlainAsync(context, 405, "A list is read with GET and added to with POST; a batch is a POST.").ConfigureAwait(false);
                return;

            case [_, "tasks", _]:
                await PlainAsync(context, 405, "A task is read with GET, changed with PATCH and forgotten with DELETE.").ConfigureAwait(false);
                return;

            default:
                await PlainAsync(context, 404, "There is nothing here.").ConfigureAwait(false);
                return;
        }
    }

    private static async Task ListAsync(HttpListenerContext context, ITaskService tasks, string? list, CancellationToken ct)
    {
        var read = await tasks.ReadAsync(list, ct).ConfigureAwait(false);

        if (read.Failed)
        {
            await PlainAsync(context, Status(read.ExitCode), read.Error!).ConfigureAwait(false);

            return;
        }

        var items = read.Value!.Items;

        if (context.Request.QueryString["filter"] is { Length: > 0 } filter)
        {
            var query = ListQuery.Parse(filter);

            if (query.Failed || TaskFields.Counts(query.Value!))
            {
                await PlainAsync(context, 400, query.Error
                    ?? "Runs and launches are counted from the run journal, which this does not read. Use 'loadout task list --filter ... --json'.")
                    .ConfigureAwait(false);

                return;
            }

            var applied = TaskFields.All.Apply(query.Value!, items, DateTimeOffset.UtcNow);

            if (applied.Failed)
            {
                await PlainAsync(context, 400, applied.Error!).ConfigureAwait(false);

                return;
            }

            items = applied.Value!;
        }
        else if (Unchanged(context, read.Value.Revision))
        {
            return;
        }

        Tag(context, read.Value.Revision);
        await JsonAsync(context, 200, new
        {
            project = list,
            revision = read.Value.Revision,
            tasks = items.Select(TaskOperations.Json),
        }).ConfigureAwait(false);
    }

    private static async Task OneAsync(HttpListenerContext context, ITaskService tasks, string? list, string id, CancellationToken ct)
    {
        var read = await tasks.ReadAsync(list, ct).ConfigureAwait(false);

        if (read.Failed)
        {
            await PlainAsync(context, Status(read.ExitCode), read.Error!).ConfigureAwait(false);

            return;
        }

        if (Find(read.Value!.Items, id) is not { } task)
        {
            await PlainAsync(context, 404, $"'{id}' is not a task of {TaskPaths.Describe(list)}.").ConfigureAwait(false);

            return;
        }

        if (Unchanged(context, read.Value.Revision))
        {
            return;
        }

        Tag(context, read.Value.Revision);
        await JsonAsync(context, 200, new { project = list, revision = read.Value.Revision, task = TaskOperations.Json(task) })
            .ConfigureAwait(false);
    }

    private static async Task AddAsync(HttpListenerContext context, ITaskService tasks, string? list, string by, CancellationToken ct)
    {
        if (await BodyAsync(context, ct).ConfigureAwait(false) is not { } body)
        {
            return;
        }

        var read = Fields(body, list, by, TaskOperation.Add, null);

        if (read.Error is { } wrong)
        {
            await PlainAsync(context, 400, wrong).ConfigureAwait(false);

            return;
        }

        await SingleAsync(context, tasks, read.Operation! with { Revision = IfMatch(context) }, 201, ct).ConfigureAwait(false);
    }

    private static async Task ChangeAsync(
        HttpListenerContext context, ITaskService tasks, string? list, string id, string by, CancellationToken ct)
    {
        if (await BodyAsync(context, ct).ConfigureAwait(false) is not { } body)
        {
            return;
        }

        var read = Fields(body, list, by, TaskOperation.Declare, id);

        if (read.Error is { } wrong)
        {
            await PlainAsync(context, 400, wrong).ConfigureAwait(false);

            return;
        }

        // Changing a task that is not there would add it, which is what a
        // declare does and not what a PATCH means.
        var current = await tasks.ReadAsync(list, ct).ConfigureAwait(false);

        if (current.Succeeded && Find(current.Value!.Items, id) is null)
        {
            await PlainAsync(context, 404, $"'{id}' is not a task of {TaskPaths.Describe(list)}.").ConfigureAwait(false);

            return;
        }

        var revision = IfMatch(context);
        var operations = new List<TaskOperation>();
        var declare = read.Operation! with { Revision = revision };

        if (declare.State is not null || declare.Title is not null || declare.Note is not null || declare.Kind is not null)
        {
            operations.Add(declare);
        }

        if (read.Moves)
        {
            operations.Add(new TaskOperation(TaskOperation.Move, list, id, To: read.To, Revision: revision));
        }

        if (operations.Count == 0)
        {
            await PlainAsync(context, 400, "There was nothing to change. Give one of state, title, note, kind or to.").ConfigureAwait(false);

            return;
        }

        var applied = await tasks.ApplyAsync(operations, ct).ConfigureAwait(false);

        await AnswerAsync(context, applied.Value!, 200).ConfigureAwait(false);
    }

    private static async Task BatchAsync(HttpListenerContext context, ITaskService tasks, string? list, string by, CancellationToken ct)
    {
        string text;

        using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
        {
            text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }

        var parsed = TaskOperations.Parse(text, list, by);

        if (parsed.Failed)
        {
            await PlainAsync(context, 400, parsed.Error!).ConfigureAwait(false);

            return;
        }

        // If-Match names this route's list, so it guards every operation on
        // it that does not name a revision of its own.
        var revision = IfMatch(context);
        var operations = revision is null
            ? parsed.Value!
            : [.. parsed.Value!.Select(one => one.Revision is null && one.Project == list ? one with { Revision = revision } : one)];

        var applied = await tasks.ApplyAsync(operations, ct).ConfigureAwait(false);

        await JsonAsync(context, 200, TaskOperations.Json(applied.Value!)).ConfigureAwait(false);
    }

    private static async Task SingleAsync(HttpListenerContext context, ITaskService tasks, TaskOperation operation, int status, CancellationToken ct)
    {
        var applied = await tasks.ApplyAsync([operation], ct).ConfigureAwait(false);

        await AnswerAsync(context, applied.Value!, status).ConfigureAwait(false);
    }

    /// <summary>What one change, perhaps made as two operations, comes to.</summary>
    /// <remarks>
    /// The last outcome is the task as it ended up: for a change and a move,
    /// the move, on the list it went to. A refusal answers with the first
    /// operation's reason, which is the one that stopped the rest.
    /// </remarks>
    private static async Task AnswerAsync(HttpListenerContext context, TaskBatch batch, int status)
    {
        if (batch.Outcomes.FirstOrDefault(one => !one.Applied && !one.HeldBack) is { } refused)
        {
            var current = batch.Lists.FirstOrDefault(one => one.Project == refused.Project)?.Revision;

            await JsonAsync(context, Status(refused.Code), new { error = refused.Error, code = (int)refused.Code, revision = current })
                .ConfigureAwait(false);

            return;
        }

        var last = batch.Outcomes[^1];
        var where = last.Op == TaskOperation.Move ? last.To : last.Project;
        var revision = batch.Lists.FirstOrDefault(one => one.Project == where)?.Revision;

        Tag(context, revision);
        await JsonAsync(context, status, new
        {
            project = where,
            revision,
            task = last.Task is { } task ? TaskOperations.Json(task) : null,
            lists = batch.Lists.Select(one => new { project = one.Project, revision = one.Revision, written = one.Written }),
        }).ConfigureAwait(false);
    }

    /// <summary>A body's fields made into an operation, or what is wrong with them.</summary>
    /// <remarks>
    /// Read as a batch of one, so a field means here what it means in a batch
    /// and is checked by the same reader. A change that names no state is
    /// still a declare, given a state for the reader, which asks for one
    /// because the command line does, and then given none, which the service
    /// takes as leaving the state as it was.
    /// </remarks>
    private static (TaskOperation? Operation, bool Moves, string? To, string? Error) Fields(
        JsonElement body, string? list, string by, string op, string? id)
    {
        if (JsonNode.Parse(body.GetRawText()) is not JsonObject single)
        {
            return (null, false, null, "The body should be a JSON object.");
        }

        foreach (var named in new[] { "op", "project", "revision" }.Concat(id is null ? [] : ["id"]))
        {
            if (single.ContainsKey(named))
            {
                return (null, false, null, $"'{named}' is said by the address and the If-Match header here, not the body.");
            }
        }

        var moves = false;
        string? to = null;

        if (op == TaskOperation.Declare && single.TryGetPropertyValue("to", out var destination))
        {
            if (destination?.GetValueKind() != JsonValueKind.String)
            {
                return (null, false, null, "'to' should be a project, or '-' for the workspace-wide list.");
            }

            moves = true;
            to = TaskOperations.List(destination.GetValue<string>());
            single.Remove("to");
        }

        var stateless = op == TaskOperation.Declare && !single.ContainsKey("state");

        single["op"] = op;

        if (id is not null)
        {
            single["id"] = id;
        }

        if (stateless)
        {
            single["state"] = "open";
        }

        var parsed = TaskOperations.Parse(new JsonArray(single).ToJsonString(), list, by);

        if (parsed.Failed)
        {
            return (null, false, null, parsed.Error!.Replace("Operation 0", "The body", StringComparison.Ordinal));
        }

        var operation = parsed.Value![0];

        return (stateless ? operation with { State = null } : operation, moves, to, null);
    }

    private static async Task<JsonElement?> BodyAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: ct).ConfigureAwait(false);

            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            await PlainAsync(context, 400, "The body should be JSON.").ConfigureAwait(false);

            return null;
        }
    }

    private static TaskItem? Find(IEnumerable<TaskItem> items, string id) =>
        items.FirstOrDefault(one => string.Equals(one.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The revision a change says it was made from, from If-Match, quotes and a weak marker taken off.</summary>
    internal static string? IfMatch(HttpListenerContext context) =>
        context.Request.Headers["If-Match"] is { Length: > 0 } header && header.Trim() != "*"
            ? header.Trim().TrimStart('W', '/').Trim('"')
            : null;

    /// <summary>Answers 304 when the page already has this revision.</summary>
    internal static bool Unchanged(HttpListenerContext context, string? revision)
    {
        if (revision is null
            || context.Request.Headers["If-None-Match"] is not { Length: > 0 } header
            || header.Trim().TrimStart('W', '/').Trim('"') != revision)
        {
            return false;
        }

        Tag(context, revision);
        context.Response.StatusCode = 304;
        context.Response.Close();

        return true;
    }

    internal static void Tag(HttpListenerContext context, string? revision)
    {
        if (revision is not null)
        {
            context.Response.Headers["ETag"] = "\"" + revision + "\"";
            context.Response.Headers["Cache-Control"] = "no-cache";
        }
    }

    /// <summary>The status a refusal answers with, from the code the command would have ended with.</summary>
    internal static int Status(ExitCode code) => code switch
    {
        ExitCode.InvalidArguments or ExitCode.PolicyViolation => 400,
        ExitCode.ProjectNotFound => 404,
        ExitCode.ChangedSinceRead => 409,
        _ => 500,
    };

    private static Task JsonAsync(HttpListenerContext context, int status, object body) =>
        DashboardServer.WriteAsync(context, status, "application/json; charset=utf-8", JsonSerializer.Serialize(body, Json));

    private static Task PlainAsync(HttpListenerContext context, int status, string body) =>
        DashboardServer.WriteAsync(context, status, "text/plain; charset=utf-8", body);
}
