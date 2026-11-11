using System.Text;
using Loadout.Core.Git;
using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Models;
using Loadout.Models.Delivery;
using Loadout.Models.Results;
using Loadout.Models.Tasks;

namespace Loadout.Core.Delivery;

/// <summary>A landing, with the project it is kept under and where that project's checkout is.</summary>
/// <param name="Project">The project's slug.</param>
/// <param name="Repository">The project's checkout on this machine.</param>
/// <param name="Landing">The landing as it is kept now.</param>
/// <param name="Host">The host the project's branches land through, or null when it could not be worked out.</param>
/// <param name="HostProblem">Why the host could not be worked out, or null when it was.</param>
public sealed record LandingFound(
    string Project,
    string Repository,
    BranchLanding Landing,
    HostChoice? Host = null,
    string? HostProblem = null)
{
    /// <summary>Whether the project lands with local git, squash-merging here rather than through pull requests.</summary>
    public bool IsLocal => Host is { Host.OpensPullRequests: false };
}

/// <summary>
/// What a person can do with a branch waiting to land: open its pull request,
/// mark it ready, and ask for it to be merged.
/// </summary>
/// <remarks>
/// <para>
/// One place for these, so the command line and Studio cannot come to do them
/// differently. Each move is checked against what the landing says first, and
/// the same checks are offered on their own so a dry run refuses exactly what
/// the real run would.
/// </para>
/// <para>
/// Merging is asked of the host, never done here: the pull request is
/// squash-merged by the host once its required checks pass, with the message
/// the person wrote. The landing stays ready until the merge is seen.
/// </para>
/// </remarks>
public interface IDeliveryService
{
    /// <summary>The landing for a branch, in the named project or whichever one has it.</summary>
    Task<OperationResult<LandingFound>> FindAsync(string branch, string? project, CancellationToken ct = default);

    /// <summary>The pull request that would be opened for a landing, with the title and body given or worked out.</summary>
    Task<PullRequestOpening> OpeningAsync(LandingFound found, bool draft, string? title, string? body, CancellationToken ct = default);

    /// <summary>Pushes a pending branch and opens its pull request.</summary>
    Task<OperationResult<BranchLanding>> OpenAsync(LandingFound found, PullRequestOpening opening, CancellationToken ct = default);

    /// <summary>Marks a draft pull request ready for review.</summary>
    Task<OperationResult<BranchLanding>> ReadyAsync(LandingFound found, CancellationToken ct = default);

    /// <summary>Asks the host to squash-merge a ready pull request with this message once its checks pass.</summary>
    /// <returns>The landing as now kept, and what the host said.</returns>
    Task<OperationResult<(BranchLanding Landing, string Said)>> MergeAsync(
        LandingFound found,
        string subject,
        string body,
        CancellationToken ct = default);

    /// <summary>
    /// Works out the squash-merge of a waiting branch into the target, for a
    /// project that lands with local git, without changing anything.
    /// </summary>
    /// <param name="found">The landing.</param>
    /// <param name="into">The branch to merge into, or null for origin's default, else main, else master.</param>
    /// <param name="ct">Cancels the call.</param>
    Task<OperationResult<LocalSquashPlan>> PlanLocalAsync(LandingFound found, string? into, CancellationToken ct = default);

    /// <summary>Makes a squash-merge worked out by <see cref="PlanLocalAsync"/>, which the person has confirmed.</summary>
    /// <returns>The landing as now kept, merged, and the new commit.</returns>
    Task<OperationResult<(BranchLanding Landing, string Commit)>> MergeLocallyAsync(
        LandingFound found,
        LocalSquashPlan plan,
        string subject,
        string body,
        CancellationToken ct = default);

    /// <summary>Gives up on a waiting branch, closing its pull request first when it has one.</summary>
    Task<OperationResult<BranchLanding>> AbandonAsync(LandingFound found, CancellationToken ct = default);

    /// <summary>
    /// Marks merged the landings whose pull request the host now lists as
    /// merged, and pending again those whose pull request was closed unmerged;
    /// and marks merged the pending branches whose work is in the branch they
    /// would land in already. Each is looked at no more often than
    /// <see cref="DeliveryService.MergeEvery"/>.
    /// </summary>
    /// <returns>How many were marked merged and how many went back to pending.</returns>
    Task<(int Merged, int Reopened)> MergedAsync(CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class DeliveryService : IDeliveryService
{
    /// <summary>How often each landing's pull request is looked for among the merged.</summary>
    public static readonly TimeSpan MergeEvery = TimeSpan.FromMinutes(15);

    private readonly ILandingService _landings;
    private readonly IProjectService _projects;
    private readonly IGitManager _git;
    private readonly IRepositoryHosts _hosts;
    private readonly ILocalSquash _squash;
    private readonly ITaskService _tasks;
    private readonly TimeProvider _time;

    public DeliveryService(
        ILandingService landings,
        IProjectService projects,
        IGitManager git,
        IRepositoryHosts hosts,
        ILocalSquash squash,
        ITaskService tasks,
        TimeProvider time)
    {
        _landings = landings;
        _projects = projects;
        _git = git;
        _hosts = hosts;
        _squash = squash;
        _tasks = tasks;
        _time = time;
    }

    /// <summary>Why a pull request can't be opened for this landing where it waits, or null when it can.</summary>
    public static string? CannotOpen(LandingFound found)
    {
        ArgumentNullException.ThrowIfNull(found);

        return found.IsLocal
            ? $"{found.Project} lands with local git, which has no pull requests. Merge it here with: "
                + $"loadout pr merge {found.Landing.Branch} --subject ..."
            : CannotOpen(found.Landing) ?? NoHost(found);
    }

    /// <summary>Why this landing can't be merged where it waits, or null when it can.</summary>
    /// <remarks>
    /// With local git a pending branch is merged as it is, since there is no
    /// pull request to open or mark ready first.
    /// </remarks>
    public static string? CannotMerge(LandingFound found)
    {
        ArgumentNullException.ThrowIfNull(found);

        if (!found.IsLocal)
        {
            return CannotMerge(found.Landing) ?? NoHost(found);
        }

        return found.Landing.State is LandingState.Pending or LandingState.Ready && found.Landing.PullRequest.Length == 0
            ? null
            : $"{found.Landing.Branch} is {LandingMoves.Word(found.Landing.State)}, so there is nothing to merge.";
    }

    /// <summary>Why nothing can be asked of the project's host, or null when something can.</summary>
    public static string? NoHost(LandingFound found)
    {
        ArgumentNullException.ThrowIfNull(found);

        return found.Host is null ? found.HostProblem ?? $"Where {found.Project} lands could not be worked out." : null;
    }

    /// <summary>Why a pull request can't be opened for this landing, or null when it can.</summary>
    public static string? CannotOpen(BranchLanding landing)
    {
        ArgumentNullException.ThrowIfNull(landing);

        return landing.State switch
        {
            LandingState.Pending => null,
            LandingState.Draft or LandingState.Ready => $"{landing.Branch} already has a pull request: {landing.PullRequest}",
            _ => $"{landing.Branch} is {LandingMoves.Word(landing.State)}, so there is nothing to open.",
        };
    }

    /// <summary>Why this landing's pull request can't be marked ready, or null when it can.</summary>
    public static string? CannotReady(BranchLanding landing)
    {
        ArgumentNullException.ThrowIfNull(landing);

        return landing.State switch
        {
            LandingState.Draft when landing.PullRequest.Length > 0 => null,
            LandingState.Draft => $"{landing.Branch} is kept as a draft but with no pull request to mark ready.",
            LandingState.Ready => $"{landing.Branch}'s pull request is already ready: {landing.PullRequest}",
            LandingState.Pending => $"{landing.Branch} has no pull request yet. Open one with: loadout pr open {landing.Branch}",
            _ => $"{landing.Branch} is {LandingMoves.Word(landing.State)}, so there is nothing to mark ready.",
        };
    }

    /// <summary>Why this landing's pull request can't be merged, or null when it can.</summary>
    public static string? CannotMerge(BranchLanding landing)
    {
        ArgumentNullException.ThrowIfNull(landing);

        return landing.State switch
        {
            LandingState.Ready when landing.PullRequest.Length > 0 => null,
            LandingState.Ready => $"{landing.Branch} is kept as ready but with no pull request to merge.",
            LandingState.Draft => $"{landing.Branch}'s pull request is a draft. Mark it ready first: loadout pr ready {landing.Branch}",
            LandingState.Pending => $"{landing.Branch} has no pull request yet. Open one with: loadout pr open {landing.Branch}",
            _ => $"{landing.Branch} is {LandingMoves.Word(landing.State)}, so there is nothing to merge.",
        };
    }

    /// <summary>Why this landing can't be abandoned, or null when it can.</summary>
    /// <remarks>
    /// A merge already asked for is refused: the host may merge it at any
    /// moment, so closing it now races the merge, and either outcome would be
    /// a surprise. Turn auto-merge off on the host first.
    /// </remarks>
    public static string? CannotAbandon(BranchLanding landing)
    {
        ArgumentNullException.ThrowIfNull(landing);

        return landing.State switch
        {
            LandingState.Ready when landing.MergeRequestedUtc is not null =>
                $"{landing.Branch} has a merge asked for, which the host may finish at any moment. "
                + $"Turn auto-merge off on {landing.PullRequest} first.",
            LandingState.Pending or LandingState.Draft or LandingState.Ready => null,
            _ => $"{landing.Branch} is {LandingMoves.Word(landing.State)} already.",
        };
    }

    /// <summary>Why a squash commit's subject can't be used, or null when it can.</summary>
    public static string? CannotUseSubject(string? subject) =>
        subject?.Trim() is not { Length: > 0 } trimmed
            ? "Write the squash commit's subject with --subject. The merge uses only the message you give it."
            : trimmed.Contains('\n', StringComparison.Ordinal)
                ? "The subject is one line. Put the rest in --body."
                : null;

    /// <summary>A pull request's number from its address, or null.</summary>
    public static int? Number(string url) =>
        (url ?? string.Empty).TrimEnd('/').Split('/').LastOrDefault() is { } last && int.TryParse(last, out var number)
            ? number
            : null;

    /// <inheritdoc />
    public async Task<OperationResult<LandingFound>> FindAsync(string branch, string? project, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            return OperationResult<LandingFound>.Fail("Say which branch. See them with: loadout pr list", ExitCode.InvalidArguments);
        }

        IReadOnlyList<string> slugs;

        if (project is { Length: > 0 } handle)
        {
            var named = await _projects.ResolveAsync(handle, ct).ConfigureAwait(false);

            if (named.Failed)
            {
                return OperationResult<LandingFound>.Fail(named.Error!, named.ExitCode);
            }

            slugs = [named.Value!.Entry.Slug];
        }
        else
        {
            slugs = _landings.Projects();
        }

        var found = new List<(string Project, BranchLanding Landing)>();

        foreach (var slug in slugs)
        {
            var loaded = await _landings.LoadAsync(slug, ct).ConfigureAwait(false);

            if (loaded.Failed)
            {
                return OperationResult<LandingFound>.Fail(loaded.Error!, loaded.ExitCode);
            }

            if (loaded.Value!.Landings.FirstOrDefault(one => string.Equals(one.Branch, branch.Trim(), StringComparison.Ordinal)) is { } landing)
            {
                found.Add((slug, landing));
            }
        }

        if (found.Count == 0)
        {
            return OperationResult<LandingFound>.Fail(
                $"No branch called {branch.Trim()} is waiting to land. See them with: loadout pr list",
                ExitCode.ProjectNotFound);
        }

        if (found.Count > 1)
        {
            return OperationResult<LandingFound>.Fail(
                $"{branch.Trim()} is waiting in more than one project. Say which with --project: "
                + string.Join(", ", found.Select(one => one.Project)),
                ExitCode.InvalidArguments);
        }

        var (where, kept) = found[0];
        var resolved = await _projects.ResolveAsync(where, ct).ConfigureAwait(false);

        if (resolved.Failed || resolved.Value!.LocalPath is not { } repository)
        {
            return OperationResult<LandingFound>.Fail(
                $"{where} has no checkout on this machine, so nothing can be done with {kept.Branch} from here.",
                ExitCode.RepositoryUnavailable);
        }

        var host = await _hosts.ForAsync(where, repository, ct).ConfigureAwait(false);

        return OperationResult<LandingFound>.Ok(host.Succeeded
            ? new LandingFound(where, repository, kept, host.Value)
            : new LandingFound(where, repository, kept, HostProblem: host.Error));
    }

    /// <inheritdoc />
    public async Task<PullRequestOpening> OpeningAsync(
        LandingFound found,
        bool draft,
        string? title,
        string? body,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(found);

        var landing = found.Landing;
        TaskItem? task = null;

        if (landing.Task.Length > 0)
        {
            var tasks = await _tasks.ListAsync(found.Project, ct).ConfigureAwait(false);

            task = tasks.Succeeded ? TaskRuns.Find(landing.Task, tasks.Value!) : null;
        }

        var worked = task is not null && task.Title.Trim().Length > 0 ? task.Title.Trim() : landing.Branch;

        return new PullRequestOpening(
            landing.Branch,
            title?.Trim() is { Length: > 0 } given ? given : worked,
            body?.Trim() is { Length: > 0 } written ? written : Body(landing, task),
            draft);
    }

    /// <summary>
    /// What a pull request opened from the waiting list says about where its branch came from.
    /// </summary>
    /// <remarks>
    /// Whoever reviews it did not watch the run, so it carries the task it was
    /// done for and its done-when where there was one, and which run and node
    /// made it.
    /// </remarks>
    internal static string Body(BranchLanding landing, TaskItem? task)
    {
        var body = new StringBuilder();

        if (task is not null)
        {
            body.Append(task.Title.Trim().Length > 0 ? task.Title.Trim() : task.Id).Append("\n\n");

            if (task.Note.Trim().Length > 0)
            {
                body.Append(task.Note.Trim()).Append("\n\n");
            }

            var conditions = task.DoneWhen.Where(one => one.Trim().Length > 0).ToList();

            if (conditions.Count > 0)
            {
                body.Append("## Done when\n\n");

                foreach (var condition in conditions)
                {
                    body.Append("- ").Append(condition.Trim()).Append('\n');
                }

                body.Append('\n');
            }
        }

        var from = landing.Node.Length > 0
            ? $"From {landing.Node} of team run {landing.RunId}"
            : landing.RunId.Length > 0 ? $"From team run {landing.RunId}" : $"From branch {landing.Branch}";

        body.Append(from)
            .Append(landing.Team.Length > 0 ? $", team {landing.Team}" : string.Empty)
            .Append(".\n");
        body.Append("Opened from the branches waiting to land. Nothing reaches the base branch until a person merges it.\n");

        return body.ToString();
    }

    /// <inheritdoc />
    public async Task<OperationResult<BranchLanding>> OpenAsync(
        LandingFound found,
        PullRequestOpening opening,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(opening);

        var landing = found.Landing;

        if (CannotOpen(found) is { } refused)
        {
            return OperationResult<BranchLanding>.Fail(refused, ExitCode.InvalidArguments);
        }

        var host = found.Host!.Host;

        // Asked before the push, so a branch never reaches the remote for a
        // pull request that was never going to open.
        if (await host.UnavailableAsync(found.Repository, ct).ConfigureAwait(false) is { } cannot)
        {
            return OperationResult<BranchLanding>.Fail(
                $"No pull request can be opened from here: {cannot}.", ExitCode.AgentUnavailable);
        }

        var there = await _git.ResolveAsync(found.Repository, "refs/heads/" + landing.Branch, ct).ConfigureAwait(false);

        if (there.Failed)
        {
            return OperationResult<BranchLanding>.Fail(
                $"{landing.Branch} has gone from {found.Project}'s repository, so there is nothing to push.",
                ExitCode.RepositoryUnavailable);
        }

        var pushed = await _git.PushWithUpstreamAsync(found.Repository, "origin", landing.Branch, ct).ConfigureAwait(false);

        if (pushed.Failed)
        {
            return OperationResult<BranchLanding>.Fail($"{landing.Branch} could not be pushed: {pushed.Error}", pushed.ExitCode);
        }

        var opened = await host.OpenAsync(found.Repository, opening, ct).ConfigureAwait(false);

        if (opened.Failed)
        {
            return OperationResult<BranchLanding>.Fail(
                $"{landing.Branch} was pushed, but its pull request did not open: {opened.Error}", opened.ExitCode);
        }

        landing.State = opening.Draft ? LandingState.Draft : LandingState.Ready;
        landing.PullRequest = opened.Value!;
        landing.Why = opening.Draft ? "Opened from the waiting list as a draft." : "Opened from the waiting list, ready for review.";

        return await KeepAsync(found.Project, landing, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<BranchLanding>> ReadyAsync(LandingFound found, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(found);

        var landing = found.Landing;

        if ((CannotReady(landing) ?? NoHost(found)) is { } refused)
        {
            return OperationResult<BranchLanding>.Fail(refused, ExitCode.InvalidArguments);
        }

        var marked = await found.Host!.Host.MarkReadyAsync(found.Repository, landing.PullRequest, ct).ConfigureAwait(false);

        if (marked.Failed)
        {
            return OperationResult<BranchLanding>.Fail(marked.Error!, marked.ExitCode);
        }

        landing.State = LandingState.Ready;
        landing.Why = "Marked ready for review.";

        return await KeepAsync(found.Project, landing, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<(BranchLanding Landing, string Said)>> MergeAsync(
        LandingFound found,
        string subject,
        string body,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(found);

        var landing = found.Landing;

        if (found.IsLocal)
        {
            return OperationResult<(BranchLanding, string)>.Fail(
                $"{found.Project} lands with local git, so it is merged here once you have seen what goes in, not asked of a host.",
                ExitCode.InvalidArguments);
        }

        if ((CannotMerge(found) ?? CannotUseSubject(subject)) is { } refused)
        {
            return OperationResult<(BranchLanding, string)>.Fail(refused, ExitCode.InvalidArguments);
        }

        var asked = await found.Host!.Host.MergeWhenReadyAsync(
            found.Repository, landing.PullRequest, subject.Trim(), body?.Trim() ?? string.Empty, ct).ConfigureAwait(false);

        if (asked.Failed)
        {
            return OperationResult<(BranchLanding, string)>.Fail(asked.Error!, asked.ExitCode);
        }

        landing.MergeRequestedUtc = _time.GetUtcNow();
        landing.Why = "Squash-merge asked for: the host merges it once its required checks pass.";

        var kept = await KeepAsync(found.Project, landing, ct).ConfigureAwait(false);

        return kept.Failed
            ? OperationResult<(BranchLanding, string)>.Fail(kept.Error!, kept.ExitCode)
            : OperationResult<(BranchLanding, string)>.Ok((kept.Value!, asked.Value!));
    }

    /// <inheritdoc />
    public async Task<OperationResult<LocalSquashPlan>> PlanLocalAsync(LandingFound found, string? into, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(found);

        if (!found.IsLocal)
        {
            return OperationResult<LocalSquashPlan>.Fail(
                NoHost(found) ?? $"{found.Project} lands through {found.Host!.Host.Provider}, not local git.", ExitCode.InvalidArguments);
        }

        if (CannotMerge(found) is { } refused)
        {
            return OperationResult<LocalSquashPlan>.Fail(refused, ExitCode.InvalidArguments);
        }

        string target;

        if (into?.Trim() is { Length: > 0 } named)
        {
            target = named;
        }
        else
        {
            var worked = await _squash.TargetAsync(found.Repository, ct).ConfigureAwait(false);

            if (worked.Failed)
            {
                return OperationResult<LocalSquashPlan>.Fail(worked.Error!, worked.ExitCode);
            }

            target = worked.Value!;
        }

        return await _squash.PlanAsync(found.Repository, found.Landing.Branch, target, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<(BranchLanding Landing, string Commit)>> MergeLocallyAsync(
        LandingFound found,
        LocalSquashPlan plan,
        string subject,
        string body,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(plan);

        if (!found.IsLocal)
        {
            return OperationResult<(BranchLanding, string)>.Fail(
                NoHost(found) ?? $"{found.Project} lands through {found.Host!.Host.Provider}, not local git.", ExitCode.InvalidArguments);
        }

        if ((CannotMerge(found) ?? CannotUseSubject(subject)) is { } refused)
        {
            return OperationResult<(BranchLanding, string)>.Fail(refused, ExitCode.InvalidArguments);
        }

        var made = await _squash.ApplyAsync(found.Repository, plan, subject, body ?? string.Empty, ct).ConfigureAwait(false);

        if (made.Failed)
        {
            return OperationResult<(BranchLanding, string)>.Fail(made.Error!, made.ExitCode);
        }

        var commit = made.Value!;
        var landing = found.Landing;
        landing.State = LandingState.Merged;
        landing.Why = $"Squash-merged into {plan.Target} on this machine as {commit[..Math.Min(12, commit.Length)]}.";

        var kept = await KeepAsync(found.Project, landing, ct).ConfigureAwait(false);

        return kept.Failed
            ? OperationResult<(BranchLanding, string)>.Fail(kept.Error!, kept.ExitCode)
            : OperationResult<(BranchLanding, string)>.Ok((kept.Value!, commit));
    }

    /// <inheritdoc />
    public async Task<OperationResult<BranchLanding>> AbandonAsync(LandingFound found, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(found);

        var landing = found.Landing;

        if (CannotAbandon(landing) is { } refused)
        {
            return OperationResult<BranchLanding>.Fail(refused, ExitCode.InvalidArguments);
        }

        // Closed first, so a pull request is never left open on the host for a
        // branch Loadout has stopped watching.
        if (landing.PullRequest.Length > 0)
        {
            if (NoHost(found) is { } nowhere)
            {
                return OperationResult<BranchLanding>.Fail(nowhere, ExitCode.InvalidArguments);
            }

            var closed = await found.Host!.Host.CloseAsync(found.Repository, landing.PullRequest, ct).ConfigureAwait(false);

            if (closed.Failed)
            {
                return OperationResult<BranchLanding>.Fail(
                    $"{landing.Branch} was not abandoned, because its pull request did not close: {closed.Error}", closed.ExitCode);
            }
        }

        landing.State = LandingState.Abandoned;
        landing.Why = landing.PullRequest.Length > 0
            ? $"Abandoned, and its pull request closed: {landing.PullRequest}"
            : "Abandoned. The branch is left where it is.";

        return await KeepAsync(found.Project, landing, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Each pull request is asked about on its own, through the host its
    /// project lands through, so a project on GitLab or Azure DevOps is
    /// noticed the same way as one on GitHub. A host that cannot be asked, or
    /// a pull request it cannot read, is passed over until the next look.
    /// </para>
    /// <para>
    /// A pending branch, which has no pull request to ask about, is looked
    /// for in the branch it would land in instead, as this machine has it:
    /// one merged or squash-merged by hand, here or on the host and then
    /// pulled, is marked merged. Nothing is fetched to find out.
    /// </para>
    /// </remarks>
    public async Task<(int Merged, int Reopened)> MergedAsync(CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        var marked = 0;
        var reopened = 0;

        foreach (var project in _landings.Projects())
        {
            var loaded = await _landings.LoadAsync(project, ct).ConfigureAwait(false);

            if (loaded.Failed)
            {
                continue;
            }

            var due = loaded.Value!.Landings
                .Where(one => (one.PullRequest.Length > 0
                        ? one.State is LandingState.Draft or LandingState.Ready
                        : one.State is LandingState.Pending)
                    && (one.MergeChecked is not { } checkedAt || now - checkedAt >= MergeEvery))
                .ToList();

            if (due.Count == 0)
            {
                continue;
            }

            var resolved = await _projects.ResolveAsync(project, ct).ConfigureAwait(false);

            if (resolved.Failed || resolved.Value!.LocalPath is not { } repository)
            {
                continue;
            }

            var looked = new List<BranchLanding>();
            var waiting = due.Where(one => one.PullRequest.Length == 0).ToList();

            if (waiting.Count > 0 && await _squash.TargetAsync(repository, ct).ConfigureAwait(false) is { Succeeded: true } target)
            {
                foreach (var landing in waiting)
                {
                    // Null when the branch has gone or git could not say:
                    // left to be looked at again rather than counted a look.
                    if (await _squash.StandingAsync(repository, landing.Branch, target.Value!, ct).ConfigureAwait(false) is not { } standing)
                    {
                        continue;
                    }

                    landing.MergeChecked = now;
                    looked.Add(landing);

                    if (standing.Contained)
                    {
                        landing.State = LandingState.Merged;
                        landing.Why = $"Everything on it is in {standing.Target} already, merged without Loadout.";
                        marked++;
                    }
                }
            }

            var opened = due.Where(one => one.PullRequest.Length > 0).ToList();

            if (opened.Count > 0
                && await _hosts.ForAsync(project, repository, ct).ConfigureAwait(false) is { Succeeded: true } choice
                && choice.Value!.Host is { OpensPullRequests: true } host)
            {
                foreach (var landing in opened)
                {
                    var state = await host.StateAsync(repository, landing.PullRequest, ct).ConfigureAwait(false);

                    if (state.Failed)
                    {
                        continue;
                    }

                    landing.MergeChecked = now;
                    looked.Add(landing);

                    if (state.Value == PullRequestState.Merged)
                    {
                        landing.State = LandingState.Merged;
                        landing.Why = $"Its pull request was merged: {landing.PullRequest}";
                        marked++;
                    }
                    else if (state.Value == PullRequestState.Closed)
                    {
                        // Pending again, with nothing of the closed pull request
                        // left on it, so it can be opened afresh or abandoned.
                        landing.Why = $"Its pull request was closed without being merged: {landing.PullRequest}";
                        landing.State = LandingState.Pending;
                        landing.PullRequest = string.Empty;
                        landing.MergeRequestedUtc = null;
                        landing.MergeChecked = null;
                        reopened++;
                    }
                }
            }

            if (looked.Count > 0)
            {
                await _landings.RecordAsync(project, looked, ct).ConfigureAwait(false);
            }
        }

        return (marked, reopened);
    }

    private async Task<OperationResult<BranchLanding>> KeepAsync(string project, BranchLanding landing, CancellationToken ct)
    {
        var recorded = await _landings.RecordAsync(project, [landing], ct).ConfigureAwait(false);

        if (recorded.Failed)
        {
            return OperationResult<BranchLanding>.Fail(recorded.Error!, recorded.ExitCode);
        }

        return recorded.Value!.Count > 0
            ? OperationResult<BranchLanding>.Fail(
                $"It was done, but the record of it could not be kept: {recorded.Value[0]}.", ExitCode.GeneralFailure)
            : OperationResult<BranchLanding>.Ok(landing);
    }
}
