namespace Loadout.Platform.Abstractions;

/// <summary>
/// What the platform's own installer recorded about where it put the launcher.
/// </summary>
/// <remarks>
/// An update has to know how the copy it is replacing was installed: a copy
/// the MSI installed is updated by the MSI, or Windows goes on believing the
/// old version is installed. Only Windows has an installer that records this;
/// everywhere else the answer is that nothing did.
/// </remarks>
public interface IInstallRecord
{
    /// <summary>The folder the installer put the launcher in, or null when it installed nothing here.</summary>
    string? InstalledPath();
}

/// <summary>A platform whose installer records nothing.</summary>
public sealed class NoInstallRecord : IInstallRecord
{
    /// <inheritdoc />
    public string? InstalledPath() => null;
}
