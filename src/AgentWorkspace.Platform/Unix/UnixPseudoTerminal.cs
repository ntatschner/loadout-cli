using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AgentWorkspace.Models;
using AgentWorkspace.Models.Results;
using AgentWorkspace.Platform.Abstractions;
using Microsoft.Win32.SafeHandles;

namespace AgentWorkspace.Platform.Unix;

/// <summary>
/// A pseudo-terminal owned by the launcher, built on <c>forkpty</c>.
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

        // Everything the child touches is built here, in the parent, and pinned
        // in unmanaged memory. See the remarks on Launch for why.
        using var arguments = NativeStringArray.ForArguments(request);
        using var environment = NativeStringArray.ForEnvironment(request);
        using var path = new NativeString(request.Executable);

        var directory = request.WorkingDirectory;

        if (directory is not null && !Directory.Exists(directory))
        {
            return Task.FromResult(OperationResult.Fail(
                $"The working directory '{directory}' does not exist.", ExitCode.InvalidArguments));
        }

        return Task.FromResult(Launch(path, arguments, environment, directory, ref size));
    }

    /// <summary>
    /// Forks and execs.
    /// </summary>
    /// <remarks>
    /// The few statements that run in the child are the delicate part of this
    /// class. A forked process inherits one thread out of however many the
    /// runtime was using, along with any locks the others were holding, so
    /// anything that allocates, takes a lock or triggers compilation can
    /// deadlock and never return. The rules observed here are therefore:
    /// every string and array is marshalled before the fork, every method the
    /// child calls is compiled before the fork, and the child does nothing but
    /// exec and exit.
    /// </remarks>
    private OperationResult Launch(
        NativeString path,
        NativeStringArray arguments,
        NativeStringArray environment,
        string? directory,
        ref NativeTerminal.WindowSize size)
    {
        // Compiled now so the child never has to. A first call inside a forked
        // process would run the compiler while holding none of the locks it
        // expects to exist.
        RuntimeHelpers.PrepareMethod(
            typeof(NativeTerminal).GetMethod(nameof(NativeTerminal.Execve))!.MethodHandle);

        RuntimeHelpers.PrepareMethod(
            typeof(NativeTerminal).GetMethod(nameof(NativeTerminal.Exit))!.MethodHandle);

        // Changed before the fork rather than after it. chdir in the child is
        // one more call than the rules above allow, and the launcher is
        // single-purpose enough that moving back afterwards is safe.
        var previous = Directory.GetCurrentDirectory();

        if (directory is not null)
        {
            Directory.SetCurrentDirectory(directory);
        }

        int child;

        try
        {
            child = NativeTerminal.ForkPty(out var master, 0, 0, ref size);

            if (child == 0)
            {
                // In the child. Two calls, no allocation, no return.
                NativeTerminal.Execve(path.Pointer, arguments.Pointer, environment.Pointer);
                NativeTerminal.Exit(127);
            }

            if (child < 0)
            {
                return OperationResult.Fail(
                    "A pseudo-terminal could not be allocated: "
                    + Marshal.GetLastPInvokeErrorMessage());
            }

            _child = child;
            _master = new SafeFileHandle(master, ownsHandle: true);
            _stream = new FileStream(_master, FileAccess.ReadWrite, bufferSize: 1, isAsync: false);
        }
        finally
        {
            if (directory is not null)
            {
                Directory.SetCurrentDirectory(previous);
            }
        }

        return OperationResult.Ok();
    }

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

        // The request number differs between the two Unixes, and the wrong one
        // fails harmlessly with EINVAL rather than doing something else, so the
        // second attempt costs nothing.
        var request = OperatingSystem.IsMacOS()
            ? NativeTerminal.SetWindowSizeBsd
            : NativeTerminal.SetWindowSize;

        if (NativeTerminal.Ioctl((int)_master.DangerousGetHandle(), request, ref size) == 0)
        {
            return OperationResult.Ok();
        }

        var fallback = OperatingSystem.IsMacOS()
            ? NativeTerminal.SetWindowSize
            : NativeTerminal.SetWindowSizeBsd;

        return NativeTerminal.Ioctl((int)_master.DangerousGetHandle(), fallback, ref size) == 0
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
        internal NativeString(string value) => Pointer = Marshal.StringToHGlobalAnsi(value);

        internal nint Pointer { get; }

        public void Dispose() => Marshal.FreeHGlobal(Pointer);
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
