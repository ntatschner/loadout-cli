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

    /// <summary>How long the office stream stays silent before saying it is still there.</summary>
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    /// <summary>The segments under a run that only ever read.</summary>
    /// <remarks>
    /// Named rather than assumed. The right default for a segment nobody has
    /// thought about yet is "this changes something": a read routed as a change
    /// is a 400 somebody notices, and a change routed as a read is a change
    /// that skipped the check.
    /// </remarks>
    /// <summary>The port the daemon serves on unless the machine names another.</summary>
    /// <remarks>
    /// Chosen from the unassigned range above the registered ports, where
    /// nothing common listens. It is fixed because an installed app window
    /// is tied to the address it came from.
    /// </remarks>
    public const int DefaultPort = 47611;

    private static readonly string[] Reads =
        ["events", "documents", "diff", "stream", "spent", "outcomes", "file", "outbox.zip"];

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
    /// <summary>What this machine is set to, read per request.</summary>
    /// <remarks>
    /// Null where the server cannot change anything, and the page draws no
    /// settings at all rather than a pane of controls that would be refused -
    /// the same rule the start form follows.
    /// </remarks>
    public Func<CancellationToken, Task<MachineSettings>>? Settings { get; set; }

    /// <summary>
    /// The sessions this server hosts for the browser, or null for none.
    /// </summary>
    /// <remarks>
    /// Only the daemon's server has them. A session lives as long as whatever
    /// hosts it, and a dashboard started in a terminal goes when that terminal
    /// does.
    /// </remarks>
    public Loadout.Core.Sessions.Hosting.HostedSessions? Sessions { get; set; }

    /// <summary>
    /// Past conversations, and resuming one, or null where this dashboard
    /// offers neither. Set with <see cref="Sessions"/> on the daemon's.
    /// </summary>
    public Loadout.Core.Sessions.ISessionResumption? Resumption { get; set; }

    /// <summary>Registers a folder as a project and returns its slug, for 'Register and resume'.</summary>
    public Func<string, CancellationToken, Task<Loadout.Models.Results.OperationResult<string>>>? Register { get; set; }

    private Resuming? Resumable =>
        Resumption is not null && Register is not null ? new Resuming(Resumption, Register) : null;

    /// <summary>
    /// Whether this machine has chosen Studio as the page its address opens,
    /// asked per request so a change is seen without a restart. Null keeps
    /// classic there.
    /// </summary>
    public Func<CancellationToken, Task<bool>>? StudioFirst { get; set; }

    /// <summary>
    /// Whether this server only watches, as 'loadout dashboard --watch-only'
    /// asks: Studio then shows every change as the command to type instead,
    /// and its routes refuse to start, end or type into a session.
    /// </summary>
    public bool WatchOnly { get; set; }

    /// <summary>What Studio's New session sheet offers and would load, or null where nothing here starts sessions.</summary>
    public IStudioLaunch? Launch { get; set; }

    /// <summary>Studio's settings as this person has them, read per request; null answers none.</summary>
    public Func<CancellationToken, Task<object>>? StudioPreferences { get; set; }

    /// <summary>
    /// Whether this platform can host a pseudo-terminal, in words for Studio
    /// to say when it cannot, as doctor does; null when it can.
    /// </summary>
    public string? PseudoTerminal { get; set; }

    /// <summary>What Studio's memory page reads and saves, or null where nothing gave this server one.</summary>
    public IStudioMemory? Memory { get; set; }

    /// <summary>
    /// The task lists, read and changed one list or one task at a time, or
    /// null where nothing gave this server them.
    /// </summary>
    /// <remarks>
    /// The service itself rather than a shape of it, unlike <see cref="TasksFor"/>:
    /// the command line and the agents' tools change tasks through the same
    /// one, so a change from the page is the same change, checked the same way.
    /// </remarks>
    public Tasks.ITaskService? Tasks
    {
        get => _tasks;
        set
        {
            _tasks = value;
            _taskStamps = value is null ? null : new SharedPoll<IReadOnlyList<Tasks.TaskListStamp>>(value.StampsAsync, Interval - TimeSpan.FromMilliseconds(100));
        }
    }

    private Tasks.ITaskService? _tasks;
    private SharedPoll<IReadOnlyList<Tasks.TaskListStamp>>? _taskStamps;

    /// <summary>
    /// The ideas, read one list or one idea at a time and changed in batches,
    /// or null where nothing gave this server them.
    /// </summary>
    /// <remarks>The service itself, as <see cref="Tasks"/> is, for the same reason.</remarks>
    public Ideas.IIdeaService? Ideas
    {
        get => _ideas;
        set
        {
            _ideas = value;
            _ideaStamps = value is null ? null : new SharedPoll<IReadOnlyList<Ideas.IdeaStamp>>(value.StampsAsync, Interval - TimeSpan.FromMilliseconds(100));
        }
    }

    private Ideas.IIdeaService? _ideas;
    private SharedPoll<IReadOnlyList<Ideas.IdeaStamp>>? _ideaStamps;

    /// <summary>
    /// A count that moves whenever the page starts or finishes an idea round of
    /// its own, or null where nothing here starts one.
    /// </summary>
    /// <remarks>
    /// The page marks a round busy before the command takes the idea's lock and
    /// clears it after the lock is let go, so neither file says when the mark
    /// goes. Without this the page would show a finished round as running
    /// until something else changed.
    /// </remarks>
    public Func<int>? IdeasBusy { get; set; }

    /// <summary>The commands Studio can run and the ones it has, or null where this server runs none.</summary>
    public CommandJobs? Jobs { get; set; }

    /// <summary>Where Studio's Settings page reads Loadout's settings from, or null where nothing gave this server one.</summary>
    public Loadout.Core.Configuration.IConfigurationService? Configuration { get; set; }

    /// <summary>Changing one, by running the command somebody would have typed.</summary>
    public Func<SettingsChange, CancellationToken, Task<OperationResult>>? Settle { get; set; }

    public Attaching? Attach { get; set; }
    // Not readonly: a failed Start disposes it, so asking for another port
    // means asking with another listener.
    private HttpListener _listener = new();

    public DashboardServer(IRunJournal journal, Git.IGitManager? git = null)
    {
        _journal = journal;
        _git = git;
        _office = new OfficeFeed(OfficeSnapshot, Interval);
    }

    /// <summary>What the office shows, read once for every page listening.</summary>
    private readonly OfficeFeed _office;

    /// <summary>
    /// Each run's summary, with what its files looked like when it was read.
    /// </summary>
    /// <remarks>
    /// The office feed reads every run twice a second while a page listens,
    /// and nearly all of them have not changed since the last look. A run is
    /// read again only when its journal or its directory has: a question
    /// arrives as a file beside the journal, not a line in it, and adding a
    /// file changes the directory.
    /// </remarks>
    private readonly Dictionary<string, (string Stamp, RunSummary Summary)> _summaries = new(StringComparer.Ordinal);

    /// <summary>Who is on which floor, remembered between looks so moves wait as the rules say.</summary>
    private readonly OfficeBuilding _building = new();

    /// <summary>The kit in use and what it was read from, re-read when the set changes on disk.</summary>
    private (string Stamp, OfficeKit Kit, OfficeRules Rules, int Capacity, string Set)? _kit;

    /// <summary>Floors already laid out, by run, which of its floors, and how many sit there.</summary>
    private readonly Dictionary<string, OfficeScene> _floors = new(StringComparer.Ordinal);

    /// <summary>
    /// Where the office art lives on this machine, or null for none.
    /// </summary>
    /// <remarks>
    /// The office folder, where the built-in set is unpacked and anybody's own
    /// sets sit beside it. Null draws the building in its own shapes.
    /// </remarks>
    public string? OfficeRoot { get; set; }

    /// <summary>Which set in that directory to draw with, or empty for none.</summary>
    public string OfficeSet { get; set; } = string.Empty;

    /// <summary>Who in the kit's cast plays which role, from team-office-cast; empty lets the office choose.</summary>
    public IReadOnlyDictionary<string, string> OfficeCastPins { get; set; } = new Dictionary<string, string>();

    /// <summary>Whether this server is the daemon's, for the server room to say so.</summary>
    public bool IsDaemon { get; set; }

    /// <summary>Whether the daemon runs trigger rules, for Studio to show what waits on them; every daemon does.</summary>
    public bool Triggers { get; set; }

    /// <summary>Whether the daemon's schedules are held, for the server room; null where nothing can say.</summary>
    public Func<bool>? DaemonPaused { get; set; }

    /// <summary>
    /// What the daemon knows about updates, and whether one is waiting for
    /// its restart; reading the feed first when asked to check now. Null for
    /// a dashboard, which updates nothing.
    /// </summary>
    public Func<bool, CancellationToken, Task<object>>? Update { get; set; }

    /// <summary>The bin, for the garbage room to show; null where nothing gave this server one.</summary>
    public TeamBin? Bin { get; set; }

    /// <summary>How many days the bin keeps things, from team-bin-days, so the page can say when each goes.</summary>
    public int BinDays { get; set; } = TeamBin.DefaultDays;

    /// <summary>
    /// The event history, which only the daemon keeps: a dashboard on its own
    /// watches nothing while nobody has it open, so it has nothing to show.
    /// </summary>
    public Events.EventStore? Events { get; set; }

    /// <summary>How many days the history keeps, from events-days, so the page can say.</summary>
    public int EventsDays { get; set; } = Loadout.Core.Events.EventStore.DefaultDays;

    /// <summary>How large the office may be drawn, from team-office-scale; the default when unset or unreadable.</summary>
    public OfficeScale OfficeScale { get; set; } = OfficeScale.Default;

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
    /// True for <c>dashboard</c>, which hosts the run inside a command
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

    /// <summary>
    /// Lets a browser in with a cookie rather than the token in its address.
    /// Null where there is none, and never used off loopback: a credential
    /// that lasts ninety days belongs on the one machine it was made for.
    /// </summary>
    public SignIn? SignIn { get; set; }

    /// <summary>The port listened on, once started.</summary>
    public int Port { get; private set; }

    /// <summary>Where to point a browser, token and all.</summary>
    public string Address { get; private set; } = string.Empty;

    /// <summary>How many requests have been answered, for the tests and for doctor.</summary>
    public int Answered { get; private set; }

    /// <summary>
    /// What to do about a triggered run, or null to refuse every one.
    /// </summary>
    /// <remarks>
    /// Set by whatever can actually start a run through the parser. Null is the
    /// ordinary case and the safe one: <c>dashboard</c> sets nothing, so a
    /// dashboard somebody opened to watch a run cannot start one.
    /// </remarks>
    public Func<TriggerRequest, CancellationToken, Task<OperationResult>>? Trigger { get; set; }

    /// <summary>
    /// What to do about a run: answer a gate, say something, or stop it.
    /// </summary>
    /// <remarks>
    /// Set by whatever can run a command through the parser. Null refuses
    /// every one of them, which is what <c>dashboard</c> does: a page
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
    /// What there is to start, or null where nothing can say.
    /// </summary>
    /// <remarks>
    /// A delegate like the rest, so this type still knows nothing about
    /// catalogues, workspaces or project registries. Null answers an empty
    /// list, and the page falls back to what it did before: the names of teams
    /// that have already run here.
    /// </remarks>
    public Func<CancellationToken, Task<Choosable>>? Choices { get; set; }

    /// <summary>
    /// What to do when somebody writes a team from the page, or null where
    /// nothing can.
    /// </summary>
    /// <remarks>
    /// The same shape as <see cref="Begin"/>, and set by the same things. A
    /// page that can start a run should be able to write the team it wants to
    /// run, because the alternative is what happened: somebody read the start
    /// form as the way to make one.
    /// </remarks>
    public Func<MakeRequest, CancellationToken, Task<OperationResult>>? Make { get; set; }

    /// <summary>
    /// What to do about a schedule, or null where nothing can.
    /// </summary>
    /// <remarks>
    /// The same shape as the rest, and the same reason. A dashboard is the
    /// surface somebody leaves open on a machine that stays up, which is
    /// exactly the machine that should be running things on a clock — and
    /// arranging that was the one thing it could only be told to do from a
    /// terminal.
    /// </remarks>
    public Func<ScheduleAction, CancellationToken, Task<OperationResult>>? Plan { get; set; }

    /// <summary>
    /// Doing something to an idea or a dump, or null where nothing can.
    /// </summary>
    /// <remarks>
    /// A round with an agent takes a minute or two, so whatever answers this
    /// starts it and comes back; the page learns it has finished from
    /// <see cref="IdeasFor"/>, which says which ideas have a round going.
    /// </remarks>
    public Func<IdeaAction, CancellationToken, Task<OperationResult>>? Ideate { get; set; }

    /// <summary>
    /// Every idea and dump, as the page draws them, or null where nothing can
    /// read them.
    /// </summary>
    /// <remarks>
    /// Shaped by whoever supplies it, as the command line's JSON is, so the
    /// page reads what <c>idea show --json</c> writes rather than a second
    /// description of the same record.
    /// </remarks>
    public Func<CancellationToken, Task<object>>? IdeasFor { get; set; }

    /// <summary>
    /// Doing something to a task - adding, moving, editing, removing, or
    /// running a team on it - or null where nothing can.
    /// </summary>
    public Func<TaskAction, CancellationToken, Task<OperationResult>>? Tend { get; set; }

    /// <summary>
    /// Every task list, as the page draws them, or null where nothing can
    /// read them.
    /// </summary>
    public Func<CancellationToken, Task<object>>? TasksFor { get; set; }

    /// <summary>
    /// Clearing out runs in a batch, or null where nothing can.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Act"/> because it names no run. The page can
    /// forget one run from inside it, which is fine for one and is twenty
    /// journeys for twenty - and the runs pane is where somebody looking at
    /// twenty of them is standing.
    /// </remarks>
    public Func<PruneAction, CancellationToken, Task<OperationResult>>? Clear { get; set; }

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

        // Asking the machine for a free port does not reserve it. Free() binds
        // port zero, reads what it was given and lets go, so anything else can
        // take it before the listener opens. The gap cannot be closed - there
        // is no handing an already-bound socket to HttpListener - so a
        // machine-chosen port is simply asked for again.
        //
        // Only a machine-chosen one. A port somebody named and cannot have is
        // a real failure and has to say so: quietly serving the dashboard on a
        // different port would put it somewhere they are not looking.
        for (var attempt = 1; ; attempt++)
        {
            var chosen = port == 0 ? Offer() : port;
            var prefix = $"http://{where}:{chosen.ToString(CultureInfo.InvariantCulture)}/";

            _listener.Prefixes.Add(prefix);

            try
            {
                _listener.Start();
            }
            catch (HttpListenerException ex)
            {
                // Spent either way, so it is replaced either way - a listener
                // whose Start failed cannot be asked again, and reusing it
                // throws ObjectDisposedException from Prefixes.
                Discard();

                if (port == 0 && attempt < 20)
                {
                    continue;
                }

                // Windows refuses a non-loopback prefix to anything unelevated,
                // and the message it gives ("Access is denied") says nothing
                // about why. Naming the reservation is the difference between a
                // person fixing this in a minute and concluding the feature
                // does not work.
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
            Port = chosen;

            return OperationResult.Ok();
        }
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

    /// <summary>One true-or-false out of a request body: true only when it says true.</summary>
    private static bool Flag(JsonElement? body, string name) =>
        body is { ValueKind: JsonValueKind.Object } given
            && given.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.True;

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
            Text(asked, "instead"),
            Text(asked, "budget"),

            // A flag rather than an answer, because "Think again" is not one
            // of the lead's options and the page should not have to know the
            // words the runner listens for.
            Flag(asked, "thinkAgain"));

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
        var here = From(context.Request);

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

    /// <summary>How a request is judged to have come from this machine.</summary>
    /// <remarks>
    /// A seam, and only a seam. Every test here talks to the server over
    /// loopback, so the refusal that matters - somewhere else asking to set
    /// the passphrase - cannot happen in one. Without this it would be an
    /// untested claim in a security surface, which is the kind that turns out
    /// to be wrong.
    /// </remarks>
    internal Func<HttpListenerRequest, bool> From { get; set; } = Nearby;

    /// <summary>Whether an address is this machine talking to itself.</summary>
    private static bool IsLoopback(string address) =>
        address is "127.0.0.1" or "localhost" or "::1";

    /// <summary>What to try next when the machine is choosing the port.</summary>
    /// <remarks>
    /// A seam, and only a seam. The race it exists for - a port read from the
    /// operating system and taken by something else before the listener opens
    /// - happens rarely and never on demand, so a test that waited for it
    /// would pass whether or not the retry worked. This lets a test hand out a
    /// port that is already taken and watch what happens next.
    /// </remarks>
    internal Func<int> Offer { get; set; } = Free;

    /// <summary>Lets go of a listener that could not start.</summary>
    /// <remarks>
    /// Closing one whose Start failed can itself throw, and does on the
    /// managed HttpListener that macOS and Linux use: the prefix that failed
    /// to bind is left in the endpoint manager's bookkeeping, so Close walks
    /// it, looks the endpoint up again and throws the same "Address already in
    /// use" - at teardown, from Dispose, nowhere near what caused it. Windows
    /// does not show it, because HTTP.SYS keeps no such bookkeeping.
    ///
    /// There is nothing to do about it and nothing worth reporting: the
    /// listener is being thrown away.
    /// </remarks>
    private void Discard()
    {
        try
        {
            ((IDisposable)_listener).Dispose();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        {
            // Being thrown away regardless.
            //
            // Defensive, and say so: removing this catch does not fail
            // anything on Linux, which uses the same managed listener macOS
            // does. It is here because a macOS run really did throw from
            // Close - though on the clear-and-reuse path that has since gone,
            // so what is left may never throw at all. An unexercised catch
            // over a Dispose of something already being discarded is a cheaper
            // thing to carry than the run it would otherwise take down.
        }

        _listener = new HttpListener();
    }

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

        if (given is { Length: > 0 }
            && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(given),
                Encoding.UTF8.GetBytes(Token)))
        {
            return true;
        }

        // A page opened through a sign-in link carries no token, and sends
        // an empty one, so the cookie is the fallback rather than the first
        // thing asked.
        return SignedIn(request) != CookieStanding.Refused;
    }

    /// <summary>Whether a request carries a sign-in cookie that lets it in.</summary>
    /// <remarks>
    /// Cookies are kept per host, not per port, so a browser sends this one
    /// to anything else listening on loopback as well, and anything else on
    /// loopback can make a browser send requests here. The cookie only counts
    /// when the request names this listener as its host and, where it says
    /// where it came from, came from this listener's own page.
    /// </remarks>
    internal CookieStanding SignedIn(HttpListenerRequest request)
    {
        if (SignIn is null || Beyond || !From(request) || !Ours(request))
        {
            return CookieStanding.Refused;
        }

        return SignIn.Check(request.Cookies[SignIn.CookieName]?.Value);
    }

    /// <summary>Whether a request names this listener as its host, and as its origin when it gives one.</summary>
    private bool Ours(HttpListenerRequest request)
    {
        var host = request.Headers["Host"];

        if (host is null
            || !(string.Equals(host, $"127.0.0.1:{Port.ToString(CultureInfo.InvariantCulture)}", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, $"localhost:{Port.ToString(CultureInfo.InvariantCulture)}", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var origin = request.Headers["Origin"];

        return origin is null || string.Equals(origin, "http://" + host, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A link that signs a browser in to the dashboard at an address, or null
    /// where that address is not on this machine.
    /// </summary>
    /// <param name="address">Where the dashboard is, as it was printed.</param>
    /// <param name="signIn">The sign-in to mint the link's code with.</param>
    /// <param name="classic">
    /// Whether the browser lands on the classic page rather than wherever the
    /// address itself opens.
    /// </param>
    public static string? SignInLink(string? address, SignIn? signIn, bool classic = false)
    {
        if (signIn is null
            || !Uri.TryCreate(address, UriKind.Absolute, out var where)
            || !IsLoopback(where.Host.Trim('[', ']')))
        {
            return null;
        }

        return $"{where.Scheme}://{where.Authority}/signin?code={Uri.EscapeDataString(signIn.MintCode())}"
            + (classic ? "&to=classic" : string.Empty);
    }

    /// <summary>
    /// Whether the address opens Studio for a machine's <c>dashboard</c>
    /// setting: it does unless the machine chose the classic page.
    /// </summary>
    /// <param name="dashboard">The setting as saved, null where it is unset.</param>
    public static bool OpensStudio(string? dashboard) =>
        !string.Equals(dashboard, "classic", StringComparison.Ordinal);

    /// <summary>Hands a request under /api/v1 to the part of Studio's API it names.</summary>
    /// <remarks>
    /// Every change, and every terminal opened, has to come from Studio's own
    /// page when it says where it came from. The cookie check already insists
    /// on that, but a request carrying the token does not go through it, and
    /// a browser sends its cookies to every port on a host, so any page on
    /// this machine could otherwise start an agent here with the token it was
    /// never meant to see. A request that says nothing about where it came
    /// from is a tool rather than a page, and is let through on its
    /// credential alone.
    /// </remarks>
    private async Task StudioAsync(HttpListenerContext context, string path, CancellationToken ct)
    {
        static bool Under(string path, string root) =>
            path == root || path.StartsWith(root + "/", StringComparison.Ordinal);

        var request = context.Request;
        var changes = !string.Equals(request.HttpMethod, "GET", StringComparison.Ordinal) || request.IsWebSocketRequest;

        if (changes && request.Headers["Origin"] is not null && !Ours(request))
        {
            await WriteAsync(context, 403, "text/plain; charset=utf-8",
                "That came from another page. Only Studio's own page can change anything here.").ConfigureAwait(false);

            return;
        }

        if (Under(path, SessionsApi.Root))
        {
            if (Sessions is null)
            {
                await WriteAsync(context, 404, "text/plain; charset=utf-8",
                    "This dashboard hosts no sessions. The daemon's does: loadout daemon").ConfigureAwait(false);

                return;
            }

            await SessionsApi.HandleAsync(context, Sessions, path, WatchOnly, ct, Resumable).ConfigureAwait(false);

            return;
        }

        if (Under(path, HistoryApi.Root))
        {
            if (Resumable is null)
            {
                await WriteAsync(context, 404, "text/plain; charset=utf-8",
                    "This dashboard keeps no history. The daemon's does: loadout daemon").ConfigureAwait(false);

                return;
            }

            await HistoryApi.HandleAsync(context, Resumable, Sessions, path, WatchOnly, ct).ConfigureAwait(false);

            return;
        }

        if (Under(path, EventLogApi.Root))
        {
            if (Events is null)
            {
                await WriteAsync(context, 404, "text/plain; charset=utf-8",
                    "This dashboard keeps no event history. The daemon's does: loadout daemon").ConfigureAwait(false);

                return;
            }

            await EventLogApi.HandleAsync(context, Events, path, WatchOnly, EventsDays, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);

            return;
        }

        if (Under(path, LaunchApi.Root))
        {
            if (Launch is null)
            {
                await WriteAsync(context, 404, "text/plain; charset=utf-8",
                    "This dashboard starts no sessions. The daemon's does: loadout daemon").ConfigureAwait(false);

                return;
            }

            await LaunchApi.HandleAsync(context, Launch, path, ct).ConfigureAwait(false);

            return;
        }

        // Whether there is a newer release, and whether one is waiting for the
        // daemon's restart. A read, even with check=now, which reads the
        // release feed rather than changing anything: installing is a job,
        // 'update --restart-daemon', like every other change from the page.
        if (path == "/api/v1/update" && request.HttpMethod == "GET")
        {
            if (Update is null)
            {
                await WriteAsync(context, 404, "text/plain; charset=utf-8",
                    "This dashboard does not look for updates. The daemon's does: loadout daemon").ConfigureAwait(false);

                return;
            }

            var fresh = string.Equals(request.QueryString["check"], "now", StringComparison.Ordinal);

            await WriteAsync(context, 200, "application/json; charset=utf-8",
                JsonSerializer.Serialize(await Update(fresh, ct).ConfigureAwait(false), Json)).ConfigureAwait(false);

            return;
        }

        // What the page needs to know about where it is: the version and the
        // platform a problem report is filled in with, the port it is served
        // on, and whether this server only watches. Behind the same checks as
        // the rest, though it holds nothing secret, so a page elsewhere cannot
        // use it to learn what is installed here.
        if (path == "/api/v1/about" && request.HttpMethod == "GET")
        {
            await WriteAsync(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(new
            {
                version = Version(),
                platform = $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription} "
                    + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
                port = Port,
                watchOnly = WatchOnly,
                isDaemon = IsDaemon,
                triggers = Triggers,
                hostsSessions = Sessions is not null,
                startsSessions = Launch is not null && Sessions is not null && !WatchOnly,
                pseudoTerminal = PseudoTerminal,

                // Whether one is set, for Settings to say; never what it is.
                attachPassphrase = Attach is not null && await Attach.IsSetAsync(ct).ConfigureAwait(false),

                // Whether the person's accessibility profile says they use a
                // screen reader. A page cannot ask the operating system, and
                // the terminal's own screen-reader mode is wanted only then.
                screenReader = Configuration is not null
                    && await Configuration.LoadConfigAsync(ct).ConfigureAwait(false) is { Succeeded: true, Value: { } config }
                    && string.Equals(config.Accessibility.Preset, Loadout.Models.Configuration.AccessibilityPresets.ScreenReader, StringComparison.OrdinalIgnoreCase),
            }, Json)).ConfigureAwait(false);

            return;
        }

        if (path == "/api/v1/sign-out")
        {
            await StudioSignOutAsync(context).ConfigureAwait(false);

            return;
        }

        if (path == StudioPreferencesPath)
        {
            await StudioPreferencesAsync(context, ct).ConfigureAwait(false);

            return;
        }

        if (path == "/api/v1/events" && request.HttpMethod == "GET")
        {
            await StudioEventsAsync(context, ct).ConfigureAwait(false);

            return;
        }

        if (Under(path, MemoryApi.Root)
            && path[MemoryApi.Root.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray()
                is var parts)
        {
            if (Tasks is not null && TasksApi.Handles(parts))
            {
                await TasksApi.HandleAsync(context, Tasks, parts, WatchOnly, Environment.UserName, ct).ConfigureAwait(false);

                return;
            }

            if (Ideas is not null && IdeasApi.Handles(parts))
            {
                await IdeasApi.HandleAsync(context, Ideas, parts, WatchOnly, Environment.UserName, ct).ConfigureAwait(false);

                return;
            }
        }

        if (Under(path, MemoryApi.Root) && Memory is not null)
        {
            if (WatchOnly && request.HttpMethod != "GET")
            {
                await WriteAsync(context, 403, "text/plain; charset=utf-8",
                    "This dashboard is only watching, so it cannot change memory. Use 'loadout memory replace' in a terminal.")
                    .ConfigureAwait(false);

                return;
            }

            await MemoryApi.HandleAsync(context, Memory, path, ct).ConfigureAwait(false);

            return;
        }

        if (path == StudioConfig.Root && request.HttpMethod == "GET" && Configuration is not null)
        {
            var read = await StudioConfig.ReadAsync(Configuration, ct).ConfigureAwait(false);

            await (read.Failed
                ? WriteAsync(context, 500, "text/plain; charset=utf-8", read.Error!)
                : WriteAsync(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(read.Value, Json))).ConfigureAwait(false);

            return;
        }

        if (Under(path, CommandsApi.CommandsRoot) || Under(path, CommandsApi.JobsRoot) || path == CommandsApi.ReadRoot)
        {
            if (Jobs is null)
            {
                await WriteAsync(context, 404, "text/plain; charset=utf-8",
                    "This dashboard runs no commands. The daemon's does: loadout daemon").ConfigureAwait(false);

                return;
            }

            await CommandsApi.HandleAsync(context, Jobs, path, WatchOnly, ct).ConfigureAwait(false);

            return;
        }

        await WriteAsync(context, 404, "text/plain; charset=utf-8", "There is nothing here.").ConfigureAwait(false);
    }

    /// <summary>
    /// Signs every browser out, as 'loadout dashboard sign-out' does: the key
    /// the cookies are made with is replaced, so every cookie stops working,
    /// and this browser's is cleared as well.
    /// </summary>
    private async Task StudioSignOutAsync(HttpListenerContext context)
    {
        if (context.Request.HttpMethod != "POST")
        {
            await WriteAsync(context, 405, "text/plain; charset=utf-8", "Signing out is a POST.").ConfigureAwait(false);

            return;
        }

        if (WatchOnly || SignIn is null)
        {
            await WriteAsync(context, 403, "text/plain; charset=utf-8",
                "This dashboard is only watching, so it cannot sign anybody out. Use 'loadout dashboard sign-out' in a terminal.")
                .ConfigureAwait(false);

            return;
        }

        SignIn.Rotate();
        context.Response.Headers.Add("Set-Cookie", $"{SignIn.CookieName}=; Path=/; Max-Age=0; HttpOnly; SameSite=Strict");
        context.Response.StatusCode = 204;
        context.Response.Close();
    }

    /// <summary>Where Studio reads and changes its own settings.</summary>
    internal const string StudioPreferencesPath = "/api/v1/preferences";

    /// <summary>
    /// Studio's settings: a GET reads them, a PUT changes one by running
    /// 'loadout config set', as the classic page's settings do.
    /// </summary>
    private async Task StudioPreferencesAsync(HttpListenerContext context, CancellationToken ct)
    {
        if (context.Request.HttpMethod == "GET")
        {
            if (StudioPreferences is null)
            {
                await WriteAsync(context, 200, "application/json; charset=utf-8", "{}").ConfigureAwait(false);

                return;
            }

            await WriteAsync(context, 200, "application/json; charset=utf-8",
                JsonSerializer.Serialize(await StudioPreferences(ct).ConfigureAwait(false), Json)).ConfigureAwait(false);

            return;
        }

        if (context.Request.HttpMethod != "PUT")
        {
            await WriteAsync(context, 405, "text/plain; charset=utf-8",
                "Studio's settings are read with GET and changed with PUT.").ConfigureAwait(false);

            return;
        }

        if (WatchOnly || Settle is null)
        {
            await WriteAsync(context, 403, "text/plain; charset=utf-8",
                "This dashboard is only watching, so it cannot change a setting. Use 'loadout config set' in a terminal.")
                .ConfigureAwait(false);

            return;
        }

        string? key = null, value = null;

        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: ct).ConfigureAwait(false);
            key = Text(document.RootElement, "key");
            value = Text(document.RootElement, "value") ?? string.Empty;
        }
        catch (JsonException)
        {
        }

        // Only Studio's own keys. This is a page changing settings, and the
        // rest of the machine's configuration has its own screens.
        if (key is null || !key.StartsWith("studio-", StringComparison.Ordinal))
        {
            await WriteAsync(context, 400, "text/plain; charset=utf-8",
                "The body should be JSON naming one of Studio's settings as 'key', with its 'value'.").ConfigureAwait(false);

            return;
        }

        var done = await Settle(new SettingsChange(key, value), ct).ConfigureAwait(false);

        await WriteAsync(context, done.Succeeded ? 200 : 400, "text/plain; charset=utf-8",
            done.Succeeded ? "Set." : done.Error ?? "That could not be set.").ConfigureAwait(false);
    }

    /// <summary>
    /// Says, as it happens, that something Studio shows has changed: a hosted
    /// session started, ended or was renamed, or a team run moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A server-sent event stream, which a page reconnects to by itself, and
    /// which says only what changed, not the change: the page asks the
    /// routes it already reads, so the stream and the routes cannot disagree.
    /// </para>
    /// <para>
    /// Tasks are watched by their files, because they are written from
    /// terminals, by agents and by a pull as often as from here. A list whose
    /// file changed is said as <c>tasks</c>, and as its own <c>tasks</c> event
    /// naming the list and its new revision, for a page or a script that
    /// reads one list rather than all of them. Ideas are watched the same way,
    /// one record each, said as <c>ideas</c> and an <c>ideas</c> event naming
    /// the idea and its revision. The stamps are read once for every stream
    /// open, however many tabs there are.
    /// </para>
    /// <para>
    /// A run's stamp is the one the summaries are cached by, so nothing here
    /// reads a journal that has not changed.
    /// </para>
    /// </remarks>
    private async Task StudioEventsAsync(HttpListenerContext context, CancellationToken ct)
    {
        var response = context.Response;

        response.StatusCode = 200;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.SendChunked = true;

        string? sessions = null, runs = null, eventlog = null;
        int? jobs = null;
        Dictionary<string, string>? lists = null;
        Dictionary<string, (Ideas.IdeaPlace Place, string Stamp)>? records = null;
        int? busy = null;
        var quiet = DateTimeOffset.UtcNow;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // What each session needs is part of it, so a hook saying an
                // agent is waiting, and somebody typing the answer, both reach
                // every open page without it asking; so does background work
                // gone silent long enough to read as waiting for you.
                var sessionsNow = Sessions is null
                    ? string.Empty
                    : string.Join(';', Sessions.All.Select(one => Sessions.SignalOf(one) is var signal
                        ? one.Id + (one.HasEnded ? "!" : string.Empty) + "?" + signal.Needs + (signal.Background ? "~" : string.Empty)
                            + "=" + one.Label
                        : string.Empty));
                var runsNow = string.Join(';', _journal.List(40).Select(run => run + "=" + RunStamp(run)));
                var said = new List<string>();

                if (sessionsNow != sessions)
                {
                    said.Add("sessions");
                    sessions = sessionsNow;
                }

                if (runsNow != runs)
                {
                    said.Add("runs");
                    runs = runsNow;
                }

                if (Jobs is not null && Jobs.Stamp != jobs)
                {
                    said.Add("jobs");
                    jobs = Jobs.Stamp;
                }

                // An event recorded, or one marked read here or from a
                // terminal: the rail's count and an open Events page both move.
                if (Events?.Stamp() is { } logged && logged != eventlog)
                {
                    said.Add("eventlog");
                    eventlog = logged;
                }

                var moved = new List<object>();

                if (_taskStamps is { } polled && Tasks is { } tasks)
                {
                    var now = (await polled.GetAsync(ct).ConfigureAwait(false))
                        .ToDictionary(one => Loadout.Core.Tasks.TaskOperations.Named(one.Project), one => one.Stamp, StringComparer.Ordinal);

                    // The first look says only that tasks may have changed, as
                    // it does for sessions and runs: a stream reconnecting after
                    // the daemon restarted has missed whatever happened while it
                    // was gone, and has no stamps to say which lists.
                    if (lists is null)
                    {
                        said.Add("tasks");
                    }
                    else
                    {
                        foreach (var (named, stamp) in now.Where(one => !lists.TryGetValue(one.Key, out var was) || was != one.Value)
                            .Concat(lists.Where(one => !now.ContainsKey(one.Key))))
                        {
                            var project = Loadout.Core.Tasks.TaskOperations.List(named);
                            var read = await tasks.ReadAsync(project, ct).ConfigureAwait(false);

                            moved.Add(new { project, revision = read.Value?.Revision });
                        }
                    }

                    lists = now;
                }

                if (moved.Count > 0)
                {
                    said.Add("tasks");
                    await StudioEventAsync(response, "event: tasks\ndata: " + JsonSerializer.Serialize(moved, Json) + "\n\n", ct)
                        .ConfigureAwait(false);
                }

                // The ideas the same way, one record each, with a round
                // starting or ending part of each idea's stamp.
                var changed = new List<object>();

                if (_ideaStamps is { } ideaPoll && Ideas is { } ideas)
                {
                    var now = (await ideaPoll.GetAsync(ct).ConfigureAwait(false))
                        .ToDictionary(
                            one => Loadout.Core.Tasks.TaskOperations.Named(one.Place.Project) + "/" + one.Place.Id,
                            one => (one.Place, one.Stamp),
                            StringComparer.Ordinal);
                    var marked = IdeasBusy?.Invoke() ?? 0;

                    if (records is null || marked != busy)
                    {
                        said.Add("ideas");
                    }

                    if (records is not null)
                    {
                        foreach (var (_, (place, _)) in now.Where(one => !records.TryGetValue(one.Key, out var was) || was.Stamp != one.Value.Stamp)
                            .Concat(records.Where(one => !now.ContainsKey(one.Key))))
                        {
                            var read = await ideas.ReadWithRevisionAsync(place, ct).ConfigureAwait(false);

                            changed.Add(new { project = place.Project, id = place.Id, revision = read.Value?.Revision });
                        }
                    }

                    records = now;
                    busy = marked;
                }

                if (changed.Count > 0)
                {
                    said.Add("ideas");
                    await StudioEventAsync(response, "event: ideas\ndata: " + JsonSerializer.Serialize(changed, Json) + "\n\n", ct)
                        .ConfigureAwait(false);
                }

                said = [.. said.Distinct()];

                if (said.Count > 0)
                {
                    await StudioEventAsync(response, "event: changed\ndata: " + JsonSerializer.Serialize(said) + "\n\n", ct)
                        .ConfigureAwait(false);
                    quiet = DateTimeOffset.UtcNow;
                }
                else if (DateTimeOffset.UtcNow - quiet > Heartbeat)
                {
                    await StudioEventAsync(response, ": still here\n\n", ct).ConfigureAwait(false);
                    quiet = DateTimeOffset.UtcNow;
                }

                await Task.Delay(Interval, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or IOException or ObjectDisposedException)
        {
            // The page went away, or the server is stopping.
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
            }
        }
    }

    private static async Task StudioEventAsync(HttpListenerResponse response, string text, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(text);

        await response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await response.OutputStream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>This build's version, without the commit it was built from.</summary>
    private static string Version()
    {
        var informational = typeof(DashboardServer).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "unknown";

        return informational.Split('+')[0];
    }

    /// <summary>Exchanges a one-time code for the cookie, then sends the browser to the page.</summary>
    private async Task SignInAsync(HttpListenerContext context)
    {
        var request = context.Request;

        if (SignIn is null || Beyond || !From(request) || !Ours(request) || !SignIn.Redeem(request.QueryString["code"]))
        {
            await WriteAsync(context, 403, "text/plain; charset=utf-8",
                "This sign-in link has been used or has run out. Open the dashboard again with: loadout dashboard --open")
                .ConfigureAwait(false);

            return;
        }

        GiveCookie(context.Response);
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.StatusCode = 303;

        // One of two fixed places, never an address taken from the link, so
        // a sign-in link cannot be made into a way of sending somebody
        // somewhere else.
        context.Response.Headers["Location"] = request.QueryString["to"] switch
        {
            "classic" => "/classic",
            "studio" => StudioAssets.Root,
            _ => "/",
        };
        context.Response.Close();
    }

    private void GiveCookie(HttpListenerResponse response) =>
        response.AppendHeader(
            "Set-Cookie",
            $"{SignIn.CookieName}={SignIn!.IssueCookie()}; Path=/; "
            + $"Max-Age={((long)SignIn.CookieLife.TotalSeconds).ToString(CultureInfo.InvariantCulture)}; HttpOnly; SameSite=Strict");

    private async Task HandleAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = context.Request;
        var path = request.Url?.AbsolutePath ?? "/";

        if (path == "/signin")
        {
            await SignInAsync(context).ConfigureAwait(false);

            return;
        }

        // A window in daily use never runs out: a cookie a day old goes back
        // fresh with whatever was asked for.
        if (SignedIn(request) == CookieStanding.Renew)
        {
            GiveCookie(context.Response);
        }

        // Setting the second credential, which is the one thing here that
        // this machine has to be sitting in front of.
        if (path == "/api/attach/passphrase")
        {
            if (!Allowed(request))
            {
                await WriteAsync(context, 403, "text/plain; charset=utf-8",
                    "This dashboard needs the token it printed when it started.").ConfigureAwait(false);

                return;
            }

            await PassphraseAsync(context, ct).ConfigureAwait(false);

            return;
        }

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

        // Studio's API. Above the guard that answers every other change with
        // 405, and only from this machine: a hosted session is somebody's
        // agent, and typing at it or rewriting their memory from across a
        // network is not something the token or the cookie should grant.
        // Whether this browser is signed in, and nothing else, so Studio can
        // show how to sign in without first being refused: a browser reports
        // every refused request as an error in its console.
        if (path == "/api/v1/signed-in" && request.HttpMethod == "GET" && !Beyond && From(request) && Ours(request))
        {
            await WriteAsync(context, 200, "application/json; charset=utf-8",
                JsonSerializer.Serialize(new { signedIn = Allowed(request) }, Json)).ConfigureAwait(false);

            return;
        }

        if (path.StartsWith("/api/v1/", StringComparison.Ordinal))
        {
            if (!Allowed(request))
            {
                await WriteAsync(context, 403, "text/plain; charset=utf-8",
                    "This dashboard needs the token it printed when it started.").ConfigureAwait(false);

                return;
            }

            if (Beyond || !From(request))
            {
                await WriteAsync(context, 403, "text/plain; charset=utf-8",
                    "Hosted sessions are only reachable from this machine.").ConfigureAwait(false);

                return;
            }

            await StudioAsync(context, path, ct).ConfigureAwait(false);

            return;
        }

        // Starting work, which belongs to no run yet and so is not under
        // /api/runs. Behind the dashboard's own token like everything else
        // that changes anything.
        // What this machine is set to, and changing it. Up here with the
        // others that change something: everything below the GET guard is a
        // read, and a POST reaching there is answered 405 by design.
        if (path == "/api/settings")
        {
            if (!Allowed(request))
            {
                await WriteAsync(context, 403, "text/plain; charset=utf-8",
                    "This dashboard needs the token it printed when it started.").ConfigureAwait(false);

                return;
            }

            await SettingsAsync(context, ct).ConfigureAwait(false);

            return;
        }

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

        // Arranging for a run to happen again, which belongs to no run and to
        // no team file either. Behind the token like everything else.
        if (path == "/api/schedules")
        {
            if (!Allowed(request))
            {
                await WriteAsync(context, 403, "text/plain; charset=utf-8",
                    "This dashboard needs the token it printed when it started.").ConfigureAwait(false);

                return;
            }

            await PlanAsync(context, ct).ConfigureAwait(false);

            return;
        }

        // Clearing out runs in a batch. Its own path rather than a verb under
        // /api/runs/, because every one of those names a run and this one
        // names none - a path that read as a run called "clear" is the kind of
        // collision that waits for somebody to name something badly.
        if (path == "/api/prune")
        {
            if (!Allowed(request))
            {
                await WriteAsync(context, 403, "text/plain; charset=utf-8",
                    "This dashboard needs the token it printed when it started.").ConfigureAwait(false);

                return;
            }

            await ClearAsync(context, ct).ConfigureAwait(false);

            return;
        }

        // Doing something to an idea. Matched on the method as well as the
        // path, because reading them is a GET on the same path and belongs
        // below with the other reads. Above the guard that answers every other
        // POST with 405, which a route placed beneath it never gets past.
        if (path == "/api/ideas"
            && string.Equals(context.Request.HttpMethod, "POST", StringComparison.Ordinal))
        {
            if (!Allowed(request))
            {
                await WriteAsync(context, 403, "text/plain; charset=utf-8",
                    "This dashboard needs the token it printed when it started.").ConfigureAwait(false);

                return;
            }

            await IdeateAsync(context, ct).ConfigureAwait(false);

            return;
        }

        // Doing something to a task. Matched on the method for the same
        // reason the ideas are: reading them is a GET on the same path, and
        // belongs below with the other reads.
        if (path == "/api/tasks"
            && string.Equals(context.Request.HttpMethod, "POST", StringComparison.Ordinal))
        {
            if (!Allowed(request))
            {
                await WriteAsync(context, 403, "text/plain; charset=utf-8",
                    "This dashboard needs the token it printed when it started.").ConfigureAwait(false);

                return;
            }

            await TendAsync(context, ct).ConfigureAwait(false);

            return;
        }

        // Writing a team, which belongs to no run either. Behind the token
        // like everything else that changes anything.
        if (path == "/api/teams")
        {
            if (!Allowed(request))
            {
                await WriteAsync(context, 403, "text/plain; charset=utf-8",
                    "This dashboard needs the token it printed when it started.").ConfigureAwait(false);

                return;
            }

            await MakeAsync(context, ct).ConfigureAwait(false);

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

        // Studio, the app served beside the classic page, ahead of the token
        // for the same reason the icon is. Its files are the same bytes for
        // anybody, published with the source, and a browser fetches an app's
        // manifest without its cookies: behind the token, Studio could never
        // be installed, and a window signed out could not say how to sign in.
        // What the page reads about this machine goes through the API, which
        // checks. Its address ends in a slash so the bundle's own paths
        // resolve beneath it.
        // The address itself, sent on to Studio where this machine has chosen
        // it, with whatever it carried: an address printed with its token
        // still works. Ahead of the token for the same reason Studio's files
        // are, and it tells the asker nothing but where the page is.
        if (path is "/" or "/index.html" && request.HttpMethod == "GET" && StudioAssets.Built
            && StudioFirst is { } studioFirst && await studioFirst(ct).ConfigureAwait(false))
        {
            context.Response.StatusCode = 302;
            context.Response.Headers["Location"] = StudioAssets.Root + request.Url?.Query;
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Close();

            return;
        }

        if (path == "/studio" && request.HttpMethod == "GET")
        {
            context.Response.StatusCode = 301;
            context.Response.Headers["Location"] = StudioAssets.Root;
            context.Response.Close();

            return;
        }

        if (path.StartsWith(StudioAssets.Root, StringComparison.Ordinal) && request.HttpMethod == "GET")
        {
            await StudioAssets.ServeAsync(context, path).ConfigureAwait(false);

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

        // /classic as well, so the classic page is still there for somebody
        // whose address opens Studio.
        if (path is "/" or "/index.html" or "/classic")
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
        if (path == "/api/office/building")
        {
            var summaries = _journal.List(40).Select(Summary).OfType<RunSummary>().ToList();

            await WriteAsync(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(Building(summaries), Json))
                .ConfigureAwait(false);

            return;
        }

        // The daemon as the server room shows it: whether this is it, and
        // whether its schedules are held.
        if (path == "/api/daemon")
        {
            await WriteAsync(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { daemon = IsDaemon, paused = DaemonPaused?.Invoke() }, Json)).ConfigureAwait(false);

            return;
        }

        // What is in the bin, for the garbage room. Read, never changed, here:
        // bringing something back or deleting it is a verb, through a command.
        if (path == "/api/bin")
        {
            var entries = Bin?.List() ?? [];

            await WriteAsync(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new
                {
                    days = BinDays,
                    entries = entries.Select(one => new
                    {
                        kind = one.KindWord,
                        name = one.Name,
                        team = one.Team,
                        binned = one.Removed,

                        // Null where the bin keeps things until it is emptied by hand.
                        goes = one.Expires(BinDays),
                        unmerged = one.Unmerged,
                        bytes = one.Bytes,
                    }),
                }, Json)).ConfigureAwait(false);

            return;
        }

        if (path.StartsWith("/api/office/place/", StringComparison.Ordinal))
        {
            // /api/office/place/<lobby|roof>/<seats>: the lobby or the roof, laid out to seat that many;
            // /api/office/place/<basement-1|basement-2>/0: a basement, which seats nobody.
            var rest = path["/api/office/place/".Length..].Split('/');
            var scene = rest is [var name and ("lobby" or "roof" or "basement-1" or "basement-2"), var seatsText]
                && int.TryParse(seatsText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seats)
                    ? Place(name, seats)
                    : null;

            await (scene is null
                ? WriteAsync(context, 404, "application/json; charset=utf-8", JsonSerializer.Serialize(new { error = "No such place." }, Json))
                : WriteAsync(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(scene, Json))).ConfigureAwait(false);

            return;
        }

        if (path.StartsWith("/api/office/floor/", StringComparison.Ordinal))
        {
            // /api/office/floor/<run>/<part>: the run's floor, laid out.
            // /api/office/floor/<number>: the floor, every team on it.
            var rest = path["/api/office/floor/".Length..].Split('/');
            var scene = rest is [var runId, var partText]
                && RunJournal.Names(runId)
                && int.TryParse(partText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var part)
                    ? Floor(runId, part)
                    : rest is [var numberText]
                        && int.TryParse(numberText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
                        && number is >= 1 and <= 500
                        ? FloorByNumber(number)
                        : null;

            await (scene is null
                ? WriteAsync(context, 404, "application/json; charset=utf-8", JsonSerializer.Serialize(new { error = "No such floor." }, Json))
                : WriteAsync(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(scene, Json))).ConfigureAwait(false);

            return;
        }

        if (path == "/api/office/events")
        {
            await OfficeStreamAsync(context, ct).ConfigureAwait(false);

            return;
        }

        if (path == "/api/office")
        {
            var root = OfficeRoot;

            // The art sets this machine has, each with when it last changed,
            // for the page to put in its pictures' addresses: a rebuilt set
            // must not be drawn from sheets cached before it was. The building
            // itself comes from /api/office/building.
            await WriteAsync(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new
                {
                    sets = root is null ? [] : OfficeArt.Sets(root),
                    offices = root is null
                        ? new Dictionary<string, object>()
                        : OfficeArt.Sets(root).ToDictionary(
                            one => one,
                            one => (object)new { stamp = OfficeScenes.Stamp(root, one) },
                            StringComparer.Ordinal),
                }, Json)).ConfigureAwait(false);

            return;
        }

        if (path.StartsWith("/office/", StringComparison.Ordinal))
        {
            var rest = path["/office/".Length..];

            // /office/<set>/<piece>, which is what the page asks for, and
            // /office/<piece> from the configured set.
            var cut = rest.IndexOf('/', StringComparison.Ordinal);

            await (cut > 0
                ? PieceAsync(context, rest[..cut], rest[(cut + 1)..])
                : PieceAsync(context, OfficeSet, rest)).ConfigureAwait(false);

            return;
        }

        // What is queued rather than going, for the lobby's waiting room.
        // Answered even when nothing can say, because an empty lobby is an
        // answer somebody acts on.
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

        // The ideas, as whoever supplies them shapes them. An empty answer
        // where nothing can read them, which the page draws as "nothing yet"
        // with the forms put away.
        // The task lists, as whoever supplies them shapes them. Empty where
        // nothing can read them, which the page draws as nothing to do.
        if (path == "/api/tasks")
        {
            var tasks = TasksFor is null
                ? new { lists = Array.Empty<object>(), team = (string?)null }
                : await TasksFor(ct).ConfigureAwait(false);

            await WriteTaggedAsync(context, JsonSerializer.Serialize(tasks, Json)).ConfigureAwait(false);

            return;
        }

        if (path == "/api/ideas")
        {
            var ideas = IdeasFor is null
                ? new { ideas = Array.Empty<object>(), dumps = Array.Empty<object>(), busy = Array.Empty<string>() }
                : await IdeasFor(ct).ConfigureAwait(false);

            await WriteTaggedAsync(context, JsonSerializer.Serialize(ideas, Json)).ConfigureAwait(false);

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

        // What there is to start, and what to write a new one beside. Read
        // afresh rather than cached at start, so a team written a moment ago
        // — from the page or from a terminal — is in the next answer.
        if (path == "/api/choices")
        {
            var choosable = Choices is null
                ? new Choosable([], [])
                : await Choices(ct).ConfigureAwait(false);

            await WriteAsync(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new
                {
                    // Whether the page may offer to write one at all. A
                    // watch-only server refuses the POST, and a form that is
                    // drawn and always refused is the fault this whole change
                    // set out to fix.
                    makes = Make is not null,
                    plans = Plan is not null,
                    ideas = Ideate is not null,
                    tasks = Tend is not null,
                    teams = choosable.Teams,
                    projects = choosable.Projects,
                    here = choosable.Here,
                    agents = choosable.Agents ?? [],
                    projectDoneWhen = choosable.ProjectDoneWhen
                        ?? new Dictionary<string, IReadOnlyList<ChoosableDoneWhen>>(),
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

            // What became of each criterion, with what was delivered behind it.
            // Its own address rather than part of every run's description,
            // because it reads the run's report files and the list is read
            // every few seconds.
            if (rest.EndsWith("/outcomes", StringComparison.Ordinal))
            {
                await WriteAsync(
                    context,
                    200,
                    "application/json; charset=utf-8",
                    JsonSerializer.Serialize(Outcomes(rest[..^"/outcomes".Length]), Json)).ConfigureAwait(false);

                return;
            }

            // What the run delivered, one file at a time or all at once. The
            // query says which file, because a path is full of slashes.
            if (rest.EndsWith("/file", StringComparison.Ordinal))
            {
                await DeliveredFileAsync(context, rest[..^"/file".Length], ct).ConfigureAwait(false);

                return;
            }

            if (rest.EndsWith("/outbox.zip", StringComparison.Ordinal))
            {
                await DeliveredZipAsync(context, rest[..^"/outbox.zip".Length], ct).ConfigureAwait(false);

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
                    ? JsonSerializer.Serialize(Describe(one.Value!, OfficeKitNow().Kit, OfficeCastPins), Json)
                    : JsonSerializer.Serialize(new { error = one.Error }, Json)).ConfigureAwait(false);

            return;
        }

        await WriteAsync(context, 404, "text/plain; charset=utf-8", "No such page.").ConfigureAwait(false);
    }

    /// <summary>Every run this machine knows about, newest first.</summary>
    private string Runs()
    {
        var kit = OfficeKitNow().Kit;
        var runs = _journal.List(40)
            .Select(_journal.Summarise)
            .Where(read => read.Succeeded)
            .Select(read => Describe(read.Value!, kit, OfficeCastPins))
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
    /// Only what the office draws, for every run: who is in each room and
    /// what each should be doing.
    /// </summary>
    /// <remarks>
    /// Much smaller than the runs list, and with nothing in it that changes on
    /// its own - no elapsed time, no countdown - so two reads of an unchanged
    /// machine are the same text and the feed sends nothing.
    /// </remarks>
    private string OfficeSnapshot()
    {
        var summaries = _journal.List(40).Select(Summary).OfType<RunSummary>().ToList();
        var kit = OfficeKitNow().Kit;
        var runs = summaries
            .Select(run => (run, cast: OfficeCast.For(kit, run.RunId, run.Nodes.Select(node => (node.Node, node.Role)), OfficeCastPins)))
            .Select(one => new
            {
                id = one.run.RunId,
                one.run.Running,
                nodes = one.run.Nodes.Select(node => new
                {
                    node.Node,
                    node.Role,
                    node.State,
                    activity = one.run.Activity(node),
                    node.Doing,
                    person = DeskNames.For(one.run.RunId, node.Node, OfficeCast.GenderOf(kit, one.cast.GetValueOrDefault(node.Node))),
                    personFull = DeskNames.Full(one.run.RunId, node.Node, OfficeCast.GenderOf(kit, one.cast.GetValueOrDefault(node.Node))),
                    cast = one.cast.GetValueOrDefault(node.Node),
                    office = Office(OfficeIntent.For(one.run, node)),
                }),
            })
            .ToList();

        // Who is on which floor, so a spill or a floor given back shows on the
        // page the moment it happens rather than at the next poll.
        return JsonSerializer.Serialize(new { runs, building = Building(summaries) }, Json);
    }

    /// <summary>The building's kit and rules: the configured set when it is a usable kit, the built-in one otherwise.</summary>
    private (OfficeKit Kit, OfficeRules Rules, int Capacity, string Set) OfficeKitNow()
    {
        var root = OfficeRoot;
        var set = OfficeSet;
        var stamp = root is { } found && set.Length > 0 && OfficeKits.Has(found, set)
            ? set + "|" + OfficeScenes.Stamp(found, set)
            : string.Empty;

        lock (_building)
        {
            if (_kit is { } kept && kept.Stamp == stamp)
            {
                return (kept.Kit, kept.Rules, kept.Capacity, kept.Set);
            }

            var (kit, rules, used) = (OfficeKit.Kit(), OfficeRules.Default, string.Empty);

            if (stamp.Length > 0 && OfficeKits.Check(root!, set) is { Fit: true } check)
            {
                (kit, rules, used) = (check.Kit!, check.Rules, set);
            }

            // How many a bay seats: the building hands runs bays, three to a floor.
            var capacity = FloorPlanner.BayCapacity(kit, rules);

            _kit = (stamp, kit, rules, capacity, used);
            _floors.Clear();

            return (kit, rules, capacity, used);
        }
    }

    /// <summary>Who is on which floor now, as the page reads it.</summary>
    private object Building(IReadOnlyList<RunSummary> runs)
    {
        var (kit, rules, capacity, set) = OfficeKitNow();
        var view = _building.Update(runs, capacity, rules, DateTimeOffset.UtcNow);

        return new
        {
            floors = view.Floors,
            basements = 2,

            // Every floor's size in tiles, and a tile's in pixels, so the page
            // draws the tower as wide as the floors inside it.
            plate = rules.Floor ?? [40, 24],
            tile = kit.Tile,

            // How high a storey is, in tiles, on the same scale as the plate.
            storey = rules.Storey ?? OfficeRules.StoreyTiles,

            // The smallest and largest the page may draw it, in CSS pixels per
            // pixel of art; it picks whole device-pixel steps in between.
            scale = new { min = OfficeScale.Min, max = OfficeScale.Max },

            // The images a kit lays over the neighbourhood the page generates;
            // empty for the built-in kit, which draws it in code.
            materials = set.Length > 0 ? kit.Materials ?? new Dictionary<string, OfficeMaterial>() : new Dictionary<string, OfficeMaterial>(),

            // What the tower's own outside is drawn with, or null to draw it in code.
            facade = set.Length > 0 ? OfficeFacadeArt.For(kit) : null,
            capacity,
            kit = set.Length > 0 ? set : null,
            occupied = view.Occupied.Select(floor => new
            {
                number = floor.Number,
                run = floor.Run,
                part = floor.Part,
                people = floor.People,
                state = floor.State,
                dark = floor.Dark,
                nodes = floor.Nodes,
                bay = floor.Bay,
                bays = floor.Bays,
            }),

            // Each occupied storey's outside, from its floor: solid and glass
            // bays, and which are lit. A storey nobody is on is drawn plain.
            facades = Facades(view),
        };
    }

    /// <summary>One floor of a run, laid out with every team on it, its desks the run's own.</summary>
    private OfficeScene? Floor(string runId, int part)
    {
        var view = BuildingNow();
        var mine = view.Occupied.FirstOrDefault(one => one.Run == runId && one.Part == part);

        if (mine is null || Floor(view, mine.Number) is not { } scene)
        {
            return null;
        }

        var seats = scene.Teams?.FirstOrDefault(team => team.Run == runId && team.Part == part)?.Desks;

        return seats is null ? scene : scene with { Desks = seats };
    }

    /// <summary>A floor by its number, laid out with every team on it, from the cache when nobody on it has changed.</summary>
    private OfficeScene? FloorByNumber(int number) => Floor(BuildingNow(), number);

    private BuildingView BuildingNow()
    {
        var summaries = _journal.List(40).Select(Summary).OfType<RunSummary>().ToList();
        var (_, rules, capacity, _) = OfficeKitNow();

        return _building.Update(summaries, capacity, rules, DateTimeOffset.UtcNow);
    }

    private Dictionary<string, OfficeFacadeStrips> Facades(BuildingView view)
    {
        var facades = new Dictionary<string, OfficeFacadeStrips>(StringComparer.Ordinal);

        foreach (var number in view.Occupied.Select(one => one.Number).Distinct())
        {
            if (Floor(view, number) is not { } scene)
            {
                continue;
            }

            var here = view.Occupied.Where(one => one.Number == number).ToList();
            var anyone = here.Any(one => !one.Dark && one.People > 0);

            facades[number.ToString(System.Globalization.CultureInfo.InvariantCulture)] = OfficeFacadePlan.For(
                scene,
                run => run is null ? anyone : here.Any(one => one.Run == run && !one.Dark && one.People > 0));
        }

        return facades;
    }

    private OfficeScene? Floor(BuildingView view, int number)
    {
        var (kit, rules, _, _) = OfficeKitNow();
        var tenants = view.Occupied
            .Where(one => one.Number == number)
            .Select(one => new OfficeTenant(one.Run, one.Part, one.Bay, one.Bays, Math.Max(1, one.People)))
            .ToList();

        if (tenants.Count == 0)
        {
            return null;
        }

        var building = BuildingRooms(view, number, rules);

        // Every team on it, where and how many: a team arriving lays the floor
        // out again, its own bays and the shared rooms, nobody else's desks;
        // and the building's rooms it is offered, which move up when a floor
        // below takes them.
        var key = $"floor-{number}|" + string.Join("|", tenants.Select(one => $"{one.Run}#{one.Part}#{one.Bay}#{one.Bays}#{one.People}"))
            + (building is null ? "" : $"|building:{string.Join(",", building.Kinds)}#{building.People}");

        lock (_building)
        {
            if (_floors.TryGetValue(key, out var kept))
            {
                return kept;
            }
        }

        var scene = FloorPlanner.Shared(kit, rules, number, tenants, building).Scene;

        lock (_building)
        {
            _floors[key] = scene;
        }

        return scene;
    }

    // The building's own rooms this floor is offered: what no occupied floor below took.
    private OfficeBuildingRooms? BuildingRooms(BuildingView view, int number, OfficeRules rules) =>
        FloorPlanner.BuildingRooms(
            rules,
            number,
            view.Occupied.Select(one => one.Number),
            view.Occupied.Sum(one => Math.Max(1, one.People)),
            below => Floor(view, below));

    /// <summary>
    /// The lobby or the roof, laid out for a number of seats rounded up to the
    /// next four, so one more thing waiting does not rearrange the furniture;
    /// never more than 48, which is more than either can hold anyway.
    /// </summary>
    private OfficeScene Place(string name, int seats)
    {
        var (kit, rules, _, _) = OfficeKitNow();
        var step = Math.Min(48, (Math.Max(0, seats) + 3) / 4 * 4);
        var key = $"{name}#{step}";

        lock (_building)
        {
            if (_floors.TryGetValue(key, out var kept))
            {
                return kept;
            }
        }

        var scene = (name switch
        {
            "lobby" => FloorPlanner.Lobby(kit, rules, step),
            "roof" => FloorPlanner.Roof(kit, rules, step),
            "basement-1" => FloorPlanner.Basement(kit, rules, 1),
            _ => FloorPlanner.Basement(kit, rules, 2),
        }).Scene;

        lock (_building)
        {
            _floors[key] = scene;
        }

        return scene;
    }

    /// <summary>One run's summary, read again only when its files have changed.</summary>
    /// <summary>
    /// What a run's files looked like, which changes when anything about the
    /// run does: a line in its journal, or a question arriving beside it.
    /// </summary>
    private string RunStamp(string runId)
    {
        var directory = _journal.DirectoryOf(runId);
        var journal = new FileInfo(Path.Combine(directory, "journal.jsonl"));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(journal.Exists ? journal.Length : -1)}|{(journal.Exists ? journal.LastWriteTimeUtc.Ticks : 0)}|{Directory.GetLastWriteTimeUtc(directory).Ticks}");
    }

    private RunSummary? Summary(string runId)
    {
        var stamp = RunStamp(runId);

        lock (_summaries)
        {
            if (_summaries.TryGetValue(runId, out var kept) && kept.Stamp == stamp)
            {
                return kept.Summary;
            }
        }

        var read = _journal.Summarise(runId);

        if (read.Failed)
        {
            return null;
        }

        lock (_summaries)
        {
            _summaries[runId] = (stamp, read.Value!);
        }

        return read.Value;
    }

    /// <summary>
    /// The office as it changes, one whole snapshot per message.
    /// </summary>
    /// <remarks>
    /// Each message is complete, so a page that reconnects needs nothing but
    /// the next one and nothing is lost between them. The id is the feed's
    /// sequence number, which the browser sends back as Last-Event-ID; a
    /// page already holding the latest waits for the one after it.
    /// </remarks>
    private async Task OfficeStreamAsync(HttpListenerContext context, CancellationToken ct)
    {
        var response = context.Response;

        response.StatusCode = 200;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-cache";
        response.SendChunked = true;

        var had = long.TryParse(context.Request.Headers["Last-Event-ID"], NumberStyles.None, CultureInfo.InvariantCulture, out var last)
            ? last
            : 0;

        try
        {
            // Said at once, because nothing goes to the browser - not even
            // the headers - until something is written. A page reconnecting
            // with the latest already in hand would otherwise hear nothing,
            // not even that it had connected, until the first heartbeat.
            await response.OutputStream.WriteAsync(": open\n\n"u8.ToArray(), ct).ConfigureAwait(false);
            await response.OutputStream.FlushAsync(ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                string message;

                // Nothing tells this server a page has closed; a write to it
                // fails, and nothing else does. So a quiet office still writes
                // a comment now and then, which finds a page that has gone and
                // lets the feed stop reading for it.
                using (var beat = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    beat.CancelAfter(Heartbeat);

                    try
                    {
                        var (sequence, snapshot) = await _office.NextAsync(had, beat.Token).ConfigureAwait(false);

                        message = string.Create(CultureInfo.InvariantCulture, $"id: {sequence}\nevent: office\ndata: {snapshot}\n\n");
                        had = sequence;
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        message = ": still here\n\n";
                    }
                }

                var bytes = Encoding.UTF8.GetBytes(message);

                await response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
                await response.OutputStream.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or IOException or ObjectDisposedException)
        {
            // The page went, or the server is stopping. Either way there is
            // nobody left to write to.
        }

        try
        {
            response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            // Already gone with the connection.
        }
    }

    /// <summary>What the office shows for a node, as the page reads it.</summary>
    private static object Office(OfficeIntent intent) => new
    {
        lamp = intent.Lamp.ToString().ToLowerInvariant(),
        place = intent.Place.ToString().ToLowerInvariant(),
        pose = intent.Pose.ToString().ToLowerInvariant(),
        intent.Bubble,
    };

    /// <summary>
    /// One run, as the page needs it.
    /// </summary>
    /// <remarks>
    /// Shaped here rather than serialising the summary itself, so the page has
    /// a contract that does not move when the summary grows a field, and so
    /// what is sent is only what the page shows.
    /// </remarks>
    /// <summary>A run's criterion outcomes as the page shows them, or none for a run that cannot be read.</summary>
    private object Outcomes(string runId)
    {
        var events = _journal.Read(runId);

        if (events.Failed)
        {
            return Array.Empty<object>();
        }

        var directory = _journal.DirectoryOf(runId);

        return CriterionOutcomes
            .Of(RunJournal.Fold(runId, directory, events.Value!), RunLeftBehind.In(directory, events.Value!))
            .Select(one => new
            {
                one.Criterion,
                one.Reading,
                one.Verdict,
                one.Met,
                one.Because,
                one.Source,
                delivered = one.Delivered.Select(d => new { d.Node, d.Kind, at = d.Ref }),
                one.Unfound,
            })
            .ToList();
    }

    private static object Describe(RunSummary run, OfficeKit kit, IReadOnlyDictionary<string, string> pins) =>
        Described(run, kit, OfficeCast.For(kit, run.RunId, run.Nodes.Select(node => (node.Node, node.Role)), pins));

    private static object Described(RunSummary run, OfficeKit kit, IReadOnlyDictionary<string, string> cast) => new
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

        // The sentence is what a person reads; this is the word a filter can
        // act on. Null where the ending is one nothing here knows how to
        // file, so a page can say "ended" rather than invent a category.
        outcome = RunOutcomes.Of(run) is { } filed ? RunOutcomes.Spell(filed) : null,
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

        // What the goal was broken into and where each part got to. Empty for
        // a run given no criteria, which is every run written before they
        // existed - so a page drawing this has to treat empty as "this run
        // does not work that way" rather than as "nothing is met".
        coverage = run.Coverage.Select(one => new
        {
            one.Criterion,
            one.Verdict,

            // Words rather than the stored spelling, which for old runs is
            // "notattempted" - and a page that printed what it was given in
            // capitals showed a reader NOTATTEMPTED.
            verdictInWords = one.InWords,
            one.Because,
            one.Understood,
            one.Met,
            one.Source,
        }),

        // How the lead last said it reads each criterion, from the journal,
        // so the page can show readings before there is a verdict on anything.
        readings = run.Readings.Select(one => new { criterion = one.Key, reading = one.Value }),

        // What the lead took the goal to mean, beside the goal itself.
        run.GoalUnderstood,
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

            // When the run stops waiting. The page counts it down, because a
            // question with a deadline nobody is shown is one somebody answers
            // too late and is told it worked.
            gate.Until,

            // When the run answers it itself, and how, for a timed question.
            // Sooner than the wait above, and the one to count down to.
            timer = gate.Timer is { } timer ? new { at = timer.At, does = timer.Does } : null,
        }),

        // Where the spend stands, which the design asked for and the list
        // never carried: what it has cost, and the cap if the team set one.
        budget = new
        {
            spent = run.CostUsd,
            cap = run.BudgetUsd,

            // Said, so the page can tell a run with no cap on purpose from one
            // whose budget it simply does not know.
            uncapped = run.Uncapped,
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

            // What to show beside it: working, waiting for you, waiting on
            // another node, done. The state above is what it last reported,
            // and stays for the page's own logic.
            activity = run.Activity(node),

            // The picture of the same thing: the light on the desk, where the
            // person is and how they sit. Worked out here, beside the words,
            // so that the office and the badges cannot disagree about a node.
            office = Office(OfficeIntent.For(run, node)),
            node.Turns,
            cost = node.CostUsd,
            node.Branch,
            node.Denials,
            node.Doing,
            node.Said,
            node.Model,
            node.Base,

            // The agent's own conversation, which is kept out of every resume
            // list. This is where somebody finds it to take the node over.
            node.Session,
            tookSeconds = node.Took is { } took ? (int)took.TotalSeconds : (int?)null,

            // When it last said anything, which is how the terminal screen
            // decides whose output is worth showing without asking anybody.
            lastSeen = node.LastSeen,

            // Somebody to call it. "implementer/1" is the same string in every
            // room on the machine; this points at exactly one node of exactly
            // one run, which is what a person needs to say a week later. The
            // technical name is still right there beside it.
            person = DeskNames.For(run.RunId, node.Node, OfficeCast.GenderOf(kit, cast.GetValueOrDefault(node.Node))),
            personFull = DeskNames.Full(run.RunId, node.Node, OfficeCast.GenderOf(kit, cast.GetValueOrDefault(node.Node))),

            // Which of the office kit's people draws them, so the name above fits the face.
            cast = cast.GetValueOrDefault(node.Node),
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
    /// <summary>
    /// Sets the passphrase that lets somebody type at a node, from the page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// From this machine only, and that is not the same test as the token.
    /// Every other route here is something a person somewhere else may
    /// legitimately do with the token they were given: watch a run, answer a
    /// gate, hold it, type at a node. This decides what may type at a node
    /// from now on, and a token should not be able to buy that from across a
    /// network - a stolen one would otherwise rewrite the credential it was
    /// stolen alongside.
    /// </para>
    /// <para>
    /// The other half of why it is here: set from the command line it goes in
    /// as a flag, which lands in shell history and, on Unix, in the process
    /// list where ps shows other people's command lines. A field on a page
    /// that only this machine can reach avoids both.
    /// </para>
    /// </remarks>
    private async Task PassphraseAsync(HttpListenerContext context, CancellationToken ct)
    {
        // Whether it could be set from here, and whether one is set at all.
        // Neither is the passphrase and neither is worth withholding: a person
        // sitting at a page wants to know where they stand before they type
        // anything into a box.
        if (string.Equals(context.Request.HttpMethod, "GET", StringComparison.Ordinal))
        {
            await WriteAsync(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new
                {
                    here = From(context.Request),
                    set = Attach is not null && await Attach.IsSetAsync(ct).ConfigureAwait(false),
                    can = Attach is not null,
                }, Json)).ConfigureAwait(false);

            return;
        }

        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.Ordinal))
        {
            await WriteAsync(context, 405, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { error = "Setting the passphrase is a POST." }, Json)).ConfigureAwait(false);

            return;
        }

        if (!From(context.Request))
        {
            await WriteAsync(context, 403, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new
                {
                    error = "The passphrase is set at the machine the dashboard is running on, "
                        + "not from somewhere else. Set it there with: loadout team attach set",
                }, Json)).ConfigureAwait(false);

            return;
        }

        if (Attach is null)
        {
            await WriteAsync(context, 501, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { error = "This dashboard cannot type at a node at all." }, Json)).ConfigureAwait(false);

            return;
        }

        string? given;
        var make = false;

        try
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

            var root = JsonDocument.Parse(body).RootElement;

            given = Text(root, "passphrase");
            make = root.TryGetProperty("generate", out var asked)
                && asked.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            given = null;
        }

        var chosen = make ? Attaching.Make() : given;

        if (chosen is not { Length: > 0 })
        {
            await WriteAsync(context, 400, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { error = "Give a passphrase, or ask for one to be made." }, Json)).ConfigureAwait(false);

            return;
        }

        var kept = await Attach.RememberAsync(chosen, ct).ConfigureAwait(false);

        await WriteAsync(
            context,
            kept.Succeeded ? 200 : 400,
            "application/json; charset=utf-8",
            JsonSerializer.Serialize(
                kept.Succeeded
                    ? new
                    {
                        // Only what was made, and only this once. A passphrase
                        // somebody typed is one they already have, and handing
                        // it back would put it in a response for no reason.
                        made = make ? chosen : null,
                        error = (string?)null,
                    }
                    : new { made = (string?)null, error = kept.Error },
                Json)).ConfigureAwait(false);
    }

    /// <summary>
    /// Reading what this machine is set to, and changing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A GET reads and a POST changes, and a watch-only server refuses the POST
    /// the way it refuses every other one. Where notices go comes back as held
    /// or not held and never as itself: that address is the credential, and this
    /// answer travels to a browser that may not be on this machine.
    /// </para>
    /// <para>
    /// Two changes are not like the others. Trusting a remedy needs the grant
    /// from <c>/api/attach</c>, because it is standing permission for a script
    /// to run unattended and the dashboard token only ever meant "you may read
    /// this page". Moving the listen address off loopback is the change that
    /// makes every run here reachable by anyone holding the address; the page
    /// says so at the moment of the change, and the answer says so again, so
    /// that a script driving this API is told as plainly as a person is.
    /// </para>
    /// </remarks>
    private async Task SettingsAsync(HttpListenerContext context, CancellationToken ct)
    {
        if (string.Equals(context.Request.HttpMethod, "GET", StringComparison.Ordinal))
        {
            if (Settings is null)
            {
                await WriteAsync(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(
                    new { settings = (MachineSettings?)null }, Json)).ConfigureAwait(false);

                return;
            }

            await WriteAsync(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { settings = await Settings(ct).ConfigureAwait(false) }, Json)).ConfigureAwait(false);

            return;
        }

        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.Ordinal))
        {
            await WriteAsync(context, 405, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { error = "Reading a setting is a GET and changing one is a POST." }, Json))
                .ConfigureAwait(false);

            return;
        }

        if (Settle is null)
        {
            await WriteAsync(context, 501, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new
                {
                    error = "This dashboard is watching only and cannot change this machine. "
                        + "Start it without --watch-only, run the daemon, or use the command line.",
                }, Json)).ConfigureAwait(false);

            return;
        }

        SettingsChange? change;

        try
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);

            change = JsonSerializer.Deserialize<SettingsChange>(
                await reader.ReadToEndAsync(ct).ConfigureAwait(false), Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            change = null;
        }

        if (change is null || string.IsNullOrWhiteSpace(change.What))
        {
            await WriteAsync(context, 400, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { error = "Say which setting, as 'what'." }, Json)).ConfigureAwait(false);

            return;
        }

        // The second credential, for the change that grants a script standing
        // permission to run when nobody is here.
        if (NeedsAGrant(change.What) && !(Attach?.Holds(change.Grant ?? string.Empty) ?? false))
        {
            await WriteAsync(context, 403, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new
                {
                    error = "Trusting a remedy needs the passphrase, not just this page. It is "
                        + "standing permission for a script to run when nobody is watching.",
                    needs = "passphrase",
                }, Json)).ConfigureAwait(false);

            return;
        }

        var done = await Settle(change, ct).ConfigureAwait(false);

        await WriteAsync(
            context,
            done.Succeeded ? 200 : 400,
            "application/json; charset=utf-8",
            JsonSerializer.Serialize(
                done.Succeeded
                    ? new { changed = true, error = (string?)null }
                    : new { changed = false, error = done.Error },
                Json)).ConfigureAwait(false);
    }

    /// <summary>Whether this setting needs more than the dashboard's own token.</summary>
    /// <remarks>
    /// Named here rather than taken from the caller, because the server is what
    /// enforces it: a page that forgot to ask for the passphrase must still be
    /// refused by the thing holding the port.
    /// </remarks>
    private static bool NeedsAGrant(string what) =>
        string.Equals(what, "remedy", StringComparison.Ordinal);

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

    /// <summary>
    /// Writing a team from the page.
    /// </summary>
    /// <remarks>
    /// A POST for the same reason starting a run is one, and the same refusal
    /// when nothing here can run a command. What it writes, where it writes it
    /// and whether the name is allowed are all <c>team new</c>'s: this carries
    /// three strings to it and reports what it said.
    /// </remarks>
    private async Task MakeAsync(HttpListenerContext context, CancellationToken ct)
    {
        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.Ordinal))
        {
            await WriteAsync(context, 405, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { error = "Writing a team is a POST." }, Json)).ConfigureAwait(false);

            return;
        }

        if (Make is null)
        {
            await WriteAsync(context, 501, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new
                {
                    error = "This dashboard is watching only and cannot run commands. "
                        + "Start it without --watch-only, run the daemon, or use the command line.",
                }, Json)).ConfigureAwait(false);

            return;
        }

        MakeRequest? asking;

        try
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);

            asking = JsonSerializer.Deserialize<MakeRequest>(
                await reader.ReadToEndAsync(ct).ConfigureAwait(false), Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            asking = null;
        }

        if (asking is null || string.IsNullOrWhiteSpace(asking.Name))
        {
            await WriteAsync(context, 400, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { error = "A team needs a name." }, Json)).ConfigureAwait(false);

            return;
        }

        var done = await Make(asking, ct).ConfigureAwait(false);

        await WriteAsync(
            context,
            done.Succeeded ? 201 : 400,
            "application/json; charset=utf-8",
            JsonSerializer.Serialize(
                done.Succeeded
                    ? new { made = true, error = (string?)null }
                    : new { made = false, error = done.Error },
                Json)).ConfigureAwait(false);
    }

    /// <summary>
    /// Adding a schedule, or taking one away.
    /// </summary>
    /// <remarks>
    /// Awaited like writing a team and unlike starting a run: both verbs write
    /// a small file and come back, so there is no reason to answer before
    /// knowing, and every reason not to.
    /// </remarks>
    private async Task PlanAsync(HttpListenerContext context, CancellationToken ct)
    {
        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.Ordinal))
        {
            await WriteAsync(context, 405, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { error = "Changing the schedules is a POST." }, Json)).ConfigureAwait(false);

            return;
        }

        if (Plan is null)
        {
            await WriteAsync(context, 501, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new
                {
                    error = "This dashboard is watching only and cannot run commands. "
                        + "Start it without --watch-only, run the daemon, or use the command line.",
                }, Json)).ConfigureAwait(false);

            return;
        }

        ScheduleAction? asking;

        try
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);

            asking = JsonSerializer.Deserialize<ScheduleAction>(
                await reader.ReadToEndAsync(ct).ConfigureAwait(false), Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            asking = null;
        }

        if (asking is null || string.IsNullOrWhiteSpace(asking.Name)
            || string.IsNullOrWhiteSpace(asking.Verb))
        {
            await WriteAsync(context, 400, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { error = "A schedule needs a name and something to do to it." }, Json))
                .ConfigureAwait(false);

            return;
        }

        var done = await Plan(asking, ct).ConfigureAwait(false);

        await WriteAsync(
            context,
            done.Succeeded ? 202 : 400,
            "application/json; charset=utf-8",
            JsonSerializer.Serialize(
                done.Succeeded
                    ? new { planned = true, error = (string?)null }
                    : new { planned = false, error = done.Error },
                Json)).ConfigureAwait(false);
    }

    /// <summary>
    /// Something to do to an idea or a dump, through whatever runs the command.
    /// </summary>
    /// <remarks>
    /// Awaited, and answered with what came back. A round with an agent is
    /// started and answered at once by whatever supplies <see cref="Ideate"/>;
    /// everything else writes a small file and says whether it took. Where
    /// what came back carries words for the page, they go as <c>said</c>, for
    /// the page to show in place of its own.
    /// </remarks>
    private async Task IdeateAsync(HttpListenerContext context, CancellationToken ct)
    {
        if (Ideate is null)
        {
            await WriteAsync(context, 501, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new
                {
                    error = "This dashboard is watching only and cannot run commands. "
                        + "Start it without --watch-only, run the daemon, or use the command line.",
                }, Json)).ConfigureAwait(false);

            return;
        }

        IdeaAction? asking;

        try
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);

            asking = JsonSerializer.Deserialize<IdeaAction>(
                await reader.ReadToEndAsync(ct).ConfigureAwait(false), Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            asking = null;
        }

        if (asking is null || string.IsNullOrWhiteSpace(asking.Verb))
        {
            await WriteAsync(context, 400, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { error = "Say what to do to the idea." }, Json)).ConfigureAwait(false);

            return;
        }

        var done = await Ideate(asking, ct).ConfigureAwait(false);

        await WriteAsync(
            context,
            done.Succeeded ? 202 : 400,
            "application/json; charset=utf-8",
            JsonSerializer.Serialize(
                done.Succeeded
                    ? new { done = true, error = (string?)null, said = (done as OperationResult<string>)?.Value }
                    : new { done = false, error = done.Error, said = (string?)null },
                Json)).ConfigureAwait(false);
    }

    /// <summary>
    /// Something to do to a task, through whatever runs the command.
    /// </summary>
    private async Task TendAsync(HttpListenerContext context, CancellationToken ct)
    {
        if (Tend is null)
        {
            await WriteAsync(context, 501, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new
                {
                    error = "This dashboard is watching only and cannot run commands. "
                        + "Start it without --watch-only, run the daemon, or use the command line.",
                }, Json)).ConfigureAwait(false);

            return;
        }

        TaskAction? asking;

        try
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);

            asking = JsonSerializer.Deserialize<TaskAction>(
                await reader.ReadToEndAsync(ct).ConfigureAwait(false), Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            asking = null;
        }

        if (asking is null || string.IsNullOrWhiteSpace(asking.Verb))
        {
            await WriteAsync(context, 400, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { error = "Say what to do to the task." }, Json)).ConfigureAwait(false);

            return;
        }

        var done = await Tend(asking, ct).ConfigureAwait(false);

        await WriteAsync(
            context,
            done.Succeeded ? 202 : 400,
            "application/json; charset=utf-8",
            JsonSerializer.Serialize(
                done.Succeeded
                    ? new { done = true, error = (string?)null }
                    : new { done = false, error = done.Error },
                Json)).ConfigureAwait(false);
    }

    /// <summary>
    /// Clears out runs in a batch, through whatever can run a command.
    /// </summary>
    /// <remarks>
    /// Like every other action here, this decides nothing: it hands the ask
    /// to the command, which is the one place that knows which runs a set of
    /// conditions picks and the one place that refuses to touch a run still
    /// going.
    /// </remarks>
    private async Task ClearAsync(HttpListenerContext context, CancellationToken ct)
    {
        Answered++;

        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.Ordinal))
        {
            await WriteAsync(context, 405, "text/plain; charset=utf-8",
                "Forgetting runs is a POST.").ConfigureAwait(false);

            return;
        }

        if (Clear is null)
        {
            await WriteAsync(context, 501, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new
                {
                    error = "This dashboard is watching only and cannot run commands. "
                        + "Start it without --watch-only, run the daemon, or use the command line.",
                }, Json)).ConfigureAwait(false);

            return;
        }

        PruneAction? asking;

        try
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);

            asking = JsonSerializer.Deserialize<PruneAction>(
                await reader.ReadToEndAsync(ct).ConfigureAwait(false), Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            asking = null;
        }

        if (asking is null)
        {
            await WriteAsync(context, 400, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { error = "Say which runs to forget." }, Json)).ConfigureAwait(false);

            return;
        }

        // An empty ask is "forget everything", which is the one reading of it
        // nobody means. The command refuses it too; refusing here as well is
        // what stops a mis-wired page ever putting the question.
        if (asking is { Outcome: null or "", OlderThan: null or "", Keep: null })
        {
            await WriteAsync(context, 400, "application/json; charset=utf-8", JsonSerializer.Serialize(
                new { error = "Say which runs to forget: an ending, an age, a number to keep, or several." },
                Json)).ConfigureAwait(false);

            return;
        }

        var done = await Clear(asking, ct).ConfigureAwait(false);

        await WriteAsync(
            context,
            done.Succeeded ? 202 : 400,
            "application/json; charset=utf-8",
            JsonSerializer.Serialize(
                done.Succeeded
                    ? new { cleared = true, error = (string?)null }
                    : new { cleared = false, error = done.Error },
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

    /// <summary>
    /// Where a run's outbox comes from: what <c>team outbox</c> reads, or
    /// whatever a test puts in its place.
    /// </summary>
    /// <remarks>
    /// Null means the one this server can make for itself, which needs git;
    /// a server without git has no outbox to read from.
    /// </remarks>
    public IRunOutbox? OutboxSource { get; set; }

    /// <summary>The run's outbox, or the status and words for why not.</summary>
    private async Task<(Outbox? Outbox, int Status, string? Error)> OutboxOfAsync(string runId, CancellationToken ct)
    {
        if ((OutboxSource ?? (_git is null ? null : new RunOutbox(_journal, _git))) is not { } source || _git is null)
        {
            return (null, 501, "This dashboard was started without anything that can run git.");
        }

        var read = await source.ForAsync(runId, ct).ConfigureAwait(false);

        return read.Succeeded ? (read.Value!, 200, null) : (null, 404, read.Error);
    }

    /// <summary>The status a refusal to read one delivered file is answered with.</summary>
    private static int StatusOf(ExitCode code) => code switch
    {
        ExitCode.InvalidArguments => 400,
        ExitCode.PolicyViolation => 403,
        ExitCode.ProjectNotFound => 404,
        _ => 409,
    };

    /// <summary>
    /// One file a run delivered: <c>?commit=&amp;path=</c> for a committed one,
    /// <c>?loose=</c> for one a node reported, and <c>download=1</c> for the
    /// file itself rather than a view of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bytes, with what a page needs to say about them in headers, rather
    /// than JSON: an image goes straight into an <c>img</c>, and text arrives
    /// as text. Text is always served as plain text whatever it is - an HTML
    /// file a node wrote, served as HTML from this origin, would be a page
    /// running with the dashboard's token.
    /// </para>
    /// <para>
    /// A download is the file exactly, unredacted, as the run made it; see
    /// <see cref="RunOutboxFiles"/> for why.
    /// </para>
    /// </remarks>
    private async Task DeliveredFileAsync(HttpListenerContext context, string runId, CancellationToken ct)
    {
        var query = context.Request.QueryString;
        var (commit, path, loose) = (query["commit"], query["path"], query["loose"]);

        if ((loose is { Length: > 0 }) == (commit is { Length: > 0 } || path is { Length: > 0 }))
        {
            await WriteAsync(context, 400, "text/plain; charset=utf-8",
                "Ask for one file: ?commit= and ?path= for a committed one, or ?loose= for one a node reported.")
                .ConfigureAwait(false);

            return;
        }

        var (outbox, status, error) = await OutboxOfAsync(runId, ct).ConfigureAwait(false);

        if (outbox is null)
        {
            await WriteAsync(context, status, "text/plain; charset=utf-8", error ?? "No such run.").ConfigureAwait(false);

            return;
        }

        var read = loose is { Length: > 0 }
            ? RunOutboxFiles.Loose(outbox, loose)
            : await RunOutboxFiles.CommittedAsync(_git!, outbox, commit, path, ct).ConfigureAwait(false);

        if (read.Failed)
        {
            await WriteAsync(context, StatusOf(read.ExitCode), "text/plain; charset=utf-8", read.Error!)
                .ConfigureAwait(false);

            return;
        }

        var file = read.Value!;
        var response = context.Response;

        Untrusted(response);

        if (file.FromCommit is { } from)
        {
            response.Headers["X-Loadout-From-Commit"] = from;
        }

        if (query["download"] is "1" or "true")
        {
            await SendBytesAsync(response, 200, "application/octet-stream", file.Bytes, Attachment(file.Name))
                .ConfigureAwait(false);

            return;
        }

        var view = RunOutboxFiles.View(file);

        if (view.Status != 200)
        {
            await SendBytesAsync(response, view.Status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(view.Error ?? string.Empty), null)
                .ConfigureAwait(false);

            return;
        }

        if (view.Text)
        {
            response.Headers["X-Loadout-Redacted-Lines"] = view.RedactedLines.ToString(CultureInfo.InvariantCulture);
            response.Headers["X-Loadout-Truncated"] = view.Truncated ? "true" : "false";
            response.Headers["X-Loadout-Lines"] = view.Lines.ToString(CultureInfo.InvariantCulture);
        }

        await SendBytesAsync(response, 200, view.ContentType, view.Body, null).ConfigureAwait(false);
    }

    /// <summary>Everything a run delivered, as <c>&lt;run&gt;-outbox.zip</c>.</summary>
    private async Task DeliveredZipAsync(HttpListenerContext context, string runId, CancellationToken ct)
    {
        var (outbox, status, error) = await OutboxOfAsync(runId, ct).ConfigureAwait(false);

        if (outbox is null)
        {
            await WriteAsync(context, status, "text/plain; charset=utf-8", error ?? "No such run.").ConfigureAwait(false);

            return;
        }

        var zip = await RunOutboxFiles.ZipAsync(_git!, outbox, ct).ConfigureAwait(false);
        var response = context.Response;

        Untrusted(response);

        // Said, so a page can tell somebody before they open it that it is short.
        response.Headers["X-Loadout-Left-Out"] = zip.LeftOut.Count.ToString(CultureInfo.InvariantCulture);

        await SendBytesAsync(response, 200, "application/zip", zip.Bytes, Attachment(Uri.UnescapeDataString(runId) + "-outbox.zip"))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The headers for bytes this server did not write: a run's files, which
    /// a model chose the contents of.
    /// </summary>
    /// <remarks>
    /// The sandbox is the one that matters for an SVG opened on its own rather
    /// than in an <c>img</c>: there it is a document, and without it any script
    /// in it would run on this origin. It is on every one of these because
    /// none of them is a page.
    /// </remarks>
    private static void Untrusted(HttpListenerResponse response)
    {
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; style-src 'unsafe-inline'";
    }

    /// <summary>A file's name as a download, plainly and for browsers that read the encoded form.</summary>
    private static string Attachment(string name)
    {
        var plain = string.Concat(name.Select(one => one is < ' ' or > '~' or '"' or '\\' or ';' ? '_' : one));

        return $"attachment; filename=\"{plain}\"; filename*=UTF-8''{Uri.EscapeDataString(name)}";
    }

    private static async Task SendBytesAsync(
        HttpListenerResponse response, int status, string type, byte[] bytes, string? disposition)
    {
        response.StatusCode = status;
        response.ContentType = type;
        response.ContentLength64 = bytes.Length;

        if (disposition is not null)
        {
            response.Headers["Content-Disposition"] = disposition;
        }

        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);

        response.Close();
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

    /// <summary>
    /// Writes JSON with an ETag of its own text, or 304 when the page already
    /// has exactly that.
    /// </summary>
    /// <remarks>
    /// For the routes that answer with everything, which a page and a script
    /// ask for over and over and which are mostly unchanged: the answer is
    /// still made each time, from lists kept parsed, but not sent again.
    /// </remarks>
    private static Task WriteTaggedAsync(HttpListenerContext context, string json)
    {
        var revision = Loadout.Core.Configuration.YamlStore.Revision(json);

        if (TasksApi.Unchanged(context, revision))
        {
            return Task.CompletedTask;
        }

        TasksApi.Tag(context, revision);

        return WriteAsync(context, 200, "application/json; charset=utf-8", json);
    }

    internal static async Task WriteAsync(HttpListenerContext context, int status, string type, string body)
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
            // 'self' is the art: it is served by this daemon from a directory on
            // this machine. data: is the page's own drawings - the state glyphs,
            // the select chevron and the mark in the top bar are written into
            // the stylesheet and the markup as data: images - and font-src data:
            // is its type, three woff2 files inlined into the page. Both are
            // bytes the page already carries, so nothing is fetched from
            // anywhere, which was true when the page had no images and stays
            // true now.
            "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; "
            + "connect-src 'self'; img-src 'self' data:; font-src data:";

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

    public void Dispose()
    {
        _office.Dispose();
        Discard();
    }
}
