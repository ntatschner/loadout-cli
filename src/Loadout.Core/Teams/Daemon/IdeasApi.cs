using System.Net;
using System.Text.Json;
using Loadout.Core.Ideas;
using Loadout.Core.Tasks;
using Loadout.Models.Ideas;

namespace Loadout.Core.Teams.Daemon;

/// <summary>
/// The ideas on one list, or one idea, under <c>/api/v1/projects</c>, read and
/// changed through the same service the command line and the agents' tools use.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>GET {list}/ideas</term><description>The ideas on the list, each with its record's revision.</description></item>
/// <item><term>POST {list}/ideas</term><description>Captures one from <c>{"text", "title"?, "id"?}</c>: 201 with it.</description></item>
/// <item><term>GET {list}/ideas/{id}</term><description>One idea in full, with its revision as the ETag.</description></item>
/// <item><term>DELETE {list}/ideas/{id}</term><description>Forgets it, its task and its record.</description></item>
/// <item><term>POST {list}/ideas:batch</term><description>A JSON array of operations, as <c>idea apply</c> takes; one result each.</description></item>
/// </list>
/// <para>
/// Answers, choices and verdicts go through the batch, one operation each or
/// several for one idea together. Refining is not here: a round takes minutes
/// and starts an agent, and has its own way in.
/// </para>
/// </remarks>
internal static class IdeasApi
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Whether a path under <see cref="MemoryApi.Root"/> is one of these.</summary>
    public static bool Handles(string[] parts) =>
        parts is [_, "ideas", ..] or [_, "ideas:batch"];

    public static async Task HandleAsync(
        HttpListenerContext context,
        IIdeaService ideas,
        string[] parts,
        bool watchOnly,
        string by,
        CancellationToken ct)
    {
        var method = context.Request.HttpMethod;

        if (method != "GET" && watchOnly)
        {
            await PlainAsync(context, 403, "This dashboard is only watching, so it cannot change ideas. Use 'loadout idea' in a terminal.")
                .ConfigureAwait(false);

            return;
        }

        var list = TaskOperations.List(parts[0]);

        switch (parts)
        {
            case [_, "ideas"] when method == "GET":
                await ListAsync(context, ideas, list, ct).ConfigureAwait(false);
                return;

            case [_, "ideas"] when method == "POST":
                await CaptureAsync(context, ideas, list, by, ct).ConfigureAwait(false);
                return;

            case [_, "ideas", var id] when method == "GET":
                await OneAsync(context, ideas, new IdeaPlace(list, id), ct).ConfigureAwait(false);
                return;

            case [_, "ideas", var id] when method == "DELETE":
                await SingleAsync(context, ideas, new IdeaOperation(IdeaOperation.Remove, list, id, Revision: TasksApi.IfMatch(context)), 200, ct)
                    .ConfigureAwait(false);
                return;

            case [_, "ideas:batch"] when method == "POST":
                await BatchAsync(context, ideas, list, by, ct).ConfigureAwait(false);
                return;

            case [_, "ideas"] or [_, "ideas:batch"]:
                await PlainAsync(context, 405, "A list of ideas is read with GET and added to with POST; a batch is a POST.").ConfigureAwait(false);
                return;

            case [_, "ideas", _]:
                await PlainAsync(context, 405, "An idea is read with GET and forgotten with DELETE; it is changed through the batch.")
                    .ConfigureAwait(false);
                return;

            default:
                await PlainAsync(context, 404, "There is nothing here.").ConfigureAwait(false);
                return;
        }
    }

    private static async Task ListAsync(HttpListenerContext context, IIdeaService ideas, string? list, CancellationToken ct)
    {
        var listed = await ideas.ListAsync(ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            await PlainAsync(context, TasksApi.Status(listed.ExitCode), listed.Error!).ConfigureAwait(false);

            return;
        }

        var shown = new List<object>();

        foreach (var idea in listed.Value!.Where(one => one.Place.Project == list))
        {
            var read = await ideas.ReadWithRevisionAsync(idea.Place, ct).ConfigureAwait(false);

            if (read.Succeeded)
            {
                shown.Add(new
                {
                    title = idea.Title,
                    revision = read.Value!.Revision,
                    running = idea.Running,
                    idea = IdeaJson.Of(idea.Place, read.Value.Record),
                });
            }
        }

        var body = JsonSerializer.Serialize(new { project = list, ideas = shown }, Json);
        var revision = Configuration.YamlStore.Revision(body);

        if (TasksApi.Unchanged(context, revision))
        {
            return;
        }

        TasksApi.Tag(context, revision);
        await DashboardServer.WriteAsync(context, 200, "application/json; charset=utf-8", body).ConfigureAwait(false);
    }

    private static async Task OneAsync(HttpListenerContext context, IIdeaService ideas, IdeaPlace place, CancellationToken ct)
    {
        var read = await ideas.ReadWithRevisionAsync(place, ct).ConfigureAwait(false);

        if (read.Failed)
        {
            await PlainAsync(context, read.ExitCode == Models.ExitCode.InvalidArguments ? 404 : TasksApi.Status(read.ExitCode), read.Error!)
                .ConfigureAwait(false);

            return;
        }

        if (TasksApi.Unchanged(context, read.Value!.Revision))
        {
            return;
        }

        TasksApi.Tag(context, read.Value.Revision);
        await JsonAsync(context, 200, new { project = place.Project, revision = read.Value.Revision, idea = IdeaJson.Of(place, read.Value.Record) })
            .ConfigureAwait(false);
    }

    private static async Task CaptureAsync(HttpListenerContext context, IIdeaService ideas, string? list, string by, CancellationToken ct)
    {
        JsonElement body;

        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: ct).ConfigureAwait(false);
            body = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            await PlainAsync(context, 400, "The body should be JSON.").ConfigureAwait(false);

            return;
        }

        if (body.ValueKind != JsonValueKind.Object)
        {
            await PlainAsync(context, 400, "The body should be a JSON object with the idea's 'text'.").ConfigureAwait(false);

            return;
        }

        // Read as a batch of one, so the fields mean here what they mean in a
        // batch and are checked by the same reader.
        var single = System.Text.Json.Nodes.JsonNode.Parse(body.GetRawText())!.AsObject();

        foreach (var named in new[] { "op", "project", "revision" })
        {
            if (single.ContainsKey(named))
            {
                await PlainAsync(context, 400, $"'{named}' is said by the address here, not the body.").ConfigureAwait(false);

                return;
            }
        }

        single["op"] = IdeaOperation.Capture;

        var parsed = IdeaOperations.Parse(new System.Text.Json.Nodes.JsonArray(single).ToJsonString(), list, by);

        if (parsed.Failed)
        {
            await PlainAsync(context, 400, parsed.Error!.Replace("Operation 0", "The body", StringComparison.Ordinal)).ConfigureAwait(false);

            return;
        }

        await SingleAsync(context, ideas, parsed.Value![0], 201, ct).ConfigureAwait(false);
    }

    private static async Task BatchAsync(HttpListenerContext context, IIdeaService ideas, string? list, string by, CancellationToken ct)
    {
        string text;

        using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
        {
            text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }

        var parsed = IdeaOperations.Parse(text, list, by);

        if (parsed.Failed)
        {
            await PlainAsync(context, 400, parsed.Error!).ConfigureAwait(false);

            return;
        }

        var applied = await ideas.ApplyAsync(parsed.Value!, ct).ConfigureAwait(false);

        await JsonAsync(context, 200, IdeaOperations.Json(applied.Value!)).ConfigureAwait(false);
    }

    private static async Task SingleAsync(HttpListenerContext context, IIdeaService ideas, IdeaOperation operation, int status, CancellationToken ct)
    {
        var outcome = (await ideas.ApplyAsync([operation], ct).ConfigureAwait(false)).Value!.Outcomes[0];

        if (!outcome.Applied)
        {
            await JsonAsync(context, outcome.Code == Models.ExitCode.InvalidArguments && outcome.Error!.Contains("no idea called", StringComparison.Ordinal)
                    ? 404
                    : TasksApi.Status(outcome.Code),
                new { error = outcome.Error, code = (int)outcome.Code }).ConfigureAwait(false);

            return;
        }

        TasksApi.Tag(context, outcome.Revision);
        await JsonAsync(context, status, IdeaOperations.Json(outcome)).ConfigureAwait(false);
    }

    private static Task JsonAsync(HttpListenerContext context, int status, object body) =>
        DashboardServer.WriteAsync(context, status, "application/json; charset=utf-8", JsonSerializer.Serialize(body, Json));

    private static Task PlainAsync(HttpListenerContext context, int status, string body) =>
        DashboardServer.WriteAsync(context, status, "text/plain; charset=utf-8", body);
}
