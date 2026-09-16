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
/// Loopback only, and a token generated at start that every request must
/// carry. Any page in any browser tab can reach a loopback port, so a server
/// without one would let a web page somebody opened read what their agents are
/// doing. The token goes in the address printed at start; nothing else knows
/// it.
/// </para>
/// <para>
/// Read-only, deliberately, for now. Approving a gate or stopping a run from
/// here has to go through the same parser somebody would type at, which is the
/// rule the launcher already follows, and there is nothing yet on the other
/// end of it to receive that: a run is a process the command started, not
/// something this can reach into. The daemon is where that arrives.
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
    /// Starts listening on a loopback address, or says why it could not.
    /// </summary>
    /// <param name="port">The port to listen on, or 0 to let the machine choose.</param>
    public OperationResult Start(int port)
    {
        if (port is < 0 or > 65535)
        {
            return OperationResult.Fail($"{port} is not a port.", ExitCode.InvalidArguments);
        }

        var chosen = port == 0 ? Free() : port;
        var prefix = $"http://127.0.0.1:{chosen.ToString(CultureInfo.InvariantCulture)}/";

        _listener.Prefixes.Add(prefix);

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            return OperationResult.Fail(
                $"Could not listen on {prefix}: {ex.Message}. Another dashboard may already be "
                + "running, or the port may be in use.",
                ExitCode.GeneralFailure);
        }

        Address = $"{prefix}?token={Token}";

        return OperationResult.Ok();
    }

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
            // Every change goes through the command line, which is the rule
            // the launcher follows: two implementations of one behaviour drift,
            // and the one nobody is watching drifts furthest.
            await WriteAsync(context, 405, "text/plain; charset=utf-8",
                "This dashboard only reads. Run team commands from the command line.").ConfigureAwait(false);

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
        cost = run.CostUsd,
        run.Rounds,
        run.RoundLimit,
        elapsedSeconds = (int)run.Elapsed.TotalSeconds,
        atMostRemainingSeconds = run.AtMostRemaining is { } left ? (int)left.TotalSeconds : (int?)null,
        merged = run.Merged,
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
            tookSeconds = node.Took is { } took ? (int)took.TotalSeconds : (int?)null,
        }),
    };

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
        response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'";

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
