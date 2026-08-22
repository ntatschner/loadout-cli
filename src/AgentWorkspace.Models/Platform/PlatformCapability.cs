namespace AgentWorkspace.Models.Platform;

/// <summary>
/// Capabilities whose availability genuinely varies by operating system or by
/// what is installed on the machine.
/// <para>
/// This enum exists to satisfy the cross-platform contract (spec section 5):
/// a feature that cannot work somewhere must be "documented, detectable,
/// testable, exposed through diagnostics and gracefully handled" — never a
/// silent <c>if (macOS) disable</c>. Anything a platform cannot do is reported
/// through <see cref="CapabilityStatus"/> and surfaced by <c>agentctl doctor</c>.
/// </para>
/// </summary>
public enum PlatformCapability
{
    /// <summary>An OS-native credential store is reachable (Credential Manager / Secret Service / Keychain).</summary>
    NativeSecretStore,

    /// <summary>The launcher can allocate a pseudo-terminal it owns (ConPTY / forkpty).</summary>
    PseudoTerminal,

    /// <summary>Unix mode bits can be read and applied (chmod, executable bit).</summary>
    UnixFilePermissions,

    /// <summary>A desktop entry / shortcut / application bundle can be installed.</summary>
    DesktopIntegration,

    /// <summary>A graphical file manager can be opened at a path.</summary>
    FileManagerIntegration,

    /// <summary>The system clipboard can be written to.</summary>
    Clipboard,

    /// <summary>A new terminal emulator window can be spawned.</summary>
    TerminalSpawning,

    /// <summary>A graphical session is present. Headless servers report false (spec section 86).</summary>
    GraphicalSession,
}

/// <summary>Whether a capability is usable here, and if not, why not.</summary>
/// <param name="Capability">The capability being reported on.</param>
/// <param name="IsSupported">True when the capability can be used on this machine right now.</param>
/// <param name="Detail">
/// Why it is unavailable, or which implementation is providing it. Never null —
/// an unexplained "unsupported" is exactly the silent failure section 5 forbids.
/// </param>
public readonly record struct CapabilityStatus(
    PlatformCapability Capability,
    bool IsSupported,
    string Detail)
{
    public static CapabilityStatus Supported(PlatformCapability capability, string detail) =>
        new(capability, true, detail);

    public static CapabilityStatus Unsupported(PlatformCapability capability, string reason) =>
        new(capability, false, reason);
}
