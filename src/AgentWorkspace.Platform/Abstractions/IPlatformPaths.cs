using AgentWorkspace.Models.Platform;

namespace AgentWorkspace.Platform.Abstractions;

/// <summary>
/// Resolves the launcher's storage locations for this operating system
/// (spec section 16).
/// <para>
/// This is the most important seam for the cross-platform contract: core code
/// must never contain a literal such as a drive letter, /home/ or ~/Library/
/// (spec section 8), so every path the launcher writes to comes from here.
/// </para>
/// </summary>
public interface IPlatformPaths
{
    /// <summary>Identifies the machine, its OS and its architecture.</summary>
    HostPlatform Host { get; }

    /// <summary>The config, state, cache, log and runtime roots for this platform.</summary>
    PlatformPathSet Paths { get; }

    /// <summary>
    /// Creates the config, state, cache and log directories if absent, applying
    /// restrictive permissions to those that hold sensitive material.
    /// </summary>
    void EnsureDirectoriesExist();

    /// <summary>
    /// Creates a fresh isolated directory for one launch (spec section 82).
    /// The caller deletes it when the launch ends.
    /// </summary>
    string CreateRuntimeDirectory();
}
