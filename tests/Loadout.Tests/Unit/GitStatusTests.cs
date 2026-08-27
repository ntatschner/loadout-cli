using FluentAssertions;
using Loadout.Core.Git;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Reading the branch, the head commit and the dirty flag out of one status.
/// </summary>
/// <remarks>
/// Every sample below was captured from a real repository — a fresh one with no
/// commits, one with an edited file, one with a detached head — rather than
/// written from the documentation. The placeholders are the reason: nothing in
/// the format suggests that a repository with no commits says
/// <c>(initial)</c> where a commit should be, and a parser that assumed a hash
/// would be there would read the word as one.
/// </remarks>
public sealed class GitStatusTests
{
    [Fact]
    public void A_clean_branch_gives_its_name_and_its_commit()
    {
        var (branch, head, isClean) = GitManager.ReadStatus("""
            # branch.oid d14056bda616de945d849692c77b19e1b47bd166
            # branch.head main
            """);

        branch.Should().Be("main");
        head.Should().Be("d14056bda616de945d849692c77b19e1b47bd166");
        isClean.Should().BeTrue();
    }

    [Fact]
    public void Upstream_and_ahead_behind_headers_are_not_changes()
    {
        // The headers vary with how the repository is configured. Counting any
        // of them as a modification would report every tracked branch dirty.
        var (branch, _, isClean) = GitManager.ReadStatus("""
            # branch.oid 2f3e513d6bfecc579c84ca174e0456b43e31235a
            # branch.head main
            # branch.upstream origin/main
            # branch.ab +0 -0
            """);

        branch.Should().Be("main");
        isClean.Should().BeTrue();
    }

    [Fact]
    public void A_repository_with_no_commits_has_no_head()
    {
        var (branch, head, isClean) = GitManager.ReadStatus("""
            # branch.oid (initial)
            # branch.head main
            """);

        branch.Should().Be("main");

        // Not the literal word "(initial)", which is what a parser expecting a
        // hash would hand to anything displaying a commit.
        head.Should().BeNull();
        isClean.Should().BeTrue();
    }

    [Fact]
    public void A_detached_head_is_not_a_branch()
    {
        var (branch, head, _) = GitManager.ReadStatus("""
            # branch.oid d14056bda616de945d849692c77b19e1b47bd166
            # branch.head (detached)
            """);

        branch.Should().BeNull();
        head.Should().Be("d14056bda616de945d849692c77b19e1b47bd166");
    }

    [Fact]
    public void A_modified_file_makes_the_tree_dirty()
    {
        var (branch, _, isClean) = GitManager.ReadStatus("""
            # branch.oid d14056bda616de945d849692c77b19e1b47bd166
            # branch.head main
            1 .M N... 100644 100644 100644 ce013625030ba8dba906f756967f9e9ca394464a ce013625030ba8dba906f756967f9e9ca394464a a.txt
            """);

        branch.Should().Be("main");
        isClean.Should().BeFalse();
    }

    [Fact]
    public void An_untracked_file_counts_as_dirty()
    {
        // It did under the old format too, and the status line showed it. A
        // quietly clean repository with new files in it would be a regression
        // nobody would notice until they trusted it.
        var (_, _, isClean) = GitManager.ReadStatus("""
            # branch.oid (initial)
            # branch.head main
            ? a.txt
            """);

        isClean.Should().BeFalse();
    }

    [Fact]
    public void A_file_whose_name_begins_with_a_hash_is_still_a_change()
    {
        // Entries are introduced by their own marker, so the hash lands in the
        // path rather than at the start of the line. Splitting on "starts with
        // #" alone would read this as a header and call the tree clean.
        var (branch, _, isClean) = GitManager.ReadStatus("""
            # branch.oid d14056bda616de945d849692c77b19e1b47bd166
            # branch.head main
            ? #notes.md
            """);

        branch.Should().Be("main");
        isClean.Should().BeFalse();
    }

    [Fact]
    public void Carriage_returns_do_not_become_part_of_the_branch_name()
    {
        var (branch, head, isClean) = GitManager.ReadStatus(
            "# branch.oid d14056bda616de945d849692c77b19e1b47bd166\r\n# branch.head main\r\n");

        branch.Should().Be("main");
        head.Should().Be("d14056bda616de945d849692c77b19e1b47bd166");
        isClean.Should().BeTrue();
    }

    [Fact]
    public void Nothing_at_all_is_reported_as_nothing_known()
    {
        var (branch, head, isClean) = GitManager.ReadStatus(string.Empty);

        branch.Should().BeNull();
        head.Should().BeNull();

        // No changes were listed, so there is nothing to call dirty.
        isClean.Should().BeTrue();
    }
}
