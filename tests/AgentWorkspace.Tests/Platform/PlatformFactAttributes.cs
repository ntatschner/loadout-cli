using Xunit;

namespace AgentWorkspace.Tests.Platform;

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
