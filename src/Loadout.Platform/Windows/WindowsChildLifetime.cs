using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Loadout.Platform.Common;
using Microsoft.Win32.SafeHandles;

namespace Loadout.Platform.Windows;

/// <summary>
/// Puts the launcher's children in a job object that dies with it.
/// </summary>
/// <remarks>
/// <para>
/// The strong form of the promise. A job object with
/// <c>KILL_ON_JOB_CLOSE</c> is enforced by the kernel: when the last handle
/// to it closes, which happens however this process ends, every process in
/// it is terminated. A launcher killed from Task Manager takes its agents
/// with it, which an exit handler cannot do.
/// </para>
/// <para>
/// It falls back rather than fails. A machine where the job cannot be
/// created, or a process already inside a job that forbids nesting, still
/// gets the exit handler underneath, and says which it got.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsChildLifetime : TrackedChildLifetime
{
    /// <summary>JobObjectExtendedLimitInformation, the class the limit below belongs to.</summary>
    private const int ExtendedLimitClass = 9;
    private const uint KillOnJobClose = 0x00002000;
    private const uint AllAccess = 0x1F001F;

    private readonly SafeFileHandle? _job;
    private readonly string? _reason;

    internal WindowsChildLifetime()
    {
        try
        {
            var job = CreateJobObject(nint.Zero, null);

            if (job.IsInvalid)
            {
                _reason = $"the job object could not be created (error {Marshal.GetLastWin32Error()})";
                job.Dispose();

                return;
            }

            var information = new ExtendedLimitInformation
            {
                BasicLimitInformation = new BasicLimitInformation { LimitFlags = KillOnJobClose },
            };

            var size = Marshal.SizeOf<ExtendedLimitInformation>();
            var buffer = Marshal.AllocHGlobal(size);

            try
            {
                Marshal.StructureToPtr(information, buffer, fDeleteOld: false);

                if (!SetInformationJobObject(job, ExtendedLimitClass, buffer, (uint)size))
                {
                    _reason = $"the job object would not take the kill-on-close limit (error {Marshal.GetLastWin32Error()})";
                    job.Dispose();

                    return;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            _job = job;
        }
        catch (DllNotFoundException ex)
        {
            _reason = ex.Message;
        }
        catch (EntryPointNotFoundException ex)
        {
            _reason = ex.Message;
        }
    }

    /// <inheritdoc />
    public override bool IsEnforced => _job is not null;

    /// <inheritdoc />
    public override string Detail => _job is not null
        ? "a job object the kernel closes with the launcher, which takes its children with it however it ends"
        : $"children are stopped on an ordinary exit only, because {_reason}";

    /// <inheritdoc />
    public override void Adopt(int processId)
    {
        // Always, job or no job: the tracked list is what covers Ctrl+C
        // before the job would, and what covers everything if the job could
        // not be made.
        base.Adopt(processId);

        if (_job is null)
        {
            return;
        }

        try
        {
            using var child = System.Diagnostics.Process.GetProcessById(processId);

            if (!AssignProcessToJobObject(_job, child.Handle))
            {
                // Already in a job that forbids nesting, or gone. The list
                // above is still holding it.
                return;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Closing the job is what the kernel is waiting for: the last handle to
    /// a kill-on-close job going away takes every process in it. That is the
    /// same thing that happens when this process ends, by any means, which
    /// is why closing it here is a faithful way to prove the arrangement
    /// works.
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _job?.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        internal long PerProcessUserTimeLimit;
        internal long PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal nuint MinimumWorkingSetSize;
        internal nuint MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal nuint Affinity;
        internal uint PriorityClass;
        internal uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        internal ulong ReadOperationCount;
        internal ulong WriteOperationCount;
        internal ulong OtherOperationCount;
        internal ulong ReadTransferCount;
        internal ulong WriteTransferCount;
        internal ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        internal BasicLimitInformation BasicLimitInformation;
        internal IoCounters IoInfo;
        internal nuint ProcessMemoryLimit;
        internal nuint JobMemoryLimit;
        internal nuint PeakProcessMemoryUsed;
        internal nuint PeakJobMemoryUsed;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateJobObject(nint security, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(SafeFileHandle job, int infoClass, nint information, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(SafeFileHandle job, nint process);
}
