namespace AgentWorkspace.Platform.Abstractions;

/// <summary>
/// Reads the process and user environment. Abstracted so tests can supply a
/// fake environment instead of mutating the real one, which keeps the XDG and
/// APPDATA lookups testable from any host OS.
/// </summary>
public interface IEnvironmentProvider
{
    /// <summary>Reads an environment variable, or null when unset or empty.</summary>
    string? GetVariable(string name);

    /// <summary>The current user's home directory.</summary>
    string HomeDirectory { get; }

    /// <summary>Machine name, used to key machine-local state and label commits.</summary>
    string MachineName { get; }

    /// <summary>Directories on PATH, already split on the platform separator.</summary>
    IReadOnlyList<string> PathDirectories { get; }

    /// <summary>Extensions an executable may carry here. Empty on Unix.</summary>
    IReadOnlyList<string> ExecutableExtensions { get; }
}
