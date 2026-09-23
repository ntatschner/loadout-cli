using Loadout.Core.Git;
using Loadout.Models.Results;

namespace Loadout.Core.Teams;

/// <summary>One file a run produced.</summary>
/// <param name="Path">Where it is, relative to the repository.</param>
/// <param name="Change">What the run did to it: added, changed, removed.</param>
/// <param name="Commit">The commit that did it.</param>
/// <param name="Node">The node that delivered that commit.</param>
public sealed record OutboxFile(string Path, string Change, string Commit, string Node);

/// <summary>One file a node reported that no commit of its contains.</summary>
/// <param name="Path">Where the node said it is.</param>
/// <param name="Kind">What the node called it: a file, a document, a plan.</param>
/// <param name="Bytes">How big it is, or null where nothing is there now.</param>
/// <param name="Node">The node that reported it.</param>
/// <param name="Note">What the node said about it.</param>
public sealed record OutboxAsset(string Path, string Kind, long? Bytes, string Node, string? Note);

/// <summary>What a run delivered, as files rather than as references.</summary>
/// <param name="Repository">The repository the run worked in, when it can be told.</param>
/// <param name="Files">Every file the run's commits touched, oldest commit first.</param>
/// <param name="Loose">Files a node reported directly, which no commit carries.</param>
/// <param name="Missing">Commits a node reported that the repository no longer has.</param>
public sealed record Outbox(
    string? Repository,
    IReadOnlyList<OutboxFile> Files,
    IReadOnlyList<OutboxAsset> Loose,
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
/// without one is launched in the repository itself - unless it was sent to
/// read another node's branch, when it is launched in that branch's tree. The
/// first node launched in neither says where the run was working, and that path outlives
/// the worktrees, which are cleared away after a merge.
/// </para>
/// <para>
/// Not everything a run produces gets committed. A planner on one real run
/// wrote PLAN.md into the repository and reported it, and because it was in no
/// commit the outbox said the run had delivered one changed README.md and
/// never mentioned it. A deliverable whose reference is a path is therefore
/// looked for on disk, and reported with its size or as gone.
/// </para>
/// <para>
/// Whether a reference is a path is settled by its shape, not by its kind: a
/// plan may be "PLAN.md" or it may be "round-3", and the second is not a file
/// that has gone missing. A reference with no whitespace that either carries a
/// directory separator or ends in an extension is treated as a path; anything
/// else is left where it is, in the report, where team status reads it.
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

        var loose = Loose(left.Delivered, repository);

        if (repository is null)
        {
            return OperationResult<Outbox>.Ok(
                new Outbox(null, [], loose, [.. commits.Select(one => one.Commit)]));
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

        return OperationResult<Outbox>.Ok(new Outbox(repository, files, loose, missing));
    }

    /// <summary>
    /// The deliverables that name a file, each looked for where it was said to
    /// be.
    /// </summary>
    private static IReadOnlyList<OutboxAsset> Loose(
        IReadOnlyList<Delivered> delivered,
        string? repository)
    {
        var loose = new List<OutboxAsset>();

        foreach (var one in delivered)
        {
            if (string.Equals(one.Kind, "commit", StringComparison.Ordinal)
                || string.Equals(one.Kind, "branch", StringComparison.Ordinal)
                || !LooksLikeAPath(one.Ref))
            {
                continue;
            }

            var at = Path.IsPathRooted(one.Ref) || repository is null
                ? one.Ref
                : Path.Combine(repository, one.Ref);

            long? bytes = null;

            try
            {
                var file = new FileInfo(at);

                if (file.Exists)
                {
                    bytes = file.Length;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException)
            {
                // Nothing readable there. Reported as gone, which is what it is
                // to anybody trying to collect it.
            }

            loose.Add(new OutboxAsset(at, one.Kind, bytes, one.Node, one.Note));
        }

        return loose;
    }

    /// <summary>
    /// Whether a reference is meant to be a file.
    /// </summary>
    /// <remarks>
    /// Settled by shape rather than by the deliverable's kind, because the kind
    /// does not decide it: a plan is reported as "PLAN.md" on one run and as
    /// "round-3" on another. A reference with whitespace in it is prose - one
    /// real plan reads "round-1: implementer/1 -&gt; reviewer" - and a bare word
    /// with no separator and no extension is a verdict, not a path.
    /// </remarks>
    private static bool LooksLikeAPath(string reference)
    {
        if (reference.Length == 0 || reference.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (reference.Contains('/', StringComparison.Ordinal)
            || reference.Contains('\\', StringComparison.Ordinal))
        {
            return true;
        }

        var dot = reference.LastIndexOf('.');

        // An extension, and something in front of it: ".gitignore" is a file
        // and "v1." is not an answer to anything.
        return dot > 0 && dot < reference.Length - 1;
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

            // A node reading another's branch is started in that branch's
            // tree, which is cleared away after a merge: it is no more the
            // repository than the implementer's own launch is.
            if (one.Text("worktree") is { Length: > 0 } || one.Text("reviewing") is { Length: > 0 })
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
