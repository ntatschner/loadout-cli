using FluentAssertions;
using Loadout.Core.Checkpoints;
using Loadout.Models.Checkpoints;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a checkpoint may be called, and what returning to one says.
/// </summary>
/// <remarks>
/// The binding is the only new thing here: backups, Git, handoffs and the
/// ledger each already held a piece. So the parts worth testing are the two
/// that are this type's own — the name, which becomes a filename, and the
/// account it gives of a repository it deliberately refuses to move.
/// </remarks>
public sealed class CheckpointTests
{
    [Theory]
    [InlineData("before-the-refactor")]
    [InlineData("v2.1")]
    [InlineData("a")]
    [InlineData("Release_1")]
    public void An_ordinary_name_is_accepted(string name) =>
        CheckpointNames.Rejection(name).Should().BeNull();

    [Theory]
    [InlineData("before/the-refactor")]
    [InlineData("..")]
    [InlineData("../escape")]
    [InlineData("nested\\name")]
    public void A_name_that_is_really_a_path_is_refused(string name)
    {
        // The name becomes a filename. "before the refactor/v2" is the sort of
        // thing somebody types without thinking of it as a path at all, and a
        // separator in it would write outside the directory it belongs in.
        CheckpointNames.Rejection(name).Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_missing_name_is_refused(string? name) =>
        CheckpointNames.Rejection(name).Should().NotBeNull();

    [Fact]
    public void A_name_that_starts_with_a_dot_is_refused()
    {
        // A leading dot makes a hidden file on Unix and is the first character
        // of "..", so it is easier to exclude than to reason about.
        CheckpointNames.Rejection(".hidden").Should().NotBeNull();
    }

    [Fact]
    public void A_name_longer_than_the_limit_is_refused() =>
        CheckpointNames.Rejection(new string('a', 65)).Should().NotBeNull();

    [Fact]
    public void Returning_names_the_commit_and_refuses_to_move_it()
    {
        var advice = CheckpointService.Advice(new Checkpoint
        {
            RepositoryCommit = "0123456789abcdef0123",
            RepositoryBranch = "main",
        });

        advice.Should().NotBeNull();
        advice!.Should().Contain("0123456789ab");
        advice.Should().Contain("main");

        // Checking a commit out can discard work nobody asked to lose. Doing
        // that because somebody typed a checkpoint name is the surprise that
        // preview-before-mutation exists to prevent.
        advice.Should().Contain("does not move it");
    }

    [Fact]
    public void A_checkpoint_taken_on_a_dirty_tree_says_the_commit_is_not_the_whole_story()
    {
        var advice = CheckpointService.Advice(new Checkpoint
        {
            RepositoryCommit = "0123456789abcdef0123",
            RepositoryBranch = "main",
            RepositoryWasDirty = true,
        });

        // Said now rather than discovered on the way back: the commit does not
        // describe what was actually on disk.
        advice!.Should().Contain("uncommitted changes");
    }

    [Fact]
    public void A_clean_tree_is_not_warned_about()
    {
        CheckpointService.Advice(new Checkpoint
        {
            RepositoryCommit = "0123456789abcdef0123",
            RepositoryWasDirty = false,
        })!.Should().NotContain("uncommitted");
    }

    [Fact]
    public void A_checkpoint_with_no_commit_says_nothing_about_a_repository()
    {
        // A reference that was never taken is reported as absent rather than
        // described. Inventing a commit here would send somebody looking for
        // one that does not exist.
        CheckpointService.Advice(new Checkpoint { RepositoryCommit = null })
            .Should().BeNull();
    }

    [Fact]
    public void A_short_commit_is_not_cut_past_its_end()
    {
        // Shortening is for reading, and a hash shorter than the window is
        // still a hash. Slicing to a fixed twelve would throw on it.
        CheckpointService.Advice(new Checkpoint { RepositoryCommit = "abc123" })!
            .Should().Contain("abc123");
    }
}
