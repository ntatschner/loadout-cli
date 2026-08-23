using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Unix;

/// <summary>
/// Detects the user's shell on Linux and macOS (spec section 41).
/// Shared between the two because the mechanism is identical: SHELL names the
/// login shell on both. What differs is only which shell is likely, and the
/// launcher does not guess at that.
/// </summary>
public sealed class UnixShellProvider : IShellProvider
{
    private readonly IEnvironmentProvider _environment;
    private readonly IExecutableResolver _resolver;

    public UnixShellProvider(IEnvironmentProvider environment, IExecutableResolver resolver)
    {
        _environment = environment;
        _resolver = resolver;
    }

    /// <inheritdoc />
    public ShellKind? DetectCurrentShell()
    {
        var shell = _environment.GetVariable("SHELL");
        if (shell is null)
        {
            // Returning null rather than assuming zsh on macOS and bash on
            // Linux. Both defaults are frequently wrong, and a wrong
            // completion script is worse than an honest "could not tell".
            return null;
        }

        return Path.GetFileName(shell).ToLowerInvariant() switch
        {
            "zsh" => ShellKind.Zsh,
            "bash" => ShellKind.Bash,
            "fish" => ShellKind.Fish,
            "pwsh" or "powershell" => ShellKind.PowerShell,
            _ => null,
        };
    }

    /// <inheritdoc />
    public OperationResult<string> GetInteractiveShellPath()
    {
        var shell = _environment.GetVariable("SHELL");
        if (shell is not null && File.Exists(shell))
        {
            return OperationResult<string>.Ok(shell);
        }

        // /bin/sh is the one shell POSIX guarantees exists, so it is the
        // fallback rather than a guess at the user's preference.
        foreach (var candidate in new[] { "zsh", "bash", "sh" })
        {
            var resolved = _resolver.Resolve(candidate);
            if (resolved is not null)
            {
                return OperationResult<string>.Ok(resolved);
            }
        }

        return OperationResult<string>.Fail("No interactive shell could be found.");
    }

    /// <inheritdoc />
    public OperationResult<string> GetCompletionInstallPath(ShellKind shell)
    {
        var home = _environment.HomeDirectory;

        return shell switch
        {
            // zsh reads completions from any directory on fpath; ~/.zfunc is
            // the conventional user-owned one.
            ShellKind.Zsh => OperationResult<string>.Ok(
                Path.Combine(home, ".zfunc", "_loadout")),

            ShellKind.Bash => OperationResult<string>.Ok(
                Path.Combine(ResolveXdgData(home), "bash-completion", "completions", "loadout")),

            ShellKind.Fish => OperationResult<string>.Ok(
                Path.Combine(ResolveXdgConfig(home), "fish", "completions", "loadout.fish")),

            ShellKind.PowerShell => OperationResult<string>.Ok(
                Path.Combine(home, ".config", "powershell", "loadout.completion.ps1")),

            _ => OperationResult<string>.Fail($"No completion convention is known for {shell}."),
        };
    }

    private string ResolveXdgData(string home)
    {
        var value = _environment.GetVariable("XDG_DATA_HOME");
        return !string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value)
            ? value
            : Path.Combine(home, ".local", "share");
    }

    private string ResolveXdgConfig(string home)
    {
        var value = _environment.GetVariable("XDG_CONFIG_HOME");
        return !string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value)
            ? value
            : Path.Combine(home, ".config");
    }
}
