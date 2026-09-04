using FluentAssertions;
using Loadout.Core.Packs;
using Loadout.Models.Packs;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which packs may be loaded on this machine, and why the rest may not.
/// </summary>
/// <remarks>
/// A pack's content becomes instructions an agent follows, and the declaration
/// lives in a workspace anybody on the team can edit. So the declaration
/// proposes and this decides, from approvals that never leave the machine — the
/// same split command policy uses, guarding the same failure: a change reaching
/// your machine because it reached somebody else's repository.
/// </remarks>
public sealed class PackGateTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Other = "fedcba9876543210fedcba9876543210fedcba98";

    private static SpecialistPack Pack(string commit = Commit) =>
        new() { Name = "house", Remote = "https://example.invalid/house.git", Ref = "main", Commit = commit };

    private static PackApproval Approval(string commit = Commit) =>
        new() { Name = "house", Commit = commit, ApprovedBy = "someone", ApprovedUtc = DateTimeOffset.UtcNow };

    [Fact]
    public void A_pack_nobody_approved_does_not_load()
    {
        var standing = PackGate.Standing([Pack()], []).Single();

        // The default, and it has to be: a pack arriving in the workspace is a
        // proposal from whoever pushed it, not permission.
        standing.IsActive.Should().BeFalse();
        standing.Reason.Should().Be(PackStandingReason.NeverApproved);
    }

    [Fact]
    public void A_pack_approved_at_the_commit_it_is_pinned_to_loads()
    {
        PackGate.Standing([Pack()], [Approval()]).Single()
            .IsActive.Should().BeTrue();
    }

    [Fact]
    public void A_pack_that_moved_since_approval_stops_loading()
    {
        var standing = PackGate.Standing([Pack(Other)], [Approval(Commit)]).Single();

        // Approval is of a commit, never of a pack. Approving "the standards
        // pack" would mean approving whatever it says next week, which is
        // exactly what cannot be delegated to a file somebody else can push to.
        standing.IsActive.Should().BeFalse();
        standing.Reason.Should().Be(PackStandingReason.MovedSinceApproval);
        standing.ApprovedCommit.Should().Be(Commit);
    }

    [Fact]
    public void A_pack_pinned_to_nothing_loads_nothing()
    {
        var standing = PackGate.Standing([Pack(commit: string.Empty)], [Approval()]).Single();

        // A pack naming a branch and no commit would load whatever the branch
        // says today. That is the unpinned dependency this refuses, and an
        // approval on file does not rescue it.
        standing.IsActive.Should().BeFalse();
        standing.Reason.Should().Be(PackStandingReason.NotPinned);
    }

    [Fact]
    public void An_approval_for_a_different_pack_does_not_carry_over()
    {
        var elsewhere = new PackApproval
        {
            Name = "somebody-elses",
            Commit = Commit,
            ApprovedBy = "someone",
        };

        PackGate.Standing([Pack()], [elsewhere]).Single()
            .IsActive.Should().BeFalse();
    }

    [Fact]
    public void Nothing_declared_is_nothing_to_decide()
    {
        PackGate.Standing(null, null).Should().BeEmpty();
        PackGate.Standing([], [Approval()]).Should().BeEmpty();
    }

    [Fact]
    public void A_declaration_with_no_name_is_ignored_rather_than_matched()
    {
        // A nameless pack would match a nameless approval, and an empty string
        // is what a half-written file produces.
        PackGate.Standing(
            [new SpecialistPack { Commit = Commit }],
            [new PackApproval { Commit = Commit }])
            .Should().BeEmpty();
    }

    [Fact]
    public void Every_refusal_says_what_to_do_about_it()
    {
        var never = PackGate.Explain(PackGate.Standing([Pack()], []).Single());
        var moved = PackGate.Explain(PackGate.Standing([Pack(Other)], [Approval()]).Single());

        never.Should().Contain("pack approve");
        moved.Should().Contain("approved at");

        // The short commit, because nobody reads forty characters of hex, and
        // both commits appear so somebody can see what changed under them.
        moved.Should().Contain(Commit[..12]);
        moved.Should().Contain(Other[..12]);
    }

    [Theory]
    [InlineData("house/style")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData(".hidden")]
    public void A_pack_name_that_is_really_a_path_is_refused(string name)
    {
        // The name becomes a directory under the state root, and it arrives
        // from a file somebody else may have written.
        PackNames.Rejection(name).Should().NotBeNull();
    }

    [Theory]
    [InlineData("house")]
    [InlineData("house-style")]
    [InlineData("v2.1")]
    public void An_ordinary_pack_name_is_accepted(string name) =>
        PackNames.Rejection(name).Should().BeNull();

    [Fact]
    public void A_pack_layers_under_the_workspace_and_over_the_built_ins()
    {
        // The order is the decision, not an accident. A pack is house
        // standards from elsewhere; the workspace and the project belong to
        // this team and this project, so whatever they say has to win, or
        // adopting a pack would quietly overrule somebody's deliberate choice.
        ((int)Loadout.Models.Instructions.SpecialistOrigin.BuiltIn)
            .Should().BeLessThan((int)Loadout.Models.Instructions.SpecialistOrigin.Pack);

        ((int)Loadout.Models.Instructions.SpecialistOrigin.Pack)
            .Should().BeLessThan((int)Loadout.Models.Instructions.SpecialistOrigin.Workspace);
    }
}
