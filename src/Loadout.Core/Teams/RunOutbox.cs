using Loadout.Core.Git;
using Loadout.Models.Results;

namespace Loadout.Core.Teams;

/// <summary>One file a run produced.</summary>
/// <param name="Path">Where it is, relative to the repository.</param>
/// <param name="Change">What the run did to it: added, changed, removed.</param>
/// <param name="Commit">The commit that did it.</param>
/// <param name="Node">The node that delivered that commit.</param>
public sealed record OutboxFile(string Path, string Change, string Commit, string Node);

/// <summary>What a run delivered, as files rather than as references.</summary>
/// <param name="Repository">The repository the run worked in, when it can be told.</param>
/// <param name="Files">Every file the run's commits touched, oldest commit first.</param>
/// <param name="Missing">Commits a node reported that the repository no longer has.</param>
public sealed record Outbox(
    string? Repository,
    IReadOnlyList<OutboxFile> Files,
    IReadOnlyList<string> Missing);

/// <summary>What a run delivered, resolved to the files it left in the repository.</summary>
public interface IRunOutbox
{
    /// <summary>The files a run produced.</summary>
    Task<OperationResult<Outbox>> ForAsync(string runId, CancellationToken ct = default);
}

/// <summary>
/// Turns a run's reported commits into the files they contain.
/// </summary>
/// <remarks>
/// <para>
/// A node reports what it produced by reference - a commit hash, a branch -
/// because that is what it can say truthfully about work it has committed. It
/// is not what somebody asking "what did this run deliver" wants to read, and
/// resolving it by hand means knowing which repository the run was in and
/// typing git commands at it.
/// </para>
/// <para>
/// Which repository it was in is not recorded directly, and is recoverable:
/// a node given its own worktree is launched in that worktree, and a node
/// without one is launched in the repository itself. The first node launched
/// without a worktree says where the run was working, and that path outlives
/// the worktrees, which are cleared away after a merge.
/// </para>
/// <para>
/// A commit the repository no longer has is named rather than dropped. That is
/// the one failure worth reporting on its own: a run whose deliverables cannot
/// be found is a run that appears to have produced nothing, and the difference
/// between "produced nothing" and "produced something nobody can reach" is the
/// whole question.
/// </para>
/// </remarks>
public sealed class RunOutbox : IRunOutbox
{
    private readonly IRunJournal _journal;
    private readonly IGitManager _git;

    public RunOutbox(IRunJournal journal, IGitManager git)
    {
        _journal = journal;
        _git = git;
    }

    /// <inheritdoc />
    public async Task<OperationResult<Outbox>> ForAsync(string runId, CancellationToken ct = default)
    {
        var read = _journal.Read(runId);

        if (read.Failed)
        {
            return OperationResult<Outbox>.Fail(read.Error!);
        }

        var events = read.Value!;
        var repository = Repository(events);
        var left = RunLeftBehind.In(_journal.DirectoryOf(runId), events);

        // Each commit once, in the order the nodes reported them. Two nodes
        // reporting the same commit - a lead repeating its implementer's, which
        // happens - is one piece of work, not two.
        var commits = new List<(string Commit, string Node)>();

        foreach (var one in left.Delivered)
        {
            if (!string.Equals(one.Kind, "commit", StringComparison.Ordinal))
            {
                continue;
            }

            if (!commits.Any(seen => string.Equals(seen.Commit, one.Ref, StringComparison.Ordinal)))
            {
                commits.Add((one.Ref, one.Node));
            }
        }

        if (repository is null)
        {
            return OperationResult<Outbox>.Ok(new Outbox(null, [], [.. commits.Select(one => one.Commit)]));
        }

        var files = new List<OutboxFile>();
        var missing = new List<string>();

        foreach (var (commit, node) in commits)
        {
            var listed = await _git.ListCommitFilesAsync(repository, commit, ct).ConfigureAwait(false);

            if (listed.Failed)
            {
                missing.Add(commit);
                continue;
            }

            foreach (var file in listed.Value!)
            {
                files.Add(new OutboxFile(file.Path, file.Change, commit, node));
            }
        }

        return OperationResult<Outbox>.Ok(new Outbox(repository, files, missing));
    }

    /// <summary>
    /// Where the run was working, from the first node launched without a
    /// worktree of its own.
    /// </summary>
    private static string? Repository(IReadOnlyList<RunEvent> events)
    {
        foreach (var one in events)
        {
            if (!string.Equals(one.Kind, "node.launched", StringComparison.Ordinal))
            {
                continue;
            }

            if (one.Text("worktree") is { Length: > 0 })
            {
                continue;
            }

            if (one.Text("directory") is { Length: > 0 } directory)
            {
                return directory;
            }
        }

        return null;
    }
}
