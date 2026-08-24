using Xunit;

namespace Loadout.Tests.Platform;

/// <summary>
/// A test that only makes sense on Windows.
/// <para>
/// Skipping is deliberate rather than a silent early return inside the test
/// body: the run summary then shows how many platform checks did not apply
/// here, which is what makes an incomplete CI leg visible instead of looking
/// like full coverage.
/// </para>
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows-only: exercises the Credential Manager and ACL behaviour.";
        }
    }
}

/// <summary>A theory that only makes sense on Windows.</summary>
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows-only: exercises Windows process and console behaviour.";
        }
    }
}

/// <summary>A test that only makes sense on Linux or macOS.</summary>
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Unix-only: exercises real chmod mode bits.";
        }
    }
}

/// <summary>
/// A Unix test that additionally cannot pass on macOS, where setting a pty
/// window size does not work.
/// </summary>
/// <remarks>
/// Skipped with the reason rather than deleted, and the same reason is reported
/// by <c>loadout doctor</c> as an unsupported capability. Spec section 35
/// requires a gap to be documented, detectable and graceful — a silently
/// missing test is none of those.
/// </remarks>
public sealed class WindowSizeFactAttribute : FactAttribute
{
    public WindowSizeFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Unix-only: exercises a Unix pseudo-terminal.";
        }
        else if (OperatingSystem.IsMacOS())
        {
            Skip = "Not supported on macOS: see "
                + "PlatformCapability.PseudoTerminalWindowSize for the measured reason.";
        }
    }
}

/// <summary>A test that only makes sense on macOS.</summary>
public sealed class MacOSFactAttribute : FactAttribute
{
    public MacOSFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip = "macOS-only: exercises the Keychain and bundle discovery.";
        }
    }
}

/// <summary>A test that only makes sense on Linux.</summary>
public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip = "Linux-only: exercises XDG behaviour and desktop entry installation.";
        }
    }
}
