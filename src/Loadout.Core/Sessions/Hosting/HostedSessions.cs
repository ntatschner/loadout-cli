using System.Security.Cryptography;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Sessions.Hosting;

/// <summary>
/// What somebody asked a hosted session to be: a launch of an agent against a
/// project, or one of the commands that ask as they go.
/// </summary>
/// <param name="Project">Project slug, alias or name, as 'loadout launch' takes it.</param>
/// <param name="Agent">The agent to launch, or null for the project's own.</param>
/// <param name="Task">What the session is for, which chooses the specialists.</param>
/// <param name="TaskId">A recorded task the session is for.</param>
/// <param name="Mode">advise, investigate, implement or review.</param>
/// <param name="Columns">Width to start at.</param>
/// <param name="Rows">Height to start at.</param>
/// <param name="Profile">The context profile, or null for the project's own settings.</param>
/// <param name="Worktree">The working tree by branch or directory, or null for the main one.</param>
/// <param name="Offline">Do not reach the network.</param>
/// <param name="NoSync">Do not synchronise the workspace first.</param>
/// <param name="Handoff">Append the most recent handoff to the context.</param>
/// <param name="Command">
/// A command to run in the terminal instead of a launch, such as 'setup' or
/// 'voice learn', with <paramref name="Words"/> after it. Which commands may
/// be run this way is the caller's to decide; the launch fields are unused.
/// </param>
/// <param name="Words">Everything after the command's name.</param>
/// <param name="Resuming">
/// The agent's id for the conversation a 'resume' command carries on, or
/// null. It runs as the command, and is shown as the session it reopens:
/// the project, agent, task and mode fields hold what the resume planned.
/// </param>
public sealed record HostedLaunch(
    string Project,
    string? Agent = null,
    string? Task = null,
    string? TaskId = null,
    string? Mode = null,
    int Columns = HostedSessions.DefaultColumns,
    int Rows = HostedSessions.DefaultRows,
    string? Profile = null,
    string? Worktree = null,
    bool Offline = false,
    bool NoSync = false,
    bool Handoff = false,
    string? Command = null,
    IReadOnlyList<string>? Words = null,
    string? Resuming = null);

/// <summary>
/// The sessions the daemon is hosting for the browser, and the one way of
/// starting another.
/// </summary>
/// <remarks>
/// <para>
/// A hosted session is this launcher run again as 'loadout launch', inside a
/// pseudo-terminal rather than a console. Nothing here compiles context,
/// chooses an agent or strips an environment: the command does all of that,
/// exactly as it does for somebody typing it, so a session started from the
/// page cannot be told a different thing from one started in a terminal.
/// </para>
/// <para>
/// A session that ends stays listed, with its exit code, until somebody
/// removes it, so a page that was closed while it ended can still say how.
/// They all end with the daemon (Q10).
/// </para>
/// </remarks>
public sealed class HostedSessions : IAsyncDisposable
{
    /// <summary>Width a session starts at when the page does not say.</summary>
    public const int DefaultColumns = 120;

    /// <summary>Height a session starts at when the page does not say.</summary>
    public const int DefaultRows = 32;

    private readonly Func<IPseudoTerminal> _terminals;
    private readonly Func<(string Command, IReadOnlyList<string> Prefix)?> _launcher;
    private readonly IChildLifetime? _lifetime;
    private readonly IProcessTree? _tree;
    private readonly string? _signals;
    private readonly TimeProvider _time;
    private readonly Action<string>? _noted;
    private readonly Dictionary<string, (DateTimeOffset At, bool Escalated)> _backgrounds = new(StringComparer.Ordinal);
    private int _backgroundCount;
    private int _escalatedCount;
    private readonly object _gate = new();
    private readonly List<HostedSession> _sessions = [];
    private readonly Dictionary<string, HostedLaunch> _launches = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <param name="terminals">A new pseudo-terminal for each session.</param>
    /// <param name="launcher">How to start this launcher again, or null where it cannot say.</param>
    /// <param name="lifetime">Ties each session's process to the daemon's.</param>
    /// <param name="tree">Measures what each session's processes hold, or null for nothing to measure with.</param>
    /// <param name="signals">
    /// Where agents' hooks report a session waiting (<see cref="SessionSignals"/>),
    /// or null where nothing reads them, and no session is told where to write.
    /// </param>
    /// <param name="time">The clock a background signal's time is checked against; the system's unless a test says.</param>
    /// <param name="noted">
    /// Told a line for the daemon's log each time a session is first read as
    /// waiting on background work, and each time such a session goes on to
    /// read as waiting for you, with the counts so far and nothing else.
    /// </param>
    public HostedSessions(
        Func<IPseudoTerminal> terminals,
        Func<(string Command, IReadOnlyList<string> Prefix)?> launcher,
        IChildLifetime? lifetime = null,
        IProcessTree? tree = null,
        string? signals = null,
        TimeProvider? time = null,
        Action<string>? noted = null)
    {
        _terminals = terminals;
        _launcher = launcher;
        _lifetime = lifetime;
        _tree = tree;
        _signals = signals;
        _time = time ?? TimeProvider.System;
        _noted = noted;
    }

    /// <summary>
    /// What a running session needs, as its agent's hooks said and nobody has
    /// answered since (<c>waiting</c> or <c>approval</c>), whether it is
    /// instead waiting on background work of its own, which does not need
    /// you, and the agent's own id for its conversation; null and false where
    /// nothing was said.
    /// </summary>
    public (string? Needs, bool Background, string? AgentSession) SignalOf(HostedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var signal = _signals is null ? null : SessionSignals.Read(_signals, session.Id);
        var status = SessionSignals.Status(signal, session.LastInputAt, session.HasEnded, _time.GetUtcNow());

        if (signal is { State: "background" })
        {
            Note(session.Id, signal.At, status);
        }

        return (status is "background" ? null : status, status is "background", signal?.AgentSession);
    }

    /// <summary>
    /// Counts a background signal once when it is first read, and once more
    /// if it goes on to read as waiting for you. Every open page asks, so the
    /// signal's own time is what makes each count once.
    /// </summary>
    private void Note(string id, DateTimeOffset at, string? status)
    {
        if (_noted is null)
        {
            return;
        }

        string? line = null;

        lock (_gate)
        {
            _backgrounds.TryGetValue(id, out var seen);

            if (seen.At != at)
            {
                _backgroundCount++;
                seen = (at, false);
                line = $"Session {id} is waiting on background work of its own, not on you "
                    + $"({_backgroundCount} so far, {_escalatedCount} of them silent long enough to read as waiting for you).";
            }

            if (status == "waiting" && !seen.Escalated)
            {
                _escalatedCount++;
                seen = (at, true);
                line = $"Session {id}'s background work has gone quiet past its time, so it now reads as waiting for you "
                    + $"({_escalatedCount} of {_backgroundCount} so far).";
            }

            _backgrounds[id] = seen;
        }

        if (line is not null)
        {
            _noted(line);
        }
    }

    /// <summary>
    /// The agent's conversation id for a session this daemon does not host,
    /// left by one an earlier daemon did, or null. A daemon that stops ends
    /// its sessions, and this is how the page can still offer to resume one.
    /// </summary>
    public string? LostAgentSession(string id) =>
        _signals is null || Find(id) is not null ? null : SessionSignals.Read(_signals, id)?.AgentSession;

    /// <summary>
    /// The sessions an earlier daemon hosted that left a conversation to
    /// resume, newest first, as the id the page knew them by.
    /// </summary>
    /// <remarks>
    /// A session that ended before its first turn left no signal, and so is
    /// not here: there was nothing to resume.
    /// </remarks>
    public IReadOnlyList<(string Id, SessionSignal Signal)> Lost() =>
        _signals is null
            ? []
            : SessionSignals.All(_signals)
                .Where(entry => entry.Signal.AgentSession is { Length: > 0 } && Find(entry.Id) is null)
                .ToList();

    /// <summary>Stops offering a lost session, once it is resumed or somebody says no.</summary>
    /// <returns>False for an id this daemon hosts, which is not lost and is ended with DELETE.</returns>
    public bool ForgetLost(string id)
    {
        if (_signals is null || Find(id) is not null)
        {
            return false;
        }

        SessionSignals.Remove(_signals, id);

        return true;
    }

    /// <summary>The running session carrying that conversation, by its id here, or null.</summary>
    /// <remarks>
    /// A resume is matched by the conversation it was started for as well as
    /// by what its agent has said, so a second Resume pressed before the
    /// first turn goes to the terminal already opening rather than a third.
    /// </remarks>
    public string? Hosting(string agentSession) =>
        string.IsNullOrEmpty(agentSession)
            ? null
            : All.FirstOrDefault(session => !session.HasEnded
                && (SignalOf(session).AgentSession == agentSession
                    || LaunchOf(session.Id)?.Resuming == agentSession))?.Id;

    /// <summary>
    /// The resident memory of each running session's processes, the launch
    /// and the agent it started, by session id; a session that could not be
    /// measured is left out.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, long>> MemoryAsync(CancellationToken ct = default)
    {
        var running = All.Where(one => !one.HasEnded && one.ProcessId > 0).ToList();

        if (_tree is null || running.Count == 0)
        {
            return new Dictionary<string, long>();
        }

        var table = await _tree.SnapshotAsync(ct).ConfigureAwait(false);
        var measured = ProcessTrees.Resident(table, running.Select(one => one.ProcessId));

        return running
            .Where(one => measured.ContainsKey(one.ProcessId))
            .ToDictionary(one => one.Id, one => measured[one.ProcessId], StringComparer.Ordinal);
    }

    /// <summary>
    /// Raised when a session starts, ends or is removed, for whatever says
    /// how many are running. It may be raised on any thread.
    /// </summary>
    public event Action? Changed;

    /// <summary>How many have not ended.</summary>
    public int Running
    {
        get
        {
            lock (_gate)
            {
                return _sessions.Count(one => !one.HasEnded);
            }
        }
    }

    /// <summary>Every session, oldest first, running or ended.</summary>
    public IReadOnlyList<HostedSession> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _sessions];
            }
        }
    }

    /// <summary>The session with this id, or null.</summary>
    public HostedSession? Find(string id)
    {
        lock (_gate)
        {
            return _sessions.Find(one => string.Equals(one.Id, id, StringComparison.Ordinal));
        }
    }

    /// <summary>Starts a launch in a new pseudo-terminal.</summary>
    public async Task<OperationResult<HostedSession>> StartAsync(HostedLaunch launch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(launch);

        if (_launcher() is not ({ Length: > 0 } command, var prefix))
        {
            return OperationResult<HostedSession>.Fail(
                "This launcher cannot say how it was started, so it cannot start a session.");
        }

        var arguments = Arguments(prefix, launch);

        if (arguments.Failed)
        {
            return OperationResult<HostedSession>.Fail(arguments.Error!);
        }

        var title = launch.Resuming is { Length: > 0 } && launch.Agent is { Length: > 0 } resumed
                ? $"{launch.Project} ({resumed}), resumed"
            : launch.Command is { Length: > 0 } asked ? $"loadout {asked}"
            : launch.Agent is { Length: > 0 } agent ? $"{launch.Project} ({agent})"
            : launch.Project;

        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();

        // The session's own id, inherited by the launch and the agent it
        // starts, so the agent's hooks know which signal file is theirs.
        var environment = _signals is null
            ? null
            : new Dictionary<string, string>(StringComparer.Ordinal) { [SessionSignals.Variable] = id };

        var started = await HostedSession.StartAsync(
            _terminals(),
            new ProcessRequest(command, arguments.Value!, Environment: environment),
            id,
            title,
            Math.Clamp(launch.Columns, 20, 500),
            Math.Clamp(launch.Rows, 5, 200),
            lifetime: _lifetime,
            ct: ct).ConfigureAwait(false);

        if (started.Failed)
        {
            return started;
        }

        // Checked after starting rather than before, because the daemon can
        // stop while a start is under way, and a session added after the
        // others were ended would outlive them.
        lock (_gate)
        {
            if (!_disposed)
            {
                _sessions.Add(started.Value!);
                _launches[started.Value!.Id] = launch;
                Watch(started.Value!);

                return started;
            }
        }

        await started.Value!.DisposeAsync().ConfigureAwait(false);

        return OperationResult<HostedSession>.Fail("The daemon is stopping.");
    }

    /// <summary>Ends a session if it is still running and forgets it.</summary>
    /// <returns>False where there was no session with that id.</returns>
    public async Task<bool> RemoveAsync(string id)
    {
        HostedSession? found;

        lock (_gate)
        {
            found = _sessions.Find(one => string.Equals(one.Id, id, StringComparison.Ordinal));

            if (found is not null)
            {
                _sessions.Remove(found);
                _launches.Remove(found.Id);
            }
        }

        if (found is null)
        {
            return false;
        }

        await found.DisposeAsync().ConfigureAwait(false);

        if (_signals is not null)
        {
            SessionSignals.Remove(_signals, found.Id);
        }

        Changed?.Invoke();

        return true;
    }

    /// <summary>Says so now, and again when the session ends by itself.</summary>
    private void Watch(HostedSession session)
    {
        Changed?.Invoke();

        _ = session.Completion.ContinueWith(
            _ => Changed?.Invoke(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// The command line after the launcher's own prefix, or why there is none.
    /// </summary>
    /// <remarks>
    /// Every option is written joined to its value, because the parser reads a
    /// value starting with a dash as an option and refuses it. A project
    /// cannot be written that way, being positional, so one starting with a
    /// dash is refused here rather than by a session that opens and dies.
    /// </remarks>
    public static OperationResult<IReadOnlyList<string>> Arguments(IReadOnlyList<string> prefix, HostedLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(launch);

        if (launch.Command is { Length: > 0 } command)
        {
            // Passed as words, never through a shell, so nothing in them is
            // interpreted; a control character is still nothing a form types.
            var words = launch.Words ?? [];

            if (command.Any(char.IsControl) || command.StartsWith('-') || words.Any(word => word.Any(char.IsControl)))
            {
                return OperationResult<IReadOnlyList<string>>.Fail("A session's command cannot hold control characters.");
            }

            return OperationResult<IReadOnlyList<string>>.Ok([.. prefix, .. command.Split(' ', StringSplitOptions.RemoveEmptyEntries), .. words]);
        }

        var project = launch.Project?.Trim() ?? string.Empty;

        if (project.Length == 0)
        {
            return OperationResult<IReadOnlyList<string>>.Fail("A session needs a project.");
        }

        if (project.StartsWith('-'))
        {
            return OperationResult<IReadOnlyList<string>>.Fail($"There is no project called '{project}'.");
        }

        if (new[] { project, launch.Agent, launch.Task, launch.TaskId, launch.Mode, launch.Profile, launch.Worktree }
            .Any(value => value?.Any(char.IsControl) == true))
        {
            return OperationResult<IReadOnlyList<string>>.Fail("A session's settings cannot hold control characters.");
        }

        // 'launch' named rather than left implied, as 'loadout <project>'
        // would: a project called like a command would otherwise run it.
        var arguments = new List<string>(prefix) { "launch", project };

        Add(arguments, "--task-id", launch.TaskId);
        Add(arguments, "--task", launch.Task);
        Add(arguments, "--agent", launch.Agent);
        Add(arguments, "--mode", launch.Mode);
        Add(arguments, "--profile", launch.Profile);
        Add(arguments, "--worktree", launch.Worktree);

        if (launch.Offline)
        {
            arguments.Add("--offline");
        }

        if (launch.NoSync)
        {
            arguments.Add("--no-sync");
        }

        if (launch.Handoff)
        {
            arguments.Add("--handoff");
        }

        return OperationResult<IReadOnlyList<string>>.Ok(arguments);
    }

    /// <summary>
    /// The command a launch runs, as somebody would type it at a terminal,
    /// or why there is none.
    /// </summary>
    /// <remarks>
    /// Built from <see cref="Arguments"/>, so what the page shows is what the
    /// session runs: a value with a space is quoted, and nothing else changes.
    /// </remarks>
    public static OperationResult<string> CommandLine(HostedLaunch launch)
    {
        var arguments = Arguments([], launch);

        return arguments.Failed
            ? OperationResult<string>.Fail(arguments.Error!)
            : OperationResult<string>.Ok("loadout " + string.Join(' ', arguments.Value!.Select(Quoted)));
    }

    private static string Quoted(string argument)
    {
        var at = argument.IndexOf('=', StringComparison.Ordinal);

        if (!argument.Contains(' ', StringComparison.Ordinal) && !argument.Contains('"', StringComparison.Ordinal))
        {
            return argument;
        }

        var value = (at > 0 ? argument[(at + 1)..] : argument).Replace("\"", "\\\"", StringComparison.Ordinal);

        return at > 0 ? $"{argument[..(at + 1)]}\"{value}\"" : $"\"{value}\"";
    }

    /// <summary>What a session was launched as, for the page to show beside it.</summary>
    public HostedLaunch? LaunchOf(string id)
    {
        lock (_gate)
        {
            return _launches.GetValueOrDefault(id);
        }
    }

    private static void Add(List<string> arguments, string option, string? value)
    {
        if (value?.Trim() is { Length: > 0 } given)
        {
            arguments.Add($"{option}={given}");
        }
    }

    /// <summary>Ends every session, which is what happens to them when the daemon stops.</summary>
    public async ValueTask DisposeAsync()
    {
        HostedSession[] ending;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ending = [.. _sessions];
            _sessions.Clear();
        }

        foreach (var session in ending)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }
}
