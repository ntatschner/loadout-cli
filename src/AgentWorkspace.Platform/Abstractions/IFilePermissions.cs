using AgentWorkspace.Models.Results;

namespace AgentWorkspace.Platform.Abstractions;

/// <summary>
/// Applies file protection appropriate to the platform (spec sections 82, 83).
/// <para>
/// On Unix this means real mode bits: 0600 for secret and runtime material,
/// 0700 for the directories holding it, and the executable bit for generated
/// scripts. On Windows the equivalent is an ACL restricted to the current user.
/// It is not a no-op there, because the same runtime files hold the same
/// sensitive content on every platform.
/// </para>
/// </summary>
public interface IFilePermissions
{
    /// <summary>Restricts a file to the current user only. Unix 0600.</summary>
    OperationResult RestrictToCurrentUser(string filePath);

    /// <summary>Restricts a directory to the current user only. Unix 0700.</summary>
    OperationResult RestrictDirectoryToCurrentUser(string directoryPath);

    /// <summary>Marks a generated script executable. A no-op where the concept does not apply.</summary>
    OperationResult MakeExecutable(string filePath);
}
