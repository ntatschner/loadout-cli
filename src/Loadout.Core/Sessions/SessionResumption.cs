using Loadout.Core.Git;
using Loadout.Core.Projects;
using Loadout.Models;
using Loadout.Models.Agents;
using Loadout.Models.Results;

namespace Loadout.Core.Sessions;

/// <summary>Whether a conversation is running now, and where.</summary>
public enum RunningKind
{
    /// <summary>Not running, as far as anything here can tell.</summary>
    None,

    /// <summary>In a terminal this daemon hosts, which can be gone to rather than started again.</summary>
    Hosted,

    /// <summary>
    /// Somewhere else on this machine: its launch is still running, or, for a
    /// conversation with no recorded launch, its transcript was written to a
    /// moment ago.
    /// </summary>
    Elsewhere,
}

/// <summary>Whether a conversation is running, with the hosted terminal when it is one.</summary>
/// <param name="Kind">What the check found.</param>
/// <param name="StudioSession">The daemon's id for the terminal, when <see cref="RunningKind.Hosted"/>.</param>
public sealed record SessionRunning(RunningKind Kind, string? StudioSession = null)
{
    /// <summary>Not running.</summary>
    public static readonly SessionRunning None = new(RunningKind.None);

    /// <summary>As the CLI's JSON and the daemon spell it: none, elsewhere or hosted:&lt;id&gt;.</summary>
    public string Wire => Kind switch
    {
        RunningKind.Hosted => $"hosted:{StudioSession}",
        RunningKind.Elsewhere => "elsewhere",
        _ => "none",
    };
}

/// <summary>A conversation, with what it was launched for and whether it is running.</summary>
/// <param name="Session">The conversation, as its agent's history has it.</param>
/// <param name="LaunchedFor">The launch it is filed under, or null when there is no link.</param>
/// <param name="Running">Whether it is running now.</param>
public sealed record ResumableSession(
    AgentSession Session,
    LaunchRecord? LaunchedFor,
    SessionRunning Running);

/// <summary>What somebody resuming asked for in place of what would be carried over.</summary>
/// <remarks>Null everywhere means carry everything over.</remarks>
/// <param name="Task">What it is for now, in place of the carried task.</param>
/// <param name="TaskId">A recorded task it is for now; its title is the task unless one is given.</param>
/// <param name="Mode">The posture to run in.</param>
/// <param name="Profile">The context profile.</param>
/// <param name="Worktree">The worktree to reopen in.</param>
/// <param name="Fresh">
/// Carry nothing over from its launch: only what is given here applies. How
/// "no task" or "the main checkout" is asked for, since an option cannot be
/// given an empty value.
/// </param>
public sealed record ResumeOverrides(
    string? Task = null,
    string? TaskId = null,
    string? Mode = null,
    string? Profile = null,
    string? Worktree = null,
    bool Fresh = false)
{
    /// <summary>Whether anything was asked for that changes what the session is for.</summary>
    /// <remarks>
    /// A profile or worktree does not count: naming either leaves the task
    /// and mode as they were, and the carried task and mode are still worth
    /// saying.
    /// </remarks>
    public bool ChangesPurpose => Fresh || Task is not null || TaskId is not null || Mode is not null;
}

/// <summary>What a resume will launch with, and what to tell the person about it.</summary>
/// <param name="ProjectSlug">The project it reopens in.</param>
/// <param name="Agent">The agent that ran it.</param>
/// <param name="SessionId">The agent's own id for it.</param>
/// <param name="Task">What it is for now.</param>
/// <param name="TaskId">The recorded task it is for now.</param>
/// <param name="Mode">The posture it runs in.</param>
/// <param name="Profile">The context profile.</param>
/// <param name="Worktree">The working tree, or null for the main one.</param>
/// <param name="Carried">What was carried over from its launch, as somebody would say each part.</param>
/// <param name="Notices">Anything the person should be told before it starts.</param>
public sealed record ResumeValues(
    string ProjectSlug,
    string Agent,
    string SessionId,
    string? Task,
    string? TaskId,
    string? Mode,
    string? Profile,
    string? Worktree,
    IReadOnlyList<string> Carried,
    IReadOnlyList<string> Notices);

/// <summary>
/// Choosing a conversation to resume, what carries over into it, and whether
/// it is already running. The one place the CLI and the daemon ask.
/// </summary>
public interface ISessionResumption
{
    /// <summary>Conversations, newest first, each with its launch and whether it is running.</summary>
    /// <param name="query">Which conversations.</param>
    /// <param name="hosted">
    /// The daemon's terminal running a conversation, by agent and id, or null
    /// for none. Only the daemon knows; anywhere else it is left out.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<OperationResult<IReadOnlyList<ResumableSession>>> ListAsync(
        SessionQuery query,
        Func<AgentSession, string?>? hosted = null,
        CancellationToken ct = default);

    /// <summary>The one conversation whose id starts with what was asked for.</summary>
    Task<OperationResult<ResumableSession>> FindAsync(
        SessionQuery query,
        string wanted,
        Func<AgentSession, string?>? hosted = null,
        CancellationToken ct = default);

    /// <summary>
    /// What resuming a conversation launches with. Fails for one from a folder
    /// that is not a registered project.
    /// </summary>
    Task<OperationResult<ResumeValues>> PlanAsync(
        ResumableSession chosen,
        ResumeOverrides overrides,
        CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class SessionResumption : ISessionResumption
{
    private readonly ISessionHistoryService _history;
    private readonly ILaunchLedger _ledger;
    private readonly ISessionRegistry _running;
    private readonly IProjectService _projects;
    private readonly IGitManager _git;
    private readonly TimeProvider _time;

    public SessionResumption(
        ISessionHistoryService history,
        ILaunchLedger ledger,
        ISessionRegistry running,
        IProjectService projects,
        IGitManager git,
        TimeProvider time)
    {
        _history = history;
        _ledger = ledger;
        _running = running;
        _projects = projects;
        _git = git;
        _time = time;
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<ResumableSession>>> ListAsync(
        SessionQuery query,
        Func<AgentSession, string?>? hosted = null,
        CancellationToken ct = default)
    {
        var listed = await _history.ListAsync(query, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<IReadOnlyList<ResumableSession>>.Fail(listed.Error!, listed.ExitCode);
        }

        return OperationResult<IReadOnlyList<ResumableSession>>.Ok(
            await DecorateAsync(listed.Value!, hosted, ct).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<OperationResult<ResumableSession>> FindAsync(
        SessionQuery query,
        string wanted,
        Func<AgentSession, string?>? hosted = null,
        CancellationToken ct = default)
    {
        var listed = await _history.ListAsync(query, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<ResumableSession>.Fail(listed.Error!, listed.ExitCode);
        }

        var match = Resumption.Match(listed.Value!, wanted);

        if (match.Failed)
        {
            return OperationResult<ResumableSession>.Fail(match.Error!, match.ExitCode);
        }

        var decorated = await DecorateAsync([match.Value!], hosted, ct).ConfigureAwait(false);

        return OperationResult<ResumableSession>.Ok(decorated[0]);
    }

    /// <inheritdoc />
    public async Task<OperationResult<ResumeValues>> PlanAsync(
        ResumableSession chosen,
        ResumeOverrides overrides,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        ArgumentNullException.ThrowIfNull(overrides);

        var session = chosen.Session;

        if (session.ProjectSlug is not { Length: > 0 } slug)
        {
            // Resuming goes through the project pipeline, so a session from an
            // unregistered directory has nothing to hang off. Say what would
            // fix it rather than just refusing.
            return OperationResult<ResumeValues>.Fail(
                $"That session ran in {session.Directory}, which is not a registered project. "
                + $"Register it with 'loadout project add \"{session.Directory}\"' and try again.",
                ExitCode.ProjectNotFound);
        }

        var values = Resumption.Values(chosen, overrides);
        var notices = new List<string>();

        if (overrides.Worktree is null && values.Worktree is { Length: > 0 } worktree
            && !await WorktreeExistsAsync(slug, worktree, ct).ConfigureAwait(false))
        {
            // Removed since. Failing inside the launch with "no worktree named"
            // would read as resume being broken; the conversation itself is
            // fine and can carry on in the main tree.
            notices.Add($"Its worktree '{worktree}' is no longer there, so it reopens in the main tree.");
            values = values with { Worktree = null, Carried = values.Carried.Where(c => !c.StartsWith("worktree ", StringComparison.Ordinal)).ToList() };
        }

        return OperationResult<ResumeValues>.Ok(values with { Notices = notices });
    }

    private async Task<bool> WorktreeExistsAsync(string slug, string worktree, CancellationToken ct)
    {
        var project = await _projects.ResolveAsync(slug, ct).ConfigureAwait(false);

        if (project.Failed || project.Value!.LocalPath is not { Length: > 0 } path)
        {
            // Not this check's to refuse: the launch says what is wrong with
            // the project in its own words.
            return true;
        }

        var listed = await _git.ListWorktreesAsync(path, ct).ConfigureAwait(false);

        return listed.Failed || Resumption.HasWorktree(listed.Value!, worktree);
    }

    private async Task<IReadOnlyList<ResumableSession>> DecorateAsync(
        IReadOnlyList<AgentSession> sessions,
        Func<AgentSession, string?>? hosted,
        CancellationToken ct)
    {
        // A ledger that cannot be read links nothing, which carries nothing
        // over: the same as a conversation from before links, and no reason
        // to refuse to list or resume anything.
        var links = await _ledger.ReadLinksAsync(ct).ConfigureAwait(false);
        var launches = await _ledger.ReadAsync(DateTimeOffset.UnixEpoch, ct).ConfigureAwait(false);
        var running = await _running.ListAsync(ct).ConfigureAwait(false);

        var byLink = Resumption.LaunchesByConversation(
            links.Succeeded ? links.Value! : [],
            launches.Succeeded ? launches.Value! : []);

        var live = running.Select(r => r.LaunchId).ToHashSet(StringComparer.Ordinal);
        var now = _time.GetUtcNow();

        return sessions
            .Select(session =>
            {
                byLink.TryGetValue(Resumption.Key(session.Agent, session.SessionId), out var launch);

                return new ResumableSession(
                    session,
                    launch,
                    Resumption.Running(session, launch, live, hosted?.Invoke(session), now));
            })
            .ToList();
    }
}

/// <summary>The parts of resuming that need nothing but their arguments.</summary>
public static class Resumption
{
    /// <summary>
    /// How recently an unlinked conversation's transcript must have been
    /// written for it to count as running elsewhere.
    /// </summary>
    /// <remarks>
    /// The same quiet time after which a running session counts as idle. It
    /// is only ever a warning, so a conversation closed a minute ago that is
    /// flagged costs one confirmation, not a refusal.
    /// </remarks>
    public static readonly TimeSpan RecentlyWritten = SessionMonitor.IdleAfter;

    /// <summary>
    /// The one session whose id starts with what was asked for.
    /// </summary>
    /// <remarks>
    /// Prefixes are accepted because nobody types a whole UUID. Separated out
    /// so that "no such session" and "more than one" can be said, which they
    /// could not while this returned the same null the picker returns when
    /// somebody backs out of it.
    /// </remarks>
    public static OperationResult<AgentSession> Match(
        IReadOnlyList<AgentSession> sessions,
        string wanted)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var matches = sessions
            .Where(s => s.SessionId.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches switch
        {
            [var only] => OperationResult<AgentSession>.Ok(only),

            [] => OperationResult<AgentSession>.Fail(
                $"No recorded session starts with '{wanted}'. "
                + "Run 'loadout sessions' to see what there is.",
                ExitCode.ProjectNotFound),

            _ => OperationResult<AgentSession>.Fail(
                $"{matches.Count} sessions start with '{wanted}'. Give more of the id.",
                ExitCode.InvalidArguments),
        };
    }

    /// <summary>
    /// The launch each conversation is filed under: the newest link's, so a
    /// conversation resumed with a new task carries that task next time.
    /// </summary>
    /// <remarks>
    /// A link whose launch is not in the ledger is dropped, never matched to
    /// some other launch: knowing nothing is better than a confident guess.
    /// </remarks>
    public static IReadOnlyDictionary<string, LaunchRecord> LaunchesByConversation(
        IReadOnlyList<SessionLink> links,
        IReadOnlyList<LaunchRecord> launches)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(launches);

        var byId = new Dictionary<string, LaunchRecord>(StringComparer.Ordinal);

        foreach (var launch in launches)
        {
            byId[launch.Id] = launch;
        }

        var found = new Dictionary<string, LaunchRecord>(StringComparer.Ordinal);

        foreach (var link in links.OrderBy(link => link.LinkedAt))
        {
            if (byId.TryGetValue(link.LaunchId, out var launch))
            {
                found[Key(link.Agent, link.SessionId)] = launch;
            }
        }

        return found;
    }

    /// <summary>A conversation's key: two agents' ids live in different namespaces.</summary>
    public static string Key(string agent, string sessionId) =>
        $"{agent.ToLowerInvariant()}\n{sessionId}";

    /// <summary>Whether a conversation is running, and where.</summary>
    /// <param name="session">The conversation.</param>
    /// <param name="launch">Its launch, when it has a link.</param>
    /// <param name="live">The ids of launches still running on this machine.</param>
    /// <param name="hosted">The daemon's terminal running it, or null.</param>
    /// <param name="now">The current time.</param>
    public static SessionRunning Running(
        AgentSession session,
        LaunchRecord? launch,
        IReadOnlySet<string> live,
        string? hosted,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(live);

        if (hosted is { Length: > 0 })
        {
            return new SessionRunning(RunningKind.Hosted, hosted);
        }

        if (launch is not null)
        {
            // Known: its launch is running or it is not. A transcript written
            // a minute ago by a launch that has since ended is a conversation
            // somebody has just closed, not one still going.
            return live.Contains(launch.Id) && launch.EndedAt is null
                ? new SessionRunning(RunningKind.Elsewhere)
                : SessionRunning.None;
        }

        return now - session.LastActive < RecentlyWritten
            ? new SessionRunning(RunningKind.Elsewhere)
            : SessionRunning.None;
    }

    /// <summary>What a resume launches with, before checking the worktree is still there.</summary>
    public static ResumeValues Values(ResumableSession chosen, ResumeOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        ArgumentNullException.ThrowIfNull(overrides);

        var carried = overrides.Fresh ? null : chosen.LaunchedFor;

        // A task named by id replaces the carried one rather than sitting
        // beside it: the carried words would otherwise choose the specialists
        // for a session now said to be about something else.
        var task = overrides.Task ?? (overrides.TaskId is null ? carried?.Task : null);
        var taskId = overrides.TaskId ?? (overrides.Task is null ? carried?.TaskId : null);
        var mode = overrides.Mode ?? carried?.Mode;
        var profile = overrides.Profile ?? carried?.Profile;
        var worktree = overrides.Worktree ?? carried?.Worktree;

        // What is said is what came from the launch and was kept, so the
        // person is not told a task carried over when they replaced it.
        var said = new List<string>();

        if (carried is not null)
        {
            if (task is { Length: > 0 } && overrides.Task is null && overrides.TaskId is null)
            {
                said.Add($"task '{task}'");
            }

            if (mode is { Length: > 0 } && overrides.Mode is null)
            {
                said.Add($"mode {mode}");
            }

            if (profile is { Length: > 0 } && overrides.Profile is null)
            {
                said.Add($"profile {profile}");
            }

            if (worktree is { Length: > 0 } && overrides.Worktree is null)
            {
                said.Add($"worktree {worktree}");
            }
        }

        return new ResumeValues(
            chosen.Session.ProjectSlug ?? string.Empty,
            chosen.Session.Agent,
            chosen.Session.SessionId,
            task,
            taskId,
            mode,
            profile,
            // An empty override means the main tree, as the launch sheet says it.
            worktree is { Length: > 0 } ? worktree : null,
            said,
            []);
    }

    /// <summary>Whether a worktree by that name is still there, named as the launcher matches it.</summary>
    public static bool HasWorktree(IReadOnlyList<GitWorktree> worktrees, string name)
    {
        ArgumentNullException.ThrowIfNull(worktrees);

        return worktrees.Any(w =>
            string.Equals(w.Branch, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(w.Path), name, StringComparison.OrdinalIgnoreCase));
    }
}
