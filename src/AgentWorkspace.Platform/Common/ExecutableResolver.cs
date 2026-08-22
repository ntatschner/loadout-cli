using AgentWorkspace.Platform.Abstractions;

namespace AgentWorkspace.Platform.Common;

/// <summary>
/// Locates executables on PATH and in platform-standard directories
/// (spec section 65). The standard directories are supplied by the platform
/// layer rather than baked in here, so nothing in the shared path can grow a
/// hardcoded /opt/homebrew or C:\Program Files.
/// </summary>
public sealed class ExecutableResolver : IExecutableResolver
{
    private readonly IEnvironmentProvider _environment;

    public ExecutableResolver(IEnvironmentProvider environment, IReadOnlyList<string> standardSearchPaths)
    {
        _environment = environment;
        StandardSearchPaths = standardSearchPaths;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> StandardSearchPaths { get; }

    /// <inheritdoc />
    public string? Resolve(string name, IReadOnlyList<string>? additionalPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // An explicit path in configuration wins outright: it is the user
        // telling the launcher where the binary is.
        if (Path.IsPathRooted(name))
        {
            return IsExecutableFile(name) ? name : null;
        }

        // Configured paths first, then PATH, then the platform defaults.
        // PATH before the defaults matters on macOS, where a user who has put
        // a specific Homebrew prefix on PATH means that one, not the other.
        var searchOrder = (additionalPaths ?? [])
            .Concat(_environment.PathDirectories)
            .Concat(StandardSearchPaths);

        foreach (var directory in searchOrder)
        {
            var match = TryDirectory(directory, name);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private string? TryDirectory(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        foreach (var extension in _environment.ExecutableExtensions)
        {
            string candidate;
            try
            {
                candidate = Path.Combine(directory, name + extension);
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry should be skipped, not fatal.
                return null;
            }

            if (IsExecutableFile(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsExecutableFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            // On Unix a file on PATH is only actually runnable if a mode bit
            // says so, and PATH routinely contains non-executable files.
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(path);
                const UnixFileMode AnyExecute =
                    UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

                return (mode & AnyExecute) != 0;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
