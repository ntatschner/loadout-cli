using FluentAssertions;
using Loadout.Core.Packs;
using Loadout.Models;
using Loadout.Models.Packs;
using Loadout.Models.Results;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which pack directories reach a library that loads content from them.
/// </summary>
/// <remarks>
/// The trust boundary, from the other side. <see cref="PackGate"/> decides
/// which packs stand approved; this is the step that turns those standings
/// into paths something will read, and the failure it has to not have is a
/// declared-but-unapproved pack arriving in that list. Its content becomes
/// instructions an agent follows, so nobody having read it is the whole of the
/// reason it must not load.
/// </remarks>
public sealed class PackDirectoriesTests
{
    [Fact]
    public async Task Only_a_pack_somebody_approved_at_its_pinned_commit_is_handed_over()
    {
        var packs = new StubPacks(
            new PackStanding(Named("approved"), PackStandingReason.Active, "abc"),
            new PackStanding(Named("unread"), PackStandingReason.NeverApproved, null),
            new PackStanding(Named("moved"), PackStandingReason.MovedSinceApproval, "old"),
            new PackStanding(Named("floating"), PackStandingReason.NotPinned, null));

        var directories = await PackDirectories.ApprovedAsync(packs, "teams");

        directories.Should().ContainSingle()
            .Which.Should().Be(Path.Combine("packs", "approved", "teams"));
    }

    [Fact]
    public async Task The_same_packs_give_a_different_directory_for_a_different_kind()
    {
        var packs = new StubPacks(new PackStanding(Named("approved"), PackStandingReason.Active, "abc"));

        (await PackDirectories.ApprovedAsync(packs, "specialists")).Should()
            .ContainSingle().Which.Should().EndWith(Path.Combine("approved", "specialists"));
    }

    [Fact]
    public async Task A_pack_that_is_not_on_this_machine_is_skipped_rather_than_named_as_a_path()
    {
        // DirectoryFor returns null when nothing was fetched. A path built from
        // that would be read as the working directory.
        var packs = new StubPacks(new PackStanding(Named("absent"), PackStandingReason.Active, "abc"))
        {
            Fetched = false,
        };

        (await PackDirectories.ApprovedAsync(packs, "teams")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_workspace_that_cannot_be_read_loads_no_packs_rather_than_failing()
    {
        // The built-in library has to load on a machine with no workspace at
        // all, so this returns nothing instead of throwing.
        var packs = new StubPacks { Readable = false };

        (await PackDirectories.ApprovedAsync(packs, "teams")).Should().BeEmpty();
    }

    private static SpecialistPack Named(string name) => new() { Name = name, Commit = "abc" };

    /// <summary>Standings and directories, and nothing that touches Git.</summary>
    private sealed class StubPacks(params PackStanding[] standing) : IPackService
    {
        public bool Readable { get; init; } = true;

        public bool Fetched { get; init; } = true;

        public Task<OperationResult<IReadOnlyList<PackStanding>>> StandingAsync(CancellationToken ct = default) =>
            Task.FromResult(Readable
                ? OperationResult<IReadOnlyList<PackStanding>>.Ok(standing)
                : OperationResult<IReadOnlyList<PackStanding>>.Fail("no workspace", ExitCode.GeneralFailure));

        public string? DirectoryFor(string name) =>
            Fetched ? Path.Combine("packs", name) : null;

        public Task<OperationResult<SpecialistPack>> AddAsync(
            string name, string remote, string reference = "main", CancellationToken ct = default) =>
            throw new InvalidOperationException("Reading directories fetches nothing.");

        public Task<OperationResult> ApproveAsync(string name, string approvedBy, CancellationToken ct = default) =>
            throw new InvalidOperationException("Reading directories approves nothing.");

        public Task<OperationResult<SpecialistPack>> UpdateAsync(string name, CancellationToken ct = default) =>
            throw new InvalidOperationException("Reading directories moves no pin.");

        public Task<OperationResult> RemoveAsync(string name, CancellationToken ct = default) =>
            throw new InvalidOperationException("Reading directories removes nothing.");
    }
}
