using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Loadout.Platform.Windows;

/// <summary>
/// Minimal interop surface for the Windows Credential Manager. Kept internal
/// and confined to the platform layer: nothing above it is aware that a native
/// call is involved.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class NativeCredentials
{
    internal const uint CredTypeGeneric = 1;

    /// <summary>Stored for this user on this machine only, and never roamed.</summary>
    internal const uint CredPersistLocalMachine = 2;

    internal const int ErrorNotFound = 1168;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Credential
    {
        internal uint Flags;
        internal uint Type;
        internal nint TargetName;
        internal nint Comment;
        // FILETIME is spelled out as its two halves so the struct stays
        // blittable. The source-generated marshaller refuses a struct that
        // needs runtime marshalling, and the field is never read anyway.
        internal uint LastWrittenLow;
        internal uint LastWrittenHigh;
        internal uint CredentialBlobSize;
        internal nint CredentialBlob;
        internal uint Persist;
        internal uint AttributeCount;
        internal nint Attributes;
        internal nint TargetAlias;
        internal nint UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CredRead(string target, uint type, uint reservedFlag, out nint credentialPtr);

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CredWrite(ref Credential userCredential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CredDelete(string target, uint type, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    internal static partial void CredFree(nint buffer);
}
