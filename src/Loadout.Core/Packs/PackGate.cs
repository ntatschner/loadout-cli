using Loadout.Models.Packs;

namespace Loadout.Core.Packs;

/// <summary>Why a declared pack is not being loaded.</summary>
public enum PackStandingReason
{
    /// <summary>It is approved at the commit it is pinned to, and will load.</summary>
    Active,

    /// <summary>Nobody on this machine has approved it.</summary>
    NeverApproved,

    /// <summary>It was approved, and then pinned to a different commit.</summary>
    MovedSinceApproval,

    /// <summary>It names no commit, so there is nothing to approve or load.</summary>
    NotPinned,
}

/// <summary>Where a declared pack stands on this machine.</summary>
/// <param name="Pack">The declaration.</param>
/// <param name="Reason">Whether it will load, and why not when it will not.</param>
/// <param name="ApprovedCommit">The commit somebody approved, when one was.</param>
public sealed record PackStanding(
    SpecialistPack Pack,
    PackStandingReason Reason,
    string? ApprovedCommit)
{
    /// <summary>Whether this pack's specialists will be loaded.</summary>
    public bool IsActive => Reason == PackStandingReason.Active;
}

/// <summary>
/// Decides which declared packs may be loaded on this machine.
/// </summary>
/// <remarks>
/// <para>
/// The whole of the trust boundary, in one pure function. A pack's content
/// becomes instructions an agent follows, and the declaration lives in a
/// workspace anybody on the team can edit — so the declaration proposes and
/// this decides, using approvals that never leave the machine.
/// </para>
/// <para>
/// Approval is of a <em>commit</em>, never of a pack. Approving "the standards
/// pack" would mean approving whatever it says next week, which is precisely
/// the thing that cannot be delegated to a file somebody else can push to. A
/// pack whose pin moves goes back to unapproved, and says so rather than
/// quietly loading the old commit or the new one.
/// </para>
/// </remarks>
public static class PackGate
{
    /// <summary>Where every declared pack stands.</summary>
    /// <param name="declared">What the workspace declares.</param>
    /// <param name="approvals">What this machine has approved.</param>
    public static IReadOnlyList<PackStanding> Standing(
        IReadOnlyList<SpecialistPack>? declared,
        IReadOnlyList<PackApproval>? approvals)
    {
        var standing = new List<PackStanding>();

        foreach (var pack in declared ?? [])
        {
            if (pack.Name.Length == 0)
            {
                continue;
            }

            var approved = (approvals ?? [])
                .FirstOrDefault(approval => string.Equals(
                    approval.Name, pack.Name, StringComparison.OrdinalIgnoreCase));

            if (pack.Commit.Length == 0)
            {
                // Nothing to load and nothing to approve. A pack that names a
                // branch and no commit would load whatever the branch says
                // today, which is the unpinned dependency this exists to
                // refuse.
                standing.Add(new PackStanding(
                    pack, PackStandingReason.NotPinned, approved?.Commit));

                continue;
            }

            if (approved is null)
            {
                standing.Add(new PackStanding(
                    pack, PackStandingReason.NeverApproved, null));

                continue;
            }

            standing.Add(string.Equals(approved.Commit, pack.Commit, StringComparison.OrdinalIgnoreCase)
                ? new PackStanding(pack, PackStandingReason.Active, approved.Commit)
                : new PackStanding(pack, PackStandingReason.MovedSinceApproval, approved.Commit));
        }

        return standing;
    }

    /// <summary>How to say a standing to somebody, in a line.</summary>
    public static string Explain(PackStanding standing)
    {
        ArgumentNullException.ThrowIfNull(standing);

        return standing.Reason switch
        {
            PackStandingReason.Active =>
                $"loaded, at {Short(standing.Pack.Commit)}",

            PackStandingReason.NeverApproved =>
                $"not loaded: nobody on this machine has approved {Short(standing.Pack.Commit)}. "
                + $"Read it, then 'loadout pack approve {standing.Pack.Name}'.",

            PackStandingReason.MovedSinceApproval =>
                $"not loaded: approved at {Short(standing.ApprovedCommit)}, now pinned to "
                + $"{Short(standing.Pack.Commit)}. Approving is of a commit, not of a pack.",

            _ => "not loaded: pinned to no commit, so there is nothing to approve.",
        };
    }

    private static string Short(string? commit) =>
        commit is { Length: > 0 }
            ? commit[..Math.Min(12, commit.Length)]
            : "(none)";
}
