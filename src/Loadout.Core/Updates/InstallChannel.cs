namespace Loadout.Core.Updates;

/// <summary>How the running copy of the launcher was put on this machine.</summary>
public enum InstallChannel
{
    /// <summary>
    /// Unpacked from a release archive, or anything else that is not the
    /// installer's: updated by putting the new binary in place, keeping the
    /// old one beside it.
    /// </summary>
    Archive,

    /// <summary>
    /// Installed by the MSI, directly or through winget, which installs the
    /// same MSI: updated by running the newer MSI, so Windows goes on knowing
    /// which version is installed.
    /// </summary>
    Installer,
}

/// <summary>Telling the two kinds of copy apart.</summary>
/// <remarks>
/// <para>
/// From what the MSI itself writes rather than from a marker of our own: the
/// MSI records the folder it installed into under
/// <c>HKCU\Software\Loadout\InstalledPath</c>, so a copy is the installer's
/// exactly when it is running from that folder. Every copy already installed
/// carries the value, which a new marker file would not.
/// </para>
/// <para>
/// A copy elsewhere on a machine that also has the MSI installed is an
/// archive copy, because it is: running the MSI would update the other one.
/// </para>
/// </remarks>
public static class InstallChannels
{
    /// <summary>The channel for an executable, given the folder the MSI says it installed into.</summary>
    /// <param name="executable">The running executable's full path.</param>
    /// <param name="installedBin">The MSI's recorded folder, or null when there is none.</param>
    public static InstallChannel Detect(string executable, string? installedBin)
    {
        if (string.IsNullOrWhiteSpace(executable) || string.IsNullOrWhiteSpace(installedBin))
        {
            return InstallChannel.Archive;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(executable));

        return directory is not null && string.Equals(
            Path.TrimEndingDirectorySeparator(directory),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(installedBin)),
            StringComparison.OrdinalIgnoreCase)
            ? InstallChannel.Installer
            : InstallChannel.Archive;
    }

    /// <summary>Where an archive install keeps the binary it replaced.</summary>
    public static string PreviousOf(string executable) => executable + ".previous";
}
