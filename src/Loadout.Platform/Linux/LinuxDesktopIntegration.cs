using System.Runtime.Versioning;
using System.Text;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Linux;

/// <summary>
/// Installs a freedesktop .desktop entry for the launcher (spec section 44).
/// <para>
/// Written into the per-user applications directory, so it never needs root
/// (spec section 19) and works the same under GNOME, KDE, Cinnamon, Xfce and
/// MATE without depending on any of them. On a headless machine the directory
/// is still writable and the entry is simply never shown, which is why absence
/// of a desktop session is not treated as an error here.
/// </para>
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxDesktopIntegration : IDesktopIntegration
{
    private const string EntryFileName = "loadout.desktop";

    private readonly IEnvironmentProvider _environment;

    public LinuxDesktopIntegration(IEnvironmentProvider environment) => _environment = environment;

    private string EntryPath => Path.Combine(ApplicationsDirectory, EntryFileName);

    private string ApplicationsDirectory
    {
        get
        {
            var dataHome = _environment.GetVariable("XDG_DATA_HOME");

            var root = !string.IsNullOrWhiteSpace(dataHome) && Path.IsPathRooted(dataHome)
                ? dataHome
                : Path.Combine(_environment.HomeDirectory, ".local", "share");

            return Path.Combine(root, "applications");
        }
    }

    /// <inheritdoc />
    public OperationResult<bool> IsInstalled()
    {
        try
        {
            return OperationResult<bool>.Ok(File.Exists(EntryPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<bool>.Fail($"Could not inspect '{EntryPath}': {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult> InstallAsync(string executablePath, CancellationToken ct = default)
    {
        if (!File.Exists(executablePath))
        {
            return OperationResult.Fail($"No executable exists at '{executablePath}'.");
        }

        try
        {
            Directory.CreateDirectory(ApplicationsDirectory);

            var content = new StringBuilder()
                .AppendLine("[Desktop Entry]")
                .AppendLine("Type=Application")
                .AppendLine("Name=Loadout")
                .AppendLine("Comment=Launch AI coding agents against development projects")
                // Quoted so an install path containing spaces still parses,
                // which the Desktop Entry specification requires.
                .AppendLine($"Exec=\"{executablePath}\"")
                .AppendLine("Icon=utilities-terminal")
                // The launcher is a TUI, so the desktop must give it a
                // terminal to run in rather than starting it detached.
                .AppendLine("Terminal=true")
                .AppendLine("Categories=Development;Utility;")
                .AppendLine("StartupNotify=false")
                .ToString();

            await File.WriteAllTextAsync(EntryPath, content, ct).ConfigureAwait(false);

            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"Could not write '{EntryPath}': {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<OperationResult> UninstallAsync(CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(EntryPath))
            {
                File.Delete(EntryPath);
            }

            return Task.FromResult(OperationResult.Ok());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(OperationResult.Fail($"Could not remove '{EntryPath}': {ex.Message}"));
        }
    }
}
