namespace AgentWorkspace.Platform.Abstractions;

/// <summary>
/// Locates executables such as git, an agent CLI or an editor
/// (spec section 65).
/// <para>
/// Search order is PATH, then platform-standard directories, then paths the
/// user configured explicitly. On macOS that includes both Homebrew prefixes,
/// since Apple Silicon installs to /opt/homebrew/bin and Intel to
/// /usr/local/bin. Neither may be hardcoded as the only valid location, and
/// Homebrew is never a hard dependency.
/// </para>
/// </summary>
public interface IExecutableResolver
{
    /// <summary>
    /// Finds an executable by name. Returns null when it is not installed,
    /// which is an ordinary answer rather than a failure.
    /// </summary>
    /// <param name="name">Executable name without a platform extension.</param>
    /// <param name="additionalPaths">Extra directories to search before the platform defaults.</param>
    string? Resolve(string name, IReadOnlyList<string>? additionalPaths = null);

    /// <summary>Directories searched after PATH on this platform, in order.</summary>
    IReadOnlyList<string> StandardSearchPaths { get; }
}
