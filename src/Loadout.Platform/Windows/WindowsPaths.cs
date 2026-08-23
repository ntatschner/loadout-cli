using Loadout.Models.Platform;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;

namespace Loadout.Platform.Windows;

/// <summary>
/// Windows storage layout (spec section 16).
/// <para>
/// The split between APPDATA and LOCALAPPDATA is deliberate and load-bearing
/// on a domain-joined machine with roaming profiles. Preferences roam;
/// machines.yaml, the workspace clone, caches, logs and runtime state must
/// not, because they describe this machine specifically and roaming them would
/// carry one machine's absolute paths onto another.
/// </para>
/// </summary>
public sealed class WindowsPaths : PlatformPathsBase
{
    private const string AppFolder = "Loadout";

    /// <summary>What the directory was called before the tool was renamed.</summary>
    private const string LegacyAppFolder = "AgentWorkspaceLauncher";

    public WindowsPaths(IEnvironmentProvider environment, IFilePermissions permissions, HostPlatform host)
        : base(environment, permissions, host)
    {
    }

    /// <inheritdoc />
    protected override PlatformPathSet BuildPaths()
    {
        var roaming = Environment.GetVariable("APPDATA")
            ?? Path.Combine(Environment.HomeDirectory, "AppData", "Roaming");

        var local = Environment.GetVariable("LOCALAPPDATA")
            ?? Path.Combine(Environment.HomeDirectory, "AppData", "Local");

        var localRoot = Adopt(local, AppFolder, LegacyAppFolder);

        return new PlatformPathSet(
            Config: Adopt(roaming, AppFolder, LegacyAppFolder),
            State: localRoot,
            Cache: Path.Combine(localRoot, "cache"),
            Logs: Path.Combine(localRoot, "logs"),
            Runtime: Path.Combine(localRoot, "runtime"));
    }
}
