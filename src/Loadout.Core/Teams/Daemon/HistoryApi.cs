using System.Net;
using System.Text.Json;
using Loadout.Core.Sessions;
using Loadout.Core.Sessions.Hosting;
using Loadout.Models.Results;

namespace Loadout.Core.Teams.Daemon;

/// <summary>How the daemon resumes a conversation: the history, and registering the folder one ran in.</summary>
/// <param name="Sessions">Choosing, carry-over and the running check, as the CLI has them.</param>
/// <param name="Register">Registers a folder as a project, returning its slug.</param>
internal sealed record Resuming(
    ISessionResumption Sessions,
    Func<string, CancellationToken, Task<OperationResult<string>>> Register);

/// <summary>What the page asks to resume, inside a POST to <c>/api/v1/sessions</c>.</summary>
/// <param name="AgentSession">The agent's id for the conversation, whole.</param>
/// <param name="Agent">The agent, where the page knows it.</param>
/// <param name="RegisterFolder">
/// The folder the conversation ran in, to register first. It must be that
/// conversation's own folder: the page cannot name any other.
/// </param>
/// <param name="ConfirmRunning">The person was told it looks to be running elsewhere and said go ahead.</param>
/// <param name="Lost">The id of the session a restart ended, to stop offering once this one starts.</param>
/// <param name="Fresh">
/// Carry nothing over: the launch sheet's fields are the whole of what it
/// reopens with. 'Resume with changes' sends it, so that choosing no task or
/// the main checkout there means that.
/// </param>
internal sealed record ResumeAsked(
    string? AgentSession,
    string? Agent = null,
    string? RegisterFolder = null,
    bool ConfirmRunning = false,
    string? Lost = null,
    bool Fresh = false);

/// <summary>
/// Past conversations under <c>/api/v1/history</c>, and resuming one.
/// </summary>
/// <remarks>
/// <para>
/// <list type="table">
/// <item><term>GET /api/v1/history</term><description>Conversations newest first; <c>project</c>, <c>agent</c> and <c>limit</c> narrow it.</description></item>
/// <item><term>GET /api/v1/history/query</term><description>The same, put through <c>q</c>, the query every list takes: <c>{ conversations, recorded, query }</c>, or a 400 naming what it cannot read.</description></item>
/// <item><term>GET /api/v1/history/lost</term><description>Sessions an earlier daemon hosted that can be resumed.</description></item>
/// <item><term>DELETE /api/v1/history/lost/{id}</term><description>Stops offering one; 204.</description></item>
/// </list>
/// </para>
/// <para>
/// A resume itself is a POST to <c>/api/v1/sessions</c> with a <c>resume</c>
/// field, because resuming is starting a session: the same refusal for a
/// watch-only page, the same 201. Nothing the page sends reaches the command
/// line unchecked. The id is looked up in the history and the arguments are
/// built from what the history holds, a node's conversation is not offered
/// (its run's page is where it is taken over), and a folder is registered
/// only when it is the one that conversation ran in.
/// </para>
/// </remarks>
internal static class HistoryApi
{
    /// <summary>Where the history lives.</summary>
    public const string Root = "/api/v1/history";

    /// <summary>The most conversations one answer holds.</summary>
    internal const int MaximumLimit = 200;

    /// <summary>How far back a resume looks for the id it was given.</summary>
    /// <remarks>The widest the history service reads; an id older than that has gone from the list too.</remarks>
    internal const int ResumeLookup = 500;

    /// <summary>Answers a request whose path is <see cref="Root"/> or below it.</summary>
    public static async Task HandleAsync(
        HttpListenerContext context,
        Resuming resuming,
        HostedSessions? hosted,
        string path,
        bool watchOnly,
        CancellationToken ct)
    {
        var method = context.Request.HttpMethod;
        var parts = path[Root.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);

        switch (parts)
        {
            case [] when method == "GET":
                await ListAsync(context, resuming, hosted, ct).ConfigureAwait(false);
                return;

            case ["query"] when method == "GET":
                await QueryAsync(context, resuming, hosted, ct).ConfigureAwait(false);
                return;

            case ["lost"] when method == "GET":
                await LostAsync(context, resuming, hosted, ct).ConfigureAwait(false);
                return;

            case ["lost", _] when method == "DELETE" && watchOnly:
                await PlainAsync(context, 403, SessionsApi.WatchOnlyRefusal).ConfigureAwait(false);
                return;

            case ["lost", var id] when method == "DELETE":
                if (hosted?.ForgetLost(id) == true)
                {
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                }
                else
                {
                    await PlainAsync(context, 404, "There is no lost session with that id.").ConfigureAwait(false);
                }

                return;

            case [] or ["query"] or ["lost"] or ["lost", _]:
                await PlainAsync(context, 405, "The history is read with GET; a lost session is let go of with DELETE.").ConfigureAwait(false);
                return;

            default:
                await PlainAsync(context, 404, "There is nothing here.").ConfigureAwait(false);
                return;
        }
    }

    private static async Task ListAsync(
        HttpListenerContext context, Resuming resuming, HostedSessions? hosted, CancellationToken ct)
    {
        var listed = await ReadAsync(context, resuming, hosted, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            await PlainAsync(context, 500, listed.Error ?? "The history could not be read.").ConfigureAwait(false);

            return;
        }

        await JsonAsync(context, 200, listed.Value!.Select(Shown).ToArray()).ConfigureAwait(false);
    }

    /// <summary>
    /// The history put through the shared query, by the same fields
    /// <c>loadout sessions --filter</c> uses, with what a toolbar draws from.
    /// </summary>
    private static async Task QueryAsync(
        HttpListenerContext context, Resuming resuming, HostedSessions? hosted, CancellationToken ct)
    {
        var parsed = Lists.ListQuery.Parse(context.Request.QueryString["q"]);

        if (parsed.Failed)
        {
            await PlainAsync(context, 400, parsed.Error!).ConfigureAwait(false);

            return;
        }

        var listed = await ReadAsync(context, resuming, hosted, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            await PlainAsync(context, 500, listed.Error ?? "The history could not be read.").ConfigureAwait(false);

            return;
        }

        var applied = Lists.SessionFields.All.Apply(parsed.Value!, [.. listed.Value!.Select(r => r.Session)], DateTimeOffset.UtcNow);

        if (applied.Failed)
        {
            await PlainAsync(context, 400, applied.Error!).ConfigureAwait(false);

            return;
        }

        var bySession = listed.Value!.ToDictionary<ResumableSession, AgentSession>(r => r.Session, ReferenceEqualityComparer.Instance);

        await JsonAsync(context, 200, new
        {
            conversations = applied.Value!.Select(session => Shown(bySession[session])).ToArray(),
            recorded = listed.Value!.Count,
            query = Lists.SessionFields.All.Shape(parsed.Value!),
        }).ConfigureAwait(false);
    }

    /// <summary>The history as <c>project</c>, <c>agent</c> and <c>limit</c> narrow it.</summary>
    private static Task<OperationResult<IReadOnlyList<ResumableSession>>> ReadAsync(
        HttpListenerContext context, Resuming resuming, HostedSessions? hosted, CancellationToken ct)
    {
        var query = context.Request.QueryString;
        var limit = int.TryParse(query["limit"], out var asked) ? Math.Clamp(asked, 1, MaximumLimit) : 50;

        return resuming.Sessions.ListAsync(
            new SessionQuery(
                query["project"] is { Length: > 0 } project ? project : null,
                query["agent"] is { Length: > 0 } agent ? agent : null,
                Limit: limit),
            HostedBy(hosted),
            ct);
    }

    private static async Task LostAsync(
        HttpListenerContext context, Resuming resuming, HostedSessions? hosted, CancellationToken ct)
    {
        var lost = hosted?.Lost() ?? [];

        if (lost.Count == 0)
        {
            await JsonAsync(context, 200, Array.Empty<object>()).ConfigureAwait(false);

            return;
        }

        // Joined to the history for what the page shows beside each: the
        // project and agent come from the conversation, not from the signal,
        // which only ever held the id.
        var listed = await resuming.Sessions.ListAsync(
            new SessionQuery(Limit: ResumeLookup), HostedBy(hosted), ct).ConfigureAwait(false);

        var byId = (listed.Succeeded ? listed.Value! : [])
            .GroupBy(r => r.Session.SessionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        await JsonAsync(context, 200, lost
            .Select(entry => new
            {
                id = entry.Id,
                endedAt = entry.Signal.At,
                agentSession = entry.Signal.AgentSession,
                conversation = byId.TryGetValue(entry.Signal.AgentSession!, out var found) ? Shown(found) : null,
            })
            .ToArray()).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a hosted <c>loadout resume</c> for a conversation in the
    /// history, or says why not in a way the page can act on.
    /// </summary>
    /// <remarks>
    /// The refusals the page asks the person about are 409s with a
    /// <c>conflict</c> it can read: <c>hosted</c> (go to that terminal),
    /// <c>elsewhere</c> (confirm, and send <c>confirmRunning</c>) and
    /// <c>unregistered</c> (confirm the folder, and send it back as
    /// <c>registerFolder</c>).
    /// </remarks>
    internal static async Task ResumeAsync(
        HttpListenerContext context,
        Resuming? resuming,
        HostedSessions sessions,
        ResumeAsked asked,
        HostedLaunch overrides,
        CancellationToken ct)
    {
        if (resuming is null)
        {
            await PlainAsync(context, 404, "This dashboard resumes nothing. The daemon's does: loadout daemon").ConfigureAwait(false);

            return;
        }

        if (!SessionLinks.IsAgentSession(asked.AgentSession) || asked.AgentSession!.StartsWith('-'))
        {
            await PlainAsync(context, 400, "'resume' needs the conversation's id as 'agentSession'.").ConfigureAwait(false);

            return;
        }

        var wanted = asked.AgentSession;

        // The history the person resumes from, nodes left out: a node's
        // conversation is taken over from its run's page, where what it was
        // doing can be seen. Looked up whole, never by prefix.
        var found = await resuming.Sessions.FindAsync(
            new SessionQuery(Agent: asked.Agent is { Length: > 0 } agent ? agent : null, Limit: ResumeLookup),
            wanted,
            HostedBy(sessions),
            ct).ConfigureAwait(false);

        if (found.Failed || !string.Equals(found.Value!.Session.SessionId, wanted, StringComparison.Ordinal))
        {
            await PlainAsync(context, 404,
                "There is no conversation with that id in your history. A team node's is taken over from its run's page.")
                .ConfigureAwait(false);

            return;
        }

        var chosen = found.Value!;

        if (chosen.Running is { Kind: RunningKind.Hosted, StudioSession: { } there })
        {
            await JsonAsync(context, 409, new { conflict = "hosted", session = there }).ConfigureAwait(false);

            return;
        }

        if (chosen.Running.Kind == RunningKind.Elsewhere && !asked.ConfirmRunning)
        {
            await JsonAsync(context, 409, new { conflict = "elsewhere" }).ConfigureAwait(false);

            return;
        }

        if (chosen.Session.ProjectSlug is not { Length: > 0 })
        {
            if (asked.RegisterFolder is null)
            {
                await JsonAsync(context, 409, new { conflict = "unregistered", directory = chosen.Session.Directory })
                    .ConfigureAwait(false);

                return;
            }

            // Only the folder this conversation ran in, so the page cannot
            // use resuming to register any folder it likes.
            if (!SameFolder(asked.RegisterFolder, chosen.Session.Directory))
            {
                await PlainAsync(context, 400, "Only the folder the conversation ran in can be registered to resume it.")
                    .ConfigureAwait(false);

                return;
            }

            var registered = await resuming.Register(chosen.Session.Directory, ct).ConfigureAwait(false);

            if (registered.Failed)
            {
                await PlainAsync(context, 400, registered.Error ?? "The folder could not be registered.").ConfigureAwait(false);

                return;
            }

            chosen = chosen with { Session = chosen.Session with { ProjectSlug = registered.Value } };
        }

        var planned = await resuming.Sessions.PlanAsync(chosen, Overrides(overrides, asked.Fresh), ct).ConfigureAwait(false);

        if (planned.Failed)
        {
            await PlainAsync(context, 400, planned.Error ?? "It could not be resumed.").ConfigureAwait(false);

            return;
        }

        var values = planned.Value!;

        var started = await sessions.StartAsync(
            new HostedLaunch(
                values.ProjectSlug,
                values.Agent,
                values.Task,
                values.TaskId,
                values.Mode,
                overrides.Columns,
                overrides.Rows,
                values.Profile,
                values.Worktree,
                Command: "resume",
                Words: Words(values, overrides, asked.Fresh),
                Resuming: values.SessionId),
            ct).ConfigureAwait(false);

        if (started.Failed)
        {
            await PlainAsync(context, 400, started.Error ?? "The session could not be started.").ConfigureAwait(false);

            return;
        }

        if (asked.Lost is { Length: > 0 } lost)
        {
            sessions.ForgetLost(lost);
        }

        context.Response.Headers["Location"] = $"{SessionsApi.Root}/{Uri.EscapeDataString(started.Value!.Id)}";
        await JsonAsync(context, 201, SessionsApi.Shown(sessions, started.Value!, null)).ConfigureAwait(false);
    }

    /// <summary>What the launch sheet's fields override, where it sent any.</summary>
    /// <remarks>An empty field is no override: an option cannot be given an empty value.</remarks>
    internal static ResumeOverrides Overrides(HostedLaunch sheet, bool fresh) =>
        new(Given(sheet.Task), Given(sheet.TaskId), Given(sheet.Mode), Given(sheet.Profile), Given(sheet.Worktree), fresh);

    private static string? Given(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// The words after 'resume': the id, the project, and the overrides the
    /// sheet sent, each joined to its value as the parser needs.
    /// </summary>
    /// <remarks>
    /// Overrides only, not the carried values: the resume works carry-over out
    /// again for itself, from the same service, and says it in the terminal.
    /// </remarks>
    internal static IReadOnlyList<string> Words(ResumeValues values, HostedLaunch sheet, bool fresh)
    {
        var words = new List<string> { values.SessionId, $"--project={values.ProjectSlug}", $"--agent={values.Agent}" };

        if (fresh)
        {
            words.Add("--fresh");
        }

        if (sheet.TaskId is { Length: > 0 } taskId)
        {
            words.Add($"--task-id={taskId}");
        }

        if (sheet.Task is { Length: > 0 } task)
        {
            words.Add($"--task={task}");
        }

        if (sheet.Mode is { Length: > 0 } mode)
        {
            words.Add($"--mode={mode}");
        }

        if (sheet.Profile is { Length: > 0 } profile)
        {
            words.Add($"--profile={profile}");
        }

        // Left out, the resume carries the worktree over, or falls back to
        // the main tree itself when it has gone; with --fresh, left out is
        // the main tree.
        if (sheet.Worktree is { Length: > 0 } worktree)
        {
            words.Add($"--worktree={worktree}");
        }

        if (sheet.Offline)
        {
            words.Add("--offline");
        }

        if (sheet.NoSync)
        {
            words.Add("--no-sync");
        }

        return words;
    }

    /// <summary>A conversation as the page reads it.</summary>
    internal static object Shown(ResumableSession resumable)
    {
        var session = resumable.Session;

        return new
        {
            agent = session.Agent,
            id = session.SessionId,
            title = session.Label,
            project = session.ProjectSlug,
            registered = session.ProjectSlug is { Length: > 0 },
            directory = session.Directory,
            branch = session.Branch,
            lastActive = session.LastActive,
            launchedFor = resumable.LaunchedFor is { } launch
                ? new
                {
                    launch = launch.Id,
                    task = launch.Task,
                    taskId = launch.TaskId,
                    mode = launch.Mode,
                    profile = launch.Profile,
                    worktree = launch.Worktree,
                }
                : null,
            running = resumable.Running.Wire,
        };
    }

    private static Func<AgentSession, string?>? HostedBy(HostedSessions? hosted) =>
        hosted is null ? null : session => hosted.Hosting(session.SessionId);

    private static bool SameFolder(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static Task JsonAsync(HttpListenerContext context, int status, object body) =>
        DashboardServer.WriteAsync(context, status, "application/json; charset=utf-8", JsonSerializer.Serialize(body, LaunchApi.Json));

    private static Task PlainAsync(HttpListenerContext context, int status, string body) =>
        DashboardServer.WriteAsync(context, status, "text/plain; charset=utf-8", body);
}
