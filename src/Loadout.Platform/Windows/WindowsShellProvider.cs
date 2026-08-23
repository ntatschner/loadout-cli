using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Windows;

/// <summary>
/// Detects the shell on Windows (spec section 41).
/// <para>
/// PowerShell is the practical default, but bash, zsh and fish all exist on
/// Windows through Git Bash and MSYS2, so the SHELL variable is honoured when
/// it is set rather than being ignored as a Unix-only concept.
/// </para>
/// </summary>
public sealed class WindowsShellProvider : IShellProvider
{
    private readonly IEnvironmentProvider _environment;
    private readonly IExecutableResolver _resolver;

    public WindowsShellProvider(IEnvironmentProvider environment, IExecutableResolver resolver)
    {
        _environment = environment;
        _resolver = resolver;
    }

    /// <inheritdoc />
    public ShellKind? DetectCurrentShell()
    {
        var shell = _environment.GetVariable("SHELL");
        if (shell is not null)
        {
            var name = Path.GetFileNameWithoutExtension(shell).ToLowerInvariant();
            var fromShellVariable = name switch
            {
                "bash" => (ShellKind?)ShellKind.Bash,
                "zsh" => ShellKind.Zsh,
                "fish" => ShellKind.Fish,
                "pwsh" or "powershell" => ShellKind.PowerShell,
                _ => null,
            };

            if (fromShellVariable is not null)
            {
                return fromShellVariable;
            }
        }

        // PSModulePath is set by any PowerShell host and is the most reliable
        // signal that the launcher was started from one.
        return _environment.GetVariable("PSModulePath") is not null
            ? ShellKind.PowerShell
            : null;
    }

    /// <inheritdoc />
    public OperationResult<string> GetInteractiveShellPath()
    {
        foreach (var candidate in new[] { "pwsh", "powershell" })
        {
            var resolved = _resolver.Resolve(candidate);
            if (resolved is not null)
            {
                return OperationResult<string>.Ok(resolved);
            }
        }

        var comspec = _environment.GetVariable("COMSPEC");
        return comspec is not null
            ? OperationResult<string>.Ok(comspec)
            : OperationResult<string>.Fail("No interactive shell could be found.");
    }

    /// <inheritdoc />
    public OperationResult<string> GetCompletionInstallPath(ShellKind shell)
    {
        if (shell != ShellKind.PowerShell)
        {
            // Git Bash and MSYS2 place their completions inside their own
            // installation, which the launcher should not write into.
            return OperationResult<string>.Fail(
                $"On Windows the launcher can only place {ShellKind.PowerShell} completions automatically; "
                + $"generate the {shell} script and install it where that shell expects it.");
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
        {
            return OperationResult<string>.Fail("The Documents folder could not be located.");
        }

        return OperationResult<string>.Ok(
            Path.Combine(documents, "PowerShell", "Scripts", "loadout.completion.ps1"));
    }
}
