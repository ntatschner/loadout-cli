using System.Runtime.Versioning;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Windows;

/// <summary>
/// Installs a Start Menu shortcut for the launcher (spec sections 18 and 44).
/// <para>
/// The shortcut is created through the Windows Script Host shell object driven
/// from PowerShell, rather than by taking a COM interop dependency for one
/// operation. It is written to the per-user Start Menu, so no administrator
/// rights are involved.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDesktopIntegration : IDesktopIntegration
{
    private const string ShortcutName = "AI Workspace Launcher.lnk";

    private readonly IProcessLauncher _processes;
    private readonly IExecutableResolver _resolver;

    public WindowsDesktopIntegration(IProcessLauncher processes, IExecutableResolver resolver)
    {
        _processes = processes;
        _resolver = resolver;
    }

    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        "Programs",
        ShortcutName);

    /// <inheritdoc />
    public OperationResult<bool> IsInstalled()
    {
        try
        {
            return OperationResult<bool>.Ok(File.Exists(ShortcutPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<bool>.Fail($"Could not inspect '{ShortcutPath}': {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult> InstallAsync(string executablePath, CancellationToken ct = default)
    {
        if (!File.Exists(executablePath))
        {
            return OperationResult.Fail($"No executable exists at '{executablePath}'.");
        }

        var powershell = _resolver.Resolve("powershell") ?? _resolver.Resolve("pwsh");
        if (powershell is null)
        {
            return OperationResult.Fail(
                "Neither powershell nor pwsh was found, so the Start Menu shortcut cannot be created.");
        }

        var shortcutPath = ShortcutPath;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"Could not create the Start Menu folder: {ex.Message}");
        }

        // Paths reach the script through environment variables rather than
        // string interpolation, so a path containing a quote or a dollar sign
        // cannot alter the script (spec section 84 requires odd paths to work).
        var environment = new Dictionary<string, string>
        {
            ["LOADOUT_SHORTCUT_PATH"] = shortcutPath,
            ["LOADOUT_TARGET_PATH"] = executablePath,
        };

        const string Script =
            "$shell = New-Object -ComObject WScript.Shell; "
            + "$link = $shell.CreateShortcut($env:LOADOUT_SHORTCUT_PATH); "
            + "$link.TargetPath = $env:LOADOUT_TARGET_PATH; "
            + "$link.Description = 'AI Workspace Launcher'; "
            + "$link.Save()";

        var result = await _processes.RunAsync(
            new ProcessRequest(
                powershell,
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", Script],
                Environment: environment),
            TimeSpan.FromSeconds(30),
            ct).ConfigureAwait(false);

        if (result.Failed || result.Value is null)
        {
            return OperationResult.Fail(result.Error ?? "The shortcut could not be created.");
        }

        return result.Value.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(
                $"The shortcut could not be created: {result.Value.StandardError.Trim()}");
    }

    /// <inheritdoc />
    public Task<OperationResult> UninstallAsync(CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(ShortcutPath))
            {
                File.Delete(ShortcutPath);
            }

            return Task.FromResult(OperationResult.Ok());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(
                OperationResult.Fail($"Could not remove '{ShortcutPath}': {ex.Message}"));
        }
    }
}
