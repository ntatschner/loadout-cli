using Loadout.Models.Results;

namespace Loadout.Platform.Abstractions;

/// <summary>Shells the launcher can generate completion scripts for (spec section 41).</summary>
public enum ShellKind
{
    PowerShell,
    Bash,
    Zsh,
    Fish,
}

/// <summary>
/// Detects the user's shell and describes where completion belongs
/// (spec section 41). Core never names a shell binary directly (spec section 8).
/// </summary>
public interface IShellProvider
{
    /// <summary>
    /// The shell the launcher appears to be running under, or null when it
    /// cannot be determined. Null is a legitimate answer and callers must not
    /// guess: on macOS the default is zsh, on most Linux bash, but neither can
    /// be assumed without evidence.
    /// </summary>
    ShellKind? DetectCurrentShell();

    /// <summary>The interactive shell to spawn for "open development shell" (spec section 22).</summary>
    OperationResult<string> GetInteractiveShellPath();

    /// <summary>
    /// Conventional install location for a completion script, used to tell the
    /// user where to put it. Returns a failure where there is no convention.
    /// </summary>
    OperationResult<string> GetCompletionInstallPath(ShellKind shell);
}
