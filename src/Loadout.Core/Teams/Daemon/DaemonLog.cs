using System.Text;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Teams.Daemon;

/// <summary>
/// Where a daemon running in the background says what it is doing, and how a
/// terminal follows it.
/// </summary>
/// <remarks>
/// <para>
/// A daemon with no window has nowhere to print, and what it prints is the
/// dashboard's address and every run it starts or finishes. So it writes here,
/// beside its note, and <c>loadout team daemon</c> shows the file as it grows.
/// Closing that terminal closes the view and leaves the daemon alone, which is
/// the point of doing it this way round.
/// </para>
/// <para>
/// Appended to rather than started afresh, so a daemon that died leaves its
/// last words for the next start to show rather than overwrite. It is set aside
/// once it passes <see cref="Limit"/>, keeping one previous file, which is all
/// anybody reads.
/// </para>
/// </remarks>
public static class DaemonLog
{
    /// <summary>How large the log may grow before it is set aside.</summary>
    internal const long Limit = 1024 * 1024;

    /// <summary>The flag a background daemon is started with, naming its log.</summary>
    public const string Flag = "--log";

    /// <summary>Where the log lives.</summary>
    public static string PathFor(IPlatformPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return Path.Combine(paths.Paths.State, "teams", "daemon.log");
    }

    /// <summary>
    /// The log this invocation was asked to write to, when it is a background
    /// daemon.
    /// </summary>
    /// <remarks>
    /// Read from the arguments before anything is parsed, because everything
    /// the process writes has to go there - the dashboard's address, the runs
    /// it starts, and an exception nobody caught - and the console everything
    /// writes through is made before any command runs. Only <c>team daemon</c>
    /// is looked at, so the same flag on another command means nothing here.
    /// </remarks>
    public static string? Asked(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2
            || !string.Equals(arguments[0], "team", StringComparison.Ordinal)
            || !string.Equals(arguments[1], "daemon", StringComparison.Ordinal))
        {
            return null;
        }

        for (var i = 2; i < arguments.Count - 1; i++)
        {
            if (string.Equals(arguments[i], Flag, StringComparison.Ordinal)
                && arguments[i + 1] is { Length: > 0 } path)
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Opens the log for the daemon to write to, setting the old one aside
    /// first when it has grown past <see cref="Limit"/>.
    /// </summary>
    /// <remarks>
    /// Shared for reading, because a terminal is following it, and for
    /// deleting, so the next daemon can set it aside while this one - on its
    /// way out during a restart - still holds it.
    /// </remarks>
    public static TextWriter Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > Limit)
            {
                File.Move(path, Path.ChangeExtension(path, ".previous.log"), overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept growing rather than refused. A daemon that would not start
            // because it could not tidy its log is worse than a large log.
        }

        var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);

        return TextWriter.Synchronized(new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true });
    }

    /// <summary>How long the log is now, which is where following it starts.</summary>
    public static long Length(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Whatever has been written since <paramref name="position"/>, moving it on.
    /// </summary>
    /// <remarks>
    /// A log shorter than where the reader had got to has been set aside and
    /// started again, so it is read from the beginning rather than waited on
    /// until it grows back past a point that no longer means anything.
    /// Only whole lines are handed back: a line caught half-written would be
    /// shown in two pieces.
    /// </remarks>
    public static string Since(string path, ref long position)
    {
        try
        {
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length < position)
            {
                position = 0;
            }

            if (stream.Length == position)
            {
                return string.Empty;
            }

            stream.Seek(position, SeekOrigin.Begin);

            var buffer = new byte[stream.Length - position];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            var end = read == 0 ? -1 : Array.LastIndexOf(buffer, (byte)'\n', read - 1);

            if (end < 0)
            {
                return string.Empty;
            }

            position += end + 1;

            return Encoding.UTF8.GetString(buffer, 0, end + 1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Caught mid-rename, or briefly locked. The next look will do.
            return string.Empty;
        }
    }
}
