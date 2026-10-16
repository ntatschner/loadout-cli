using System.Net;
using System.Text;
using System.Text.Json;
using Loadout.Core.Sessions.Hosting;

namespace Loadout.Core.Teams.Daemon;

/// <summary>
/// The hosted sessions under <c>/api/v1/sessions</c>: listing them, starting
/// one, ending one, and the terminal a window opens on it.
/// </summary>
/// <remarks>
/// <para>
/// Reached only through <see cref="DashboardServer"/>, which has already
/// checked the token or the cookie and that the request came from this
/// machine. A hosted session is somebody's agent with their file access, and
/// typing at it from across a network is the same thing the second credential
/// exists to stop for team nodes; here it is simply not offered.
/// </para>
/// <para>
/// <list type="table">
/// <item><term>GET /api/v1/sessions</term><description>Every session, oldest first.</description></item>
/// <item><term>POST /api/v1/sessions</term><description>Starts one from <c>{"project", "agent"?, "task"?, "taskId"?, "mode"?, "columns"?, "rows"?}</c>; 201 with the session.</description></item>
/// <item><term>GET /api/v1/sessions/{id}</term><description>One session.</description></item>
/// <item><term>DELETE /api/v1/sessions/{id}</term><description>Ends it if it is running, and forgets it; 204.</description></item>
/// <item><term>GET /api/v1/sessions/{id}/terminal</term><description>A WebSocket carrying the terminal; see <see cref="TerminalSocket"/>.</description></item>
/// </list>
/// </para>
/// </remarks>
internal static class SessionsApi
{
    /// <summary>Where the sessions live, so the page and these routes cannot disagree.</summary>
    public const string Root = "/api/v1/sessions";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Answers a request whose path is <see cref="Root"/> or below it.</summary>
    public static async Task HandleAsync(HttpListenerContext context, HostedSessions sessions, string path, CancellationToken ct)
    {
        if (path.Length > Root.Length && path[Root.Length] != '/')
        {
            await PlainAsync(context, 404, "There is nothing here.").ConfigureAwait(false);

            return;
        }

        var method = context.Request.HttpMethod;
        var parts = path[Root.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);

        switch (parts)
        {
            case [] when method == "GET":
                var memory = await sessions.MemoryAsync(ct).ConfigureAwait(false);
                await JsonAsync(context, 200, sessions.All
                    .Select(one => Shown(one, memory.TryGetValue(one.Id, out var bytes) ? bytes : null))
                    .ToArray()).ConfigureAwait(false);
                return;

            case [] when method == "POST":
                await StartAsync(context, sessions, ct).ConfigureAwait(false);
                return;

            case []:
                await PlainAsync(context, 405, "Sessions are listed with GET and started with POST.").ConfigureAwait(false);
                return;
        }

        if (parts.Length > 2 || sessions.Find(parts[0]) is not { } session)
        {
            await PlainAsync(context, 404, "There is no session with that id.").ConfigureAwait(false);

            return;
        }

        switch (parts)
        {
            case [_] when method == "GET":
                await JsonAsync(context, 200, Shown(session)).ConfigureAwait(false);
                return;

            case [_] when method == "DELETE":
                await sessions.RemoveAsync(session.Id).ConfigureAwait(false);
                context.Response.StatusCode = 204;
                context.Response.Close();
                return;

            case [_]:
                await PlainAsync(context, 405, "A session is read with GET and ended with DELETE.").ConfigureAwait(false);
                return;

            case [_, "terminal"] when context.Request.IsWebSocketRequest:
                var accepted = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
                await TerminalSocket.ServeAsync(accepted.WebSocket, session, ct).ConfigureAwait(false);
                return;

            case [_, "terminal"]:
                await PlainAsync(context, 400, "This is a session's terminal, and it is opened as a WebSocket.").ConfigureAwait(false);
                return;

            default:
                await PlainAsync(context, 404, "There is nothing here.").ConfigureAwait(false);
                return;
        }
    }

    private static async Task StartAsync(HttpListenerContext context, HostedSessions sessions, CancellationToken ct)
    {
        Asked? asked;

        try
        {
            asked = await JsonSerializer.DeserializeAsync<Asked>(context.Request.InputStream, Json, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            asked = null;
        }

        if (asked is not { Project: { Length: > 0 } project })
        {
            await PlainAsync(context, 400, "The body should be JSON naming a 'project'.").ConfigureAwait(false);

            return;
        }

        var launch = new HostedLaunch(
            project,
            asked.Agent,
            asked.Task,
            asked.TaskId,
            asked.Mode,
            asked.Columns ?? HostedSessions.DefaultColumns,
            asked.Rows ?? HostedSessions.DefaultRows);

        var started = await sessions.StartAsync(launch, ct).ConfigureAwait(false);

        if (started.Failed)
        {
            await PlainAsync(context, 400, started.Error ?? "The session could not be started.").ConfigureAwait(false);

            return;
        }

        context.Response.Headers["Location"] = Address(started.Value!);
        await JsonAsync(context, 201, Shown(started.Value!)).ConfigureAwait(false);
    }

    private static string Address(HostedSession session) => $"{Root}/{Uri.EscapeDataString(session.Id)}";

    /// <summary>A session as the page reads it.</summary>
    internal static object Shown(HostedSession session) => Shown(session, null);

    /// <param name="session">The session.</param>
    /// <param name="memory">What its processes hold resident, an estimate, or null where it was not measured.</param>
    internal static object Shown(HostedSession session, long? memory) => new
    {
        memory,
        id = session.Id,
        title = session.Title,
        startedAt = session.StartedAt,
        ended = session.HasEnded,
        exitCode = session.ExitCode,
        viewers = session.Viewers,
        columns = session.Size.Columns,
        rows = session.Size.Rows,
        terminal = Address(session) + "/terminal",
    };

    private static Task JsonAsync(HttpListenerContext context, int status, object body) =>
        DashboardServer.WriteAsync(context, status, "application/json; charset=utf-8", JsonSerializer.Serialize(body, Json));

    private static Task PlainAsync(HttpListenerContext context, int status, string body) =>
        DashboardServer.WriteAsync(context, status, "text/plain; charset=utf-8", body);

    private sealed record Asked(string? Project, string? Agent, string? Task, string? TaskId, string? Mode, int? Columns, int? Rows);
}
