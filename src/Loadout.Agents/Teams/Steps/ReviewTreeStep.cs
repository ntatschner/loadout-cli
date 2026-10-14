using Loadout.Models.Instructions;
using Loadout.Models.Teams;

namespace Loadout.Agents.Teams.Steps;

/// <summary>
/// Finds the tree a reading node should be started in: the tree of the work
/// it was asked to read, where there is one.
/// </summary>
internal sealed class ReviewTreeStep
{
    private readonly Core.Projects.IProjectService? _projects;
    private readonly Core.Git.IGitManager? _git;

    public ReviewTreeStep(Core.Projects.IProjectService? projects, Core.Git.IGitManager? git)
    {
        _projects = projects;
        _git = git;
    }

    /// <summary>
    /// Where a node that reads another node's work should run: that work's
    /// tree, when the brief names one of this run's branches and its tree is
    /// still there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only a node with no tree of its own and no job of changing anything. An
    /// implementer is given a tree to commit in; a reviewer or a verifier was
    /// started in the repository somebody works in, told in words which branch
    /// to read, and left to reach it with <c>cd</c> or <c>git -C</c>.
    /// </para>
    /// <para>
    /// Which is what the first long run did, and its verifier was refused
    /// every command it tried: its session allows <c>dotnet test</c> as the
    /// first word of a command, and every command it had began with the
    /// directory it needed. Three launches, eleven refusals, nothing verified
    /// and nothing merged. Started in the tree, the command it needs is the
    /// command it is allowed.
    /// </para>
    /// </remarks>
    public async Task<string?> RunAsync(
        RunContext run,
        JournalWriter journal,
        TeamRunRequest request,
        SpecialistDocument role,
        Brief brief,
        List<string> warnings,
        CancellationToken ct)
    {
        var mode = role.Role?.Mode;
        var named = TeamRunner.BranchNamed(brief);
        var from = brief.Constraints.From;

        if (brief.Constraints.Worktree is { Length: > 0 }
            || string.Equals(mode, "implement", StringComparison.OrdinalIgnoreCase)
            || (named is null && from is null)
            || _projects is null
            || _git is null)
        {
            return null;
        }

        var resolution = await _projects.ResolveAsync(request.ProjectHandle, ct).ConfigureAwait(false);

        if (resolution.Failed || resolution.Value?.LocalPath is not { Length: > 0 } repository)
        {
            return null;
        }

        var listed = await _git.ListWorktreesAsync(repository, ct).ConfigureAwait(false);
        var ours = (listed.Value ?? [])
            .Where(tree => !tree.IsPrimary && tree.Branch is { } b && b.StartsWith($"teams-{brief.Run}-", StringComparison.Ordinal))
            .ToList();

        // What the lead said as data wins over what its words mention. A
        // commit is placed by the tree whose branch it is the head of: that
        // is the work as it stands, and a commit further back is history the
        // node can read from any tree.
        if (from is not null)
        {
            if (ours.FirstOrDefault(tree => string.Equals(tree.Branch, from, StringComparison.Ordinal)) is { } byName)
            {
                return byName.Branch;
            }

            var wanted = await _git.ResolveAsync(repository, from, ct).ConfigureAwait(false);
            var heads = new List<string>();

            if (wanted.Succeeded)
            {
                foreach (var tree in ours)
                {
                    var head = await _git.ResolveAsync(repository, tree.Branch!, ct).ConfigureAwait(false);

                    if (head.Succeeded && string.Equals(head.Value, wanted.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        heads.Add(tree.Branch!);
                    }
                }
            }

            if (heads.Count == 1)
            {
                return heads[0];
            }

            if (named is null)
            {
                warnings.Add(
                    $"{brief.Node} was asked to read {from}, which is not where any one of this run's worktrees stands, "
                    + "so it runs in the repository.");

                return null;
            }
        }

        if (ours.Any(tree => string.Equals(tree.Branch, named, StringComparison.Ordinal)))
        {
            return named;
        }

        warnings.Add($"{brief.Node} was asked about {named}, which has no worktree any more, so it runs in the repository.");

        return null;
    }
}
