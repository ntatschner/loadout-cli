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
    private static readonly string[] Reads = ["events", "documents", "diff", "stream", "spent"];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly IRunJournal _journal;

    /// <summary>
    /// Whatever runs git, or null where nothing does.
    /// </summary>
    /// <remarks>
    /// Optional because the dashboard is useful without it and a run's journal
    /// is readable on a machine whose repository has since moved. What a node
    /// changed is the one thing that needs the repository itself, so it is the
    /// one thing that says so when it is not there.
    /// </remarks>
    private readonly Git.IGitManager? _git;

    /// <summary>
    /// The second credential, or null where nothing can be typed at anyway.
    /// </summary>
    /// <remarks>
    /// Separate from the dashboard's own token on purpose. That one is for
    /// watching and deciding - reading a run, answering a gate it asked,
    /// stopping it - and every one of those is something the run offered to
    /// have decided. Typing at a live node is not: it puts words into a process
    /// running with somebody's file access, at a moment nobody chose, over a
    /// port that may be on a network.
    /// </remarks>
    public Attaching? Attach { get; set; }
    private readonly HttpListener _listener = new();

    public DashboardServer(IRunJournal journal, Git.IGitManager? git = null)
    {
        _journal = journal;
        _git = git;
    }

    /// <summary>
    /// Where the office art lives on this machine, or null for none.
    /// </summary>
    /// <remarks>
    /// Null is the ordinary case and draws what the office always drew: a
    /// square with the node's name in it. Loadout ships no art, so this points
    /// at a directory somebody filled themselves.
    /// </remarks>
    public string? OfficeRoot { get; set; }

    /// <summary>Which set in that directory to draw with, or empty for none.</summary>
    public string OfficeSet { get; set; } = string.Empty;

    /// <summary>Which set the waiting area draws with, or empty for none.</summary>
    /// <remarks>
    /// Its own, because a reception full of people waiting and an office full
    /// of people working are different rooms and somebody may well want them
    /// to look different.
    /// </remarks>
    public string WaitingSet { get; set; } = string.Empty;

    /// <summary>
    /// What is queued rather than going, or null where nothing can say.
    /// </summary>
    /// <remarks>
    /// A delegate rather than the two services, for the same reason the other
    /// three are: this type reads journals off a disk and should not acquire a
    /// schedule store and a task store to do it. Null answers an empty room,
    /// which is what a dashboard that cannot see either of them should say.
    /// </remarks>
    public Func<CancellationToken, Task<IReadOnlyList<Waiting>>>? WaitingFor { get; set; }

    /// <summary>
    /// Which page this is: everything it can draw, or nothing that is not
    /// information.
    /// </summary>
    /// <remarks>
    /// Rich by default, because the ordinary case is somebody who has said
    /// nothing about how they read a screen, and the plain page is the one
    /// built for the hardest case. Whoever starts the server sets this from
    /// the person's own profile; see <see cref="Presenting" />.
    /// </remarks>
    public Presentation Look { get; set; } = Presentation.Rich;

    /// <summary>full, reduced or none: how much the page may move.</summary>
    /// <remarks>
    /// The browser is asked as well, through <c>prefers-reduced-motion</c>, and
    /// the page takes the quieter answer. This never restores motion the
    /// operating system has asked to be stopped.
    /// </remarks>
    public string Motion { get; set; } = "full";

    /// <summary>full or safe: whether the palette has to survive a colour deficiency.</summary>
    public string Colour { get; set; } = "full";

    /// <summary>
    /// What this machine says things with, or null where nothing is wired to
    /// one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A web page cannot detect a screen reader. There is no API for it, every
    /// heuristic that claims to is wrong often, and the ones that work at all
    /// work by fingerprinting somebody because of a disability. The machine
    /// serving the page can, because Loadout already has a channel to one and
    /// already reports whether it was a reader that answered or the system's
    /// own voice.
    /// </para>
    /// <para>
    /// So the detection the page cannot do happens here and is sent to it.
    /// Null is the ordinary case for a server nothing wired a voice to, and
    /// answers "nothing here can speak", which is a true answer.
    /// </para>
    /// </remarks>
    public Platform.Abstractions.ISpeech? Voice { get; set; }

    /// <summary>
    /// Whether a run started from this page stops when this server does.
    /// </summary>
    /// <remarks>
    /// True for <c>team dashboard</c>, which hosts the run inside a command
    /// somebody is sitting in front of and will eventually close. False for
    /// the daemon, which is meant to stay up. The page says which, because it
    /// cannot tell and the difference is a run somebody loses.
    /// </remarks>
    public bool Owns { get; set; }

    /// <summary>Whether this person has asked to be spoken to.</summary>
    /// <remarks>
    /// Their own <c>show-speech</c> setting, off by default and off even under
    /// the screen-reader preset. Somebody who has said "speak to me" has said
    /// it once; they should not have to say it again per surface. Somebody who
    /// has not said it is not going to be surprised by a machine that talks.
    /// </remarks>
    public bool MaySpeak { get; set; }

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
    /// What to do when somebody starts a team from the page, or null where
    /// nothing can.
    /// </summary>
    /// <remarks>
    /// Like <see cref="Act"/>, and for the same reason: this maps an ask onto
    /// the command line somebody would have typed and the parser does the
    /// rest. Nothing here knows what a team is, whether the project exists or
    /// what an autonomy is, which is why the page cannot be wrong about them
    /// in a way the terminal would not be.
    /// </remarks>
    public Func<StartRequest, CancellationToken, Task<OperationResult>>? Begin { get; set; }

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
        Beyond = !IsLoopback(where);

        return OperationResult.Ok();
    }

    /// <summary>Whether it is listening on more than this machine.</summary>
    /// <remarks>
    /// Worth saying out loud wherever it is true. The token is the whole of
    /// the protection, and on loopback the only thing that can reach the port
    /// is already running as the person who started it; off loopback that is
    /// no longer so.
    /// </remarks>
    public bool Beyond { get; private set; }

    /// <summary>
    /// Addresses on this machine that something else could actually type.
    /// </summary>
    /// <remarks>
    /// A server bound to 0.0.0.0 prints an address nothing can open. What
    /// somebody wants is the one to type into a phone, so the wildcard is
    /// turned back into whatever this machine is called on its network.
    /// </remarks>
    public IReadOnlyList<string> Reachable() => Spread(Address, Beyond, Here);

    /// <summary>The wildcards a listener can be bound to.</summary>
    private static readonly string[] Everywhere = ["//0.0.0.0:", "//+:", "//*:"];

    /// <summary>
    /// One address per way in, given what this machine is called.
    /// </summary>
    /// <remarks>
    /// Separated from the lookup so it can be tested without a network: what
    /// is worth being sure of is that a wildcard becomes something typeable
    /// and that everything else is left exactly as it was.
    /// </remarks>
    internal static IReadOnlyList<string> Spread(
        string address,
        bool beyond,
        Func<IReadOnlyList<string>> hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);

        if (string.IsNullOrEmpty(address))
        {
            return [];
        }

        if (!beyond || !Everywhere.Any(one => address.Contains(one, StringComparison.Ordinal)))
        {
            return [address];
        }

        var found = hosts()
            .Select(host => Everywhere.Aggregate(
                address,
                (so, wildcard) => so.Replace(wildcard, $"//{host}:", StringComparison.Ordinal)))
            .ToList();

        // A machine that cannot say what it is called on its own network is
        // unusual rather than broken, and the wildcard is still true even when
        // nobody can type it.
        return found.Count > 0 ? found : [address];
    }

    /// <summary>What this machine is called on whatever network it is on.</summary>
    private static IReadOnlyList<string> Here()
    {
        try
        {
            return
            [
                .. System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName())
                    .Where(one => one.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Where(one => !System.Net.IPAddress.IsLoopback(one))
                    .Select(one => one.ToString()),
            ];
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException)
        {
            return [];
        }
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
                "This server only reads. Start it without --watch-only, or run the daemon.")
                .ConfigureAwait(false);

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
            Text(asked, "message"),
            Text(asked, "room"),
            Text(asked, "node"),
            Text(asked, "instead"));

        var done = await Act(action, ct).ConfigureAwait(false);

        await WriteAsync(
            context,
            done.Succeeded ? 202 : 400,
            "text/plain; charset=utf-8",
            done.Succeeded ? "Done." : done.Error ?? "That could not be done.").ConfigureAwait(false);
    }

    /// <summary>
    /// What this machine can say, and saying it.
    /// </summary>
    /// <remarks>
    /// A GET reports; a POST speaks. The report is answered whatever the
    /// settings say, because a page that cannot tell "switched off" from
    /// "nothing here can speak" cannot explain either to anybody.
    /// </remarks>
    private async Task SpeechAsync(HttpListenerContext context, CancellationToken ct)
    {
        var here = Nearby(context.Request);

        if (Voice is null)
        {
            await WriteAsync(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new
                {
                    can = false,
                    reader = false,
                    allowed = MaySpeak,
                    here,
                    said = "This dashboard was not given anything to speak with.",
                },
                Json)).ConfigureAwait(false);

            return;
        }

        var ready = await Voice.IsAvailableAsync(ct).ConfigureAwait(false);

        // A reader answered, as opposed to a voice being available. Only the
        // first is somebody already listening to their machine; the second is
        // a pair of speakers, and talking through those uninvited is a
        // surprise rather than a feature.
        var reader = ready.Succeeded
            && !string.Equals(Voice.Name, "sapi", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Voice.Name, "say", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(context.Request.HttpMethod, "GET", StringComparison.Ordinal))
        {
            await WriteAsync(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new
                {
                    can = reader && MaySpeak && here,
                    reader,
                    who = Voice.Name,
                    allowed = MaySpeak,
                    here,
                    said = ready.Succeeded ? ready.Value : ready.Error,
                },
                Json)).ConfigureAwait(false);

            return;
        }

        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.Ordinal))
        {
            await WriteAsync(context, 405, "text/plain; charset=utf-8",
                "Saying something is a POST.").ConfigureAwait(false);

            return;
        }

        if (!MaySpeak)
        {
            await WriteAsync(context, 403, "text/plain; charset=utf-8",
                "Nobody asked to be spoken to. Turn it on with: "
                + "loadout config set show-speech screen-reader").ConfigureAwait(false);

            return;
        }

        if (!reader)
        {
            await WriteAsync(context, 409, "text/plain; charset=utf-8",
                "No screen reader answered on this machine. This speaks to a reader that is "
                + "already running and never to the speakers.").ConfigureAwait(false);

            return;
        }

        if (!here)
        {
            // Checked per request rather than per listener: a dashboard on
            // 0.0.0.0 is reachable from the network, and a browser on another
            // machine making this one talk is not something anybody asked for.
            await WriteAsync(context, 403, "text/plain; charset=utf-8",
                "Only a browser on this machine can make it speak.").ConfigureAwait(false);

            return;
        }

        string body;

        using (var reading = new StreamReader(context.Request.InputStream, Encoding.UTF8))
        {
            body = await reading.ReadToEndAsync(ct).ConfigureAwait(false);
        }

        JsonElement? asked;

        try
        {
            asked = body.Length == 0 ? null : JsonDocument.Parse(body).RootElement;
        }
        catch (JsonException)
        {
            asked = null;
        }

        var line = Text(asked, "say");

        if (line is not { Length: > 0 })
        {
            await WriteAsync(context, 400, "text/plain; charset=utf-8",
                "Nothing to say.").ConfigureAwait(false);

            return;
        }

        // A ceiling, not a guess at what is sensible: this speaks the page's
        // own announcements, which are a sentence, and a reader handed a
        // megabyte would be reading it for an hour.
        if (line.Length > 400)
        {
            line = line[..400];
        }

        var done = await Voice.SayAsync(line, interrupt: true, ct).ConfigureAwait(false);

        await WriteAsync(
            context,
            done.Succeeded ? 202 : 400,
            "text/plain; charset=utf-8",
            done.Succeeded ? "Said." : done.Error ?? "That could not be said.").ConfigureAwait(false);
    }

    /// <summary>Whether the request came from this machine.</summary>
    private static bool Nearby(HttpListenerRequest request) =>
        request.RemoteEndPoint?.Address is { } from && IPAddress.IsLoopback(from);

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

        // Exchanging the second credential for a grant. Behind the
        // dashboard's own token as well, because there is no reason for
        // anything without it to be guessing at this one.
        if (path == "/api/attach")
        {
            if (!Allowed(request))
            {
                await WriteAsync(context, 403, "text/plain; charset=utf-8",
                    "This dashboard needs the token it printed when it started.").ConfigureAwait(false);

                return;
            }

            await AttachAsync(context, ct).ConfigureAwait(false);

            return;
        }

        // Whether this machine can say something, and saying it. Behind the
        // token like everything else, and behind two more conditions the
        // handler checks: that the person asked to be spoken to, and that the
        // request came from this machine rather than across a network.
        if (path == "/api/speech")
        {
            if (!Allowed(request))
            {
                await WriteAsync(context, 403, "text/plain; charset=utf-8",
                    "This dashboard needs the token it printed when it started.").ConfigureAwait(false);

                return;
            }

            await SpeechAsync(context, ct).ConfigureAwait(false);

            return;
        }

        // Starting work, which belongs to no run yet and so is not under
        // /api/runs. Behind the dashboard's own token like everything else
        // that changes anything.
        if (path == "/api/start")
        {
            if (!Allowed(request))
            {
                await WriteAsync(context, 403, "text/plain; charset=utf-8",
                    "This dashboard needs the token it printed when it started.").ConfigureAwait(false);

                return;
            }

            await StartAsync(context, ct).ConfigureAwait(false);

            return;
        }

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

            // The one verb the dashboard's own token does not grant.
            if (string.Equals(verb, "say", StringComparison.Ordinal)
                && !(Attach?.Holds(request.Headers["X-Loadout-Attach"]) ?? false))
            {
                await WriteAsync(context, 403, "text/plain; charset=utf-8",
                    "Typing at a running node is a separate grant. Give the attach passphrase "
                    + "first; the token that got you here is for watching and deciding.")
                    .ConfigureAwait(false);

                return;
            }

            await ActOnAsync(context, run, verb, ct).ConfigureAwait(false);

            return;
        }

        /*
            The tab's own icon, ahead of the token.

            A browser asks for a favicon before any script has run, so the
            request can never carry the token - which is why every load logged
            a 403 for /favicon.ico, and why the tab badge that says a run needs
            somebody has never once appeared. The page drew the icon itself as
            a data: URI, and its own content security policy forbids those.

            There is nothing here to keep behind a token. It is two coloured
            rectangles, the same bytes for anybody who asks. Which of the two
            is showing is the page's decision, not this server's, so asking for
            either one tells the asker nothing it did not already choose.
        */
        if (path is "/icon" or "/icon-needs" or "/favicon.ico")
        {
            await WriteAsync(context, 200, "image/svg+xml; charset=utf-8",
                string.Equals(path, "/icon-needs", StringComparison.Ordinal)
                    ? NeedsSomebodyIcon
                    : IdleIcon).ConfigureAwait(false);

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
            await WriteAsync(context, 200, "text/html; charset=utf-8", Page(Look, Motion, Colour))
                .ConfigureAwait(false);

            return;
        }

        // One screen, filling the window, for a browser dragged onto a second
        // monitor. The same page every time: which screen it is, is the
        // page's business, and a name it does not know shows the whole
        // dashboard rather than nothing.
        if (path.StartsWith("/screen/", StringComparison.Ordinal))
        {
            await WriteAsync(context, 200, "text/html; charset=utf-8", Page(Look, Motion, Colour))
                .ConfigureAwait(false);

            return;
        }

        // What art there is, if any, so the page knows which desks it can
        // draw and which it has to leave as a square. Answered even with
        // nothing installed, because "none" is an answer the page acts on.
        if (path == "/api/office")
        {
            var root = OfficeRoot;

            await WriteAsync(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new
                {
                    set = root is null ? string.Empty : OfficeSet,
                    sets = root is null ? [] : OfficeArt.Sets(root),
                    pieces = root is null || OfficeSet.Length == 0
                        ? []
                        : OfficeArt.Pieces(root, OfficeSet),
                    room = root is null || OfficeSet.Length == 0
                        ? null
                        : OfficeArt.Room(root, OfficeSet),

                    // Every set this machine has, each with its own room and
                    // its own pieces. A viewer showing four teams at once
                    // draws four different offices, and it cannot ask for
                    // them one page load at a time.
                    offices = root is null
                        ? new Dictionary<string, object>()
                        : OfficeArt.Sets(root).ToDictionary(
                            one => one,
                            one => (object)new
                            {
                                room = OfficeArt.Room(root, one),
                                pieces = OfficeArt.Pieces(root, one),
                            },
                            StringComparer.Ordinal),

                    waitingSet = root is null ? string.Empty : WaitingSet,
                    waitingPieces = root is null || WaitingSet.Length == 0
                        ? []
                        : OfficeArt.Pieces(root, WaitingSet),
                }, Json)).ConfigureAwait(false);

            return;
        }

        if (path.StartsWith("/office/", StringComparison.Ordinal))
        {
            var rest = path["/office/".Length..];

            // /office/<set>/<piece> for a page drawing several offices at
            // once, and /office/<piece> for one drawing the configured one.
            // Two forms rather than a flag, because the caller knows which
            // question it is asking.
            var cut = rest.IndexOf('/', StringComparison.Ordinal);

            await (cut > 0
                ? PieceAsync(context, rest[..cut], rest[(cut + 1)..])
                : PieceAsync(context, OfficeSet, rest)).ConfigureAwait(false);

            return;
        }

        if (path.StartsWith("/waiting/", StringComparison.Ordinal))
        {
            await PieceAsync(context, WaitingSet, path["/waiting/".Length..]).ConfigureAwait(false);

            return;
        }

        // What is queued rather than going. Answered even when nothing can
        // say, because an empty waiting area is an answer somebody acts on.
        if (path == "/api/waiting")
        {
            var waiting = WaitingFor is null
                ? []
                : await WaitingFor(ct).ConfigureAwait(false);

            await WriteAsync(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new
                {
                    waiting = waiting.Select(one => new
                    {
                        kind = one.Kind.ToString().ToLowerInvariant(),
                        one.Id,
                        one.Title,
                        one.Team,
                        one.Project,
                        one.Due,
                        one.Because,
                        one.Held,
                    }),
                }, Json)).ConfigureAwait(false);

            return;
        }

        if (path == "/api/roles")
        {
            await WriteAsync(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new
                {
                    roles = TeamMetrics.Across(_journal).Select(one => new
                    {
                        one.Role,
                        one.Model,
                        one.Runs,
                        one.Turns,
                        cost = one.CostUsd,
                        each = one.Each,
                        one.Accepted,
                        one.Rejected,
                        one.Accepting,
                        one.Denials,
                        one.Refusals,
                        one.Seconds,
                    }),
                }, Json)).ConfigureAwait(false);

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

            if (rest.IndexOf("/stream/", StringComparison.Ordinal) is var from && from > 0)
            {
                await TrailAsync(
                    context,
                    rest[..from],
                    Uri.UnescapeDataString(rest[(from + "/stream/".Length)..]),
                    int.TryParse(request.QueryString["after"], out var had) && had > 0 ? had : 0)
                    .ConfigureAwait(false);

                return;
            }

            if (rest.IndexOf("/diff/", StringComparison.Ordinal) is var at && at > 0)
            {
                // Everything after the word, slashes and all: a node is called
                // implementer/1, and whether that arrives escaped or not is the
                // browser's business rather than this one's.
                await ChangedAsync(
                    context,
                    rest[..at],
                    Uri.UnescapeDataString(rest[(at + "/diff/".Length)..]),
                    ct).ConfigureAwait(false);

                return;
            }

            if (rest.EndsWith("/spent", StringComparison.Ordinal))
            {
                await SpentAsync(context, rest[..^"/spent".Length]).ConfigureAwait(false);

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

        // Whether this server can do anything about any of them. The page
        // draws a row of controls per run, and drawing "Stop it" on a page
        // whose server refuses it is exactly the fault this field exists to
        // stop: a control that cannot be honoured is worse than no control.
        return JsonSerializer.Serialize(
            new
            {
                runs,
                acts = Act is not null,
                begins = Begin is not null,

                // Whether a run started here would stop with this server. The
                // page cannot tell a daemon from a command somebody is sitting
                // in front of, and the difference is a run somebody loses.
                owns = Owns,
            },
            Json);
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

        // Somewhere for the office to put it, and something a person can say
        // out loud a week later. Worked out from the identifier rather than
        // stored, so every machine reading this journal calls it the same.
        room = RoomNames.For(run.Directory, run.RunId),
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

        // What it has cost is on the page already and nobody acts on it. What
        // it is costing, and where that ends up, is the number somebody stops
        // a run over - so it is worked out here rather than left for a person
        // to do in their head at three in the morning.
        numbers = Numbers(run),

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
            node.Base,
            tookSeconds = node.Took is { } took ? (int)took.TotalSeconds : (int?)null,

            // When it last said anything, which is how the terminal screen
            // decides whose output is worth showing without asking anybody.
            lastSeen = node.LastSeen,

            // Somebody to call it. "implementer/1" is the same string in every
            // room on the machine; this points at exactly one node of exactly
            // one run, which is what a person needs to say a week later. The
            // technical name is still right there beside it.
            person = DeskNames.For(run.RunId, node.Node),
            personFull = DeskNames.Full(run.RunId, node.Node),
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

    /// <summary>
    /// Exchanges the attach passphrase for a grant that expires.
    /// </summary>
    /// <remarks>
    /// What comes back is not the passphrase and does not become it: a grant is
    /// thirty-two random bytes this process made a moment ago, and it stops
    /// working on its own whether or not anybody remembers to give it back.
    /// </remarks>
    private async Task AttachAsync(HttpListenerContext context, CancellationToken ct)
    {
        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.Ordinal))
        {
            await WriteAsync(context, 405, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { error = "Asking to attach is a POST." }, Json)).ConfigureAwait(false);

            return;
        }

        if (Attach is null)
        {
            await WriteAsync(context, 501, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new
                {
                    error = "This dashboard cannot type at a node at all. Run the daemon's "
                        + "dashboard, and set a passphrase with: loadout team attach set",
                }, Json)).ConfigureAwait(false);

            return;
        }

        string? given;

        try
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

            given = body.Length == 0
                ? null
                : Text(JsonDocument.Parse(body).RootElement, "passphrase");
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            given = null;
        }

        var grant = await Attach.GrantAsync(given, ct).ConfigureAwait(false);

        await WriteAsync(
            context,
            grant.Succeeded ? 200 : 403,
            "application/json; charset=utf-8",
            JsonSerializer.Serialize(
                grant.Succeeded
                    ? new
                    {
                        grant = grant.Value,
                        minutes = (int)Attaching.Lasts.TotalMinutes,
                        error = (string?)null,
                    }
                    : new { grant = (string?)null, minutes = 0, error = grant.Error },
                Json)).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a team, through whatever can run a command.
    /// </summary>
    /// <remarks>
    /// It answers as soon as the run has been asked for rather than when it
    /// finishes. A team run takes twenty minutes on a good day, and a browser
    /// tab holding a request open for twenty minutes is a tab that has already
    /// given up - the run appears in the list within a few seconds, which is
    /// the answer somebody actually wanted.
    /// </remarks>
    private async Task StartAsync(HttpListenerContext context, CancellationToken ct)
    {
        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.Ordinal))
        {
            await WriteAsync(context, 405, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { error = "Starting a team is a POST." }, Json)).ConfigureAwait(false);

            return;
        }

        if (Begin is null)
        {
            await WriteAsync(context, 501, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new
                {
                    error = "This dashboard is watching only and cannot run commands. "
                        + "Start it without --watch-only, run the daemon, or use the command line.",
                }, Json)).ConfigureAwait(false);

            return;
        }

        StartRequest? asking;

        try
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);

            asking = JsonSerializer.Deserialize<StartRequest>(
                await reader.ReadToEndAsync(ct).ConfigureAwait(false), Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            asking = null;
        }

        if (asking is null || string.IsNullOrWhiteSpace(asking.Team) || string.IsNullOrWhiteSpace(asking.Goal))
        {
            await WriteAsync(context, 400, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { error = "A run needs a team and something to do." }, Json)).ConfigureAwait(false);

            return;
        }

        var done = await Begin(asking, ct).ConfigureAwait(false);

        await WriteAsync(
            context,
            done.Succeeded ? 202 : 400,
            "application/json; charset=utf-8",
            JsonSerializer.Serialize(
                done.Succeeded
                    ? new { started = true, error = (string?)null }
                    : new { started = false, error = done.Error },
                Json)).ConfigureAwait(false);
    }

    /// <summary>Everything one node did, rather than the few lines the journal kept.</summary>
    /// <remarks>
    /// The journal is thin on purpose - one line every few seconds, repeats
    /// dropped - which is right for watching and wrong for working out what a
    /// node actually did. This is the other one.
    /// </remarks>
    private async Task TrailAsync(
        HttpListenerContext context,
        string runId,
        string node,
        int after)
    {
        var directory = _journal.DirectoryOf(runId);
        var read = NodeStream.Read(directory, node, after: after);

        if (read.Failed)
        {
            await WriteAsync(
                context,
                read.ExitCode == ExitCode.InvalidArguments ? 400 : 404,
                "application/json; charset=utf-8",
                JsonSerializer.Serialize(new { error = read.Error }, Json)).ConfigureAwait(false);

            return;
        }

        var steps = read.Value!.Select(step => new
        {
            at = step.At,
            step.Kind,
            step.Tool,
            step.Target,
            step.Text,
            step.Sub,
        });

        await WriteAsync(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(new
        {
            node,

            // What the total became, so a page asking "what is new" knows what
            // to ask for next time and can tell a file that was replaced from
            // one that simply grew.
            total = NodeStream.Count(directory, node),
            steps,
        }, Json)).ConfigureAwait(false);
    }

    /// <summary>
    /// Where each node's time went, from its own stream.
    /// </summary>
    /// <remarks>
    /// Its own endpoint rather than part of the run, because answering it means
    /// reading every node's whole stream and the list is polled every few
    /// seconds. This is asked for when somebody opens the page that shows it.
    /// </remarks>
    private async Task SpentAsync(HttpListenerContext context, string runId)
    {
        var read = _journal.Summarise(runId);

        if (read.Failed)
        {
            await WriteAsync(context, 404, "application/json; charset=utf-8",
                JsonSerializer.Serialize(new { error = read.Error }, Json)).ConfigureAwait(false);

            return;
        }

        var run = read.Value!;
        var nodes = new List<object>();

        foreach (var node in run.Nodes)
        {
            var steps = NodeStream.Read(run.Directory, node.Node);

            if (steps.Failed || steps.Value is not { Count: > 1 } walked)
            {
                continue;
            }

            var spent = NodeStream.Spent(walked);

            if (!spent.Any)
            {
                continue;
            }

            nodes.Add(new
            {
                node.Node,
                node.Role,
                thinking = (int)spent.Thinking.TotalSeconds,
                tools = (int)spent.Tools.TotalSeconds,
                writing = (int)spent.Writing.TotalSeconds,
                idle = (int)spent.Idle.TotalSeconds,
            });
        }

        await WriteAsync(context, 200, "application/json; charset=utf-8",
            JsonSerializer.Serialize(new { nodes }, Json)).ConfigureAwait(false);
    }

    /// <summary>What one node changed, as a patch.</summary>
    /// <remarks>
    /// Every way this can fail is a sentence rather than a status: the
    /// repository has moved, the run is too old to have written down where its
    /// branches started, the branch has been tidied away. Each is ordinary,
    /// and none of them is the page's fault.
    /// </remarks>
    private async Task ChangedAsync(
        HttpListenerContext context,
        string runId,
        string node,
        CancellationToken ct)
    {
        if (_git is null)
        {
            await WriteAsync(context, 501, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { error = "This dashboard was started without anything that can run git." }, Json))
                .ConfigureAwait(false);

            return;
        }

        var read = _journal.Summarise(runId);

        if (read.Failed)
        {
            await WriteAsync(context, 404, "application/json; charset=utf-8",
                JsonSerializer.Serialize(new { error = read.Error }, Json)).ConfigureAwait(false);

            return;
        }

        var run = read.Value!;
        var which = run.Nodes.FirstOrDefault(one =>
            string.Equals(one.Node, node, StringComparison.OrdinalIgnoreCase));

        if (which is null)
        {
            await WriteAsync(context, 404, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { error = $"{runId} had no node called {node}." }, Json)).ConfigureAwait(false);

            return;
        }

        var diff = await RunDiff.ForAsync(_git, run.Path, which, run.Merged, ct).ConfigureAwait(false);

        if (diff.Failed)
        {
            await WriteAsync(context, 409, "application/json; charset=utf-8",
                JsonSerializer.Serialize(new { error = diff.Error }, Json)).ConfigureAwait(false);

            return;
        }

        await WriteAsync(context, 200, "application/json; charset=utf-8",
            JsonSerializer.Serialize(diff.Value, Json)).ConfigureAwait(false);
    }

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

    /// <summary>A run's numbers, shaped for the page.</summary>
    private static object Numbers(RunSummary run)
    {
        var read = RunMetrics.For(run, DateTimeOffset.UtcNow);

        return new
        {
            money = new
            {
                read.Money.Spent,
                perMinute = read.Money.PerMinute,
                rate = RunMetrics.Rate(read.Money.PerMinute),
                read.Money.Projected,
                read.Money.Budget,
                read.Money.Overrunning,
            },
            rounds = read.Rounds.Select(round => new
            {
                round.Round,
                round.Seconds,
                round.Requests,
                round.Running,
            }),
            nodes = read.Nodes.Select(node => new
            {
                node.Node,
                node.Role,
                node.Seconds,
                cost = node.CostUsd,
            }),
            trouble = new
            {
                read.Trouble.Denials,
                read.Trouble.Rejected,
                read.Trouble.Retried,
                quietRounds = read.Trouble.QuietRounds,
                read.Trouble.Conflicts,
                read.Trouble.Any,
            },
        };
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

                    // The same line without its clock in front of it. Two
                    // events that say the same thing are worth showing as one
                    // thing and a count, and the whole line never repeats -
                    // the time is different every time, which is exactly what
                    // made the first attempt at this collapse nothing at all.
                    what = RunJournal.Wording(entry),
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

    /// <summary>The tab icon while nothing is waiting on anybody.</summary>
    private const string IdleIcon =
        "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 16 16'>"
        + "<rect width='16' height='16' rx='3' fill='#0b4f9e'/>"
        + "<rect x='4' y='7' width='8' height='2' rx='1' fill='#fff'/></svg>";

    /// <summary>And while a run is stopped on a question somebody has to answer.</summary>
    private const string NeedsSomebodyIcon =
        "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 16 16'>"
        + "<rect width='16' height='16' rx='3' fill='#7a3b00'/>"
        + "<circle cx='8' cy='8' r='3' fill='#fff'/></svg>";

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
            // img-src is what the office needs to draw a sprite at all, and
            // 'self' is the whole of it: the art is served by this daemon from
            // a directory on this machine. Nothing is fetched from anywhere,
            // which was true when the page had no images and stays true now.
            "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; "
            + "connect-src 'self'; img-src 'self'";

        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);

        response.Close();
    }

    /// <summary>
    /// One piece of the chosen set, as an image.
    /// </summary>
    /// <remarks>
    /// Only from the set that is configured, and only a name that is a name.
    /// A request naming another set is a request to read a directory this was
    /// not pointed at, and the answer to that is the same as for a piece that
    /// is not there.
    /// </remarks>
    private async Task PieceAsync(HttpListenerContext context, string set, string piece)
    {
        if (OfficeRoot is not { Length: > 0 } root
            || set.Length == 0
            || OfficeArt.FileOf(root, set, Uri.UnescapeDataString(piece)) is not { } file
            || OfficeArt.TypeOf(file) is not { } type)
        {
            await WriteAsync(context, 404, "text/plain; charset=utf-8", "No such piece.").ConfigureAwait(false);

            return;
        }

        byte[] bytes;

        try
        {
            bytes = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await WriteAsync(context, 404, "text/plain; charset=utf-8", "That piece could not be read.")
                .ConfigureAwait(false);

            return;
        }

        var response = context.Response;

        response.StatusCode = 200;
        response.ContentType = type;
        response.ContentLength64 = bytes.Length;
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";

        // A set does not change while the daemon is running, and a desk asks
        // for the same sprite on every redraw.
        response.Headers["Cache-Control"] = "private, max-age=3600";

        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);

        response.Close();
    }

    /// <summary>
    /// The anchor the page's own settings are written into.
    /// </summary>
    /// <remarks>
    /// Exact, and checked by a test, because a page whose opening tag was
    /// reworded would go on serving perfectly while quietly ignoring the
    /// profile - the chrome would simply always be on, which is what it looks
    /// like when nothing is wrong.
    /// </remarks>
    internal const string Opening = "<html lang=\"en\">";

    /// <summary>The page, told what it is.</summary>
    /// <remarks>
    /// Written into the markup rather than fetched, so the page is right on
    /// its first paint. A page that asked afterwards would draw itself plain
    /// and then redecorate, which is a flash of the wrong thing for everybody
    /// and a redraw for the people most likely to have asked for none.
    /// </remarks>
    internal static string Page(Presentation look, string motion, string colour)
    {
        var page = Page();

        var told = Opening[..^1]
            + $" data-presentation=\"{(look == Presentation.Plain ? "plain" : "rich")}\""
            + $" data-motion=\"{motion}\""
            + $" data-colour=\"{colour}\">";

        // Never at the cost of serving the page. A missing anchor is a bug in
        // this build, and the dashboard still has to come up.
        return page.Replace(Opening, told, StringComparison.Ordinal);
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
