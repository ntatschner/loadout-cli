using Loadout.Core.Delivery;
using Loadout.Core.Git;
using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Core.Teams;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Tasks;
using Loadout.Models.Teams;
using Loadout.Platform.Abstractions;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>
/// Carries out what a team's queue decides: preparing each run's worktree,
/// starting it, reading how it went, and what happens after.
/// </summary>
/// <remarks>
/// <para>
/// The deciding is <see cref="QueueRules"/>'s and the reading is
/// <see cref="QueueSettling"/>'s; this is the part that touches git, processes
/// and the task list. 'team queue run-next' and the daemon both come through
/// here, so a run started by hand and one started overnight are prepared,
/// recorded and landed the same way.
/// </para>
/// <para>
/// Every queued run works in a worktree of its own, on a branch of its own,
/// made from the repository's default branch. Unattended runs in the person's
/// own checkout swap its branch and its build output under them; this keeps
/// the two apart.
/// </para>
/// </remarks>
public sealed class TeamQueueDriver
{
    /// <summary>How often a draft pull request is looked at to see whether it has been merged.</summary>
    internal static readonly TimeSpan MergeEvery = TimeSpan.FromMinutes(15);

    private readonly IQueueService _queues;
    private readonly IScheduleService _schedules;
    private readonly ITaskService _tasks;
    private readonly IRunJournal _journal;
    private readonly IProjectService _projects;
    private readonly IGitManager _git;
    private readonly IProcessLauncher _launcher;
    private readonly IProcessInspector _processes;
    private readonly IRepositoryHost _host;
    private readonly IPullRequestReader _pulls;
    private readonly IWorkspaceManager _workspace;
    private readonly IPlatformPaths _paths;
    private readonly TimeProvider _time;

    public TeamQueueDriver(
        IQueueService queues,
        IScheduleService schedules,
        ITaskService tasks,
        IRunJournal journal,
        IProjectService projects,
        IGitManager git,
        IProcessLauncher launcher,
        IProcessInspector processes,
        IRepositoryHost host,
        IPullRequestReader pulls,
        IWorkspaceManager workspace,
        IPlatformPaths paths,
        TimeProvider time)
    {
        _queues = queues;
        _schedules = schedules;
        _tasks = tasks;
        _journal = journal;
        _projects = projects;
        _git = git;
        _launcher = launcher;
        _processes = processes;
        _host = host;
        _pulls = pulls;
        _workspace = workspace;
        _paths = paths;
        _time = time;
    }

    /// <summary>
    /// Makes a worktree for an item, on a new branch from the repository's default branch.
    /// </summary>
    public async Task<OperationResult<(string Path, string Branch)>> PrepareAsync(
        QueueItem item,
        DateTimeOffset started,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);

        var project = await _projects.ResolveAsync(item.Project, ct).ConfigureAwait(false);

        if (project.Failed || project.Value!.LocalPath is not { Length: > 0 } repository)
        {
            return OperationResult<(string, string)>.Fail(
                project.Error ?? $"{item.Project} has no checkout on this machine.", ExitCode.RepositoryUnavailable);
        }

        // The remote's default branch where there is one, so a queued run
        // starts from what was agreed rather than from whatever the person
        // happened to have checked out.
        string? from = null;

        foreach (var candidate in (string[])["refs/remotes/origin/HEAD", "HEAD"])
        {
            if (await _git.ResolveAsync(repository, candidate, ct).ConfigureAwait(false) is { Succeeded: true } resolved)
            {
                from = resolved.Value;

                break;
            }
        }

        var branch = QueueSettling.Branch(item, started);
        var path = Path.Combine(
            _paths.Paths.State, "worktrees", project.Value.Entry.Slug, branch.Replace('/', '-'));

        var made = await _git.AddWorktreeAsync(repository, path, branch, from, ct).ConfigureAwait(false);

        return made.Succeeded
            ? OperationResult<(string, string)>.Ok((made.Value!.Path, branch))
            : OperationResult<(string, string)>.Fail(
                $"A worktree for '{item.Task}' could not be made: {made.Error}", ExitCode.RepositoryUnavailable);
    }

    /// <summary>The words 'team run' is given for a queued item, after the command's own name.</summary>
    public static IReadOnlyList<string> RunArguments(
        string team,
        QueueItem item,
        TaskItem? task,
        string worktree,
        string? autonomy,
        bool unattended)
    {
        ArgumentNullException.ThrowIfNull(item);

        return
        [
            team,
            task is null ? item.Task : TaskRuns.Goal(task),
            "--project", item.Project,
            "--task", item.Task,
            "--work-in", worktree,
            .. item.Follows is { Length: > 0 } follows ? (string[])["--follows", follows] : [],
            .. autonomy is { Length: > 0 } given ? (string[])["--autonomy", given] : [],
            .. unattended ? (string[])["--non-interactive"] : [],
        ];
    }

    /// <summary>
    /// Takes the next item off a team's queue, under its lock, and prepares it, recording the run as started.
    /// </summary>
    /// <returns>The item and its run record, or the decision when nothing starts.</returns>
    public async Task<(QueueItem? Item, QueueRun? Run, QueueDecision? Decision, string? Error)> TakeAsync(
        string team,
        QueueWindow? window,
        string by,
        CancellationToken ct)
    {
        var loaded = await _queues.LoadAsync(team, ct).ConfigureAwait(false);

        if (loaded.Failed)
        {
            return (null, null, null, loaded.Error);
        }

        var lists = await TeamQueueing.ListsAsync(_tasks, loaded.Value!, ct).ConfigureAwait(false);
        var started = _time.GetUtcNow();
        QueueDecision? decision = null;
        QueueRun? run = null;

        var taken = await _queues.UpdateAsync(
            team,
            queue =>
            {
                decision = QueueRules.Next(queue, lists, window);

                if (decision.Verdict != QueueVerdict.Start)
                {
                    return decision.Reason;
                }

                queue.Items.Remove(decision.Item!);
                run = new QueueRun
                {
                    Task = decision.Item!.Task,
                    Project = decision.Item.Project,
                    PrMode = decision.Item.PrMode,
                    StartedUtc = started,
                    StartedBy = by,
                };
                queue.Runs.Add(run);

                return null;
            },
            ct).ConfigureAwait(false);

        if (taken.Failed)
        {
            return (null, null, decision, decision is { Verdict: not QueueVerdict.Start } ? null : taken.Error);
        }

        var item = decision!.Item!;
        var prepared = await PrepareAsync(item, started, ct).ConfigureAwait(false);

        if (prepared.Failed)
        {
            await EndAsync(team, run!, new QueueStanding(QueueRunState.Failed, string.Empty, prepared.Error!, 0m), ct)
                .ConfigureAwait(false);

            return (null, null, decision, prepared.Error);
        }

        await RecordAsync(team, run!, one =>
        {
            one.Worktree = prepared.Value.Path;
            one.Branch = prepared.Value.Branch;
        }, ct).ConfigureAwait(false);

        run!.Worktree = prepared.Value.Path;
        run.Branch = prepared.Value.Branch;

        return (item, run, decision, null);
    }

    /// <summary>
    /// Starts a taken item as a process of its own, as the daemon does, so it
    /// does not hold up the next one when it stops to ask something.
    /// </summary>
    public async Task<string?> StartInBackgroundAsync(
        string team,
        QueueItem item,
        QueueRun run,
        string? autonomy,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(run);

        // Named as it was invoked: on a development build that is dotnet with
        // the launcher's dll, and naming dotnet alone runs nothing.
        if (Loadout.Core.Agents.LauncherInvocation.Parts() is not { } launcher)
        {
            await EndAsync(team, run, new QueueStanding(
                QueueRunState.Failed, string.Empty, "the launcher could not name itself to start the run", 0m), ct)
                .ConfigureAwait(false);

            return "The launcher could not name itself to start the run.";
        }

        var tasks = await _tasks.ListAsync(item.Project, ct).ConfigureAwait(false);
        var task = tasks.Succeeded ? TaskRuns.Find(item.Task, tasks.Value!) : null;

        var started = _launcher.StartBackground(new ProcessRequest(
            launcher.Command,
            [.. launcher.Prefix, "team", "run", .. RunArguments(team, item, task, run.Worktree, autonomy, unattended: true)],
            WorkingDirectory: run.Worktree));

        if (started.Failed)
        {
            await EndAsync(team, run, new QueueStanding(
                QueueRunState.Failed, string.Empty, $"the run could not be started: {started.Error}", 0m), ct)
                .ConfigureAwait(false);

            return started.Error;
        }

        await RecordAsync(team, run, one =>
        {
            one.Pid = started.Value!.Pid;
            one.PidStarted = started.Value.StartedAt;
        }, ct).ConfigureAwait(false);

        return null;
    }

    /// <summary>
    /// Reads how every run a queue still has as going stands now, and lands
    /// the ones that have ended.
    /// </summary>
    /// <returns>The runs that ended in this pass.</returns>
    public async Task<IReadOnlyList<QueueRun>> SettleAsync(string team, CancellationToken ct)
    {
        var loaded = await _queues.LoadAsync(team, ct).ConfigureAwait(false);

        if (loaded.Failed)
        {
            return [];
        }

        var going = loaded.Value!.Runs
            .Where(one => one.State is QueueRunState.Running or QueueRunState.Held || !one.Landed)
            .ToList();

        if (going.Count == 0)
        {
            return [];
        }

        var summaries = _journal.List(50)
            .Select(_journal.Summarise)
            .Where(one => one.Succeeded)
            .Select(one => one.Value!)
            .ToList();

        var ended = new List<QueueRun>();

        foreach (var run in going)
        {
            if (run.State is QueueRunState.Running or QueueRunState.Held)
            {
                bool? alive = run.Pid > 0 && run.PidStarted is { } since ? _processes.IsRunning(run.Pid, since) : null;
                var standing = QueueSettling.Standing(run, QueueSettling.Find(run, summaries), alive, _time.GetUtcNow());

                if (standing.State is QueueRunState.Running or QueueRunState.Held)
                {
                    if (standing.State != run.State || standing.RunId != run.RunId || standing.CostUsd != run.CostUsd)
                    {
                        await RecordAsync(team, run, one =>
                        {
                            one.State = standing.State;
                            one.RunId = standing.RunId;
                            one.Reason = standing.Reason;
                            one.CostUsd = standing.CostUsd;
                        }, ct).ConfigureAwait(false);
                    }

                    continue;
                }

                await EndAsync(team, run, standing, ct).ConfigureAwait(false);
                ended.Add(run);

                continue;
            }

            // Ended in an earlier pass whose landing did not finish.
            await LandAsync(team, run, ct).ConfigureAwait(false);
        }

        return ended;
    }

    /// <summary>Records how a run ended, then lands it.</summary>
    private async Task EndAsync(string team, QueueRun run, QueueStanding standing, CancellationToken ct)
    {
        run.State = standing.State;
        run.RunId = standing.RunId;
        run.Reason = standing.Reason;
        run.CostUsd = standing.CostUsd;
        run.EndedUtc = _time.GetUtcNow();

        await RecordAsync(team, run, one =>
        {
            one.State = run.State;
            one.RunId = run.RunId;
            one.Reason = run.Reason;
            one.CostUsd = run.CostUsd;
            one.EndedUtc = run.EndedUtc;
        }, ct).ConfigureAwait(false);

        await LandAsync(team, run, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// What happens after a run ends: its pull request when it met its
    /// done-when and the queue may open one, where the task stands, and its
    /// worktree put away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A run that met its done-when leaves its task blocked, not done, with a
    /// line saying where the work is. Done is for when it is merged, which a
    /// person does, or which the queue notices. A failed run leaves it blocked
    /// with the run and the reason, and is not tried again.
    /// </para>
    /// <para>
    /// The branch is always kept. The worktree is removed so a night of runs
    /// does not leave a night of checkouts; git refuses to remove one holding
    /// uncommitted changes, and then it stays where it is and says so.
    /// </para>
    /// </remarks>
    public async Task LandAsync(string team, QueueRun run, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.Landed || run.State is QueueRunState.Running or QueueRunState.Held)
        {
            return;
        }

        var by = run.RunId.Length > 0 ? $"team {team} queue, run {run.RunId}" : $"team {team} queue";
        string reason;

        if (run.State == QueueRunState.Met)
        {
            var choice = await ModeAsync(team, run, ct).ConfigureAwait(false);
            var kind = choice.Mode == PullRequestMode.Draft ? "draft pull request" : "pull request";

            if (choice.Mode == PullRequestMode.None)
            {
                reason = choice.Level == PullRequestModeLevel.Default
                    ? $"Met its done-when on branch {run.Branch}, kept on this machine: nothing asks for a pull request."
                    : $"Met its done-when on branch {run.Branch}, kept on this machine: {choice.From} asks for no pull request.";
            }
            else if (await OpenAsync(team, run, choice.Mode == PullRequestMode.Draft, ct).ConfigureAwait(false) is { } url)
            {
                run.PullRequest = url;
                reason = $"Met its done-when. {char.ToUpperInvariant(kind[0])}{kind[1..]}: {url}";
            }
            else
            {
                reason = $"Met its done-when on branch {run.Branch}, but the {kind} could not be opened: {run.Reason}";
            }
        }
        else
        {
            reason = $"Queued run {(run.RunId.Length > 0 ? run.RunId + " " : string.Empty)}failed: {run.Reason}. Not tried again.";
        }

        await _tasks.DeclareAsync(run.Project, run.Task, TaskState.Blocked, by, ct: ct, reason: reason)
            .ConfigureAwait(false);

        if (run.Worktree.Length > 0 && await RepositoryAsync(run.Project, ct).ConfigureAwait(false) is { } repository)
        {
            var removed = await _git.RemoveWorktreeAsync(repository, run.Worktree, ct).ConfigureAwait(false);

            if (removed.Succeeded)
            {
                run.Worktree = string.Empty;
            }
        }

        run.Landed = true;

        await RecordAsync(team, run, one =>
        {
            one.PullRequest = run.PullRequest;
            one.Worktree = run.Worktree;
            one.Reason = run.Reason;
            one.Landed = true;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The pull request mode a met run follows: its queued task's, else its
    /// queue's, else its project's, else none.
    /// </summary>
    /// <remarks>
    /// A queue or manifest that cannot be read says nothing, so a read that
    /// fails can only ever mean fewer pull requests, never one nobody asked for.
    /// </remarks>
    private async Task<PullRequestModeChoice> ModeAsync(string team, QueueRun run, CancellationToken ct)
    {
        var queue = await _queues.LoadAsync(team, ct).ConfigureAwait(false);
        var project = await _workspace.ReadProjectAsync(run.Project, ct).ConfigureAwait(false);

        return PullRequestModes.Resolve(run.PrMode, queue.Value?.Settings ?? new QueueSettings(), project.Value?.Delivery);
    }

    /// <summary>Pushes a met run's branch and opens a pull request, returning its address or null.</summary>
    private async Task<string?> OpenAsync(string team, QueueRun run, bool draft, CancellationToken ct)
    {
        var place = run.Worktree.Length > 0 && Directory.Exists(run.Worktree)
            ? run.Worktree
            : await RepositoryAsync(run.Project, ct).ConfigureAwait(false);

        if (place is null || run.Branch.Length == 0)
        {
            run.Reason = "there was no branch to push";

            return null;
        }

        if (_host.Unavailable() is { } cannot)
        {
            run.Reason = cannot;

            return null;
        }

        var tasks = await _tasks.ListAsync(run.Project, ct).ConfigureAwait(false);
        var task = tasks.Succeeded ? TaskRuns.Find(run.Task, tasks.Value!) : null;
        var summary = run.RunId.Length > 0 && _journal.Summarise(run.RunId) is { Succeeded: true } read ? read.Value : null;

        if (task is null || summary is null)
        {
            run.Reason = "the task or the run's journal could not be read";

            return null;
        }

        var pushed = await _git.PushWithUpstreamAsync(place, "origin", run.Branch, ct).ConfigureAwait(false);

        if (pushed.Failed)
        {
            run.Reason = $"the branch could not be pushed: {pushed.Error}";

            return null;
        }

        var title = task.Title.Trim().Length > 0 ? task.Title.Trim() : task.Id;

        var opened = await _host.OpenAsync(
            place,
            new PullRequestOpening(run.Branch, title, QueueSettling.Body(task, summary, team), draft),
            ct).ConfigureAwait(false);

        if (opened.Failed)
        {
            run.Reason = opened.Error!;

            return null;
        }

        return opened.Value;
    }

    /// <summary>
    /// Marks done the tasks whose draft pull request has been merged.
    /// </summary>
    /// <remarks>
    /// Read through gh's list of recently merged pull requests, a project at a
    /// time and no more often than <see cref="MergeEvery"/>, so a queue of
    /// open pull requests costs one call a quarter of an hour.
    /// </remarks>
    public async Task<int> MergedAsync(string team, CancellationToken ct)
    {
        var loaded = await _queues.LoadAsync(team, ct).ConfigureAwait(false);

        if (loaded.Failed)
        {
            return 0;
        }

        var now = _time.GetUtcNow();
        var waiting = loaded.Value!.Runs
            .Where(one => one.State == QueueRunState.Met && one.PullRequest.Length > 0
                && (one.MergeChecked is not { } checkedAt || now - checkedAt >= MergeEvery))
            .ToList();

        var done = 0;

        foreach (var group in waiting.GroupBy(one => one.Project, StringComparer.OrdinalIgnoreCase))
        {
            if (await RepositoryAsync(group.Key, ct).ConfigureAwait(false) is not { } repository)
            {
                continue;
            }

            var merged = await _pulls.MergedAsync(repository, 50, ct).ConfigureAwait(false);

            if (merged.Failed)
            {
                continue;
            }

            var tasks = await _tasks.ListAsync(group.Key, ct).ConfigureAwait(false);

            foreach (var run in group)
            {
                var number = Number(run.PullRequest);
                var task = tasks.Succeeded ? TaskRuns.Find(run.Task, tasks.Value!) : null;

                if (number is { } n && merged.Value!.Any(one => one.Number == n) && task is { State: TaskState.Blocked })
                {
                    await _tasks.DeclareAsync(
                        run.Project, run.Task, TaskState.Done, $"team {team} queue",
                        ct: ct, reason: $"Its pull request was merged: {run.PullRequest}").ConfigureAwait(false);

                    done++;
                }

                await RecordAsync(team, run, one => one.MergeChecked = now, ct).ConfigureAwait(false);
            }
        }

        return done;
    }

    /// <summary>A pull request's number from its address, or null.</summary>
    internal static int? Number(string url) =>
        url.TrimEnd('/').Split('/').LastOrDefault() is { } last && int.TryParse(last, out var number) ? number : null;

    /// <summary>
    /// One look at every team's queue: settle what ran, notice merges, and in
    /// an open window start what is next.
    /// </summary>
    /// <param name="firing">Whether the daemon may start anything now; false while it is paused or stopping.</param>
    /// <param name="say">Where a line about what happened goes.</param>
    /// <param name="tell">Where a window's summary goes, as a title and its lines.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task PassAsync(bool firing, Action<string> say, Func<string, string, Task> tell, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(say);
        ArgumentNullException.ThrowIfNull(tell);

        var listed = await _schedules.ListAsync(ct).ConfigureAwait(false);
        var windows = listed.Succeeded
            ? listed.Value!.Where(one => one.IsQueueWindow).ToList()
            : [];

        var teams = _queues.Teams()
            .Concat(windows.Select(one => one.Team.ToLowerInvariant()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var team in teams)
        {
            foreach (var run in await SettleAsync(team, ct).ConfigureAwait(false))
            {
                say($"{team} queue: {run.Task} {run.State.ToString().ToLowerInvariant()}"
                    + (run.Reason.Length > 0 ? $" - {run.Reason}" : string.Empty));
            }

            if (await MergedAsync(team, ct).ConfigureAwait(false) is > 0 and var merged)
            {
                say($"{team} queue: {merged} task(s) done, their pull requests merged");
            }

            var window = windows.FirstOrDefault(one => string.Equals(one.Team, team, StringComparison.OrdinalIgnoreCase));
            var opened = window is null ? null : ScheduleService.Window(window, _time.GetUtcNow());

            await WindowAsync(team, window, opened, firing, say, tell, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Opens, works in and closes one team's window.</summary>
    private async Task WindowAsync(
        string team,
        TeamSchedule? window,
        DateTimeOffset? opened,
        bool firing,
        Action<string> say,
        Func<string, string, Task> tell,
        CancellationToken ct)
    {
        var loaded = await _queues.LoadAsync(team, ct).ConfigureAwait(false);

        if (loaded.Failed)
        {
            return;
        }

        var queue = loaded.Value!;

        if (opened is null)
        {
            // Closed since the last look: say what it did, once.
            if (queue.WindowOpened is { } was)
            {
                var ran = queue.Runs.Where(one => one.StartedUtc >= was).ToList();
                var (title, detail) = QueueSettling.Summary(team, ran, queue.Items);

                await tell(title, detail).ConfigureAwait(false);
                await _queues.UpdateAsync(team, one =>
                {
                    one.WindowOpened = null;

                    return null;
                }, ct).ConfigureAwait(false);
            }

            return;
        }

        if (queue.WindowOpened != opened)
        {
            await _queues.UpdateAsync(team, one =>
            {
                one.WindowOpened = opened;

                return null;
            }, ct).ConfigureAwait(false);

            say($"{team} queue: window open until {(opened.Value + window!.Length!.Value).ToLocalTime():HH:mm}");
        }

        if (!firing)
        {
            return;
        }

        var limits = new QueueWindow(
            opened.Value,
            UsdCap.TryParse(queue.Settings.WindowUsd, out var cap) ? cap.Usd : null,
            queue.Settings.WindowRuns);

        var (item, run, decision, error) = await TakeAsync(team, limits, "daemon", ct).ConfigureAwait(false);

        if (error is not null)
        {
            say($"{team} queue: {error}");

            return;
        }

        if (item is null || run is null)
        {
            return;
        }

        say($"{team} queue: starting {item.Task} on {item.Project}" + (decision?.Skipped.Count > 0
            ? $", passing over {decision.Skipped.Count}: {string.Join(" ", decision.Skipped.Select(one => one.Reason))}"
            : string.Empty));

        if (await StartInBackgroundAsync(team, item, run, window?.Autonomy, ct).ConfigureAwait(false) is { } failed)
        {
            say($"{team} queue: {item.Task} could not start: {failed}");
        }
    }

    /// <summary>The project's own checkout, or null.</summary>
    private async Task<string?> RepositoryAsync(string project, CancellationToken ct) =>
        (await _projects.ResolveAsync(project, ct).ConfigureAwait(false)).Value?.LocalPath;

    /// <summary>Changes one run's record under the queue's lock, found by its task and when it started.</summary>
    private Task<OperationResult<TeamQueue>> RecordAsync(string team, QueueRun run, Action<QueueRun> change, CancellationToken ct) =>
        _queues.UpdateAsync(
            team,
            queue =>
            {
                if (queue.Runs.LastOrDefault(one => QueueRules.Same(one.Task, run.Task)
                        && QueueRules.Same(one.Project, run.Project) && one.StartedUtc == run.StartedUtc) is { } found)
                {
                    change(found);
                }

                return null;
            },
            ct);
}
