namespace AgentWorkspace.Models.Platform;

/// <summary>The three Tier-1 operating systems (spec section 4).</summary>
public enum HostOperatingSystem
{
    Windows,
    Linux,
    MacOS,
}

/// <summary>Identifies the machine the launcher is running on.</summary>
/// <param name="OperatingSystem">Which Tier-1 OS this is.</param>
/// <param name="Architecture">Process architecture, e.g. X64 or Arm64.</param>
/// <param name="OperatingSystemDescription">Free-text OS version string for diagnostics.</param>
/// <param name="MachineName">Used to key machine-local state and to label workspace commits.</param>
public sealed record HostPlatform(
    HostOperatingSystem OperatingSystem,
    System.Runtime.InteropServices.Architecture Architecture,
    string OperatingSystemDescription,
    string MachineName)
{
    /// <summary>True on Linux and macOS. Used to select shared Unix implementations.</summary>
    public bool IsUnix => OperatingSystem is HostOperatingSystem.Linux or HostOperatingSystem.MacOS;

    /// <summary>The .NET runtime identifier for this machine, e.g. <c>osx-arm64</c>.</summary>
    public string RuntimeIdentifier => OperatingSystem switch
    {
        HostOperatingSystem.Windows => $"win-{ArchitectureMoniker}",
        HostOperatingSystem.Linux => $"linux-{ArchitectureMoniker}",
        HostOperatingSystem.MacOS => $"osx-{ArchitectureMoniker}",
        _ => "unknown",
    };

    private string ArchitectureMoniker => Architecture switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        System.Runtime.InteropServices.Architecture.X86 => "x86",
        _ => Architecture.ToString().ToLowerInvariant(),
    };
}
