using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AgentWorkspace.Models;
using AgentWorkspace.Models.Results;
using AgentWorkspace.Platform.Abstractions;
using Microsoft.Win32.SafeHandles;

namespace AgentWorkspace.Platform.Unix;

/// <summary>
/// A pseudo-terminal owned by the launcher.
/// <para>
/// Shared by Linux and macOS: the pty interface is the same on both, which is
/// the reason this lives in Unix rather than being written twice.
/// </para>
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class UnixPseudoTerminal : IPseudoTerminal
{
    private readonly object _gate = new();

    private int _child;
    private int _exitCode;
    private bool _exited;
    private bool _disposed;

    private SafeFileHandle? _master;
    private FileStream? _stream;

    /// <inheritdoc />
    public Task<OperationResult> StartAsync(
        ProcessRequest request,
        int columns,
        int rows,
        CancellationToken ct = default)
    {
        if (_child != 0)
        {
            return Task.FromResult(
                OperationResult.Fail("This pseudo-terminal has already been started."));
        }

        if (!Path.IsPathRooted(request.Executable))
        {
            // execve does not search PATH, and resolving it in the child is not
            // an option: nothing that allocates may run between fork and exec.
            // Callers resolve executables through IExecutableResolver already,
            // so this is a programming error rather than a user-facing one.
            return Task.FromResult(OperationResult.Fail(
                $"'{request.Executable}' must be an absolute path. Resolve it before starting a "
                + "pseudo-terminal.",
                ExitCode.InvalidArguments));
        }

        var size = new NativeTerminal.WindowSize
        {
            Columns = (ushort)Math.Clamp(columns, 1, ushort.MaxValue),
            Rows = (ushort)Math.Clamp(rows, 1, ushort.MaxValue),
        };

        var directory = request.WorkingDirectory;

        if (directory is not null && !Directory.Exists(directory))
        {
            return Task.FromResult(OperationResult.Fail(
                $"The working directory '{directory}' does not exist.", ExitCode.InvalidArguments));
        }

        return Task.FromResult(Launch(request, directory, ref size));
    }

    /// <summary>
    /// Allocates the pty and spawns the child into it.
    /// </summary>
    /// <remarks>
    /// The child never sees managed code. posix_spawn does the fork and exec
    /// inside the C library, so nothing about the runtime is duplicated: no
    /// inherited locks, no half-copied heap, no rules about what may be called
    /// between the two. That is the whole reason this is not forkpty, which is
    /// the more obvious call and which corrupts the process it is called from.
    /// </remarks>
    private OperationResult Launch(
        ProcessRequest request,
        string? directory,
        ref NativeTerminal.WindowSize size)
    {
        var master = NativeTerminal.OpenPseudoTerminal(NativeTerminal.MasterFlags);

        if (master < 0)
        {
            return Failure("A pseudo-terminal could not be allocated");
        }

        if (NativeTerminal.GrantSlave(master) != 0 || NativeTerminal.UnlockSlave(master) != 0)
        {
            NativeTerminal.Close(master);

            return Failure("The pseudo-terminal could not be unlocked");
        }

        var namePointer = NativeTerminal.SlaveName(master);

        if (namePointer == 0)
        {
            NativeTerminal.Close(master);

            return Failure("The pseudo-terminal has no slave device");
        }

        // Copied out immediately: the pointer is into storage the C library
        // owns and reuses on the next call.
        var slavePath = Marshal.PtrToStringAnsi(namePointer);

        if (string.IsNullOrEmpty(slavePath))
        {
            NativeTerminal.Close(master);

            return Failure("The pseudo-terminal has no slave device");
        }

        NativeTerminal.Ioctl(master, NativeTerminal.SetWindowSize, ref size);

        var spawned = SpawnChild(request, directory, slavePath, out var child);

        if (spawned.Failed)
        {
            NativeTerminal.Close(master);

            return spawned;
        }

        _child = child;
        _master = new SafeFileHandle(master, ownsHandle: true);
        _stream = new FileStream(_master, FileAccess.ReadWrite, bufferSize: 1, isAsync: false);

        return OperationResult.Ok();
    }

    private static OperationResult SpawnChild(
        ProcessRequest request,
        string? directory,
        string slavePath,
        out int child)
    {
        child = 0;

        var actions = NativeTerminal.AllocateOpaque();
        var attributes = NativeTerminal.AllocateOpaque();

        var actionsReady = false;
        var attributesReady = false;

        using var path = new NativeString(request.Executable);
        using var slave = new NativeString(slavePath);
        using var workingDirectory = new NativeString(directory);
        using var arguments = NativeStringArray.ForArguments(request);
        using var environment = NativeStringArray.ForEnvironment(request);

        try
        {
            if (NativeTerminal.FileActionsInit(actions) != 0)
            {
                return Failure("The spawn file actions could not be prepared");
            }

            actionsReady = true;

            if (NativeTerminal.AttributesInit(attributes) != 0)
            {
                return Failure("The spawn attributes could not be prepared");
            }

            attributesReady = true;

            // A new session, so the child leads its own process group and the
            // pty can become its controlling terminal. Without that Ctrl+C
            // never reaches it as SIGINT and the agent cannot be interrupted.
            if (NativeTerminal.AttributesSetFlags(attributes, NativeTerminal.NewSession) != 0)
            {
                return Failure("The child could not be given its own session");
            }

            if (directory is not null
                && NativeTerminal.FileActionsAddChangeDirectory(actions, workingDirectory.Pointer) != 0)
            {
                return Failure($"The working directory could not be set to '{directory}'");
            }

            // The slave is opened by the child rather than inherited from here.
            // Opening a terminal as the leader of a new session is what makes
            // it the controlling one, and that has to happen in the child.
            if (NativeTerminal.FileActionsAddOpen(actions, 0, slave.Pointer, NativeTerminal.SlaveFlags, 0) != 0
                || NativeTerminal.FileActionsAddDuplicate(actions, 0, 1) != 0
                || NativeTerminal.FileActionsAddDuplicate(actions, 0, 2) != 0)
            {
                return Failure("The child's standard handles could not be attached to the terminal");
            }

            var result = NativeTerminal.Spawn(
                out child,
                path.Pointer,
                actions,
                attributes,
                arguments.Pointer,
                environment.Pointer);

            if (result != 0)
            {
                // posix_spawn returns the error rather than setting errno, and
                // it reports a missing executable here rather than in the child,
                // which is more useful than the 127 a shell would give.
                return OperationResult.Fail(
                    $"'{request.Executable}' could not be started: "
                    + new System.ComponentModel.Win32Exception(result).Message,
                    ExitCode.AgentUnavailable);
            }

            return OperationResult.Ok();
        }
        finally
        {
            if (actionsReady)
            {
                NativeTerminal.FileActionsDestroy(actions);
            }

            if (attributesReady)
            {
                NativeTerminal.AttributesDestroy(attributes);
            }

            Marshal.FreeHGlobal(actions);
            Marshal.FreeHGlobal(attributes);
        }
    }

    private static OperationResult Failure(string what) =>
        OperationResult.Fail($"{what}: {Marshal.GetLastPInvokeErrorMessage()}");

    /// <inheritdoc />
    public async Task<OperationResult> WriteAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken ct = default)
    {
        if (_stream is null)
        {
            return OperationResult.Fail("The pseudo-terminal has not been started.");
        }

        try
        {
            await _stream.WriteAsync(data, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);

            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            return OperationResult.Fail($"The agent is no longer reading input: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<int>> ReadAsync(
        Memory<byte> buffer,
        CancellationToken ct = default)
    {
        if (_stream is null)
        {
            return OperationResult<int>.Fail("The pseudo-terminal has not been started.");
        }

        try
        {
            return OperationResult<int>.Ok(await _stream.ReadAsync(buffer, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Reading a master whose slave has closed raises EIO rather than
            // reporting end-of-file. It means the same thing: the child has
            // gone and there is nothing more to read.
            return OperationResult<int>.Ok(0);
        }
    }

    /// <inheritdoc />
    public OperationResult Resize(int columns, int rows)
    {
        if (_master is null || _master.IsInvalid)
        {
            return OperationResult.Fail("The pseudo-terminal has not been started.");
        }

        var size = new NativeTerminal.WindowSize
        {
            Columns = (ushort)Math.Clamp(columns, 1, ushort.MaxValue),
            Rows = (ushort)Math.Clamp(rows, 1, ushort.MaxValue),
        };

        return NativeTerminal.Ioctl(
            (int)_master.DangerousGetHandle(), NativeTerminal.SetWindowSize, ref size) == 0
            ? OperationResult.Ok()
            : OperationResult.Fail(
                "The terminal size could not be set: " + Marshal.GetLastPInvokeErrorMessage());
    }

    /// <inheritdoc />
    public async Task<OperationResult<int>> WaitForExitAsync(CancellationToken ct = default)
    {
        if (_child == 0)
        {
            return OperationResult<int>.Fail("The pseudo-terminal has not been started.");
        }

        while (true)
        {
            var reaped = TryReap();

            if (reaped is not null)
            {
                return OperationResult<int>.Ok(reaped.Value);
            }

            // Polled rather than waited on, so cancellation is honoured. A
            // blocking waitpid cannot be interrupted without a signal handler,
            // and installing one would reach outside this class into whatever
            // else the process is doing.
            await Task.Delay(25, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Collects the child if it has finished. Returns null while it is running.
    /// <para>
    /// The result is remembered because a child can only be reaped once: a
    /// second waitpid for the same process reports failure, and a caller asking
    /// twice should get the same answer rather than an error.
    /// </para>
    /// </summary>
    private int? TryReap()
    {
        lock (_gate)
        {
            if (_exited)
            {
                return _exitCode;
            }

            // Nothing was ever started, so there is nothing of ours to collect.
            // The guard is not defensive tidiness: waitpid treats a pid of zero
            // as "any child in my process group", so calling it here would reap
            // some unrelated process the runtime was tracking, take its exit
            // status away, and leave whatever was waiting on it hanging.
            if (_child <= 0)
            {
                return null;
            }

            var result = NativeTerminal.WaitPid(_child, out var status, NativeTerminal.NoHang);

            if (result == 0)
            {
                return null;
            }

            _exited = true;

            // A negative result means the child is already gone and something
            // else collected it. Nothing useful is left to report, and treating
            // it as a failure would turn a finished session into an error.
            _exitCode = result < 0 ? 0 : NativeTerminal.InterpretStatus(status);

            return _exitCode;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _stream?.Dispose();
        _stream = null;

        // Closing the master sends SIGHUP to the child's session, which is how
        // a well-behaved program learns its terminal has gone. Only if it is
        // still there afterwards is anything stronger warranted, and that is
        // the caller's decision rather than this one's.
        _master?.Dispose();
        _master = null;

        TryReap();
    }

    /// <summary>A NUL-terminated string in unmanaged memory.</summary>
    private readonly struct NativeString : IDisposable
    {
        internal NativeString(string? value) =>
            Pointer = value is null ? 0 : Marshal.StringToHGlobalAnsi(value);

        internal nint Pointer { get; }

        public void Dispose()
        {
            if (Pointer != 0)
            {
                Marshal.FreeHGlobal(Pointer);
            }
        }
    }

    /// <summary>
    /// A NULL-terminated vector of strings in unmanaged memory, laid out the way
    /// <c>execve</c> expects to find argv and envp.
    /// </summary>
    private readonly struct NativeStringArray : IDisposable
    {
        private readonly nint[] _entries;

        private NativeStringArray(IReadOnlyList<string> values)
        {
            _entries = new nint[values.Count];

            for (var i = 0; i < values.Count; i++)
            {
                _entries[i] = Marshal.StringToHGlobalAnsi(values[i]);
            }

            // One extra slot, left null, which is how the vector's end is found.
            Pointer = Marshal.AllocHGlobal(nint.Size * (values.Count + 1));

            for (var i = 0; i < values.Count; i++)
            {
                Marshal.WriteIntPtr(Pointer, nint.Size * i, _entries[i]);
            }

            Marshal.WriteIntPtr(Pointer, nint.Size * values.Count, 0);
        }

        internal nint Pointer { get; }

        /// <summary>
        /// argv, whose first entry is the program name by convention. Programs
        /// print it in their own usage text, so passing the path rather than a
        /// bare name is what makes their errors say something recognisable.
        /// </summary>
        internal static NativeStringArray ForArguments(ProcessRequest request) =>
            new([request.Executable, .. request.Arguments]);

        internal static NativeStringArray ForEnvironment(ProcessRequest request)
        {
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (System.Collections.DictionaryEntry entry in
                System.Environment.GetEnvironmentVariables())
            {
                merged[(string)entry.Key] = entry.Value?.ToString() ?? string.Empty;
            }

            if (request.Environment is not null)
            {
                foreach (var (key, value) in request.Environment)
                {
                    merged[key] = value;
                }
            }

            return new NativeStringArray(
                merged.Select(pair => $"{pair.Key}={pair.Value}").ToList());
        }

        public void Dispose()
        {
            foreach (var entry in _entries)
            {
                Marshal.FreeHGlobal(entry);
            }

            Marshal.FreeHGlobal(Pointer);
        }
    }
}
