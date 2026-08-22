using AgentWorkspace.Models.Results;

namespace AgentWorkspace.Platform.Abstractions;

/// <summary>
/// Hands a path or URL to the desktop environment (spec sections 72, 73).
/// Explorer on Windows, the configured file manager on Linux, Finder on macOS.
/// </summary>
public interface IApplicationLauncher
{
    /// <summary>Reveals a directory in the platform file manager.</summary>
    Task<OperationResult> OpenInFileManagerAsync(string path, CancellationToken ct = default);

    /// <summary>Opens a URL in the default browser.</summary>
    Task<OperationResult> OpenUrlAsync(string url, CancellationToken ct = default);
}
