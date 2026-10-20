using System.Net;
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
/// checked the token or the cookie, that the request came from this machine,
/// and that a change came from Studio's own page. A hosted session is
/// somebody's agent with their file access, and typing at it from across a
/// network is the same thing the second credential exists to stop for team
/// nodes; here it is simply not offered.
/// </para>
/// <para>
/// <list type="table">
/// <item><term>GET /api/v1/sessions</term><description>Every session, oldest first.</description></item>
/// <item><term>POST /api/v1/sessions</term><description>Starts one from the launch sheet's fields, or resumes a conversation given as <c>resume</c> (see <see cref="HistoryApi"/>); 201 with the session.</description></item>
/// <item><term>GET /api/v1/sessions/{id}</term><description>One session; 410 with the agent's conversation id for one an earlier daemon hosted.</description></item>
/// <item><term>DELETE /api/v1/sessions/{id}</term><description>Ends it if it is running, and forgets it; 204.</description></item>
/// <item><term>GET /api/v1/sessions/{id}/terminal</term><description>A WebSocket carrying the terminal; see <see cref="TerminalSocket"/>.</description></item>
/// </list>
/// </para>
/// <para>
/// A dashboard that is only watching lists and shows sessions, and opens a
/// terminal that cannot type, but refuses to start or end one, in words
/// that say so, as the classic page does for its own changes.
/// </para>
/// </remarks>
internal static class SessionsApi
{
    /// <summary>Where the sessions live, so the page and these routes cannot disagree.</summary>
    public const string Root = "/api/v1/sessions";

    /// <summary>What a watch-only dashboard says when asked to change something here.</summary>
    internal const string WatchOnlyRefusal =
        "This dashboard is only watching, so it cannot start, end or type into a session. "
        + "Run the command in a terminal instead.";

    /// <summary>Answers a request whose path is <see cref="Root"/> or below it.</summary>
    public static async Task HandleAsync(
        HttpListenerContext context, HostedSessions sessions, string path, bool watchOnly, CancellationToken ct,
        Resuming? resuming = null)
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
                    .Select(one => Shown(sessions, one, memory.TryGetValue(one.Id, out var bytes) ? bytes : null))
                    .ToArray()).ConfigureAwait(false);
                return;

            case [] when method == "POST" && watchOnly:
                await PlainAsync(context, 403, WatchOnlyRefusal).ConfigureAwait(false);
                return;

            case [] when method == "POST":
                await StartAsync(context, sessions, resuming, ct).ConfigureAwait(false);
                return;

            case []:
                await PlainAsync(context, 405, "Sessions are listed with GET and started with POST.").ConfigureAwait(false);
                return;
        }

        // A session an earlier daemon hosted ended with it. Said as gone
        // rather than not found, with the conversation's id where its agent
        // left one, so the page can offer to carry it on in a terminal.
        if (parts is [var gone] && method == "GET" && sessions.Find(gone) is null
            && sessions.LostAgentSession(gone) is { } lost)
        {
            await JsonAsync(context, 410, new { id = gone, lost = true, agentSession = lost }).ConfigureAwait(false);

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
                await JsonAsync(context, 200, Shown(sessions, session, null)).ConfigureAwait(false);
                return;

            case [_] when method == "DELETE" && watchOnly:
                await PlainAsync(context, 403, WatchOnlyRefusal).ConfigureAwait(false);
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
                await TerminalSocket.ServeAsync(accepted.WebSocket, session, ct, watching: watchOnly).ConfigureAwait(false);
                return;

            case [_, "terminal"]:
                await PlainAsync(context, 400, "This is a session's terminal, and it is opened as a WebSocket.").ConfigureAwait(false);
                return;

            default:
                await PlainAsync(context, 404, "There is nothing here.").ConfigureAwait(false);
                return;
        }
    }

    /// <summary>The launch a request's body describes, or null where it names no project.</summary>
    /// <remarks>Shared with the launch sheet's preview, so the two read the same fields.</remarks>
    internal static async Task<HostedLaunch?> ReadLaunchAsync(HttpListenerContext context, CancellationToken ct) =>
        Launch(await ReadAskedAsync(context, ct).ConfigureAwait(false));

    private static async Task<Asked?> ReadAskedAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<Asked>(context.Request.InputStream, LaunchApi.Json, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HostedLaunch? Launch(Asked? asked)
    {
        if (asked is { Command: { Length: > 0 } command })
        {
            return new HostedLaunch(
                string.Empty,
                Columns: asked.Columns ?? HostedSessions.DefaultColumns,
                Rows: asked.Rows ?? HostedSessions.DefaultRows,
                Command: command,
                Words: asked.Words ?? []);
        }

        return asked is { Project: { Length: > 0 } project }
            ? new HostedLaunch(
                project,
                asked.Agent,
                asked.Task,
                asked.TaskId,
                asked.Mode,
                asked.Columns ?? HostedSessions.DefaultColumns,
                asked.Rows ?? HostedSessions.DefaultRows,
                asked.Profile,
                asked.Worktree,
                asked.Offline,
                asked.NoSync,
                asked.Handoff)
            : null;
    }

    private static async Task StartAsync(
        HttpListenerContext context, HostedSessions sessions, Resuming? resuming, CancellationToken ct)
    {
        var asked = await ReadAskedAsync(context, ct).ConfigureAwait(false);

        if (asked?.Resume is { } resume)
        {
            // The launch sheet's fields, where 'Resume with changes' sent
            // them, are what the resume is to change. No project is needed:
            // the conversation says which.
            await HistoryApi.ResumeAsync(
                context,
                resuming,
                sessions,
                resume,
                new HostedLaunch(
                    asked.Project ?? string.Empty,
                    asked.Agent,
                    asked.Task,
                    asked.TaskId,
                    asked.Mode,
                    asked.Columns ?? HostedSessions.DefaultColumns,
                    asked.Rows ?? HostedSessions.DefaultRows,
                    asked.Profile,
                    asked.Worktree,
                    asked.Offline,
                    asked.NoSync),
                ct).ConfigureAwait(false);

            return;
        }

        if (Launch(asked) is not { } launch)
        {
            await PlainAsync(context, 400, "The body should be JSON naming a 'project', or a 'command'.").ConfigureAwait(false);

            return;
        }

        // Only the commands that ask as they go are run this way: everything
        // else is a job, where what it says is kept and a watch-only page can
        // read it. The coverage list is what says which those are.
        if (launch.Command is { Length: > 0 } command
            && (!StudioCoverage.Reach.TryGetValue(command, out var reach) || reach.Effect != StudioEffect.Session || command == "launch"))
        {
            await PlainAsync(context, 400,
                $"Studio does not open 'loadout {command}' in a terminal. Run it from its form, where what it says is kept.").ConfigureAwait(false);

            return;
        }

        var started = await sessions.StartAsync(launch, ct).ConfigureAwait(false);

        if (started.Failed)
        {
            await PlainAsync(context, 400, started.Error ?? "The session could not be started.").ConfigureAwait(false);

            return;
        }

        context.Response.Headers["Location"] = Address(started.Value!);
        await JsonAsync(context, 201, Shown(sessions, started.Value!, null)).ConfigureAwait(false);
    }

    private static string Address(HostedSession session) => $"{Root}/{Uri.EscapeDataString(session.Id)}";

    /// <summary>A session as the page reads it, with what it was launched as.</summary>
    /// <param name="sessions">Where its launch is kept.</param>
    /// <param name="session">The session.</param>
    /// <param name="memory">What its processes hold resident, an estimate, or null where it was not measured.</param>
    internal static object Shown(HostedSessions sessions, HostedSession session, long? memory)
    {
        var launch = sessions.LaunchOf(session.Id);
        var (needs, background, agentSession) = sessions.SignalOf(session);

        return new
        {
            memory,
            id = session.Id,
            title = session.Title,
            startedAt = session.StartedAt,
            lastOutputAt = session.LastOutputAt,
            ended = session.HasEnded,
            exitCode = session.ExitCode,
            viewers = session.Viewers,
            columns = session.Size.Columns,
            rows = session.Size.Rows,
            terminal = Address(session) + "/terminal",
            // 'command' for one of the commands that ask as they go, which has no
            // agent, mode or conversation to carry on; 'launch' for an agent.
            // A resume runs as a command and is shown as the agent session it
            // reopens, with what it planned to reopen with.
            kind = launch?.Command is { Length: > 0 } && launch.Resuming is null ? "command" : "launch",
            project = launch?.Command is { Length: > 0 } && launch.Resuming is null ? null : launch?.Project,
            agent = launch?.Agent,
            mode = launch?.Mode,
            task = launch?.Task,
            taskId = launch?.TaskId,
            command = launch is null ? null : HostedSessions.CommandLine(launch).Value,
            lastInputAt = session.LastInputAt,

            // From the agent's own hooks, never from the screen: 'waiting' or
            // 'approval' until somebody types, and the conversation's id for
            // 'loadout resume'. Both null for an agent whose hooks say nothing.
            // Background is the quiet state between: stopped to wait on work
            // of its own, which is not needing you.
            needs,
            background,
            agentSession = agentSession ?? launch?.Resuming,
            resuming = launch?.Resuming,
        };
    }

    private static Task JsonAsync(HttpListenerContext context, int status, object body) =>
        DashboardServer.WriteAsync(context, status, "application/json; charset=utf-8", JsonSerializer.Serialize(body, LaunchApi.Json));

    private static Task PlainAsync(HttpListenerContext context, int status, string body) =>
        DashboardServer.WriteAsync(context, status, "text/plain; charset=utf-8", body);

    private sealed record Asked(
        string? Project,
        string? Agent,
        string? Task,
        string? TaskId,
        string? Mode,
        int? Columns,
        int? Rows,
        string? Profile,
        string? Worktree,
        bool Offline,
        bool NoSync,
        bool Handoff,
        string? Command = null,
        List<string>? Words = null,
        ResumeAsked? Resume = null);
}
