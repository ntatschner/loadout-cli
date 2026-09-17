using FluentAssertions;
using Loadout.Core.Git;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Taking one branch into another, against real git.
/// </summary>
/// <remarks>
/// The three outcomes are different enough to matter to whoever is watching:
/// a fast-forward leaves the history a reviewer read, a merge commit does
/// not, and a conflict must leave the repository exactly as it was. The
/// third is the one worth testing hardest — a merge stopped half way is a
/// repository somebody has to rescue by hand, and nothing about the code
/// says whether the abort happened.
/// </remarks>
public sealed class GitMergeTests : IAsyncLifetime
{
    private readonly ThrottledProcessLauncher _processes = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-merge-" + Guid.NewGuid().ToString("N"));

    private GitManager _git = null!;
    private string _repository = null!;

    public async Task InitializeAsync()
    {
        _git = new GitManager(_processes, new ExecutableResolver(new FakeEnvironmentProvider(_root, new Dictionary<string, string>())
        {
            PathDirectories = Environment.GetEnvironmentVariable("PATH")?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [],
            ExecutableExtensions = OperatingSystem.IsWindows() ? [".exe", ".cmd"] : [string.Empty],
        }, []));

        _repository = Path.Combine(_root, "repo");
        Directory.CreateDirectory(_repository);

        await GitAsync("init", "-b", "main");
        await GitAsync("config", "user.email", "tests@example.invalid");
        await GitAsync("config", "user.name", "Merge Tests");
        await GitAsync("config", "core.excludesFile", string.Empty);

        await File.WriteAllTextAsync(Path.Combine(_repository, "README.md"), "one\n");
        await GitAsync("add", ".");
        await GitAsync("commit", "--message", "initial");
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return Task.CompletedTask;
    }

    private async Task<string> GitAsync(params string[] arguments)
    {
        var result = await _processes.RunAsync(
            new ProcessRequest("git", arguments, _repository), TimeSpan.FromSeconds(60));

        result.Succeeded.Should().BeTrue(result.Error);

        return result.Value!.StandardOutput;
    }

    /// <summary>A branch with one commit on it, made in a worktree so the repository's own tree is untouched.</summary>
    [Fact]
    public async Task A_run_can_make_its_worktree_on_a_repository_whose_branch_is_called_teams()
    {
        // The exact failure from the first real run, which happened on a
        // branch called "teams":
        //
        //   fatal: cannot lock ref 'refs/heads/teams/20260917-1036-16f5/implementer-1':
        //   'refs/heads/teams' exists
        //
        // Git refs are files. refs/heads/teams being a file means nothing can
        // live under it, so every hierarchical run branch was impossible on
        // the one branch this feature was written on.
        await GitAsync("branch", "teams");

        var branch = Loadout.Agents.Teams.TeamRunner.BranchFor("20260917-1036-16f5", "implementer/1");

        var made = await _git.AddWorktreeAsync(
            _repository, Path.Combine(_root, "tree"), branch);

        made.Succeeded.Should().BeTrue(made.Error ?? "the worktree should have been created");

        (await GitAsync("branch", "--list", branch)).Trim().Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_run_does_not_stop_anybody_creating_a_branch_called_teams_afterwards()
    {
        // The other half, and the worse one: a hierarchical run branch would
        // have made refs/heads/teams a directory, so an ordinary name would be
        // taken for good by a feature nobody asked to name their branches.
        var branch = Loadout.Agents.Teams.TeamRunner.BranchFor("20260917-1036-16f5", "implementer/1");

        (await _git.AddWorktreeAsync(_repository, Path.Combine(_root, "tree"), branch))
            .Succeeded.Should().BeTrue();

        await GitAsync("branch", "teams");

        (await GitAsync("branch", "--list", "teams")).Trim().Should().NotBeEmpty();
    }

    [Fact]
    public void A_run_branch_has_no_path_in_it_at_all()
    {
        // The rule, said once: no slash means no ref that has to be a
        // directory, which is the whole of the fix.
        Loadout.Agents.Teams.TeamRunner.BranchFor("20260917-1036-16f5", "implementer/1")
            .Should().NotContain("/")
            .And.Be("teams-20260917-1036-16f5-implementer-1");
    }

    private async Task<string> BranchAsync(string branch, string file, string content)
    {
        var path = Path.Combine(_root, "trees", branch.Replace('/', '-'));

        var added = await _git.AddWorktreeAsync(_repository, path, branch);
        added.Succeeded.Should().BeTrue(added.Error);

        await File.WriteAllTextAsync(Path.Combine(path, file), content);

        var work = new ProcessRequest("git", ["add", "."], path);
        (await _processes.RunAsync(work, TimeSpan.FromSeconds(60))).Value!.Succeeded.Should().BeTrue();

        var commit = new ProcessRequest("git", ["commit", "--message", $"add {file}"], path);
        (await _processes.RunAsync(commit, TimeSpan.FromSeconds(60))).Value!.Succeeded.Should().BeTrue();

        return path;
    }

    [Fact]
    public async Task A_branch_ahead_of_the_main_one_arrives_as_a_fast_forward()
    {
        await BranchAsync("teams/run/one", "pong.txt", "pong");

        var merged = await _git.MergeAsync(_repository, "teams/run/one");

        merged.Succeeded.Should().BeTrue(merged.Error);
        merged.Value!.Merged.Should().BeTrue();
        merged.Value.FastForward.Should().BeTrue("nothing happened on the main branch meanwhile");
        merged.Value.Conflicts.Should().BeEmpty();

        File.Exists(Path.Combine(_repository, "pong.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task A_branch_that_diverged_arrives_as_a_merge_commit()
    {
        await BranchAsync("teams/run/two", "pong.txt", "pong");

        // Something else landed on the main branch in the meantime.
        await File.WriteAllTextAsync(Path.Combine(_repository, "other.txt"), "other\n");
        await GitAsync("add", ".");
        await GitAsync("commit", "--message", "meanwhile");

        var merged = await _git.MergeAsync(_repository, "teams/run/two");

        merged.Succeeded.Should().BeTrue(merged.Error);
        merged.Value!.Merged.Should().BeTrue();
        merged.Value.FastForward.Should().BeFalse();

        File.Exists(Path.Combine(_repository, "pong.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_repository, "other.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task A_conflict_merges_nothing_and_leaves_the_repository_as_it_was()
    {
        await BranchAsync("teams/run/three", "README.md", "theirs\n");

        await File.WriteAllTextAsync(Path.Combine(_repository, "README.md"), "ours\n");
        await GitAsync("add", ".");
        await GitAsync("commit", "--message", "ours");

        var head = (await GitAsync("rev-parse", "HEAD")).Trim();

        var merged = await _git.MergeAsync(_repository, "teams/run/three");

        merged.Succeeded.Should().BeTrue(merged.Error);
        merged.Value!.Merged.Should().BeFalse();
        merged.Value.Conflicts.Should().Contain("README.md");

        (await GitAsync("rev-parse", "HEAD")).Trim().Should().Be(head, "nothing was committed");
        (await File.ReadAllTextAsync(Path.Combine(_repository, "README.md"))).Should().Be("ours\n");

        // The merge was aborted rather than left in progress: a repository
        // mid-merge refuses the next one, and this proves it does not.
        (await GitAsync("status", "--porcelain")).Trim().Should().BeEmpty();
    }

    [Fact]
    public async Task A_merged_branch_and_its_tree_can_be_cleared_away_and_an_unmerged_one_cannot()
    {
        var path = await BranchAsync("teams/run/four", "pong.txt", "pong");

        // Before the merge, the branch holds the only copy of that commit
        // and git says so rather than letting it go.
        (await _git.DeleteMergedBranchAsync(_repository, "teams/run/four")).Failed.Should().BeTrue();

        (await _git.MergeAsync(_repository, "teams/run/four")).Value!.Merged.Should().BeTrue();

        (await _git.RemoveWorktreeAsync(_repository, path)).Succeeded.Should().BeTrue();
        Directory.Exists(path).Should().BeFalse();

        (await _git.DeleteMergedBranchAsync(_repository, "teams/run/four")).Succeeded.Should().BeTrue();
        (await GitAsync("branch", "--list", "teams/run/four")).Trim().Should().BeEmpty();
    }

    [Fact]
    public async Task A_tree_with_work_nobody_committed_is_kept()
    {
        var path = await BranchAsync("teams/run/five", "pong.txt", "pong");

        await File.WriteAllTextAsync(Path.Combine(path, "unsaved.txt"), "not committed\n");

        var removed = await _git.RemoveWorktreeAsync(_repository, path);

        removed.Failed.Should().BeTrue("the refusal is the guard, not an inconvenience");
        Directory.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task A_branch_that_is_not_there_is_reported_rather_than_read_as_a_conflict()
    {
        var merged = await _git.MergeAsync(_repository, "teams/run/nowhere");

        merged.Failed.Should().BeTrue();
        merged.Error.Should().NotBeNullOrWhiteSpace();
    }
}
