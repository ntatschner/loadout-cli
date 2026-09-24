using System.Runtime.Versioning;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Windows;

/// <summary>
/// Starts something at login through a shortcut in this user's Startup folder.
/// </summary>
/// <remarks>
/// <para>
/// The Startup folder rather than the registry's Run key or a scheduled task.
/// All three work; this one is the only one a person can see, read and delete
/// with a file manager, which matters for a thing that starts itself. A
/// registry value is invisible to anybody who does not already know to look.
/// </para>
/// <para>
/// Minimised, not hidden. The window shows the daemon's log - where its
/// dashboard is, what it started - and the daemon itself runs in the
/// background, so closing the window leaves it running. A window taking focus
/// at every login is not what anybody agreed to; minimised is there when
/// wanted and out of the way when not.
/// </para>
/// <para>
/// Built through the Windows Script Host shell object driven from PowerShell,
/// the way the Start Menu shortcut already is, rather than taking a COM interop
/// dependency for two operations.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsAutostart : IAutostart
{
    private const string ShortcutName = "Loadout daemon.lnk";

    /// <summary>Minimised, in the shell's own numbering.</summary>
    private const int Minimised = 7;

    private readonly IProcessLauncher _processes;
    private readonly IExecutableResolver _resolver;

    public WindowsAutostart(IProcessLauncher processes, IExecutableResolver resolver)
    {
        _processes = processes;
        _resolver = resolver;
    }

    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        ShortcutName);

    /// <inheritdoc />
    public string Describe() => ShortcutPath;

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
    public async Task<OperationResult> InstallAsync(string command, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var powershell = _resolver.Resolve("powershell") ?? _resolver.Resolve("pwsh");

        if (powershell is null)
        {
            return OperationResult.Fail(
                "Neither powershell nor pwsh was found, so the login item cannot be created.");
        }

        // A shortcut has a target and its arguments as separate fields, so the
        // command line is split at the end of the quoted program. Anything
        // else would put the whole line in TargetPath, which the shell reads
        // as one impossible file name.
        var (target, arguments) = Split(command);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"Could not reach the Startup folder: {ex.Message}");
        }

        // Through environment variables rather than interpolation, so a path
        // with a quote or a dollar sign in it cannot alter the script.
        var environment = new Dictionary<string, string>
        {
            ["LOADOUT_SHORTCUT_PATH"] = ShortcutPath,
            ["LOADOUT_TARGET_PATH"] = target,
            ["LOADOUT_ARGUMENTS"] = arguments,
        };

        var script =
            "$shell = New-Object -ComObject WScript.Shell; "
            + "$link = $shell.CreateShortcut($env:LOADOUT_SHORTCUT_PATH); "
            + "$link.TargetPath = $env:LOADOUT_TARGET_PATH; "
            + "$link.Arguments = $env:LOADOUT_ARGUMENTS; "
            + "$link.Description = 'Loadout: fire scheduled team runs and serve the dashboard'; "
            + $"$link.WindowStyle = {Minimised}; "
            + "$link.Save()";

        var result = await _processes.RunAsync(
            new ProcessRequest(
                powershell,
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script],
                Environment: environment),
            TimeSpan.FromSeconds(30),
            ct).ConfigureAwait(false);

        if (result.Failed || result.Value is null)
        {
            return OperationResult.Fail(result.Error ?? "The login item could not be created.");
        }

        return result.Value.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(
                $"The login item could not be created: {result.Value.StandardError.Trim()}");
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

            // Ok either way. "It is not there" is the state somebody asked
            // for, and reporting a failure would make an undo that ran twice
            // look like a problem.
            return Task.FromResult(OperationResult.Ok());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(
                OperationResult.Fail($"Could not remove '{ShortcutPath}': {ex.Message}"));
        }
    }

    /// <summary>
    /// Splits a command line into the program and its arguments.
    /// </summary>
    /// <remarks>
    /// The launcher quotes its own path because it may contain spaces, so the
    /// program ends at the closing quote. An unquoted line has no spaces in
    /// its program by definition, so the first space is the split.
    /// </remarks>
    internal static (string Target, string Arguments) Split(string command)
    {
        var line = command.Trim();

        if (line.StartsWith('"'))
        {
            var close = line.IndexOf('"', 1);

            return close < 0
                ? (line.Trim('"'), string.Empty)
                : (line[1..close], line[(close + 1)..].Trim());
        }

        var space = line.IndexOf(' ', StringComparison.Ordinal);

        return space < 0 ? (line, string.Empty) : (line[..space], line[(space + 1)..].Trim());
    }
}
