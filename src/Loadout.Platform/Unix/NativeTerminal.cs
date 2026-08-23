using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Loadout.Platform.Unix;

/// <summary>
/// Interop for a Unix pseudo-terminal (spec sections 22 and 43).
/// <para>
/// Built on <c>posix_spawn</c> rather than on <c>forkpty</c>, and that is the
/// central decision in this file. forkpty is the obvious call and it does not
/// work here: it forks the whole process, and a forked copy of a multi-threaded
/// .NET runtime has one live thread and every lock the others were holding. The
/// result is not a subtle risk — it corrupts the parent's heap and brings the
/// process down with an access violation.
/// </para>
/// <para>
/// posix_spawn asks the C library to do the fork and exec on our behalf, in
/// code written for exactly that, so no managed state is ever duplicated. The
/// pty is allocated separately and handed to the child through spawn file
/// actions.
/// </para>
/// </summary>
[UnsupportedOSPlatform("windows")]
internal static partial class NativeTerminal
{
    private const string Libc = "libc";

    /// <summary>
    /// Generous, and deliberately so. <c>posix_spawn_file_actions_t</c> and
    /// <c>posix_spawnattr_t</c> are opaque, and their real sizes differ between
    /// glibc, musl and macOS. The library only ever writes within its own
    /// structure, so over-allocating is safe where guessing exactly would not
    /// be.
    /// </summary>
    private const int OpaqueSize = 1024;

    private const ulong SetWindowSizeLinux = 0x5414;

    /// <summary>The BSD spelling of the same request, which encodes the argument size.</summary>
    private const ulong SetWindowSizeBsd = 0x80087467;

    /// <summary>Sets the terminal's window size.</summary>
    internal static ulong SetWindowSize =>
        OperatingSystem.IsMacOS() ? SetWindowSizeBsd : SetWindowSizeLinux;

    private const int ReadWrite = 0x0002;

    /// <summary>
    /// Open without acquiring a controlling terminal. The value differs between
    /// the two Unixes, and the wrong one here would quietly make the launcher's
    /// own process the session leader of the agent's terminal.
    /// </summary>
    private static int NoControllingTerminal => OperatingSystem.IsMacOS() ? 0x20000 : 0x0100;

    /// <summary>
    /// How the child opens the slave: read-write, and without suppressing the
    /// controlling terminal, so that opening it is what makes it one.
    /// </summary>
    internal const int SlaveFlags = ReadWrite;

    internal static int MasterFlags => ReadWrite | NoControllingTerminal;

    /// <summary>
    /// Put the child in a new session. Without it the child shares the
    /// launcher's session, the pty never becomes its controlling terminal, and
    /// Ctrl+C never turns into SIGINT.
    /// </summary>
    internal static short NewSession => OperatingSystem.IsMacOS() ? (short)0x0400 : (short)0x0080;

    static NativeTerminal()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            NativeLibrary.SetDllImportResolver(typeof(NativeTerminal).Assembly, Resolve);
        }
        catch (InvalidOperationException)
        {
            // Already registered by something else in this assembly. Nothing to
            // do, and nothing worth failing over.
        }
    }

    /// <summary>
    /// Finds the real library behind the logical name.
    /// <para>
    /// The default probing looks for <c>libc.so</c>, which on a glibc system is
    /// a linker script that only exists once the development package is
    /// installed. The runtime file is <c>libc.so.6</c>, so relying on the
    /// default gives a launcher that works on a developer's machine and fails
    /// on every machine it is deployed to.
    /// </para>
    /// </summary>
    private static nint Resolve(string library, Assembly assembly, DllImportSearchPath? path)
    {
        if (library != Libc)
        {
            // Zero hands the decision back to the default probing, which is the
            // right outcome for a name this method knows nothing about.
            return 0;
        }

        string[] candidates =
        [
            "libc.so.6",
            "libSystem.dylib",
            "libc.musl-x86_64.so.1",
            "libc.musl-aarch64.so.1",
            "libc.so",
        ];

        foreach (var candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowSize
    {
        internal ushort Rows;
        internal ushort Columns;
        internal ushort PixelWidth;
        internal ushort PixelHeight;
    }

    /// <summary>Opens an unused pty master.</summary>
    [LibraryImport(Libc, EntryPoint = "posix_openpt", SetLastError = true)]
    internal static partial int OpenPseudoTerminal(int flags);

    /// <summary>Sets the ownership and permissions of the slave side.</summary>
    [LibraryImport(Libc, EntryPoint = "grantpt", SetLastError = true)]
    internal static partial int GrantSlave(int master);

    /// <summary>Allows the slave to be opened.</summary>
    [LibraryImport(Libc, EntryPoint = "unlockpt", SetLastError = true)]
    internal static partial int UnlockSlave(int master);

    /// <summary>
    /// The slave's path. Returns a pointer into storage the library owns, which
    /// is why the caller copies the string out of it immediately.
    /// </summary>
    [LibraryImport(Libc, EntryPoint = "ptsname", SetLastError = true)]
    internal static partial nint SlaveName(int master);

    [LibraryImport(Libc, EntryPoint = "posix_spawn", SetLastError = true)]
    internal static partial int Spawn(
        out int pid,
        nint path,
        nint fileActions,
        nint attributes,
        nint argv,
        nint envp);

    [LibraryImport(Libc, EntryPoint = "posix_spawn_file_actions_init", SetLastError = true)]
    internal static partial int FileActionsInit(nint actions);

    [LibraryImport(Libc, EntryPoint = "posix_spawn_file_actions_destroy", SetLastError = true)]
    internal static partial int FileActionsDestroy(nint actions);

    [LibraryImport(Libc, EntryPoint = "posix_spawn_file_actions_addopen", SetLastError = true)]
    internal static partial int FileActionsAddOpen(
        nint actions,
        int descriptor,
        nint path,
        int flags,
        uint mode);

    [LibraryImport(Libc, EntryPoint = "posix_spawn_file_actions_adddup2", SetLastError = true)]
    internal static partial int FileActionsAddDuplicate(nint actions, int descriptor, int target);

    /// <summary>
    /// Changes the child's working directory.
    /// <para>
    /// The _np suffix marks it non-portable, but it is in glibc from 2.29 and
    /// macOS from 10.15, both of which predate anything .NET 10 runs on. The
    /// alternative is changing the launcher's own working directory around the
    /// spawn, and that is process-wide state: every other thread would resolve
    /// relative paths somewhere else for the duration.
    /// </para>
    /// </summary>
    [LibraryImport(Libc, EntryPoint = "posix_spawn_file_actions_addchdir_np", SetLastError = true)]
    internal static partial int FileActionsAddChangeDirectory(nint actions, nint path);

    [LibraryImport(Libc, EntryPoint = "posix_spawnattr_init", SetLastError = true)]
    internal static partial int AttributesInit(nint attributes);

    [LibraryImport(Libc, EntryPoint = "posix_spawnattr_destroy", SetLastError = true)]
    internal static partial int AttributesDestroy(nint attributes);

    [LibraryImport(Libc, EntryPoint = "posix_spawnattr_setflags", SetLastError = true)]
    internal static partial int AttributesSetFlags(nint attributes, short flags);

    [LibraryImport(Libc, EntryPoint = "ioctl", SetLastError = true)]
    internal static partial int Ioctl(int descriptor, ulong request, ref WindowSize size);

    [LibraryImport(Libc, EntryPoint = "close", SetLastError = true)]
    internal static partial int Close(int descriptor);

    [LibraryImport(Libc, EntryPoint = "waitpid", SetLastError = true)]
    internal static partial int WaitPid(int pid, out int status, int options);

    /// <summary>Do not block if the child is still running.</summary>
    internal const int NoHang = 1;

    /// <summary>Allocates a zeroed buffer for one of the opaque spawn structures.</summary>
    internal static nint AllocateOpaque()
    {
        var pointer = Marshal.AllocHGlobal(OpaqueSize);

        // Zeroed, because the library assumes nothing about what it is handed
        // and a stale byte in a flags field is a bug with no symptom until it
        // suddenly has one.
        for (var offset = 0; offset < OpaqueSize; offset += nint.Size)
        {
            Marshal.WriteIntPtr(pointer, offset, 0);
        }

        return pointer;
    }

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
