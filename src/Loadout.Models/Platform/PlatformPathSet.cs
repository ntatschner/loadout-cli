namespace Loadout.Models.Platform;

/// <summary>
/// The launcher's storage locations on this machine (spec section 16).
/// <para>
/// These are deliberately separate properties rather than one "app data root".
/// No single root can express the real layouts: Windows splits roaming config
/// (<c>%APPDATA%</c>) from local state (<c>%LOCALAPPDATA%</c>); macOS puts
/// state under <c>Application Support</c> but cache under <c>Caches</c> and
/// logs under <c>Logs</c>; Linux splits four ways under XDG.
/// </para>
/// </summary>
/// <param name="Config">Holds <c>config.yaml</c>. Roaming on Windows.</param>
/// <param name="State">Holds <c>machines.yaml</c> and the workspace clone. Machine-local, never roamed.</param>
/// <param name="Cache">Discardable data. Safe to delete at any time.</param>
/// <param name="Logs">Launcher logs. Never inside an application repository (spec section 80).</param>
/// <param name="Runtime">Per-launch isolated runtime directories (spec section 82).</param>
public sealed record PlatformPathSet(
    string Config,
    string State,
    string Cache,
    string Logs,
    string Runtime)
{
    /// <summary>The launcher configuration file. Precedence tier 2 (spec section 90).</summary>
    public string ConfigFile => System.IO.Path.Combine(Config, "config.yaml");

    /// <summary>Machine-local project path mappings. Never committed anywhere (spec section 15).</summary>
    public string MachinesFile => System.IO.Path.Combine(State, "machines.yaml");

    /// <summary>Local clone of the central agent-workspaces repository.</summary>
    public string WorkspaceClone => System.IO.Path.Combine(State, "workspace");
}
