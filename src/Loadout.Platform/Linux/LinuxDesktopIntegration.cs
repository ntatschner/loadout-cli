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

    /// <summary>
    /// Where the hicolor theme expects a 256px application icon, so the entry
    /// can name the icon rather than point at a path that breaks when the
    /// launcher moves.
    /// </summary>
    private string IconDirectory
    {
        get
        {
            var dataHome = _environment.GetVariable("XDG_DATA_HOME");

            var root = !string.IsNullOrWhiteSpace(dataHome) && Path.IsPathRooted(dataHome)
                ? dataHome
                : Path.Combine(_environment.HomeDirectory, ".local", "share");

            return Path.Combine(root, "icons", "hicolor", "256x256", "apps");
        }
    }

    private string IconPath => Path.Combine(IconDirectory, "loadout.png");

    /// <summary>
    /// Writes the icon out of the assembly. Best effort: an entry with a
    /// missing icon still launches, and refusing to install a menu entry
    /// because a picture could not be written would be the wrong trade.
    /// </summary>
    private async Task WriteIconAsync(CancellationToken ct)
    {
        try
        {
            await using var source = typeof(LinuxDesktopIntegration).Assembly
                .GetManifestResourceStream("Loadout.Platform.loadout.png");

            if (source is null)
            {
                return;
            }

            Directory.CreateDirectory(IconDirectory);

            await using var target = File.Create(IconPath);
            await source.CopyToAsync(target, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The entry is still worth installing without it.
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

            await WriteIconAsync(ct).ConfigureAwait(false);

            var content = new StringBuilder()
                .AppendLine("[Desktop Entry]")
                .AppendLine("Type=Application")
                .AppendLine("Name=Loadout")
                .AppendLine("Comment=Launch AI coding agents against development projects")
                // Quoted so an install path containing spaces still parses,
                // which the Desktop Entry specification requires.
                .AppendLine($"Exec=\"{executablePath}\"")
                // Named rather than a path, so it survives the launcher being
                // moved, and resolved from the hicolor theme where the icon is
                // written just above. This used to borrow a stock terminal
                // icon from whatever theme happened to be installed.
                .AppendLine("Icon=loadout")
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

            // Taken away with it. Leaving an icon behind for an entry that no
            // longer exists is litter of exactly the kind this tool argues
            // against.
            if (File.Exists(IconPath))
            {
                File.Delete(IconPath);
            }

            return Task.FromResult(OperationResult.Ok());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(OperationResult.Fail($"Could not remove '{EntryPath}': {ex.Message}"));
        }
    }
}
