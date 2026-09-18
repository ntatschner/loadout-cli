using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Loadout.Models;
using Loadout.Models.Results;

namespace Loadout.Core.Teams.Daemon;

/// <summary>
/// Serves a page showing what the teams on this machine are doing.
/// </summary>
/// <remarks>
/// <para>
/// A run already has three views: the command that started it prints as it
/// goes, <c>team status</c> reads the journal back, and <c>team log</c> follows
/// it. This is the fourth, and the one that answers "what is happening" without
/// somebody having to ask again every thirty seconds.
/// </para>
/// <para>
/// Loopback unless the machine was told otherwise, and a token generated at
/// start that every request must carry. Any page in any browser tab can reach a
/// loopback port, so a server without one would let a web page somebody opened
/// read what their agents are doing. The token goes in the address printed at
/// start; nothing else knows it.
/// </para>
/// <para>
/// Reading is all it does on its own. One thing can change something — a
/// triggered run — and it is not implemented here: the server hands the request
/// to whatever set <see cref="Trigger"/>, which today is the daemon, which
/// starts it through the same parser somebody would type at. A server that
/// started runs itself would be a second implementation of <c>team run</c>, and
/// the one nobody is watching drifts furthest. Without a <see cref="Trigger"/>
/// there is nothing to hand to, and every POST is refused as before.
/// </para>
/// <para>
/// The trigger carries its own token, its own list of what may be started, and
/// its own reason for each refusal. None of that is this type's to decide; see
/// <see cref="Webhook"/>, which is where it is decided and where it is tested.
/// </para>
/// </remarks>
public sealed class DashboardServer : IDisposable
{
    /// <summary>How long a stream waits between looks at the journal.</summary>
    /// <remarks>
    /// A node's turn takes tens of seconds, so half a second is far faster
    /// than anything it is watching, and slow enough that an idle page costs
    /// nothing worth measuring.
    /// </remarks>
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    /// <summary>The segments under a run that only ever read.</summary>
    /// <remarks>
    /// Named rather than assumed. The right default for a segment nobody has
    /// thought about yet is "this changes something": a read routed as a change
    /// is a 400 somebody notices, and a change routed as a read is a change
    /// that skipped the check.
    /// </remarks>
    private static readonly string[] Reads = ["events", "documents"];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly IRunJournal _journal;
    private readonly HttpListener _listener = new();

    public DashboardServer(IRunJournal journal) => _journal = journal;

    /// <summary>The secret every request has to carry, made when the server starts.</summary>
    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>Where to point a browser, token and all.</summary>
    public string Address { get; private set; } = string.Empty;

    /// <summary>How many requests have been answered, for the tests and for doctor.</summary>
    public int Answered { get; private set; }

    /// <summary>
    /// What to do about a triggered run, or null to refuse every one.
    /// </summary>
    /// <remarks>
    /// Set by whatever can actually start a run through the parser. Null is the
    /// ordinary case and the safe one: <c>team dashboard</c> sets nothing, so a
    /// dashboard somebody opened to watch a run cannot start one.
    /// </remarks>
    public Func<TriggerRequest, CancellationToken, Task<OperationResult>>? Trigger { get; set; }

    /// <summary>
    /// What to do about a run: answer a gate, say something, or stop it.
    /// </summary>
    /// <remarks>
    /// Set by whatever can run a command through the parser. Null refuses
    /// every one of them, which is what <c>team dashboard</c> does: a page
    /// opened to watch a run must not be able to change it, and the daemon is
    /// the thing that can.
    /// </remarks>
    public Func<RunAction, CancellationToken, Task<OperationResult>>? Act { get; set; }

    /// <summary>
    /// Answers whether a trigger may proceed, or null when none may.
    /// </summary>
    /// <remarks>
    /// Asked per request rather than read once at start, so turning the webhook
    /// off takes effect on the next request instead of the next restart.
    /// </remarks>
    public Func<string?, string, CancellationToken, Task<Webhook.Refusal?>>? Admits { get; set; }

    /// <summary>
    /// Starts listening on a loopback address, or says why it could not.
    /// </summary>
    /// <param name="port">The port to listen on, or 0 to let the machine choose.</param>
    /// <param name="listen">
    /// The address to bind. Loopback by default; anything else is a deliberate
    /// act of putting a port on the network and is never inferred.
    /// </param>
    public OperationResult Start(int port, string listen = "127.0.0.1")
    {
        if (port is < 0 or > 65535)
        {
            return OperationResult.Fail($"{port} is not a port.", ExitCode.InvalidArguments);
        }

        var where = string.IsNullOrWhiteSpace(listen) ? "127.0.0.1" : listen.Trim();
        var chosen = port == 0 ? Free() : port;
        var prefix = $"http://{where}:{chosen.ToString(CultureInfo.InvariantCulture)}/";

        _listener.Prefixes.Add(prefix);

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            // Windows refuses a non-loopback prefix to anything unelevated, and
            // the message it gives ("Access is denied") says nothing about why.
            // Naming the reservation is the difference between a person fixing
            // this in a minute and concluding the feature does not work.
            var elevated = !IsLoopback(where)
                ? $" Binding {where} needs a reservation on Windows: "
                    + $"netsh http add urlacl url={prefix} user=%USERNAME%"
                : string.Empty;

            return OperationResult.Fail(
                $"Could not listen on {prefix}: {ex.Message}. Another dashboard may already be "
                + "running, or the port may be in use."
                + elevated,
                ExitCode.GeneralFailure);
        }

        Address = $"{prefix}?token={Token}";

        return OperationResult.Ok();
    }

    /// <summary>
    /// Starts a run something outside this machine asked for, or says why not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here decides whether it may. <see cref="Admits"/> does, from a
    /// token in the credential store and a list of teams this machine named in
    /// advance, and <see cref="Trigger"/> does the starting through the same
    /// parser somebody would type at.
    /// </para>
    /// <para>
    /// The answer says the run started, not what it did. A caller that waited
    /// for a team run to finish would be holding an HTTP request open for
    /// minutes, and the run is watchable by every other means this serves.
    /// </para>
    /// </remarks>
    private async Task TriggerAsync(HttpListenerContext context, string team, CancellationToken ct)
    {
        Answered++;

        if (Trigger is null || Admits is null)
        {
            // Nothing on the other end. A dashboard opened to watch a run
            // cannot start one, and says the route is not here rather than
            // that it is switched off.
            await WriteAsync(context, 404, "text/plain; charset=utf-8",
                "This server does not start runs.").ConfigureAwait(false);

            return;
        }

        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.Ordinal))
        {
            await WriteAsync(context, 405, "text/plain; charset=utf-8",
                "Starting a run is a POST.").ConfigureAwait(false);

            return;
        }

        string body;

        using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }

        string? goal;
        string? project;

        try
        {
            JsonElement? asked = body.Length == 0
                ? null
                : JsonDocument.Parse(body).RootElement;

            goal = Text(asked, "goal");
            project = Text(asked, "project");
        }
        catch (JsonException)
        {
            await WriteAsync(context, 400, "text/plain; charset=utf-8",
                "The body should be JSON with a 'goal'.").ConfigureAwait(false);

            return;
        }

        if (goal is not { Length: > 0 })
        {
            await WriteAsync(context, 400, "text/plain; charset=utf-8",
                "A run needs a goal: what the lead is for, in your words.").ConfigureAwait(false);

            return;
        }

        var asking = new TriggerRequest(Uri.UnescapeDataString(team), goal, project);

        if (await Admits(context.Request.Headers["X-Loadout-Token"], asking.Team, ct)
            .ConfigureAwait(false) is { } refused)
        {
            await WriteAsync(context, refused.Status, "text/plain; charset=utf-8", refused.Detail)
                .ConfigureAwait(false);

            return;
        }

        var started = await Trigger(asking, ct).ConfigureAwait(false);

        await WriteAsync(
            context,
            started.Succeeded ? 202 : 500,
            "text/plain; charset=utf-8",
            started.Succeeded
                ? $"Started {asking.Team}. Watch it with: loadout team status"
                : started.Error ?? "The run could not be started.").ConfigureAwait(false);
    }

    /// <summary>One string out of a request body, or null.</summary>
    private static string? Text(JsonElement? body, string name) =>
        body is { ValueKind: JsonValueKind.Object } given
            && given.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    /// <summary>
    /// Splits <c>/api/runs/{id}/{verb}</c>, or says there is no verb.
    /// </summary>
    /// <remarks>
    /// Deliberately not a route table. There are five of these and a table
    /// would be a framework; what matters is that <c>/api/runs/{id}</c> and
    /// <c>/api/runs/{id}/events</c> keep reading and everything else is an
    /// action.
    /// </remarks>
    private static (string Run, string? Verb) Doing(string path)
    {
        var rest = path["/api/runs/".Length..].Trim('/');
        var slash = rest.IndexOf('/', StringComparison.Ordinal);

        if (slash < 0)
        {
            return (rest, null);
        }

        var verb = rest[(slash + 1)..];

        return Reads.Contains(verb.Split('/')[0], StringComparer.Ordinal)
            ? (rest[..slash], null)
            : (rest[..slash], verb);
    }

    /// <summary>
    /// Does something to a run, through whatever can run a command.
    /// </summary>
    /// <remarks>
    /// Nothing here implements any of it. The page asks, this hands the ask to
    /// the daemon, and the daemon runs the command somebody would have typed -
    /// the rule the launcher has kept since its first screen, because two
    /// implementations of one behaviour drift and the one nobody watches
    /// drifts furthest.
    /// </remarks>
    private async Task ActOnAsync(HttpListenerContext context, string run, string verb, CancellationToken ct)
    {
        Answered++;

        if (Act is null)
        {
            await WriteAsync(context, 404, "text/plain; charset=utf-8",
                "This server only reads. Run the daemon to act on a run from here.").ConfigureAwait(false);

            return;
        }

        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.Ordinal))
        {
            await WriteAsync(context, 405, "text/plain; charset=utf-8",
                "Changing a run is a POST.").ConfigureAwait(false);

            return;
        }

        string body;

        using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }

        JsonElement? asked;

        try
        {
            asked = body.Length == 0 ? null : JsonDocument.Parse(body).RootElement;
        }
        catch (JsonException)
        {
            await WriteAsync(context, 400, "text/plain; charset=utf-8", "The body should be JSON.")
                .ConfigureAwait(false);

            return;
        }

        var action = new RunAction(
            Uri.UnescapeDataString(run),
            verb,
            Text(asked, "gate"),
            Text(asked, "answer"),
            Text(asked, "reason"),
            Text(asked, "message"));

        var done = await Act(action, ct).ConfigureAwait(false);

        await WriteAsync(
            context,
            done.Succeeded ? 202 : 400,
            "text/plain; charset=utf-8",
            done.Succeeded ? "Done." : done.Error ?? "That could not be done.").ConfigureAwait(false);
    }

    /// <summary>Whether an address is this machine talking to itself.</summary>
    private static bool IsLoopback(string address) =>
        address is "127.0.0.1" or "localhost" or "::1";

    /// <summary>A port nothing is using, asked of the machine rather than guessed.</summary>
    private static int Free()
    {
        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);

        probe.Start();

        var port = ((IPEndPoint)probe.LocalEndpoint).Port;

        probe.Stop();

        return port;
    }

    /// <summary>Answers requests until cancelled.</summary>
    public async Task ListenAsync(CancellationToken ct)
    {
        // Stopping the listener is what unblocks the accept; without this the
        // loop would sit on one that never returns.
        using var registration = ct.Register(() =>
        {
            try
            {
                _listener.Stop();
            }
            catch (ObjectDisposedException)
            {
            }
        });

        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException
                or InvalidOperationException)
            {
                return;
            }

            // Each one on its own, so a page holding an event stream open does
            // not stop anybody loading the page in a second tab.
            _ = Task.Run(() => AnswerAsync(context, ct), CancellationToken.None);
        }
    }

    private async Task AnswerAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            await HandleAsync(context, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or HttpListenerException
            or ObjectDisposedException or OperationCanceledException)
        {
            // A browser tab that went away mid-answer. Nothing to report and
            // nothing to fix.
        }
    }

    /// <summary>Whether a request carries the token, from the query or a header.</summary>
    /// <remarks>
    /// Compared in fixed time, because the alternative leaks the token one
    /// character at a time to anything that can make requests and read a
    /// clock, which on loopback is any page in any tab.
    /// </remarks>
    private bool Allowed(HttpListenerRequest request)
    {
        var given = request.QueryString["token"] ?? request.Headers["X-Loadout-Token"];

        return given is not null
            && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(given),
                Encoding.UTF8.GetBytes(Token));
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = context.Request;
        var path = request.Url?.AbsolutePath ?? "/";

        // Before the dashboard's own token, because a trigger carries a
        // different one and is the only thing here a stranger is meant to be
        // able to reach. The dashboard's token changes every start, which is
        // right for a browser tab and useless to a git hook.
        if (path.StartsWith("/api/trigger/", StringComparison.Ordinal))
        {
            await TriggerAsync(context, path["/api/trigger/".Length..], ct).ConfigureAwait(false);

            return;
        }

        // Everything that changes a run. Behind the dashboard's own token,
        // unlike a trigger, because these are for whoever has the page open
        // rather than for a machine somewhere else.
        if (path.StartsWith("/api/runs/", StringComparison.Ordinal)
            && Doing(path) is var (run, verb) && verb is { Length: > 0 })
        {
            if (!Allowed(request))
            {
                await WriteAsync(context, 403, "text/plain; charset=utf-8",
                    "This dashboard needs the token it printed when it started.").ConfigureAwait(false);

                return;
            }

            await ActOnAsync(context, run, verb, ct).ConfigureAwait(false);

            return;
        }

        if (!Allowed(request))
        {
            // Said plainly rather than as a puzzle. Whoever sees this is
            // either a person who copied half an address or a page that had
            // no business asking.
            await WriteAsync(context, 403, "text/plain; charset=utf-8",
                "This dashboard needs the token it printed when it started.").ConfigureAwait(false);

            return;
        }

        if (!string.Equals(request.HttpMethod, "GET", StringComparison.Ordinal))
        {
            // Everything this does change was matched further up, by name.
            // Anything reaching here is a read, and the only thing to do with
            // a read somebody is trying to POST to is say so.
            await WriteAsync(context, 405, "text/plain; charset=utf-8",
                "There is nothing here to change. This address only reads.").ConfigureAwait(false);

            return;
        }

        Answered++;

        if (path is "/" or "/index.html")
        {
            await WriteAsync(context, 200, "text/html; charset=utf-8", Page()).ConfigureAwait(false);

            return;
        }

        if (path == "/api/runs")
        {
            await WriteAsync(context, 200, "application/json; charset=utf-8", Runs()).ConfigureAwait(false);

            return;
        }

        if (path.StartsWith("/api/runs/", StringComparison.Ordinal))
        {
            var rest = path["/api/runs/".Length..];

            if (rest.EndsWith("/events", StringComparison.Ordinal))
            {
                await StreamAsync(context, rest[..^"/events".Length], ct).ConfigureAwait(false);

                return;
            }

            if (rest.EndsWith("/documents", StringComparison.Ordinal))
            {
                await PapersAsync(context, rest[..^"/documents".Length]).ConfigureAwait(false);

                return;
            }

            if (rest.IndexOf("/documents/", StringComparison.Ordinal) is var cut && cut > 0)
            {
                await PaperAsync(
                    context,
                    rest[..cut],
                    Uri.UnescapeDataString(rest[(cut + "/documents/".Length)..])).ConfigureAwait(false);

                return;
            }

            var one = _journal.Summarise(rest);

            await WriteAsync(
                context,
                one.Succeeded ? 200 : 404,
                "application/json; charset=utf-8",
                one.Succeeded
                    ? JsonSerializer.Serialize(Describe(one.Value!), Json)
                    : JsonSerializer.Serialize(new { error = one.Error }, Json)).ConfigureAwait(false);

            return;
        }

        await WriteAsync(context, 404, "text/plain; charset=utf-8", "No such page.").ConfigureAwait(false);
    }

    /// <summary>Every run this machine knows about, newest first.</summary>
    private string Runs()
    {
        var runs = _journal.List(40)
            .Select(_journal.Summarise)
            .Where(read => read.Succeeded)
            .Select(read => Describe(read.Value!))
            .ToList();

        return JsonSerializer.Serialize(new { runs }, Json);
    }

    /// <summary>
    /// One run, as the page needs it.
    /// </summary>
    /// <remarks>
    /// Shaped here rather than serialising the summary itself, so the page has
    /// a contract that does not move when the summary grows a field, and so
    /// what is sent is only what the page shows.
    /// </remarks>
    private static object Describe(RunSummary run) => new
    {
        id = run.RunId,
        run.Team,
        run.Goal,
        run.Autonomy,
        started = run.Started,
        finished = run.Finished,
        run.Ended,
        run.Running,

        // Its own state rather than a kind of running. A run working and a run
        // stopped on a question look identical until one is called what it is,
        // and this is the one that says the next move is yours.
        run.WaitingForYou,

        // Why it wants somebody, each saying what would clear it. Computed
        // fresh every read and never remembered, because a rail that only
        // grows is worse than no rail.
        attention = RunAttention.For(run, DateTimeOffset.UtcNow).Select(reason => new
        {
            kind = reason.Kind.ToString().ToLowerInvariant(),
            reason.Detail,
            reason.Clears,
        }),
        run.Project,
        cost = run.CostUsd,
        run.Rounds,
        run.RoundLimit,
        elapsedSeconds = (int)run.Elapsed.TotalSeconds,
        atMostRemainingSeconds = run.AtMostRemaining is { } left ? (int)left.TotalSeconds : (int?)null,
        merged = run.Merged,
        // Everything the run has stopped and asked, so a page can offer to
        // answer it. Empty on a run that is working, which is most of them.
        gates = run.Waiting.Select(gate => new
        {
            gate.Id,
            gate.Kind,
            gate.Node,
            gate.Role,
            question = gate.Question,
            options = gate.Choices,
            gate.Recommendation,
            gate.At,
        }),

        // Where the spend stands, which the design asked for and the list
        // never carried: what it has cost, and the cap if the team set one.
        budget = new
        {
            spent = run.CostUsd,
            cap = run.BudgetUsd,
        },

        nodes = run.Nodes.Select(node => new
        {
            node.Node,
            node.Role,
            node.State,
            node.Turns,
            cost = node.CostUsd,
            node.Branch,
            node.Denials,
            node.Doing,
            node.Said,
            node.Model,
            tookSeconds = node.Took is { } took ? (int)took.TotalSeconds : (int?)null,
        }),

        // Every exchange, one by one, rather than only each node's total. Two
        // nodes that cost the same are the same number and can be quite
        // different problems, and the totals cannot tell them apart.
        turns = run.Turns.Select(turn => new
        {
            at = turn.At,
            turn.Node,
            turn.Round,
            turn.Attempt,
            exchanges = turn.Exchanges,
            cost = turn.CostUsd,
            turn.Denials,
            turn.Completed,
            turn.Status,
            turn.Outcome,
        }),
    };

    /// <summary>What a run wrote down beside its journal, listed.</summary>
    /// <remarks>
    /// The listing, not the contents. A run with eight nodes has twenty of
    /// these and most of them are not the one somebody wants.
    /// </remarks>
    private async Task PapersAsync(HttpListenerContext context, string runId)
    {
        var directory = _journal.DirectoryOf(runId);

        if (!Directory.Exists(directory))
        {
            await WriteAsync(context, 404, "application/json; charset=utf-8",
                JsonSerializer.Serialize(new { error = $"No run called {runId}." }, Json)).ConfigureAwait(false);

            return;
        }

        var documents = RunDocuments.In(directory).Select(one => new
        {
            one.Name,
            one.Kind,
            one.Subject,
            one.Bytes,
            one.Written,
        });

        await WriteAsync(context, 200, "application/json; charset=utf-8",
            JsonSerializer.Serialize(new { documents }, Json)).ConfigureAwait(false);
    }

    /// <summary>One of them, as text.</summary>
    /// <remarks>
    /// The name is checked against the shape a run writes before it is joined
    /// to anything, so a name carrying a separator never becomes a path at all.
    /// </remarks>
    private async Task PaperAsync(HttpListenerContext context, string runId, string name)
    {
        var read = RunDocuments.Read(_journal.DirectoryOf(runId), name);

        if (read.Failed)
        {
            await WriteAsync(
                context,
                read.ExitCode == ExitCode.InvalidArguments ? 400 : 404,
                "application/json; charset=utf-8",
                JsonSerializer.Serialize(new { error = read.Error }, Json)).ConfigureAwait(false);

            return;
        }

        var (kind, subject) = RunDocuments.Describe(name);

        await WriteAsync(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(new
        {
            name,
            kind,
            subject,
            text = read.Value,
        }, Json)).ConfigureAwait(false);
    }

    /// <summary>
    /// The journal as it arrives, one event per message.
    /// </summary>
    /// <remarks>
    /// Server-sent events rather than a socket: the page only listens, the
    /// browser reconnects by itself, and there is nothing to negotiate.
    /// </remarks>
    private async Task StreamAsync(HttpListenerContext context, string runId, CancellationToken ct)
    {
        var response = context.Response;

        response.StatusCode = 200;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-cache";
        response.SendChunked = true;

        var sent = 0;

        while (!ct.IsCancellationRequested)
        {
            var read = _journal.Read(runId);

            if (read.Failed)
            {
                await SendAsync(response, "error", JsonSerializer.Serialize(new { error = read.Error }, Json))
                    .ConfigureAwait(false);

                break;
            }

            var events = read.Value!;

            for (; sent < events.Count; sent++)
            {
                var entry = events[sent];

                await SendAsync(response, "line", JsonSerializer.Serialize(new
                {
                    at = entry.At,
                    node = entry.Node,
                    entry.Kind,
                    said = RunJournal.Describe(entry),
                }, Json)).ConfigureAwait(false);
            }

            await Task.Delay(Interval, ct).ConfigureAwait(false);
        }

        response.Close();
    }

    private static async Task SendAsync(HttpListenerResponse response, string name, string data)
    {
        var bytes = Encoding.UTF8.GetBytes($"event: {name}\ndata: {data}\n\n");

        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        await response.OutputStream.FlushAsync().ConfigureAwait(false);
    }

    private static async Task WriteAsync(HttpListenerContext context, int status, string type, string body)
    {
        var response = context.Response;
        var bytes = Encoding.UTF8.GetBytes(body);

        response.StatusCode = status;
        response.ContentType = type;
        response.ContentLength64 = bytes.Length;

        // Nothing here is for anybody else's page to read or embed.
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        // connect-src is what the page's own fetch and event stream need, and
        // leaving it out is not a theoretical tightening: default-src 'none'
        // covers it, so the page loaded, looked right and said "Failed to
        // fetch". Nothing in the tests could see that - they check the markup
        // and the endpoints separately, and a browser is what joins them.
        response.Headers["Content-Security-Policy"] =
            "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self'";

        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);

        response.Close();
    }

    /// <summary>The page itself, carried inside the binary.</summary>
    /// <remarks>
    /// One file, no assets, nothing fetched from anywhere. A dashboard that
    /// pulled a stylesheet from the internet would be a dashboard that does
    /// not work on the machine it is most wanted on: one with no network.
    /// </remarks>
    internal static string Page()
    {
        using var stream = typeof(DashboardServer).Assembly
            .GetManifestResourceStream("Loadout.Core.Teams.Daemon.dashboard.html");

        if (stream is null)
        {
            return "<!doctype html><title>Loadout</title><p>The dashboard page is missing from this build.";
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    public void Dispose() => ((IDisposable)_listener).Dispose();
}
