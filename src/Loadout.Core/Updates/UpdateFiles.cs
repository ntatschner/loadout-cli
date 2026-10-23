using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Updates;

/// <summary>
/// Putting a binary in place and putting the old one back, for an archive
/// install.
/// </summary>
/// <remarks>
/// Apart from <see cref="UpdateService"/> because two processes do it: the
/// command updating in a terminal, which replaces its own image, and the
/// helper Studio's update starts, which replaces the daemon's once it has gone.
/// </remarks>
public static class UpdateFiles
{
    /// <summary>
    /// Puts the new binary in place, keeping the old one as
    /// <c>&lt;executable&gt;.previous</c>.
    /// </summary>
    /// <returns>Where the old binary was kept.</returns>
    public static OperationResult<string> Swap(string executable, string replacement, IFilePermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var kept = InstallChannels.PreviousOf(executable);

        try
        {
            if (File.Exists(kept))
            {
                File.Delete(kept);
            }

            // Windows refuses to overwrite a running image but allows it to be
            // renamed, which is what makes an in-place update possible at all.
            // Keeping the old binary rather than deleting it also means a bad
            // update can be undone, by hand or by Roll back.
            File.Move(executable, kept);

            try
            {
                File.Copy(replacement, executable, overwrite: true);
            }
            catch
            {
                // Put the working binary back rather than leaving the user with
                // nothing to run.
                File.Move(kept, executable, overwrite: true);
                throw;
            }

            permissions.MakeExecutable(executable);

            return OperationResult<string>.Ok(kept);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<string>.Fail(
                $"The new binary could not be put in place: {ex.Message}. "
                + "If the launcher is installed system-wide this may need elevated permissions.");
        }
    }

    /// <summary>
    /// Puts back the binary <see cref="Swap"/> kept, and discards the one it
    /// replaced.
    /// </summary>
    /// <remarks>
    /// The newer binary is not kept as the next <c>.previous</c>. A Roll back
    /// button that, pressed twice, rolled forward again would be a button whose
    /// meaning depended on how many times it had been pressed.
    /// </remarks>
    public static OperationResult Restore(string executable)
    {
        var kept = InstallChannels.PreviousOf(executable);

        if (!File.Exists(kept))
        {
            return OperationResult.Fail($"There is no previous binary at {kept} to put back.");
        }

        var discarded = executable + ".rolled-back";

        try
        {
            if (File.Exists(executable))
            {
                File.Move(executable, discarded, overwrite: true);
            }

            try
            {
                File.Move(kept, executable);
            }
            catch
            {
                if (File.Exists(discarded) && !File.Exists(executable))
                {
                    File.Move(discarded, executable);
                }

                throw;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"The previous binary could not be put back: {ex.Message}.");
        }

        try
        {
            File.Delete(discarded);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still running somewhere, on Windows. Left beside the binary,
            // where the next roll back or update replaces it.
        }

        return OperationResult.Ok();
    }
}
