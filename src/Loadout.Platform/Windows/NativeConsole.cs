using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Loadout.Platform.Windows;

/// <summary>
/// Interop for the Windows pseudo-console (spec sections 22 and 43).
/// <para>
/// ConPTY is the only supported way to own a terminal on Windows. The older
/// approach of allocating a hidden console and screen-scraping it was never
/// reliable and does not survive Windows Terminal, so a pseudo-console is not
/// one option among several here.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class NativeConsole
{
    /// <summary>Identifies the pseudo-console entry in a process attribute list.</summary>
    internal const nint ProcThreadAttributePseudoConsole = 0x00020016;

    internal const uint ExtendedStartupInfoPresent = 0x00080000;

    /// <summary>
    /// Says the standard handles in the startup information are meaningful.
    /// Set with all three left null, which is what makes the child take its
    /// handles from the console rather than from the launcher.
    /// </summary>
    internal const int UseStandardHandles = 0x00000100;

    internal const uint CreateUnicodeEnvironment = 0x00000400;

    internal const uint Infinite = 0xFFFFFFFF;

    internal const uint StillActive = 259;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Coord
    {
        internal short X;
        internal short Y;
    }

    /// <summary>
    /// The classic STARTUPINFOW, spelled out because the extended form embeds
    /// it by value and the source-generated marshaller needs a blittable type.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfo
    {
        internal int Size;
        internal nint Reserved;
        internal nint Desktop;
        internal nint Title;
        internal int X;
        internal int Y;
        internal int XSize;
        internal int YSize;
        internal int XCountChars;
        internal int YCountChars;
        internal int FillAttribute;
        internal int Flags;
        internal short ShowWindow;
        internal short Reserved2;
        internal nint Reserved3;
        internal nint StdInput;
        internal nint StdOutput;
        internal nint StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfoEx
    {
        internal StartupInfo StartupInfo;
        internal nint AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        internal nint Process;
        internal nint Thread;
        internal int ProcessId;
        internal int ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SecurityAttributes
    {
        internal int Length;
        internal nint SecurityDescriptor;

        // An int rather than a bool: the source-generated marshaller requires
        // a blittable struct, and a marshalled bool is not one.
        internal int InheritHandle;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int CreatePseudoConsole(
        Coord size,
        SafeFileHandle input,
        SafeFileHandle output,
        uint flags,
        out nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int ResizePseudoConsole(nint handle, Coord size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial void ClosePseudoConsole(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreatePipe(
        out SafeFileHandle readHandle,
        out SafeFileHandle writeHandle,
        ref SecurityAttributes attributes,
        int size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InitializeProcThreadAttributeList(
        nint attributeList,
        int attributeCount,
        int flags,
        ref nint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UpdateProcThreadAttribute(
        nint attributeList,
        uint flags,
        nint attribute,
        nint value,
        nint size,
        nint previousValue,
        nint returnSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial void DeleteProcThreadAttributeList(nint attributeList);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateProcess(
        string? applicationName,
        ref char commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        nint environment,
        string? currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetExitCodeProcess(nint process, out uint exitCode);

    /// <summary>
    /// Reports how much is waiting in a pipe without consuming it. Works on
    /// anonymous pipes despite the name.
    /// </summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PeekNamedPipe(
        SafeFileHandle pipe,
        nint buffer,
        int bufferSize,
        nint bytesRead,
        out int totalBytesAvailable,
        nint bytesLeftThisMessage);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TerminateProcess(nint process, uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);
}
