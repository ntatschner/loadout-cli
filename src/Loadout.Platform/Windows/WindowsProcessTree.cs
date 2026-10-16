using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Windows;

/// <summary>
/// Every process with its parent, from a Toolhelp snapshot, and its working
/// set from the system's own process list.
/// </summary>
/// <remarks>
/// The working sets come from <see cref="Process.GetProcesses()"/>, which
/// reads every process in one call without opening any of them, so a process
/// this account may not open is still measured.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsProcessTree : IProcessTree
{
    private const uint SnapshotProcesses = 0x00000002;

    /// <inheritdoc />
    public Task<IReadOnlyList<ProcessEntry>> SnapshotAsync(CancellationToken ct = default)
    {
        var resident = new Dictionary<int, long>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    resident[process.Id] = process.WorkingSet64;
                }
                catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
                {
                    // Gone between the list and the read.
                }
            }
        }

        var entries = new List<ProcessEntry>();
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);

        if (snapshot == -1)
        {
            return Task.FromResult<IReadOnlyList<ProcessEntry>>(entries);
        }

        // PROCESSENTRY32W as a buffer: the fields this reads, laid out by the
        // header below, then the executable's name, which it does not read.
        // The name starts straight after Flags, and the whole is padded to
        // the pointer's alignment, which is what dwSize has to match exactly.
        var size = (int)Marshal.OffsetOf<Head>(nameof(Head.Flags)) + sizeof(uint) + (260 * sizeof(char));
        size = (size + nint.Size - 1) & ~(nint.Size - 1);
        var buffer = Marshal.AllocHGlobal(size);
        var idAt = (int)Marshal.OffsetOf<Head>(nameof(Head.ProcessId));
        var parentAt = (int)Marshal.OffsetOf<Head>(nameof(Head.ParentProcessId));

        try
        {
            Marshal.WriteInt32(buffer, size);

            for (var more = Process32FirstW(snapshot, buffer); more; more = Process32NextW(snapshot, buffer))
            {
                ct.ThrowIfCancellationRequested();

                var id = Marshal.ReadInt32(buffer, idAt);

                entries.Add(new ProcessEntry(
                    id,
                    Marshal.ReadInt32(buffer, parentAt),
                    resident.TryGetValue(id, out var bytes) ? bytes : null));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            CloseHandle(snapshot);
        }

        return Task.FromResult<IReadOnlyList<ProcessEntry>>(entries);
    }

    /// <summary>The start of PROCESSENTRY32W, for the offsets of the fields read from it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Head
    {
        internal uint Size;
        internal uint Usage;
        internal uint ProcessId;
        internal nuint DefaultHeapId;
        internal uint ModuleId;
        internal uint Threads;
        internal uint ParentProcessId;
        internal int PriorityClassBase;
        internal uint Flags;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32FirstW(nint snapshot, nint entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32NextW(nint snapshot, nint entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
