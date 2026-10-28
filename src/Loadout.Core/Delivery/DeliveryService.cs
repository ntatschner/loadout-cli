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
public sealed record LandingFound(string Project, string Repository, BranchLanding Landing);

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
    /// Marks merged the landings whose pull request the host now lists as
    /// merged, looking no more often than <see cref="DeliveryService.MergeEvery"/>.
    /// </summary>
    /// <returns>How many were marked merged.</returns>
    Task<int> MergedAsync(CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class DeliveryService : IDeliveryService
{
    /// <summary>How often each landing's pull request is looked for among the merged.</summary>
    public static readonly TimeSpan MergeEvery = TimeSpan.FromMinutes(15);

    private readonly ILandingService _landings;
    private readonly IProjectService _projects;
    private readonly IGitManager _git;
    private readonly IRepositoryHost _host;
    private readonly IPullRequestReader _pulls;
    private readonly ITaskService _tasks;
    private readonly TimeProvider _time;

    public DeliveryService(
        ILandingService landings,
        IProjectService projects,
        IGitManager git,
        IRepositoryHost host,
        IPullRequestReader pulls,
        ITaskService tasks,
        TimeProvider time)
    {
        _landings = landings;
        _projects = projects;
        _git = git;
        _host = host;
        _pulls = pulls;
        _tasks = tasks;
        _time = time;
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

        return OperationResult<LandingFound>.Ok(new LandingFound(where, repository, kept));
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

        if (CannotOpen(landing) is { } refused)
        {
            return OperationResult<BranchLanding>.Fail(refused, ExitCode.InvalidArguments);
        }

        // Asked before the push, so a branch never reaches the remote for a
        // pull request that was never going to open.
        if (_host.Unavailable() is { } cannot)
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

        var opened = await _host.OpenAsync(found.Repository, opening, ct).ConfigureAwait(false);

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

        if (CannotReady(landing) is { } refused)
        {
            return OperationResult<BranchLanding>.Fail(refused, ExitCode.InvalidArguments);
        }

        var marked = await _host.MarkReadyAsync(found.Repository, landing.PullRequest, ct).ConfigureAwait(false);

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

        if ((CannotMerge(landing) ?? CannotUseSubject(subject)) is { } refused)
        {
            return OperationResult<(BranchLanding, string)>.Fail(refused, ExitCode.InvalidArguments);
        }

        var asked = await _host.MergeWhenReadyAsync(
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
    public async Task<int> MergedAsync(CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        var marked = 0;

        foreach (var project in _landings.Projects())
        {
            var loaded = await _landings.LoadAsync(project, ct).ConfigureAwait(false);

            if (loaded.Failed)
            {
                continue;
            }

            var due = loaded.Value!.Landings
                .Where(one => one.State is LandingState.Draft or LandingState.Ready
                    && Number(one.PullRequest) is not null
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

            var merged = await _pulls.MergedAsync(repository, 50, ct).ConfigureAwait(false);

            if (merged.Failed)
            {
                continue;
            }

            var numbers = merged.Value!.Select(one => one.Number).ToHashSet();

            foreach (var landing in due)
            {
                landing.MergeChecked = now;

                if (numbers.Contains(Number(landing.PullRequest)!.Value))
                {
                    landing.State = LandingState.Merged;
                    landing.Why = $"Its pull request was merged: {landing.PullRequest}";
                    marked++;
                }
            }

            await _landings.RecordAsync(project, due, ct).ConfigureAwait(false);
        }

        return marked;
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
