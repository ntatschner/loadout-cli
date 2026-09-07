using FluentAssertions;
using Loadout.Core.Git;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The commit at HEAD, read from the files git keeps rather than from git.
/// </summary>
/// <remarks>
/// Every layout git writes is laid out by hand here, because the one thing
/// this reader must never do is answer wrongly: a wrong commit keys a cache to
/// a tree it does not describe. Null is always allowed, so the negatives are
/// as important as the positives.
/// </remarks>
public sealed class GitHeadTests : IDisposable
{
    private const string Commit = "c33ae1c1f74c8bfb2eef2b14a8762379b2ccfe23";
    private const string Other = "672defd0000000000000000000000000000000ab";

    private readonly string _root;

    public GitHeadTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-head-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Not worth failing a run over a temp directory.
        }
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        return path;
    }

    [Fact]
    public void A_branch_with_a_loose_ref_reads_the_ref_file()
    {
        Write("repo/.git/HEAD", "ref: refs/heads/main\n");
        Write("repo/.git/refs/heads/main", Commit + "\n");

        GitHead.Read(Path.Combine(_root, "repo")).Should().Be(Commit);

        // From a subdirectory too, since a hook's working directory may be one.
        Directory.CreateDirectory(Path.Combine(_root, "repo", "src", "deep"));
        GitHead.Read(Path.Combine(_root, "repo", "src", "deep")).Should().Be(Commit);
    }

    [Fact]
    public void A_packed_branch_reads_packed_refs_and_prefers_the_loose_file()
    {
        Write("repo/.git/HEAD", "ref: refs/heads/main\n");
        Write(
            "repo/.git/packed-refs",
            "# pack-refs with: peeled fully-peeled sorted \n"
            + Other + " refs/heads/feature\n"
            + Commit + " refs/heads/main\n"
            + "^deadbeefdeadbeefdeadbeefdeadbeefdeadbeef\n");

        GitHead.Read(Path.Combine(_root, "repo")).Should().Be(Commit);

        // A loose file is newer than the pack and wins.
        Write("repo/.git/refs/heads/main", Other + "\n");
        GitHead.Read(Path.Combine(_root, "repo")).Should().Be(Other);
    }

    [Fact]
    public void A_detached_head_is_the_commit_itself()
    {
        Write("repo/.git/HEAD", Commit + "\n");

        GitHead.Read(Path.Combine(_root, "repo")).Should().Be(Commit);
    }

    [Fact]
    public void A_linked_worktree_reads_its_own_head_and_the_shared_refs()
    {
        Write("main/.git/HEAD", "ref: refs/heads/main\n");
        Write("main/.git/refs/heads/main", Other + "\n");
        Write("main/.git/refs/heads/feature", Commit + "\n");
        Write("main/.git/worktrees/wt/HEAD", "ref: refs/heads/feature\n");
        Write("main/.git/worktrees/wt/commondir", "../..\n");

        var gitdir = Path.Combine(_root, "main", ".git", "worktrees", "wt");

        Write("wt/.git", "gitdir: " + gitdir + "\n");

        GitHead.Read(Path.Combine(_root, "wt")).Should().Be(Commit);
    }

    [Fact]
    public void The_working_tree_root_is_the_directory_holding_dot_git_whether_file_or_directory()
    {
        Write("repo/.git/HEAD", Commit + "\n");
        Directory.CreateDirectory(Path.Combine(_root, "repo", "src", "deep"));
        Write("wt/.git", "gitdir: " + Path.Combine(_root, "repo", ".git") + "\n");
        Directory.CreateDirectory(Path.Combine(_root, "wt", "lib"));

        GitHead.WorkingTreeRoot(Path.Combine(_root, "repo", "src", "deep"))
            .Should().Be(Path.Combine(_root, "repo"));
        GitHead.WorkingTreeRoot(Path.Combine(_root, "wt", "lib"))
            .Should().Be(Path.Combine(_root, "wt"), "a linked worktree is its own tree");
        GitHead.WorkingTreeRoot(Path.Combine(_root, "nowhere")).Should().BeNull();
    }

    [Fact]
    public void Anything_not_understood_is_null_rather_than_a_guess()
    {
        GitHead.Read(Path.Combine(_root, "nowhere")).Should().BeNull();

        Write("plain/readme.txt", "no repository here");
        GitHead.Read(Path.Combine(_root, "plain")).Should().BeNull();

        Write("unborn/.git/HEAD", "ref: refs/heads/main\n");
        GitHead.Read(Path.Combine(_root, "unborn")).Should().BeNull("an unborn branch has no commit");

        Write("odd/.git/HEAD", "ref: refs/heads/main\n");
        Write("odd/.git/refs/heads/main", "not a commit\n");
        GitHead.Read(Path.Combine(_root, "odd")).Should().BeNull();

        Write("short/.git/HEAD", "c33ae1c\n");
        GitHead.Read(Path.Combine(_root, "short")).Should().BeNull("an abbreviated name is not the key");

        Write("escape/.git/HEAD", "ref: ../../secrets\n");
        GitHead.Read(Path.Combine(_root, "escape")).Should().BeNull();

        Write("dangling/.git", "gitdir: " + Path.Combine(_root, "gone") + "\n");
        GitHead.Read(Path.Combine(_root, "dangling")).Should().BeNull();
    }
}
