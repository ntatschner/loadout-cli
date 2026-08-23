using Loadout.Models.Platform;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Common;

/// <summary>
/// Shared behaviour for the three path layouts. Only the layout itself differs
/// per platform; creating the directories and minting per-launch runtime
/// directories is identical everywhere.
/// </summary>
public abstract class PlatformPathsBase : IPlatformPaths
{
    private readonly IFilePermissions _permissions;

    protected PlatformPathsBase(
        IEnvironmentProvider environment,
        IFilePermissions permissions,
        HostPlatform host)
    {
        Environment = environment;
        _permissions = permissions;
        Host = host;
        Paths = BuildPaths();
    }

    protected IEnvironmentProvider Environment { get; }

    /// <inheritdoc />
    public HostPlatform Host { get; }

    /// <inheritdoc />
    public PlatformPathSet Paths { get; }

    /// <summary>Produces the platform's layout. Called once during construction.</summary>
    protected abstract PlatformPathSet BuildPaths();

    /// <summary>
    /// Resolves a directory, adopting one left by an earlier name of this tool.
    /// <para>
    /// Renaming the product must not orphan somebody's workspace clone, machine
    /// path mappings and backup sets. Where the old directory is there and the
    /// new one is not, it is renamed once: the data is this tool's own, so
    /// moving it is tidying rather than trespass, and leaving both names in play
    /// forever would be its own kind of mess.
    /// </para>
    /// <para>
    /// If the move cannot be made — something has the directory open, a
    /// permission is missing — the old location is used as it stands. Failing to
    /// tidy up is not a reason to lose sight of the data.
    /// </para>
    /// </summary>
    protected static string Adopt(string parent, string folder, string legacyFolder)
    {
        var current = Path.Combine(parent, folder);

        if (Directory.Exists(current))
        {
            return current;
        }

        var legacy = Path.Combine(parent, legacyFolder);

        if (!Directory.Exists(legacy))
        {
            return current;
        }

        try
        {
            Directory.Move(legacy, current);

            return current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return legacy;
        }
    }

    /// <inheritdoc />
    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(Paths.Config);
        Directory.CreateDirectory(Paths.State);
        Directory.CreateDirectory(Paths.Cache);
        Directory.CreateDirectory(Paths.Logs);
        Directory.CreateDirectory(Paths.Runtime);

        // Config and state hold secret references, machine layout and the
        // workspace clone; runtime holds compiled context and generated
        // settings (spec section 82). None of it should be world-readable.
        _permissions.RestrictDirectoryToCurrentUser(Paths.Config);
        _permissions.RestrictDirectoryToCurrentUser(Paths.State);
        _permissions.RestrictDirectoryToCurrentUser(Paths.Runtime);
    }

    /// <inheritdoc />
    public string CreateRuntimeDirectory()
    {
        // Timestamp first so the directories sort chronologically when a user
        // is looking at leftovers from a crashed launch; the suffix keeps
        // concurrent launches from colliding.
        var name = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24];
        var path = Path.Combine(Paths.Runtime, name);

        Directory.CreateDirectory(path);
        _permissions.RestrictDirectoryToCurrentUser(path);

        return path;
    }

    /// <summary>
    /// Reads an XDG variable, falling back to the specification's default.
    /// A relative value is ignored, as the XDG specification requires.
    /// </summary>
    protected string ResolveXdg(string variable, string fallbackRelativeToHome)
    {
        var value = Environment.GetVariable(variable);

        return !string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value)
            ? value
            : Path.Combine(Environment.HomeDirectory, fallbackRelativeToHome);
    }
}
