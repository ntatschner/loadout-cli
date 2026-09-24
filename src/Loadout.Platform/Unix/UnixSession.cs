using System.Runtime.InteropServices;

namespace Loadout.Platform.Unix;

/// <summary>
/// Takes this process out of the terminal it was started from.
/// </summary>
/// <remarks>
/// <para>
/// A process started from a terminal on Linux or macOS is in that terminal's
/// session. Closing the terminal sends the session SIGHUP, and Ctrl+C in it
/// sends SIGINT to everything in the foreground, so a daemon started behind a
/// command that is only watching it would end with the terminal, or with the
/// Ctrl+C meant to stop the watching.
/// </para>
/// <para>
/// .NET cannot start a child in a new session, so the child leaves on its own,
/// first thing. It can: a process started this way is never a process group
/// leader, which is the one thing that makes setsid refuse. There is a moment
/// between starting and leaving when a signal would still reach it, which is
/// the price of not writing a launcher in C.
/// </para>
/// </remarks>
public static class UnixSession
{
    /// <summary>
    /// Leaves the terminal's session. Does nothing on Windows, where the child
    /// was already given a console of its own.
    /// </summary>
    /// <returns>False where it was tried and refused, so the caller can say so.</returns>
    public static bool Leave()
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            return NativeTerminal.LeaveSession() >= 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return false;
        }
    }
}
