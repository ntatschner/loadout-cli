namespace Loadout.Agents.Teams.Steps;

/// <summary>
/// Clears a merged branch and its worktree away.
/// </summary>
internal sealed class TidyStep
{
    private readonly Core.Git.IGitManager? _git;

    public TidyStep(Core.Git.IGitManager? git)
    {
        _git = git;
    }

    /// <summary>
    /// Clears away a branch that has arrived: its worktree, then the branch
    /// itself.
    /// </summary>
    /// <remarks>
    /// Only ever after a merge, so nothing is thrown away: the commits are
    /// in the repository and git refuses both steps if that turns out not to
    /// be true. A tree left for every implementer of every run is litter
    /// somebody has to clear by hand, and three of them had already
    /// accumulated before this existed.
    /// </remarks>
    public async Task RunAsync(
        RunContext run,
        JournalWriter journal,
        string repository,
        string branch,
        string node,
        List<string> warnings,
        CancellationToken ct)
    {
        var worktrees = await _git!.ListWorktreesAsync(repository, ct).ConfigureAwait(false);

        var tree = worktrees.Value?.FirstOrDefault(
            w => !w.IsPrimary && string.Equals(w.Branch, branch, StringComparison.Ordinal));

        if (tree is not null)
        {
            var removed = await _git.RemoveWorktreeAsync(repository, tree.Path, ct).ConfigureAwait(false);

            if (removed.Failed)
            {
                // Almost always because the node left something uncommitted
                // in there, which is worth keeping and worth saying.
                warnings.Add($"The worktree at {tree.Path} was kept: {removed.Error}");

                return;
            }
        }

        var deleted = await _git.DeleteMergedBranchAsync(repository, branch, ct).ConfigureAwait(false);

        await journal.WriteAsync(
            "worktree.tidied", node, new { branch, path = tree?.Path, branchDeleted = deleted.Succeeded }, ct)
            .ConfigureAwait(false);

        if (deleted.Failed)
        {
            warnings.Add($"The branch {branch} was kept: {deleted.Error}");
        }
    }
}
