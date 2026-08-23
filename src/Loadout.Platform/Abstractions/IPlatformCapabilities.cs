using Loadout.Models.Platform;

namespace Loadout.Platform.Abstractions;

/// <summary>
/// Reports which optional capabilities work on this machine.
/// <para>
/// This interface is the mechanism that satisfies the cross-platform contract
/// (spec section 5). Where a capability genuinely cannot exist, the launcher
/// records it here with a reason and lets diagnostics surface it, instead of
/// branching on the operating system and quietly dropping the feature.
/// </para>
/// </summary>
public interface IPlatformCapabilities
{
    /// <summary>Status of one capability, always with a stated reason when unsupported.</summary>
    CapabilityStatus Query(PlatformCapability capability);

    /// <summary>Every capability and its status, for the doctor report.</summary>
    IReadOnlyList<CapabilityStatus> QueryAll();
}
