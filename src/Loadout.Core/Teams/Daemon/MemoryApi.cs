using System.Net;
using System.Text.Json;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Loadout.Models.Results;

namespace Loadout.Core.Teams.Daemon;

/// <summary>A project as Studio lists it.</summary>
public sealed record StudioProject(string Slug, string Name);

/// <summary>
/// What Studio's memory page reads and saves, supplied by whoever starts the
/// server.
/// </summary>
/// <remarks>
/// An interface here and its implementation beside the commands, as the
/// tasks and ideas the classic page shows are: reading goes through Core,
/// and saving runs the command somebody would type, so the page cannot do a
/// thing the terminal would not.
/// </remarks>
public interface IStudioMemory
{
    /// <summary>Every project this machine knows.</summary>
    Task<IReadOnlyList<StudioProject>> ProjectsAsync(CancellationToken ct);

    /// <summary>A project's topics in every store, and what its agent recorded outside the workspace.</summary>
    Task<OperationResult<object>> TopicsAsync(string slug, CancellationToken ct);

    /// <summary>One topic as it is stored.</summary>
    Task<OperationResult<MemoryTopicText>> ReadAsync(string slug, MemoryScope scope, string topic, CancellationToken ct);

    /// <summary>Saves an edited topic, unless it changed since the version it was edited from.</summary>
    Task<OperationResult<MemoryReplacement>> SaveAsync(
        string slug, MemoryScope scope, string topic, string text, string version, CancellationToken ct);
}

/// <summary>
/// Studio's memory under <c>/api/v1/projects</c>.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>GET /api/v1/projects</term><description>Every project.</description></item>
/// <item><term>GET /api/v1/projects/{slug}/memory</term><description>Its topics in every store.</description></item>
/// <item><term>GET /api/v1/projects/{slug}/memory/{scope}/{topic}</term><description>One topic, with its text and version.</description></item>
/// <item><term>PUT the same</term><description>Saves <c>{"text", "version"}</c>: 200 with the saved topic, 409 with what changed it, or 400 with why not.</description></item>
/// </list>
/// Only the workspace's stores are saved from here: the project's and the
/// user's. The machine's is read-only in Studio, and what an agent recorded
/// outside the workspace is shown, never written.
/// </remarks>
internal static class MemoryApi
{
    public const string Root = "/api/v1/projects";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static async Task HandleAsync(HttpListenerContext context, IStudioMemory memory, string path, CancellationToken ct)
    {
        if (path.Length > Root.Length && path[Root.Length] != '/')
        {
            await PlainAsync(context, 404, "There is nothing here.").ConfigureAwait(false);

            return;
        }

        var method = context.Request.HttpMethod;
        var parts = path[Root.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();

        switch (parts)
        {
            case [] when method == "GET":
                await JsonAsync(context, 200, (await memory.ProjectsAsync(ct).ConfigureAwait(false))
                    .Select(one => new { slug = one.Slug, name = one.Name })).ConfigureAwait(false);
                return;

            case [var slug, "memory"] when method == "GET":
                var topics = await memory.TopicsAsync(slug, ct).ConfigureAwait(false);
                await (topics.Succeeded
                    ? JsonAsync(context, 200, topics.Value!)
                    : PlainAsync(context, 404, topics.Error ?? "There is no such project.")).ConfigureAwait(false);
                return;

            case [_, "memory", var scoped, _] when Scope(scoped) is null:
                await PlainAsync(context, 404, $"There is no store called '{scoped}'. They are project, user and machine.")
                    .ConfigureAwait(false);
                return;

            case [var slug, "memory", var scoped, var topic] when method == "GET":
                var read = await memory.ReadAsync(slug, Scope(scoped)!.Value, topic, ct).ConfigureAwait(false);
                await (read.Succeeded
                    ? JsonAsync(context, 200, Shown(read.Value!))
                    : PlainAsync(context, 404, read.Error ?? "There is no such topic.")).ConfigureAwait(false);
                return;

            case [var slug, "memory", var scoped, var topic] when method == "PUT":
                await SaveAsync(context, memory, slug, Scope(scoped)!.Value, topic, ct).ConfigureAwait(false);
                return;

            case [_, "memory", _, _]:
                await PlainAsync(context, 405, "A topic is read with GET and saved with PUT.").ConfigureAwait(false);
                return;

            default:
                await PlainAsync(context, 404, "There is nothing here.").ConfigureAwait(false);
                return;
        }
    }

    private static async Task SaveAsync(
        HttpListenerContext context, IStudioMemory memory, string slug, MemoryScope scope, string topic, CancellationToken ct)
    {
        // The machine's own store is kept off the page for now: it is the one
        // that never syncs, so a mistake made in it has no other copy.
        if (scope == MemoryScope.Machine)
        {
            await PlainAsync(context, 403, "This machine's memory is read-only here. Edit it with 'loadout memory replace'.")
                .ConfigureAwait(false);

            return;
        }

        Edit? edit;

        try
        {
            edit = await JsonSerializer.DeserializeAsync<Edit>(context.Request.InputStream, Json, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            edit = null;
        }

        if (edit is not { Text: { } text, Version: { Length: > 0 } version })
        {
            await PlainAsync(context, 400, "The body should be JSON with the topic's 'text' and the 'version' it was edited from.")
                .ConfigureAwait(false);

            return;
        }

        var saved = await memory.SaveAsync(slug, scope, topic, text, version, ct).ConfigureAwait(false);

        if (saved.Failed)
        {
            await PlainAsync(context, 400, saved.Error ?? "That could not be saved.").ConfigureAwait(false);

            return;
        }

        await JsonAsync(context, saved.Value!.Conflicted ? 409 : 200, Shown(saved.Value.Topic)).ConfigureAwait(false);
    }

    internal static MemoryScope? Scope(string named) =>
        Enum.TryParse<MemoryScope>(named, ignoreCase: true, out var scope) && Enum.IsDefined(scope)
            && !int.TryParse(named, out _)
            ? scope
            : null;

    private static object Shown(MemoryTopicText topic) => new
    {
        name = topic.Topic.Name,
        scope = topic.Topic.Scope.ToString().ToLowerInvariant(),
        description = topic.Topic.Description,
        kind = topic.Topic.Kind.ToString().ToLowerInvariant(),
        written = topic.Topic.WrittenUtc,
        version = topic.Version,
        text = topic.Text,
    };

    private static Task JsonAsync(HttpListenerContext context, int status, object body) =>
        DashboardServer.WriteAsync(context, status, "application/json; charset=utf-8", JsonSerializer.Serialize(body, Json));

    private static Task PlainAsync(HttpListenerContext context, int status, string body) =>
        DashboardServer.WriteAsync(context, status, "text/plain; charset=utf-8", body);

    private sealed record Edit(string? Text, string? Version);
}
