using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AgentWorkspace.Platform.Unix;

/// <summary>
/// Interop for a Unix pseudo-terminal (spec sections 22 and 43).
/// <para>
/// Deliberately built on <c>forkpty</c> rather than on <c>posix_spawn</c> with
/// hand-assembled file actions. forkpty opens the pty, puts the child in its
/// own session and makes the terminal its controlling one in a single call, and
/// that last part is not a detail: without a controlling terminal Ctrl+C never
/// becomes SIGINT and the agent cannot be interrupted.
/// </para>
/// </summary>
[UnsupportedOSPlatform("windows")]
internal static partial class NativeTerminal
{
    /// <summary>
    /// Where forkpty lives. On glibc it is libutil; on musl and macOS the
    /// symbol is in libc itself, and the loader resolves the name either way
    /// because the runtime falls back to the default search path.
    /// </summary>
    private const string Util = "libutil";

    private const string Libc = "libc";

    /// <summary>Sets the terminal's window size. Its number is stable across Linux and macOS.</summary>
    internal const ulong SetWindowSize = 0x5414;

    /// <summary>The macOS spelling of the same request, which encodes the argument size.</summary>
    internal const ulong SetWindowSizeBsd = 0x80087467;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowSize
    {
        internal ushort Rows;
        internal ushort Columns;
        internal ushort PixelWidth;
        internal ushort PixelHeight;
    }

    /// <summary>
    /// Opens a pty, forks, and gives the child the slave side as its controlling
    /// terminal. Returns 0 in the child and the child's process id in the parent.
    /// </summary>
    [LibraryImport(Util, EntryPoint = "forkpty", SetLastError = true)]
    internal static partial int ForkPty(
        out int master,
        nint name,
        nint termios,
        ref WindowSize size);

    /// <summary>
    /// Replaces the child image. Called with pointers prepared before the fork,
    /// because nothing that allocates is safe to run between fork and exec.
    /// </summary>
    [LibraryImport(Libc, EntryPoint = "execve", SetLastError = true)]
    internal static partial int Execve(nint path, nint argv, nint envp);

    /// <summary>
    /// Exits without running any handler. The child uses this when exec fails:
    /// a normal exit would run the runtime's shutdown in a forked process.
    /// </summary>
    [LibraryImport(Libc, EntryPoint = "_exit")]
    internal static partial void Exit(int status);

    [LibraryImport(Libc, EntryPoint = "ioctl", SetLastError = true)]
    internal static partial int Ioctl(int descriptor, ulong request, ref WindowSize size);

    [LibraryImport(Libc, EntryPoint = "waitpid", SetLastError = true)]
    internal static partial int WaitPid(int pid, out int status, int options);

    [LibraryImport(Libc, EntryPoint = "kill", SetLastError = true)]
    internal static partial int Kill(int pid, int signal);

    /// <summary>Do not block if the child is still running.</summary>
    internal const int NoHang = 1;

    /// <summary>
    /// Decodes the status word from waitpid.
    /// <para>
    /// A process that died from a signal has no exit code of its own. The shell
    /// convention of 128 plus the signal number is used so a caller sees
    /// something meaningful rather than a zero that looks like success.
    /// </para>
    /// </summary>
    internal static int InterpretStatus(int status)
    {
        var terminatedBySignal = (status & 0x7F) != 0;

        return terminatedBySignal
            ? 128 + (status & 0x7F)
            : (status >> 8) & 0xFF;
    }
}
