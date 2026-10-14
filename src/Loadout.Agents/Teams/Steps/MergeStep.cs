using Loadout.Agents.Teams;
using Loadout.Core.Git;
using Loadout.Models.Results;
using Loadout.Models.Teams;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents.Teams.Steps;

/// <summary>
/// Takes a finished run's branches through its merge gate and into the
/// repository, asking the node that wrote one to resolve a conflict once.
/// </summary>
internal sealed class MergeStep
{
    private readonly Core.Projects.IProjectService? _projects;
    private readonly IGitManager? _git;
    private readonly IPlatformPaths _paths;
    private readonly TidyStep _tidy;
    private readonly StartWorkersStep _workers;

    public MergeStep(
        Core.Projects.IProjectService? projects,
        IGitManager? git,
        IPlatformPaths paths,
        TidyStep tidy,
        StartWorkersStep workers)
    {
        _projects = projects;
        _git = git;
        _paths = paths;
        _tidy = tidy;
        _workers = workers;
    }

    /// <summary>
    /// The one word each reviewing role is told to hand back when it is
    /// satisfied. Anything else, including silence, is not satisfaction.
    /// </summary>
    private static readonly HashSet<string> Accepting = new(StringComparer.OrdinalIgnoreCase)
    {
        "accept", "accepted", "approve", "approved", "verified", "holds", "fit", "reads-true",
    };

    /// <summary>
    /// The gate's nodes that have not accepted, each with what it did
    /// instead, or nothing when there is no gate or nothing to take through
    /// it.
    /// </summary>
    internal static IReadOnlyList<string> Ungated(
        TeamDefinition team,
        IReadOnlyDictionary<string, string> branches,
        IReadOnlyDictionary<string, string> decisions)
    {
        if (branches.Count == 0 || team.Rules.Gates.Merge.Count == 0)
        {
            return [];
        }

        return team.Rules.Gates.Merge
            .Where(node => !decisions.TryGetValue(node, out var decision) || !Accepting.Contains(decision))
            .Select(node => decisions.TryGetValue(node, out var decision)
                ? $"{node} said '{decision}'"
                : $"{node} decided nothing")
            .ToList();
    }

    /// <summary>
    /// Takes the run's branches into the repository, where the team's gate
    /// says they may go and a person agrees.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mechanical, and deliberately: no model decides this and none performs
    /// it. The coordinator reads what the gate's nodes decided, asks whoever
    /// is there, and then it is git's business. A conflict is not resolved
    /// here at all.
    /// </para>
    /// <para>
    /// Nothing is merged unless the run finished, the team names a gate, and
    /// every node in that gate handed back an accepting decision. Each of
    /// those is a reason a person would want to hear, so each is said.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<string>> RunAsync(
        RunContext run,
        JournalWriter journal,
        TeamRunRequest request,
        TeamDefinition team,
        string runId,
        string autonomy,
        ITeamConsole console,
        List<decimal> spent,
        CancellationToken ct)
    {
        var (ended, branches, decisions, warnings) = (run.Ended, run.Branches, run.Decisions, run.Warnings);
        var merged = new List<string>();

        if (branches.Count == 0)
        {
            return merged;
        }

        var left = string.Join(", ", branches.Values);

        if (!string.Equals(ended, "done", StringComparison.Ordinal))
        {
            warnings.Add($"Nothing was merged: the run ended {ended}. What the nodes committed is on {left}.");

            return merged;
        }

        var gate = team.Rules.Gates.Merge;

        if (gate.Count == 0)
        {
            warnings.Add(
                $"Nothing was merged: '{team.Name}' names no merge gate, and a change nobody is required to "
                + $"look at is not one this will land for you. It is on {left}.");

            return merged;
        }

        var missing = Ungated(team, branches, decisions);

        if (missing.Count > 0)
        {
            warnings.Add($"Nothing was merged: the gate needs {string.Join(" and ", gate)}, and {string.Join("; ", missing)}. It is on {left}.");
            await journal.WriteAsync("gate.refused", null, new { gate, decisions }, ct).ConfigureAwait(false);

            return merged;
        }

        if (_projects is null || _git is null)
        {
            warnings.Add($"Nothing was merged: this runner was built without a project service or git. It is on {left}.");

            return merged;
        }

        var resolution = await _projects.ResolveAsync(request.ProjectHandle, ct).ConfigureAwait(false);

        if (resolution.Failed || resolution.Value?.LocalPath is not { Length: > 0 } repository)
        {
            warnings.Add($"Nothing was merged: the project could not be resolved. It is on {left}.");

            return merged;
        }

        var state = await _git.GetStateAsync(repository, ct).ConfigureAwait(false);

        if (state.Failed)
        {
            warnings.Add($"Nothing was merged: {state.Error} It is on {left}.");

            return merged;
        }

        if (!state.Value!.IsClean)
        {
            // Merging into a tree somebody is working in would mix the run's
            // change with theirs, and untangling that is worse than waiting.
            warnings.Add(
                $"Nothing was merged: {repository} has uncommitted changes, and merging into a tree "
                + $"somebody is working in mixes their work with the run's. It is on {left}.");

            return merged;
        }

        var target = state.Value.Branch ?? "the current branch";

        foreach (var (node, branch) in branches)
        {
            await journal.WriteAsync("gate.opened", node, new { gate = "merge", branch, target }, ct).ConfigureAwait(false);

            // Autonomous has already been given the rules to follow and the
            // gate's nodes have agreed; anything else asks, because this is
            // the moment the run changes the repository somebody works in.
            var allowed = autonomy == "autonomous"
                || await console.ConfirmAsync($"Merge {branch} into {target}", ct).ConfigureAwait(false);

            await journal.WriteAsync("gate.decided", node, new { gate = "merge", branch, allowed }, ct).ConfigureAwait(false);

            if (!allowed)
            {
                warnings.Add($"{branch} was not merged, because you said not to. It is still there.");

                continue;
            }

            var result = await _git.MergeAsync(repository, branch, ct).ConfigureAwait(false);

            if (result.Failed)
            {
                warnings.Add($"{branch} could not be merged: {result.Error}");
                await journal.WriteAsync("merge.failed", node, new { branch, error = result.Error }, ct).ConfigureAwait(false);

                continue;
            }

            if (!result.Value!.Merged)
            {
                await journal.WriteAsync("merge.conflicted", node, new { branch, result.Value.Conflicts }, ct).ConfigureAwait(false);

                // Back to whoever wrote it. The node still has its worktree,
                // it knows why it made the change, and it is better placed to
                // reconcile the two than a person reading a list of file
                // names afterwards.
                //
                // Once only. A node that cannot resolve its own conflict on a
                // second look will not on a third, and the person needs to
                // hear about it rather than watch it spend.
                var second = await ResolveAsync(
                    run, request, team, runId, node, branch, target, result.Value.Conflicts,
                    autonomy, repository, console, journal, warnings, spent, ct).ConfigureAwait(false);

                if (second is null)
                {
                    warnings.Add(
                        $"{branch} conflicts with {target} in {string.Join(", ", result.Value.Conflicts)}, so nothing "
                        + "was merged and the repository is as it was.");

                    continue;
                }

                if (!second.Merged)
                {
                    warnings.Add(
                        $"{branch} still conflicts with {target} in {string.Join(", ", second.Conflicts)}, after "
                        + $"{node} was asked to resolve it. Nothing was merged and the repository is as it was.");

                    await journal.WriteAsync("merge.conflicted", node, new { branch, conflicts = second.Conflicts, again = true }, ct)
                        .ConfigureAwait(false);

                    continue;
                }

                result = OperationResult<GitMerge>.Ok(second);
            }

            merged.Add(branch);

            await journal.WriteAsync("merge.done", node, new { branch, target, result.Value!.FastForward }, ct)
                .ConfigureAwait(false);

            console.Note(
                result.Value.FastForward
                    ? $"Merged {branch} into {target}, fast-forward."
                    : $"Merged {branch} into {target} with a merge commit.");

            await _tidy.RunAsync(run, journal, repository, branch, node, warnings, ct).ConfigureAwait(false);
        }

        return merged;
    }

    /// <summary>
    /// Asks the node that wrote a branch to reconcile it with the target, and
    /// tries the merge once more.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null when nobody was asked: the node is not in the team any more, its
    /// role is gone, a person said no, or it came back without doing the
    /// work. The caller then reports the first conflict as it stands.
    /// </para>
    /// <para>
    /// The node is briefed under the same instance name, which resolves to
    /// the same branch and therefore the same worktree it was working in.
    /// It is a fresh session with no memory of the first one, so the brief
    /// has to carry everything: which branch, which target, which files.
    /// </para>
    /// </remarks>
    private async Task<GitMerge?> ResolveAsync(
        RunContext run,
        TeamRunRequest request,
        TeamDefinition team,
        string runId,
        string nodeName,
        string branch,
        string target,
        IReadOnlyList<string> conflicts,
        string autonomy,
        string repository,
        ITeamConsole console,
        JournalWriter journal,
        List<string> warnings,
        List<decimal> spent,
        CancellationToken ct)
    {
        if (!team.Nodes.TryGetValue(TeamRunner.BaseNode(nodeName), out var node)
            || request.Specialists.Find(node.Role) is not { } role)
        {
            return null;
        }

        var files = string.Join(", ", conflicts);

        var task =
            $"Your branch '{branch}' cannot be merged into '{target}': the two changed {files}. "
            + $"In your own worktree, merge '{target}' into '{branch}' and resolve every conflict, "
            + "keeping what both sides were trying to do. Do not change "
            + $"'{target}' itself, and do not merge your branch anywhere. Commit the resolution on "
            + "your own branch and report that commit.";

        if (!await TeamRunner.GateAsync(autonomy, console, $"Ask {nodeName} to resolve the conflict between {branch} and {target}", ct)
            .ConfigureAwait(false))
        {
            return null;
        }

        await journal.WriteAsync("conflict.briefed", nodeName, new { branch, target, conflicts }, ct).ConfigureAwait(false);

        var brief = TeamRunner.MakeBrief(
            runId, nodeName, team.Lead, node, role, task, conflicts,
            doneWhen: [$"'{branch}' merges into '{target}' with no conflict"],
            team, autonomy, request.OutwardAllowed ?? [], request.Specialists.Find,
            TeamDirectory(team.Name));

        var ask = new ReportRequest(nodeName, task, DeliverableKind.Commit);

        using var alone = new SemaphoreSlim(1, 1);

        var outcome = await _workers.RunOneAsync(
            run, journal, request, team, new Briefed(ask, node, role, brief), alone, ct).ConfigureAwait(false);

        warnings.AddRange(outcome.Warnings);
        spent.Add(outcome.Cost);

        if (outcome.Report is not { Status: ReportStatus.Done })
        {
            return null;
        }

        var again = await _git!.MergeAsync(repository, branch, ct).ConfigureAwait(false);

        if (again.Failed)
        {
            warnings.Add($"{branch} could not be merged after {nodeName} resolved it: {again.Error}");

            return null;
        }

        return again.Value;
    }

    // The same place TeamRunner keeps a team's work, worked out the same way.
    private string TeamDirectory(string team) =>
        Path.Combine(_paths.Paths.State, "teams", "work", TeamRunner.Slug(team));
}
