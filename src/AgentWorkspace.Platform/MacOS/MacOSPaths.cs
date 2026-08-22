using AgentWorkspace.Models.Platform;
using AgentWorkspace.Platform.Abstractions;
using AgentWorkspace.Platform.Common;

namespace AgentWorkspace.Platform.MacOS;

/// <summary>
/// macOS storage layout using native conventions (spec section 16).
/// <para>
/// macOS is deliberately not treated as "Linux with a different home
/// directory". Application Support, Caches and Logs are three distinct roots
/// with different backup and purge semantics: Caches is expected to be
/// reclaimable by the system at any time, which is exactly right for cache and
/// per-launch runtime material and exactly wrong for the workspace clone.
/// </para>
/// <para>
/// XDG is available as an explicit opt-in for people who prefer their CLI
/// tools to sit under ~/.config on every platform, but the native layout is
/// the default.
/// </para>
/// </summary>
public sealed class MacOSPaths : PlatformPathsBase
{
    private const string AppFolder = "AgentWorkspaceLauncher";
    private const string LinuxStyleFolder = "agent-workspace-launcher";

    /// <summary>Set this to 1 to place launcher files under the XDG roots instead.</summary>
    public const string XdgOptInVariable = "AGENTCTL_USE_XDG";

    public MacOSPaths(IEnvironmentProvider environment, IFilePermissions permissions, HostPlatform host)
        : base(environment, permissions, host)
    {
    }

    /// <inheritdoc />
    protected override PlatformPathSet BuildPaths() =>
        IsXdgOptInEnabled() ? BuildXdgPaths() : BuildNativePaths();

    private bool IsXdgOptInEnabled() =>
        string.Equals(Environment.GetVariable(XdgOptInVariable), "1", StringComparison.Ordinal);

    private PlatformPathSet BuildNativePaths()
    {
        var library = Path.Combine(Environment.HomeDirectory, "Library");
        var support = Path.Combine(library, "Application Support", AppFolder);
        var caches = Path.Combine(library, "Caches", AppFolder);

        return new PlatformPathSet(
            Config: support,
            // Spec section 5 of the platform addendum names a state/
            // subdirectory here while its illustration shows machines.yaml at
            // the Application Support root. The labelled declaration is taken
            // as normative, which also keeps the config/state split consistent
            // with Windows.
            State: Path.Combine(support, "state"),
            Cache: Path.Combine(caches, "cache"),
            Logs: Path.Combine(library, "Logs", AppFolder),
            Runtime: Path.Combine(caches, "runtime"));
    }

    private PlatformPathSet BuildXdgPaths()
    {
        var config = Path.Combine(ResolveXdg("XDG_CONFIG_HOME", ".config"), LinuxStyleFolder);
        var data = Path.Combine(ResolveXdg("XDG_DATA_HOME", Path.Combine(".local", "share")), LinuxStyleFolder);
        var state = Path.Combine(ResolveXdg("XDG_STATE_HOME", Path.Combine(".local", "state")), LinuxStyleFolder);
        var cache = Path.Combine(ResolveXdg("XDG_CACHE_HOME", ".cache"), LinuxStyleFolder);

        return new PlatformPathSet(
            Config: config,
            State: data,
            Cache: cache,
            Logs: Path.Combine(state, "logs"),
            Runtime: Path.Combine(cache, "runtime"));
    }
}
