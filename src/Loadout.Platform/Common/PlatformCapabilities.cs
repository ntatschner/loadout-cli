using Loadout.Models.Platform;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Common;

/// <summary>
/// Reports capability availability from a set computed once per process.
/// <para>
/// The set is built by the platform registration, which is the only place that
/// knows which concrete implementations were selected. Keeping the reasoning
/// there rather than in a switch on the operating system is what stops the
/// forbidden pattern from creeping back in: nothing asks "am I on macOS?", it
/// asks "did a working clipboard tool get resolved?".
/// </para>
/// </summary>
public sealed class PlatformCapabilities : IPlatformCapabilities
{
    private readonly Lazy<IReadOnlyDictionary<PlatformCapability, CapabilityStatus>> _statuses;

    public PlatformCapabilities(Func<IReadOnlyList<CapabilityStatus>> probe)
    {
        // Lazy because several probes shell out, and a command such as
        // "loadout project list" should not pay for capability detection it
        // never reads.
        _statuses = new Lazy<IReadOnlyDictionary<PlatformCapability, CapabilityStatus>>(
            () => probe().ToDictionary(s => s.Capability));
    }

    /// <inheritdoc />
    public CapabilityStatus Query(PlatformCapability capability) =>
        _statuses.Value.TryGetValue(capability, out var status)
            ? status
            : CapabilityStatus.Unsupported(
                capability,
                "This capability was not probed on this platform, which is a gap in the launcher rather than "
                + "a limitation of the operating system.");

    /// <inheritdoc />
    public IReadOnlyList<CapabilityStatus> QueryAll() =>
        Enum.GetValues<PlatformCapability>().Select(Query).ToList();
}
