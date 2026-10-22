using System.Runtime.Versioning;
using Loadout.Platform.Abstractions;
using Microsoft.Win32;

namespace Loadout.Platform.Windows;

/// <summary>
/// What the MSI wrote about where it installed the launcher.
/// </summary>
/// <remarks>
/// The MSI's path component keeps <c>HKCU\Software\Loadout\InstalledPath</c>
/// as its key path (build/windows/loadout.wxs), so the value is there for as
/// long as the MSI install is, and gone once it is uninstalled.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsInstallRecord : IInstallRecord
{
    private const string Key = @"Software\Loadout";

    private const string Value = "InstalledPath";

    /// <inheritdoc />
    public string? InstalledPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);

            return key?.GetValue(Value) is string { Length: > 0 } path ? path : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Unreadable is the same as absent: an archive copy, updated the
            // archive's way, which is what every copy was before this.
            return null;
        }
    }
}
