using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Unix;

/// <summary>
/// Starts something at login by writing the file this desktop reads for it.
/// </summary>
/// <remarks>
/// <para>
/// One class for both Unixes because the shape is the same — a text file in a
/// directory the session reads at login — and only its contents and its place
/// differ. macOS reads a launch agent's plist; the freedesktop desktops read a
/// <c>.desktop</c> file under <c>~/.config/autostart</c>.
/// </para>
/// <para>
/// <strong>Neither is verified.</strong> This was written on Windows, where
/// neither path exists to test against. The files are the documented shapes and
/// the code that writes them is covered, but nothing here has been logged into.
/// </para>
/// <para>
/// A file rather than <c>launchctl</c> or <c>systemctl</c>: those need the
/// service manager to be the one running the session, which is true on a
/// desktop and false in a container, over SSH, and on several of the machines
/// somebody would want this on. A file that the session reads if there is one
/// degrades to doing nothing, which is the right failure.
/// </para>
/// <para>
/// Not marked unsupported on Windows, although nothing chooses it there. Every
/// method here is either string formatting or ordinary file IO, and the
/// attribute would only stop the formatting being tested on the machine this
/// was written on. What picks the right one is
/// <c>PlatformServices</c>, which is the guard that matters.
/// </para>
/// </remarks>
public sealed class UnixAutostart : IAutostart
{
    private readonly bool _macOS;

    public UnixAutostart(bool macOS) => _macOS = macOS;

    private string Path_ => _macOS
        ? Path.Combine(Home, "Library", "LaunchAgents", "com.loadout.daemon.plist")
        : Path.Combine(Home, ".config", "autostart", "loadout-daemon.desktop");

    private static string Home =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <inheritdoc />
    public string Describe() => Path_;

    /// <inheritdoc />
    public OperationResult<bool> IsInstalled()
    {
        try
        {
            return OperationResult<bool>.Ok(File.Exists(Path_));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<bool>.Fail($"Could not inspect '{Path_}': {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult> InstallAsync(string command, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path_)!);

            await File.WriteAllTextAsync(Path_, _macOS ? Plist(command) : Desktop(command), ct)
                .ConfigureAwait(false);

            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"Could not write '{Path_}': {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<OperationResult> UninstallAsync(CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(Path_))
            {
                File.Delete(Path_);
            }

            // Ok either way: "it is not there" is what was asked for.
            return Task.FromResult(OperationResult.Ok());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(OperationResult.Fail($"Could not remove '{Path_}': {ex.Message}"));
        }
    }

    /// <summary>
    /// A launch agent, as launchd reads one.
    /// </summary>
    /// <remarks>
    /// Arguments are one element each, so a path with a space in it stays one
    /// argument. <c>RunAtLoad</c> and nothing else: no KeepAlive, because a
    /// daemon that respawns after somebody stopped it is a daemon they cannot
    /// stop.
    /// </remarks>
    internal static string Plist(string command)
    {
        var arguments = string.Concat(
            Words(command).Select(word => $"    <string>{Escape(word)}</string>\n"));

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" "
            + "\"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
            + "<plist version=\"1.0\">\n"
            + "<dict>\n"
            + "  <key>Label</key>\n"
            + "  <string>com.loadout.daemon</string>\n"
            + "  <key>ProgramArguments</key>\n"
            + "  <array>\n"
            + arguments
            + "  </array>\n"
            + "  <key>RunAtLoad</key>\n"
            + "  <true/>\n"
            + "</dict>\n"
            + "</plist>\n";
    }

    /// <summary>A desktop entry, as an XDG autostart directory reads one.</summary>
    internal static string Desktop(string command) =>
        "[Desktop Entry]\n"
        + "Type=Application\n"
        + "Name=Loadout daemon\n"
        + "Comment=Fire scheduled team runs and serve the dashboard\n"
        + $"Exec={command}\n"
        + "Terminal=false\n"
        + "X-GNOME-Autostart-enabled=true\n";

    /// <summary>
    /// A command line as separate words, keeping quoted runs together.
    /// </summary>
    /// <remarks>
    /// The launcher quotes its own path because it may contain spaces, and a
    /// plist wants each argument as its own element. Splitting on every space
    /// would turn one path into three arguments that do not exist.
    /// </remarks>
    internal static IReadOnlyList<string> Words(string command)
    {
        var words = new List<string>();
        var word = new System.Text.StringBuilder();
        var quoted = false;

        foreach (var c in command.Trim())
        {
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (c == ' ' && !quoted)
            {
                if (word.Length > 0)
                {
                    words.Add(word.ToString());
                    word.Clear();
                }

                continue;
            }

            word.Append(c);
        }

        if (word.Length > 0)
        {
            words.Add(word.ToString());
        }

        return words;
    }

    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
