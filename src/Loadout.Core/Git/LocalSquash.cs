using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Git;

/// <summary>
/// A squash-merge of one branch into another, worked out but not yet made.
/// </summary>
/// <param name="Branch">The branch being merged.</param>
/// <param name="Target">The branch it goes into.</param>
/// <param name="BranchCommit">The branch's commit when this was worked out.</param>
/// <param name="TargetCommit">The target's commit when this was worked out.</param>
/// <param name="Tree">The tree the squash commit will have.</param>
/// <param name="DiffStat">What it changes, as <c>git diff --stat</c> says it.</param>
/// <param name="CheckedOutAt">The worktree the target is checked out in, or null when none has it.</param>
public sealed record LocalSquashPlan(
    string Branch,
    string Target,
    string BranchCommit,
    string TargetCommit,
    string Tree,
    string DiffStat,
    string? CheckedOutAt);

/// <summary>How a waiting branch stands against the branch it would land in.</summary>
/// <param name="Target">The branch it would land in.</param>
/// <param name="Behind">How many commits the target has that the branch does not.</param>
/// <param name="Contained">Whether everything on the branch is in the target already.</param>
public sealed record BranchStanding(string Target, int Behind, bool Contained);

/// <summary>
/// Squash-merges a branch into another on this machine, for a project whose
/// branches land with local git rather than through a host.
/// </summary>
/// <remarks>
/// <para>
/// Worked out first and made second, so the person sees what goes in before
/// anything changes, and declining leaves both branches as they were.
/// </para>
/// <para>
/// Worked out with <c>git merge-tree --write-tree</c>, which touches no
/// working tree, so a merge with conflicts is refused before anything is
/// written. The commit is made with <c>git commit-tree</c> and the target
/// moved only if it has not moved since: by a fast-forward in the worktree
/// that has it checked out, which must be clean, or by a compare-and-swap of
/// the ref when none does. Needs git 2.38 or later.
/// </para>
/// </remarks>
public interface ILocalSquash
{
    /// <summary>The branch a project's work lands in when none is named: origin's default, else main, else master.</summary>
    Task<OperationResult<string>> TargetAsync(string repository, CancellationToken ct = default);

    /// <summary>
    /// How a branch stands against a target: how far behind, and whether its
    /// work is in the target already. Null when either cannot be read.
    /// </summary>
    /// <remarks>
    /// Contained is only ever true for a branch merged as it was, or fast-
    /// forwarded: a squash-merge makes a new commit, so git can't see that a
    /// squashed branch went in.
    /// </remarks>
    Task<BranchStanding?> StandingAsync(string repository, string branch, string target, CancellationToken ct = default);

    /// <summary>Works out a squash-merge without changing anything.</summary>
    Task<OperationResult<LocalSquashPlan>> PlanAsync(string repository, string branch, string target, CancellationToken ct = default);

    /// <summary>Makes a squash-merge worked out by <see cref="PlanAsync"/>, returning the new commit.</summary>
    Task<OperationResult<string>> ApplyAsync(
        string repository,
        LocalSquashPlan plan,
        string subject,
        string body,
        CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class LocalSquash : ILocalSquash
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    private readonly IProcessLauncher _processes;
    private readonly IExecutableResolver _resolver;

    public LocalSquash(IProcessLauncher processes, IExecutableResolver resolver)
    {
        _processes = processes;
        _resolver = resolver;
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> TargetAsync(string repository, CancellationToken ct = default)
    {
        var origin = await GitAsync(repository, ["symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD"], ct)
            .ConfigureAwait(false);

        if (origin.Succeeded && origin.Value!.Output.Trim() is { Length: > 0 } named)
        {
            return OperationResult<string>.Ok(named.StartsWith("origin/", StringComparison.Ordinal) ? named["origin/".Length..] : named);
        }

        foreach (var candidate in new[] { "main", "master" })
        {
            if (await CommitAsync(repository, candidate, ct).ConfigureAwait(false) is not null)
            {
                return OperationResult<string>.Ok(candidate);
            }
        }

        return OperationResult<string>.Fail(
            "There is no main or master branch to merge into. Name one with --into.", ExitCode.InvalidArguments);
    }

    /// <inheritdoc />
    public async Task<BranchStanding?> StandingAsync(string repository, string branch, string target, CancellationToken ct = default)
    {
        if (await CommitAsync(repository, branch, ct).ConfigureAwait(false) is not { } branchCommit
            || await CommitAsync(repository, target, ct).ConfigureAwait(false) is not { } targetCommit)
        {
            return null;
        }

        var behind = await GitAsync(repository, ["rev-list", "--count", $"{branchCommit}..{targetCommit}"], ct).ConfigureAwait(false);

        if (behind.Failed || behind.Value!.Code != 0
            || !int.TryParse(behind.Value.Output.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var count))
        {
            return null;
        }

        var contained = await GitAsync(repository, ["merge-base", "--is-ancestor", branchCommit, targetCommit], ct).ConfigureAwait(false);

        return new BranchStanding(target, count, contained.Succeeded && contained.Value!.Code == 0);
    }

    /// <inheritdoc />
    public async Task<OperationResult<LocalSquashPlan>> PlanAsync(
        string repository,
        string branch,
        string target,
        CancellationToken ct = default)
    {
        if (await CommitAsync(repository, branch, ct).ConfigureAwait(false) is not { } branchCommit)
        {
            return OperationResult<LocalSquashPlan>.Fail($"{branch} is not a branch in the repository.", ExitCode.RepositoryUnavailable);
        }

        if (await CommitAsync(repository, target, ct).ConfigureAwait(false) is not { } targetCommit)
        {
            return OperationResult<LocalSquashPlan>.Fail($"{target} is not a branch in the repository.", ExitCode.InvalidArguments);
        }

        if (string.Equals(branch, target, StringComparison.Ordinal))
        {
            return OperationResult<LocalSquashPlan>.Fail($"{branch} can't be merged into itself.", ExitCode.InvalidArguments);
        }

        var contained = await GitAsync(repository, ["merge-base", "--is-ancestor", branchCommit, targetCommit], ct).ConfigureAwait(false);

        if (contained.Succeeded && contained.Value!.Code == 0)
        {
            return OperationResult<LocalSquashPlan>.Fail($"Everything on {branch} is in {target} already.", ExitCode.InvalidArguments);
        }

        var merged = await GitAsync(repository, ["merge-tree", "--write-tree", "--name-only", targetCommit, branchCommit], ct)
            .ConfigureAwait(false);

        if (merged.Failed)
        {
            return OperationResult<LocalSquashPlan>.Fail(merged.Error!, merged.ExitCode);
        }

        var lines = merged.Value!.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (merged.Value.Code == 1)
        {
            var clashing = lines.Skip(1).TakeWhile(line => !line.StartsWith("Auto-merging", StringComparison.Ordinal)
                && !line.StartsWith("CONFLICT", StringComparison.Ordinal)).ToList();

            return OperationResult<LocalSquashPlan>.Fail(
                $"{branch} does not merge cleanly into {target}"
                + (clashing.Count > 0 ? $": {string.Join(", ", clashing)} conflict." : "."),
                ExitCode.GeneralFailure);
        }

        if (merged.Value.Code != 0 || lines.Length == 0)
        {
            return OperationResult<LocalSquashPlan>.Fail(
                "git could not work out the merge. 'git merge-tree --write-tree' needs git 2.38 or later: "
                + merged.Value.Error.Trim(),
                ExitCode.GeneralFailure);
        }

        var tree = lines[0];
        var stat = await GitAsync(repository, ["diff", "--stat", targetCommit, tree], ct).ConfigureAwait(false);

        return OperationResult<LocalSquashPlan>.Ok(new LocalSquashPlan(
            branch,
            target,
            branchCommit,
            targetCommit,
            tree,
            stat.Succeeded ? stat.Value!.Output.TrimEnd() : string.Empty,
            await CheckedOutAtAsync(repository, target, ct).ConfigureAwait(false)));
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> ApplyAsync(
        string repository,
        LocalSquashPlan plan,
        string subject,
        string body,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        // Either branch moving since the plan was shown means the person
        // agreed to something other than what would now go in.
        if (await CommitAsync(repository, plan.Branch, ct).ConfigureAwait(false) != plan.BranchCommit
            || await CommitAsync(repository, plan.Target, ct).ConfigureAwait(false) != plan.TargetCommit)
        {
            return OperationResult<string>.Fail(
                $"{plan.Branch} or {plan.Target} moved since the merge was worked out. Nothing was changed; run it again.",
                ExitCode.GeneralFailure);
        }

        if (plan.CheckedOutAt is { } worktree)
        {
            var status = await GitAsync(worktree, ["status", "--porcelain", "--untracked-files=no"], ct).ConfigureAwait(false);

            if (status.Failed || status.Value!.Code != 0 || status.Value.Output.Trim().Length > 0)
            {
                return OperationResult<string>.Fail(
                    $"{plan.Target} is checked out at {worktree} with changes not committed. Nothing was changed; "
                    + "commit or stash them first.",
                    ExitCode.GeneralFailure);
            }
        }

        string[] message = body.Trim().Length > 0 ? ["-m", subject.Trim(), "-m", body.Trim()] : ["-m", subject.Trim()];
        var made = await GitAsync(repository, ["commit-tree", plan.Tree, "-p", plan.TargetCommit, .. message], ct).ConfigureAwait(false);

        if (made.Failed || made.Value!.Code != 0 || made.Value.Output.Trim() is not { Length: > 0 } commit)
        {
            return OperationResult<string>.Fail(
                "git could not make the squash commit: " + (made.Failed ? made.Error : made.Value.Error.Trim()), ExitCode.GeneralFailure);
        }

        var moved = plan.CheckedOutAt is { } at
            ? await GitAsync(at, ["merge", "--ff-only", "--quiet", commit], ct).ConfigureAwait(false)
            : await GitAsync(repository, ["update-ref", "refs/heads/" + plan.Target, commit, plan.TargetCommit], ct).ConfigureAwait(false);

        if (moved.Failed || moved.Value!.Code != 0)
        {
            return OperationResult<string>.Fail(
                $"The squash commit {commit[..Math.Min(12, commit.Length)]} was made but {plan.Target} could not be moved to it: "
                + (moved.Failed ? moved.Error : moved.Value.Error.Trim()),
                ExitCode.GeneralFailure);
        }

        return OperationResult<string>.Ok(commit);
    }

    private async Task<string?> CommitAsync(string repository, string branch, CancellationToken ct)
    {
        var resolved = await GitAsync(repository, ["rev-parse", "--verify", "--quiet", $"refs/heads/{branch}^{{commit}}"], ct)
            .ConfigureAwait(false);

        return resolved.Succeeded && resolved.Value!.Code == 0 && resolved.Value.Output.Trim() is { Length: > 0 } sha ? sha : null;
    }

    private async Task<string?> CheckedOutAtAsync(string repository, string target, CancellationToken ct)
    {
        var listed = await GitAsync(repository, ["worktree", "list", "--porcelain"], ct).ConfigureAwait(false);

        if (listed.Failed || listed.Value!.Code != 0)
        {
            return null;
        }

        string? path = null;

        foreach (var line in listed.Value.Output.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                path = line["worktree ".Length..];
            }
            else if (string.Equals(line, "branch refs/heads/" + target, StringComparison.Ordinal))
            {
                return path;
            }
        }

        return null;
    }

    private async Task<OperationResult<(int Code, string Output, string Error)>> GitAsync(
        string directory,
        IReadOnlyList<string> arguments,
        CancellationToken ct)
    {
        if (_resolver.Resolve("git") is not { } git)
        {
            return OperationResult<(int, string, string)>.Fail("git is not on PATH", ExitCode.AgentUnavailable);
        }

        var ran = await _processes.RunAsync(new ProcessRequest(git, arguments, directory), Timeout, ct).ConfigureAwait(false);

        return ran.Failed
            ? OperationResult<(int, string, string)>.Fail(ran.Error!, ran.ExitCode)
            : OperationResult<(int, string, string)>.Ok((ran.Value!.ExitCode, ran.Value.StandardOutput, ran.Value.StandardError));
    }
}
