namespace Loadout.Models.Configuration;

/// <summary>
/// <c>workspace.yaml</c> at the root of the central repository
/// (spec section 91). Lets an older launcher refuse a newer workspace loudly
/// instead of misreading it.
/// </summary>
public sealed class WorkspaceManifest
{
    public int WorkspaceSchema { get; set; } = 1;

    /// <summary>Oldest launcher version that understands this workspace, e.g. <c>1.0</c>.</summary>
    public string MinimumLauncherVersion { get; set; } = "0.1";

    public string Name { get; set; } = string.Empty;
}
