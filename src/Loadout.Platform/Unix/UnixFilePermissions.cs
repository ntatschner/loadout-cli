using System.Runtime.Versioning;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Unix;

/// <summary>
/// Real Unix mode bits for Linux and macOS (spec sections 82, 83).
/// Shared between the two platforms because the semantics are identical;
/// the platforms diverge on paths, secrets and desktop integration, not here.
/// <para>
/// Marked unsupported on Windows so the platform-compatibility analyser
/// rejects any accidental call from shared code. The selector reaches it only
/// through an OperatingSystem check, which the analyser understands.
/// </para>
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class UnixFilePermissions : IFilePermissions
{
    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode OwnerOnlyDirectory =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <inheritdoc />
    public OperationResult RestrictToCurrentUser(string filePath) =>
        Apply(filePath, OwnerOnlyFile, requireDirectory: false);

    /// <inheritdoc />
    public OperationResult RestrictDirectoryToCurrentUser(string directoryPath) =>
        Apply(directoryPath, OwnerOnlyDirectory, requireDirectory: true);

    /// <inheritdoc />
    public OperationResult MakeExecutable(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return OperationResult.Fail($"Cannot mark a missing file executable: {filePath}");
            }

            // Add the execute bit alongside whatever read and write bits are
            // already set, rather than replacing the mode wholesale. The umask
            // that produced the existing mode is the user's business.
            var current = File.GetUnixFileMode(filePath);
            File.SetUnixFileMode(filePath, current | UnixFileMode.UserExecute);

            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"Could not mark '{filePath}' executable: {ex.Message}");
        }
    }

    private static OperationResult Apply(string path, UnixFileMode mode, bool requireDirectory)
    {
        try
        {
            var exists = requireDirectory ? Directory.Exists(path) : File.Exists(path);
            if (!exists)
            {
                return OperationResult.Fail(
                    $"Cannot set permissions on a path that does not exist: {path}");
            }

            File.SetUnixFileMode(path, mode);
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            return OperationResult.Fail($"Could not set permissions on '{path}': {ex.Message}");
        }
    }
}
