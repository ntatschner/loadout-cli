using AgentWorkspace.Models.Platform;
using AgentWorkspace.Platform.Abstractions;
using AgentWorkspace.Platform.Common;

namespace AgentWorkspace.Platform.Linux;

/// <summary>
/// Linux storage layout following the XDG Base Directory Specification
/// (spec section 16).
/// <para>
/// The directory name is lowercase and hyphenated, unlike the PascalCase name
/// used on Windows and macOS, because that is the convention XDG consumers
/// expect. Each of the four XDG roots is honoured separately rather than
/// collapsed into one, so a user who redirects only XDG_CACHE_HOME gets what
/// they asked for.
/// </para>
/// </summary>
public sealed class LinuxPaths : PlatformPathsBase
{
    private const string AppFolder = "agent-workspace-launcher";

    public LinuxPaths(IEnvironmentProvider environment, IFilePermissions permissions, HostPlatform host)
        : base(environment, permissions, host)
    {
    }

    /// <inheritdoc />
    protected override PlatformPathSet BuildPaths()
    {
        var config = Path.Combine(ResolveXdg("XDG_CONFIG_HOME", ".config"), AppFolder);

        // The workspace clone and machines.yaml are user data that a person
        // would be upset to lose, so they belong under XDG_DATA_HOME rather
        // than XDG_STATE_HOME, which is for material that can be regenerated.
        var data = Path.Combine(ResolveXdg("XDG_DATA_HOME", Path.Combine(".local", "share")), AppFolder);

        var state = Path.Combine(ResolveXdg("XDG_STATE_HOME", Path.Combine(".local", "state")), AppFolder);
        var cache = Path.Combine(ResolveXdg("XDG_CACHE_HOME", ".cache"), AppFolder);

        return new PlatformPathSet(
            Config: config,
            State: data,
            Cache: cache,
            Logs: Path.Combine(state, "logs"),
            Runtime: Path.Combine(cache, "runtime"));
    }
}
