using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Windows;

/// <summary>
/// Restricts files and directories to the current user with an explicit ACL.
/// <para>
/// This is not a no-op standing in for the Unix implementation. Runtime
/// directories hold compiled context and generated settings, and config holds
/// secret references (spec section 82), so they need the same protection on
/// Windows that 0600 gives elsewhere. Inheritance is disabled and inherited
/// entries dropped, otherwise a permissive ACL on a parent directory would
/// still grant access.
/// </para>
/// </summary>
public sealed class WindowsFilePermissions : IFilePermissions
{
    /// <inheritdoc />
    public OperationResult RestrictToCurrentUser(string filePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return OperationResult.Fail("Windows ACLs are not available on this platform.");
        }

        return RestrictFile(filePath);
    }

    /// <inheritdoc />
    public OperationResult RestrictDirectoryToCurrentUser(string directoryPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return OperationResult.Fail("Windows ACLs are not available on this platform.");
        }

        return RestrictDirectory(directoryPath);
    }

    /// <inheritdoc />
    public OperationResult MakeExecutable(string filePath)
    {
        // Windows decides executability by extension and by the ACL's execute
        // right, which the owner already holds. There is nothing to set, and
        // reporting success is honest rather than a silent skip.
        return File.Exists(filePath)
            ? OperationResult.Ok()
            : OperationResult.Fail($"Cannot mark a missing file executable: {filePath}");
    }

    [SupportedOSPlatform("windows")]
    private static OperationResult RestrictFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return OperationResult.Fail(
                    $"Cannot set permissions on a path that does not exist: {filePath}");
            }

            var info = new FileInfo(filePath);
            var security = info.GetAccessControl();

            var user = GetCurrentUserSid();
            if (user is null)
            {
                return OperationResult.Fail("Could not determine the current Windows user.");
            }

            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            RemoveAllRules(security);
            security.AddAccessRule(new FileSystemAccessRule(
                user,
                FileSystemRights.FullControl,
                AccessControlType.Allow));

            info.SetAccessControl(security);
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or PrivilegeNotHeldException or PlatformNotSupportedException)
        {
            return OperationResult.Fail($"Could not set permissions on '{filePath}': {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static OperationResult RestrictDirectory(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                return OperationResult.Fail(
                    $"Cannot set permissions on a path that does not exist: {directoryPath}");
            }

            var info = new DirectoryInfo(directoryPath);
            var security = info.GetAccessControl();

            var user = GetCurrentUserSid();
            if (user is null)
            {
                return OperationResult.Fail("Could not determine the current Windows user.");
            }

            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            RemoveAllRules(security);
            security.AddAccessRule(new FileSystemAccessRule(
                user,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            info.SetAccessControl(security);
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or PrivilegeNotHeldException or PlatformNotSupportedException)
        {
            return OperationResult.Fail($"Could not set permissions on '{directoryPath}': {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static IdentityReference? GetCurrentUserSid() => WindowsIdentity.GetCurrent().User;

    [SupportedOSPlatform("windows")]
    private static void RemoveAllRules(FileSystemSecurity security)
    {
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            security.RemoveAccessRuleSpecific(rule);
        }
    }
}
