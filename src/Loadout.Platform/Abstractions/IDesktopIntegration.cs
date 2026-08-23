using Loadout.Models.Results;

namespace Loadout.Platform.Abstractions;

/// <summary>
/// Installs the graphical entry point for the launcher (spec section 44):
/// a Start Menu shortcut on Windows, a .desktop file on Linux, an application
/// bundle on macOS.
/// <para>
/// The desktop entry is a wrapper around the same core and TUI, never a second
/// application. Nothing here may assume a graphical session exists: spec
/// section 86 requires headless machines to stay fully usable, so absence is
/// reported as an unsupported capability.
/// </para>
/// </summary>
public interface IDesktopIntegration
{
    /// <summary>Whether the entry is currently installed for this user.</summary>
    OperationResult<bool> IsInstalled();

    /// <summary>Installs the entry for the current user. Never requires root (spec section 19).</summary>
    Task<OperationResult> InstallAsync(string executablePath, CancellationToken ct = default);

    /// <summary>Removes the entry.</summary>
    Task<OperationResult> UninstallAsync(CancellationToken ct = default);
}
