using Loadout.Core.Git;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Project identity depends on these (spec section 29). If two spellings of one
/// remote fail to compare equal, the same repository registers as two projects
/// on two machines and the whole cross-machine story quietly breaks.
/// </summary>
public sealed class GitRemoteTests
{
    [Theory]
    [InlineData("ssh://git.internal/apps/starstats.git", "git@git.internal:apps/starstats.git")]
    [InlineData("ssh://git@git.internal/apps/starstats.git", "git@git.internal:apps/starstats")]
    [InlineData("https://github.com/org/repo.git", "https://github.com/org/repo")]
    [InlineData("https://GitHub.com/org/repo", "https://github.com/org/repo")]
    [InlineData("ssh://git.internal/apps/starstats.git/", "ssh://git.internal/apps/starstats")]
    public void Equivalent_spellings_of_one_remote_match(string left, string right) =>
        GitRemote.AreEquivalent(left, right).Should().BeTrue();

    [Fact]
    public void A_non_default_ssh_port_still_matches_the_same_repository()
    {
        // One server reached over a custom port in one config and the default
        // in another is the same repository. Treating those as two projects is
        // a worse failure than the theoretical case of two repositories that
        // differ only by port.
        GitRemote.AreEquivalent(
            "ssh://git@git.internal:2222/apps/starstats.git",
            "git@git.internal:apps/starstats.git")
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("https://github.com/org/repo", "https://github.com/other/repo")]
    [InlineData("https://github.com/org/repo", "https://gitlab.com/org/repo")]
    [InlineData("https://github.com/org/Repo", "https://github.com/org/repo")]
    public void Different_repositories_do_not_match(string left, string right) =>
        GitRemote.AreEquivalent(left, right).Should().BeFalse();

    [Fact]
    public void Repository_paths_stay_case_sensitive()
    {
        // The host is lower-cased because DNS is case-insensitive, but many
        // Git hosts treat the path as case-sensitive, so folding it would merge
        // two genuinely distinct repositories.
        GitRemote.Canonicalise("https://EXAMPLE.com/Org/Repo")
            .Should().Be("example.com/Org/Repo");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_remotes_never_compare_equal(string? value)
    {
        // A repository with no remote must not collide with every other
        // repository that also has none.
        GitRemote.Canonicalise(value).Should().BeNull();
        GitRemote.AreEquivalent(value, value).Should().BeFalse();
    }

    [Theory]
    [InlineData("ssh://git.internal/apps/starstats.git", "starstats")]
    [InlineData("git@github.com:org/my-repo.git", "my-repo")]
    [InlineData("https://github.com/org/repo", "repo")]
    public void Repository_names_are_inferred_for_slug_suggestions(string remote, string expected) =>
        GitRemote.InferRepositoryName(remote).Should().Be(expected);
}
