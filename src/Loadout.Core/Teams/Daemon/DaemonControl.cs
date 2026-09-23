using Loadout.Platform.Abstractions;

namespace Loadout.Core.Teams.Daemon;

/// <summary>What somebody has asked a running daemon to do about stopping.</summary>
/// <param name="Now">End the runs it started at once, rather than waiting for them.</param>
/// <param name="Restart">Start a new daemon with the same settings once this one has gone.</param>
public sealed record DaemonStopRequest(bool Now, bool Restart);

/// <summary>
/// Telling the daemon in another process to hold, carry on, stop or restart.
/// </summary>
/// <remarks>
/// <para>
/// The same channel a run's controls use, for the same reason: the daemon is a
/// process somebody started somewhere else, often at login in a minimised
/// window, and nothing can reach into it. Files beside its note are something
/// it can look at on its own clock, and something a command in any shell can
/// write.
/// </para>
/// <para>
/// A hold outlives the daemon. One held and then stopped starts held next time,
/// because somebody who stopped the schedules firing and then restarted the
/// machine has not changed their mind by rebooting. A stop does not outlive it:
/// a request left behind would end the next daemon the moment it started.
/// </para>
/// <para>
/// Everything here fails quiet, as the run controls do. A control that threw
/// would turn a file that could not be read into a daemon that could not run.
/// </para>
/// </remarks>
public static class DaemonControl
{
    /// <summary>Present while the schedules are held.</summary>
    public const string PauseFile = "daemon-pause";

    /// <summary>Present when the daemon has been asked to stop or restart.</summary>
    public const string StopFile = "daemon-stop";

    /// <summary>Where the controls are kept: beside the daemon's own note.</summary>
    public static string DirectoryFor(IPlatformPaths paths) =>
        Path.GetDirectoryName(DaemonNote.PathFor(paths))!;

    /// <summary>Whether the schedules are held.</summary>
    public static bool Paused(IPlatformPaths paths) =>
        File.Exists(Path.Combine(DirectoryFor(paths), PauseFile));

    /// <summary>Holds the schedules until <see cref="Resume"/>.</summary>
    public static Task PauseAsync(IPlatformPaths paths, CancellationToken ct = default) =>
        WriteAsync(paths, PauseFile, "pause", ct);

    /// <summary>Lets the schedules fire again. Says whether they had been held.</summary>
    public static bool Resume(IPlatformPaths paths) => Remove(paths, PauseFile);

    /// <summary>What has been asked about stopping, or null when nothing has.</summary>
    public static DaemonStopRequest? Stopping(IPlatformPaths paths)
    {
        try
        {
            var path = Path.Combine(DirectoryFor(paths), StopFile);

            if (!File.Exists(path))
            {
                return null;
            }

            var words = File.ReadAllText(path)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            return new DaemonStopRequest(
                words.Contains("now", StringComparer.Ordinal),
                words.Contains("restart", StringComparer.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Being written as it was read. The next look gets it.
            return null;
        }
    }

    /// <summary>Asks the daemon to stop, or to restart, and how soon.</summary>
    public static Task StopAsync(IPlatformPaths paths, DaemonStopRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return WriteAsync(
            paths,
            StopFile,
            (request.Restart ? "restart" : "stop") + (request.Now ? " now" : string.Empty),
            ct);
    }

    /// <summary>Takes back a stop, which a daemon starting up does first.</summary>
    public static void ClearStop(IPlatformPaths paths) => Remove(paths, StopFile);

    private static async Task WriteAsync(IPlatformPaths paths, string name, string text, CancellationToken ct)
    {
        try
        {
            var directory = DirectoryFor(paths);

            Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(Path.Combine(directory, name), text, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool Remove(IPlatformPaths paths, string name)
    {
        try
        {
            var path = Path.Combine(DirectoryFor(paths), name);

            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
