using Loadout.Core.Git;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents.Teams.Steps;

/// <summary>
/// Combines a run's node branches into one branch when it hands over, so the
/// person is handed one branch to push rather than one per node.
/// </summary>
/// <remarks>
/// <para>
/// Only at a handover. A run that ends done takes each node's branch through
/// the merge gate as it always has. A run that hands over is asking a person
/// to push its work and see CI pass, and three branches is three pushes and
/// three CI runs of pieces that were only ever meant to land together.
/// </para>
/// <para>
/// Done in a tree of its own, under this machine's state directory, on no
/// branch. The repository somebody works in is never checked out, merged
/// into or moved. The branch, <c>teams-&lt;run&gt;</c>, is made only once
/// every node's branch has merged cleanly, so a conflict leaves nothing
/// behind but the node branches, which the handover then names as before.
/// </para>
/// </remarks>
internal sealed class IntegrateStep
{
    private readonly Core.Projects.IProjectService? _projects;
    private readonly IGitManager? _git;
    private readonly IPlatformPaths _paths;

    public IntegrateStep(Core.Projects.IProjectService? projects, IGitManager? git, IPlatformPaths paths)
    {
        _projects = projects;
        _git = git;
        _paths = paths;
    }

    /// <summary>What the integration branch of a run is called, before any number is added to it.</summary>
    /// <remarks>
    /// The run's id with nothing after it, so it can never be taken for a
    /// node's branch, which always has the node's name after a hyphen.
    /// </remarks>
    internal static string BranchFor(string runId) => $"teams-{runId}";

    /// <summary>
    /// The branches the handover should name: the integration branch where
    /// one was made, or the node branches where it was not.
    /// </summary>
    public async Task<IReadOnlyList<string>> RunAsync(RunContext run, JournalWriter journal, CancellationToken ct)
    {
        var branches = run.Branches.Values.Distinct(StringComparer.Ordinal).ToList();

        // One branch is already one branch, and without git there is nothing
        // to combine them with.
        if (branches.Count < 2 || _projects is null || _git is null)
        {
            return branches;
        }

        var resolution = await _projects.ResolveAsync(run.Request.ProjectHandle, ct).ConfigureAwait(false);

        if (resolution.Failed || resolution.Value?.LocalPath is not { Length: > 0 } repository)
        {
            run.Warnings.Add("The node branches were not combined into one: the project could not be resolved.");

            return branches;
        }

        var tree = Path.Combine(
            _paths.Paths.State, "worktrees", resolution.Value.Entry.Slug, $"{BranchFor(run.RunId)}-combining");

        var added = await _git.AddDetachedWorktreeAsync(repository, tree, branches[0], ct).ConfigureAwait(false);

        if (added.Failed)
        {
            run.Warnings.Add($"The node branches were not combined into one: {added.Error}");

            return branches;
        }

        try
        {
            foreach (var branch in branches.Skip(1))
            {
                var merged = await _git.MergeAsync(tree, branch, ct).ConfigureAwait(false);

                if (merged.Failed || !merged.Value!.Merged)
                {
                    var conflicts = merged.Value?.Conflicts ?? [];

                    await journal.WriteAsync(
                        "run.integration-conflicted",
                        null,
                        new { branch, conflicts, error = merged.Error },
                        ct).ConfigureAwait(false);

                    run.Warnings.Add(
                        $"The node branches were not combined into one: {branch} "
                        + (conflicts.Count > 0
                            ? $"conflicts with the others in {string.Join(", ", conflicts)}."
                            : $"could not be merged: {merged.Error}")
                        + " Each is still on its own branch.");

                    return branches;
                }
            }

            var head = await _git.ResolveAsync(tree, "HEAD", ct).ConfigureAwait(false);
            var name = await NameAsync(repository, run.RunId, head.Value, ct).ConfigureAwait(false);

            if (name.Made)
            {
                var made = await _git.CreateBranchAsync(tree, name.Branch, ct).ConfigureAwait(false);

                if (made.Failed)
                {
                    run.Warnings.Add($"The node branches were combined, but {name.Branch} could not be made: {made.Error}");

                    return branches;
                }
            }

            await journal.WriteAsync(
                "run.integrated",
                null,
                head.Succeeded
                    ? new { branch = name.Branch, from = branches, commit = head.Value }
                    : (object)new { branch = name.Branch, from = branches },
                ct).ConfigureAwait(false);

            run.Console.Note($"Combined {string.Join(", ", branches)} into {name.Branch}.");

            return [name.Branch];
        }
        finally
        {
            // Nothing in it is uncommitted, since a merge that could not be
            // made is undone, so this does not need forcing. If it fails all
            // the same, the tree is on no branch and holds nothing of value.
            var removed = await _git.RemoveWorktreeAsync(repository, tree, CancellationToken.None).ConfigureAwait(false);

            if (removed.Failed)
            {
                run.Warnings.Add($"The tree the branches were combined in, {tree}, could not be removed: {removed.Error}");
            }
        }
    }

    /// <summary>
    /// The integration branch's name, and whether it still has to be made.
    /// </summary>
    /// <remarks>
    /// A run picked up after a handover and handed over again finds the name
    /// already taken. Where that branch is already at the same commit it is
    /// the same work and is named again. Where it is not, it is never moved,
    /// because somebody may have pushed it and opened a pull request from it:
    /// the new one is numbered instead.
    /// </remarks>
    private async Task<(string Branch, bool Made)> NameAsync(string repository, string runId, string? head, CancellationToken ct)
    {
        // Bounded, so a git that answers every name cannot hold the run here.
        // Past the last, making the branch fails and the handover names the
        // node branches.
        for (var n = 1; ; n++)
        {
            var name = n == 1 ? BranchFor(runId) : $"{BranchFor(runId)}-{n}";

            if (n == 100)
            {
                return (name, true);
            }

            var existing = await _git!.ResolveAsync(repository, name, ct).ConfigureAwait(false);

            if (existing.Failed)
            {
                return (name, true);
            }

            if (head is not null && string.Equals(existing.Value, head, StringComparison.OrdinalIgnoreCase))
            {
                return (name, false);
            }
        }
    }
}
