using Loadout.Core.Security;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Git;

/// <inheritdoc />
internal sealed class GitManager : IGitManager
{
    private static readonly TimeSpan LocalOperationTimeout = TimeSpan.FromSeconds(30);

    private readonly IProcessLauncher _processes;
    private readonly IExecutableResolver _resolver;

    public GitManager(IProcessLauncher processes, IExecutableResolver resolver)
    {
        _processes = processes;
        _resolver = resolver;
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> GetVersionAsync(CancellationToken ct = default)
    {
        var result = await RunAsync(null, ["--version"], LocalOperationTimeout, ct).ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult<string>.Ok(result.Value!.Trim())
            : OperationResult<string>.Fail(result.Error!, ExitCode.RepositoryUnavailable);
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> FindRepositoryRootAsync(
        string path,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(path))
        {
            return OperationResult<string>.Fail(
                $"'{path}' is not a directory.", ExitCode.RepositoryUnavailable);
        }

        var result = await RunAsync(path, ["rev-parse", "--show-toplevel"], LocalOperationTimeout, ct)
            .ConfigureAwait(false);

        if (result.Failed)
        {
            return OperationResult<string>.Fail(
                $"'{path}' is not inside a Git repository.", ExitCode.RepositoryUnavailable);
        }

        // git reports forward slashes even on Windows; converting to the
        // platform form keeps the value comparable to paths from the
        // filesystem APIs.
        var root = result.Value!.Trim().Replace('/', Path.DirectorySeparatorChar);

        return OperationResult<string>.Ok(root);
    }

    /// <inheritdoc />
    public async Task<OperationResult<GitRepositoryState>> GetStateAsync(
        string repositoryPath,
        CancellationToken ct = default)
    {
        var rootResult = await FindRepositoryRootAsync(repositoryPath, ct).ConfigureAwait(false);
        if (rootResult.Failed)
        {
            return OperationResult<GitRepositoryState>.Fail(rootResult.Error!, rootResult.ExitCode);
        }

        var root = rootResult.Value!;

        // Spawning git is nearly the whole cost of asking it anything, so the
        // work is to start fewer processes rather than to ask smaller
        // questions. The branch, the head commit and whether the tree is dirty
        // all come out of one status in the version 2 format, which leaves two
        // calls where there were four, and they do not depend on each other.
        var statusTask = RunAsync(root, ["status", "--porcelain=v2", "--branch"], LocalOperationTimeout, ct);
        var remoteTask = RunAsync(root, ["remote", "get-url", "origin"], LocalOperationTimeout, ct);

        await Task.WhenAll(statusTask, remoteTask).ConfigureAwait(false);

        var remoteResult = await remoteTask.ConfigureAwait(false);
        var remote = remoteResult.Succeeded ? remoteResult.Value!.Trim() : null;

        var statusResult = await statusTask.ConfigureAwait(false);

        // One failure now costs all three answers where it used to cost one.
        // That trade is worth taking: status failing inside a directory git has
        // already agreed is a repository means something is wrong that a branch
        // name would not have made better.
        var (branch, head, isClean) = statusResult.Succeeded
            ? ReadStatus(statusResult.Value!)
            : (null, null, false);

        return OperationResult<GitRepositoryState>.Ok(
            new GitRepositoryState(root, branch, remote, isClean, head));
    }

    /// <summary>
    /// Reads the branch, the head commit and whether anything is changed out of
    /// <c>status --porcelain=v2 --branch</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The format is documented as stable and is designed to be parsed, which
    /// the version 1 format is not. Header lines begin with <c>#</c>; every
    /// other line is a change. Changed entries are introduced by <c>1</c>,
    /// <c>2</c>, <c>u</c>, <c>?</c> or <c>!</c>, never by <c>#</c>, so a file
    /// whose name begins with a hash cannot be mistaken for a header.
    /// </para>
    /// <para>
    /// Two placeholders stand where a value does not exist, and both were
    /// confirmed against a real repository rather than taken from the
    /// documentation: <c>(initial)</c> for the commit of a repository that has
    /// none yet, and <c>(detached)</c> for the branch of a detached head.
    /// Untracked files count as changes here exactly as they did before.
    /// </para>
    /// </remarks>
    internal static (string? Branch, string? Head, bool IsClean) ReadStatus(string output)
    {
        const string BranchPrefix = "# branch.head ";
        const string CommitPrefix = "# branch.oid ";

        string? branch = null;
        string? head = null;
        var isClean = true;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] != '#')
            {
                isClean = false;
                continue;
            }

            if (line.StartsWith(BranchPrefix, StringComparison.Ordinal))
            {
                var value = line[BranchPrefix.Length..].Trim();
                branch = value == "(detached)" ? null : value;
            }
            else if (line.StartsWith(CommitPrefix, StringComparison.Ordinal))
            {
                var value = line[CommitPrefix.Length..].Trim();
                head = value == "(initial)" ? null : value;
            }
        }

        return (branch, head, isClean);
    }

    /// <inheritdoc />
    public async Task<OperationResult> InitAsync(
        string path,
        string defaultBranch = "main",
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(path);

        var result = await RunAsync(path, ["init", "--initial-branch", defaultBranch],
            LocalOperationTimeout, ct).ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error!, ExitCode.RepositoryUnavailable);
    }

    /// <inheritdoc />
    public async Task<OperationResult> SetRemoteAsync(
        string repositoryPath,
        string name,
        string url,
        CancellationToken ct = default)
    {
        // Adding a remote that already exists fails, so an existing one is
        // repointed instead. Re-running setup should not be an error.
        var existing = await RunAsync(repositoryPath, ["remote", "get-url", name],
            LocalOperationTimeout, ct).ConfigureAwait(false);

        var arguments = existing.Succeeded
            ? new[] { "remote", "set-url", name, url }
            : ["remote", "add", name, url];

        var result = await RunAsync(repositoryPath, arguments, LocalOperationTimeout, ct)
            .ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error!, ExitCode.RepositoryUnavailable);
    }

    /// <inheritdoc />
    public async Task<OperationResult> PushWithUpstreamAsync(
        string repositoryPath,
        string remote,
        string branch,
        CancellationToken ct = default)
    {
        var result = await RunAsync(repositoryPath, ["push", "--set-upstream", remote, branch],
            TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error!, ExitCode.WorkspaceSyncFailed);
    }

    /// <inheritdoc />
    public async Task<OperationResult> CloneAsync(
        string remote,
        string destination,
        string? branch = null,
        CancellationToken ct = default)
    {
        var arguments = new List<string> { "clone" };

        if (!string.IsNullOrWhiteSpace(branch))
        {
            arguments.Add("--branch");
            arguments.Add(branch);
        }

        arguments.Add(remote);
        arguments.Add(destination);

        // A clone can legitimately take minutes on a large repository, so it
        // gets a far longer bound than the launch-time fetch does.
        var result = await RunAsync(null, arguments, TimeSpan.FromMinutes(30), ct).ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error!, ExitCode.RepositoryUnavailable);
    }

    /// <inheritdoc />
    public async Task<OperationResult> FetchAsync(
        string repositoryPath,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var result = await RunAsync(repositoryPath, ["fetch", "--prune"], timeout, ct).ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error!, ExitCode.WorkspaceSyncFailed);
    }

    /// <inheritdoc />
    public async Task<OperationResult> PullFastForwardAsync(
        string repositoryPath,
        CancellationToken ct = default)
    {
        var result = await RunAsync(repositoryPath, ["merge", "--ff-only", "@{upstream}"],
            LocalOperationTimeout, ct).ConfigureAwait(false);

        if (result.Succeeded)
        {
            return OperationResult.Ok();
        }

        // A refused fast-forward means local and remote have diverged, which
        // is the conflict case of spec section 47 rather than a plain failure.
        // It is reported as a Git conflict so the caller can offer recovery
        // instead of retrying.
        return OperationResult.Fail(result.Error!, ExitCode.GitConflict);
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> CommitAllAsync(
        string repositoryPath,
        string message,
        CancellationToken ct = default)
    {
        var statusResult = await RunAsync(repositoryPath, ["status", "--porcelain"], LocalOperationTimeout, ct)
            .ConfigureAwait(false);

        if (statusResult.Failed)
        {
            return OperationResult<bool>.Fail(statusResult.Error!);
        }

        if (statusResult.Value!.Trim().Length == 0)
        {
            // Nothing changed. Reported as "no commit made" rather than as a
            // failure, so callers do not treat a quiet session as an error
            // (spec section 46: do not commit meaningless changes).
            return OperationResult<bool>.Ok(false);
        }

        var stageResult = await RunAsync(repositoryPath, ["add", "--all"], LocalOperationTimeout, ct)
            .ConfigureAwait(false);
        if (stageResult.Failed)
        {
            return OperationResult<bool>.Fail(stageResult.Error!);
        }

        var commitResult = await RunAsync(repositoryPath, ["commit", "--message", message],
            LocalOperationTimeout, ct).ConfigureAwait(false);

        return commitResult.Succeeded
            ? OperationResult<bool>.Ok(true)
            : OperationResult<bool>.Fail(commitResult.Error!);
    }

    /// <inheritdoc />
    public async Task<OperationResult> CreateBranchAsync(
        string repositoryPath,
        string branchName,
        CancellationToken ct = default)
    {
        var result = await RunAsync(repositoryPath, ["branch", branchName], LocalOperationTimeout, ct)
            .ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error!);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<string>>> ListChangedFilesAsync(
        string repositoryPath,
        CancellationToken ct = default)
    {
        var result = await RunAsync(repositoryPath, ["status", "--porcelain"], LocalOperationTimeout, ct)
            .ConfigureAwait(false);

        if (result.Failed)
        {
            return OperationResult<IReadOnlyList<string>>.Fail(result.Error!);
        }

        var paths = result.Value!
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            // Porcelain output is two status characters, a space, then the path.
            .Select(line => line.Length > 3 ? line[3..].Trim() : line.Trim())
            .Where(path => path.Length > 0)
            .ToList();

        return OperationResult<IReadOnlyList<string>>.Ok(paths);
    }

    /// <inheritdoc />
    public async Task<OperationResult> PushAsync(string repositoryPath, CancellationToken ct = default)
    {
        var result = await RunAsync(repositoryPath, ["push"], TimeSpan.FromMinutes(5), ct)
            .ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error!, ExitCode.WorkspaceSyncFailed);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<GitWorktree>>> ListWorktreesAsync(
        string repositoryPath,
        CancellationToken ct = default)
    {
        var result = await RunAsync(repositoryPath, ["worktree", "list", "--porcelain"],
            LocalOperationTimeout, ct).ConfigureAwait(false);

        if (result.Failed)
        {
            return OperationResult<IReadOnlyList<GitWorktree>>.Fail(result.Error!);
        }

        var worktrees = new List<GitWorktree>();
        string? path = null;
        string? branch = null;

        foreach (var line in result.Value!.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                // A blank line separates records, but so does the next
                // "worktree" line; flushing here handles both.
                Flush();
                path = line["worktree ".Length..].Replace('/', Path.DirectorySeparatorChar);
            }
            else if (line.StartsWith("branch ", StringComparison.Ordinal))
            {
                branch = line["branch ".Length..].Replace("refs/heads/", string.Empty);
            }
        }

        Flush();

        return OperationResult<IReadOnlyList<GitWorktree>>.Ok(worktrees);

        void Flush()
        {
            if (path is not null)
            {
                // git lists the main working tree first, always.
                worktrees.Add(new GitWorktree(path, branch, worktrees.Count == 0));
            }

            path = null;
            branch = null;
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<string>>> ListFilesAsync(
        string repositoryPath,
        IReadOnlyList<string> patterns,
        GitFileSet fileSet,
        CancellationToken ct = default)
    {
        if (patterns.Count == 0)
        {
            return OperationResult<IReadOnlyList<string>>.Ok([]);
        }

        var arguments = new List<string> { "ls-files" };

        // --exclude-standard makes git apply the same ignore rules it would
        // during a normal add, including the user's global exclude file, so the
        // answer matches what a commit would actually pick up.
        arguments.AddRange(fileSet switch
        {
            GitFileSet.Tracked => ["--cached"],
            GitFileSet.UntrackedAndVisible => ["--others", "--exclude-standard"],
            _ => (string[])["--others", "--ignored", "--exclude-standard"],
        });

        arguments.Add("--");
        arguments.AddRange(patterns);

        var result = await RunAsync(repositoryPath, arguments, LocalOperationTimeout, ct)
            .ConfigureAwait(false);

        if (result.Failed)
        {
            return OperationResult<IReadOnlyList<string>>.Fail(result.Error!);
        }

        var files = result.Value!
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return OperationResult<IReadOnlyList<string>>.Ok(files);
    }

    /// <inheritdoc />
    public async Task<OperationResult> UntrackAsync(
        string repositoryPath,
        IReadOnlyList<string> paths,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.Count == 0)
        {
            return OperationResult.Ok();
        }

        // --cached is the whole point: the file leaves the index and stays on
        // disk. -r so a path that turns out to be a directory works rather than
        // failing with "not removing recursively without -r".
        var arguments = new List<string> { "rm", "--cached", "-r", "--quiet", "--" };

        arguments.AddRange(paths);

        var result = await RunAsync(repositoryPath, arguments, LocalOperationTimeout, ct)
            .ConfigureAwait(false);

        return result.Succeeded ? OperationResult.Ok() : OperationResult.Fail(result.Error!);
    }

    /// <inheritdoc />
    public async Task<OperationResult> SetGlobalConfigValueAsync(
        string key,
        string value,
        CancellationToken ct = default)
    {
        var result = await RunAsync(null, ["config", "--global", key, value], LocalOperationTimeout, ct)
            .ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error!, ExitCode.ConfigurationInvalid);
    }

    /// <inheritdoc />
    public async Task<OperationResult> SetLocalConfigValueAsync(
        string key,
        string value,
        string repositoryPath,
        CancellationToken ct = default)
    {
        var result = await RunAsync(
            repositoryPath, ["config", "--local", key, value], LocalOperationTimeout, ct)
            .ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error!, ExitCode.ConfigurationInvalid);
    }

    /// <inheritdoc />
    public async Task<OperationResult> RemoveLocalConfigValueAsync(
        string key,
        string repositoryPath,
        CancellationToken ct = default)
    {
        // Asked first, because git reports removing an absent key as a failure
        // and the helper here surfaces output rather than exit codes. A key that
        // was never there is the outcome the caller wanted.
        var existing = await GetConfigValueAsync(key, repositoryPath, ct).ConfigureAwait(false);

        if (existing.Failed || existing.Value is not { Length: > 0 })
        {
            return OperationResult.Ok();
        }

        var result = await RunAsync(
            repositoryPath, ["config", "--local", "--unset", key], LocalOperationTimeout, ct)
            .ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error ?? "The key could not be removed.");
    }

    /// <inheritdoc />
    public async Task<OperationResult<string?>> GetGlobalConfigValueAsync(
        string key,
        CancellationToken ct = default)
    {
        var result = await RunAsync(null, ["config", "--global", "--get", key],
            LocalOperationTimeout, ct).ConfigureAwait(false);

        // git exits 1 when the key is unset, which is the answer rather than a
        // failure.
        return OperationResult<string?>.Ok(result.Succeeded ? result.Value!.Trim() : null);
    }

    /// <inheritdoc />
    public async Task<OperationResult<string?>> GetConfigValueAsync(
        string key,
        string? repositoryPath = null,
        CancellationToken ct = default)
    {
        var result = await RunAsync(repositoryPath, ["config", "--get", key], LocalOperationTimeout, ct)
            .ConfigureAwait(false);

        // git exits 1 when the key is simply unset, which is not an error here.
        return OperationResult<string?>.Ok(result.Succeeded ? result.Value!.Trim() : null);
    }

    private async Task<OperationResult<string>> RunAsync(
        string? workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var git = _resolver.Resolve("git");
        if (git is null)
        {
            return OperationResult<string>.Fail(
                "git was not found on PATH. Install Git and make sure it is on PATH.",
                ExitCode.RepositoryUnavailable);
        }

        var environment = new Dictionary<string, string>
        {
            // Without this, an unattended fetch against a host needing
            // credentials pops a GUI prompt or blocks forever waiting on a
            // terminal that may not exist. Failing fast is what lets the
            // launcher fall through to offline mode (spec section 48).
            ["GIT_TERMINAL_PROMPT"] = "0",
        };

        var result = await _processes.RunAsync(
            new ProcessRequest(git, arguments, workingDirectory, environment),
            timeout,
            ct).ConfigureAwait(false);

        if (result.Failed || result.Value is null)
        {
            return OperationResult<string>.Fail(SecretRedactor.Redact(result.Error));
        }

        if (!result.Value.Succeeded)
        {
            var message = result.Value.StandardError.Trim();
            if (message.Length == 0)
            {
                message = $"git exited with code {result.Value.ExitCode}.";
            }

            // git echoes remote URLs in its errors, and those URLs can carry a
            // token, so nothing from git reaches a caller unredacted.
            return OperationResult<string>.Fail(SecretRedactor.Redact(message));
        }

        return OperationResult<string>.Ok(result.Value.StandardOutput);
    }
}
