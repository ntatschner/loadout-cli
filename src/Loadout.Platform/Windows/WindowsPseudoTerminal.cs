using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;
using Microsoft.Win32.SafeHandles;

namespace Loadout.Platform.Windows;

/// <summary>
/// A pseudo-terminal owned by the launcher, built on ConPTY.
/// <para>
/// The child sees a real console: it queries the window size, emits colour,
/// enables virtual-terminal processing and reads keys exactly as it would in
/// Windows Terminal. What it cannot see is that the console on the other end is
/// a pair of pipes the launcher is holding.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPseudoTerminal : IPseudoTerminal
{
    /// <summary>
    /// Pipe buffer size. Large enough that a burst of output from a child
    /// redrawing a full screen does not block it while the reader catches up.
    /// </summary>
    private const int PipeBufferBytes = 64 * 1024;

    private readonly object _gate = new();

    private nint _pseudoConsole;
    private nint _process;
    private nint _thread;
    private nint _attributeList;

    private SafeFileHandle? _inputRead;
    private SafeFileHandle? _outputWrite;
    private SafeFileHandle? _outputRead;

    /// <summary>
    /// Set once the reader has asked for the console to be closed, so it is
    /// asked exactly once and the reader knows to stop polling.
    /// </summary>
    private Task? _closing;

    private FileStream? _toChild;
    private FileStream? _fromChild;

    private bool _disposed;

    /// <inheritdoc />
    public async Task<OperationResult> StartAsync(
        ProcessRequest request,
        int columns,
        int rows,
        CancellationToken ct = default)
    {
        if (_process != 0)
        {
            return OperationResult.Fail("This pseudo-terminal has already been started.");
        }

        // Clamped rather than rejected. A caller that has not measured the
        // window yet passes zero, and a console of zero columns makes the child
        // misbehave in ways that are hard to attribute back to here.
        var size = new NativeConsole.Coord
        {
            X = (short)Math.Clamp(columns, 1, short.MaxValue),
            Y = (short)Math.Clamp(rows, 1, short.MaxValue),
        };

        var created = Create(request, size);

        if (created.Failed)
        {
            Cleanup();
        }

        // Nothing here is genuinely asynchronous: process creation is a
        // synchronous kernel call. The signature stays async so the Unix
        // implementation, which does have to wait, is not forced to lie.
        return await Task.FromResult(created).ConfigureAwait(false);
    }

    private OperationResult Create(ProcessRequest request, NativeConsole.Coord size)
    {
        var attributes = new NativeConsole.SecurityAttributes
        {
            Length = Marshal.SizeOf<NativeConsole.SecurityAttributes>(),
            InheritHandle = 0,
        };

        if (!NativeConsole.CreatePipe(out var inputRead, out var inputWrite, ref attributes, PipeBufferBytes)
            || !NativeConsole.CreatePipe(out var outputRead, out var outputWrite, ref attributes, PipeBufferBytes))
        {
            return Failure("The console pipes could not be created");
        }

        _inputRead = inputRead;
        _outputWrite = outputWrite;

        // The pseudo-console takes the ends the child will use. The launcher
        // keeps the other two and talks to the child through them.
        var hr = NativeConsole.CreatePseudoConsole(size, inputRead, outputWrite, 0, out _pseudoConsole);

        if (hr != 0)
        {
            return OperationResult.Fail(
                $"A pseudo-console could not be created (HRESULT 0x{hr:X8}). ConPTY requires "
                + "Windows 10 version 1809 or later.");
        }

        var prepared = PrepareAttributeList();

        if (prepared.Failed)
        {
            return prepared;
        }

        var startup = new NativeConsole.StartupInfoEx
        {
            StartupInfo = new NativeConsole.StartupInfo
            {
                Size = Marshal.SizeOf<NativeConsole.StartupInfoEx>(),

                // Declared, with all three handles left null. Without this the
                // child inherits whatever the launcher's own standard handles
                // happen to be, and when the launcher is itself running with
                // redirected output that is where the agent's output goes:
                // attached to the pseudo-console for sizing and cursor
                // behaviour, while everything it prints bypasses it entirely.
                // Null handles make the console the child attaches to supply
                // them, which is the whole point of attaching it.
                Flags = NativeConsole.UseStandardHandles,
                StdInput = 0,
                StdOutput = 0,
                StdError = 0,
            },
            AttributeList = _attributeList,
        };

        var commandLine = BuildCommandLine(request).ToCharArray().Append('\0').ToArray();
        var environment = BuildEnvironment(request);

        try
        {
            var started = NativeConsole.CreateProcess(
                null,
                ref commandLine[0],
                0,
                0,
                // True, and safe: every handle this class creates is marked
                // non-inheritable, so there is nothing for the child to inherit
                // except what the attribute list gives it. Passing false looks
                // more careful and is actively wrong — the child then starts
                // with no standard handles at all, so it writes to nothing and
                // the session appears silent while the console underneath it
                // works perfectly.
                inheritHandles: false,
                NativeConsole.ExtendedStartupInfoPresent | NativeConsole.CreateUnicodeEnvironment,
                environment.Pointer,
                request.WorkingDirectory,
                ref startup,
                out var information);

            if (!started)
            {
                return Failure("The agent could not be started");
            }

            _process = information.Process;
            _thread = information.Thread;
        }
        finally
        {
            environment.Dispose();
        }

        // Closed now, not at dispose. While the launcher still holds the
        // child's ends, a read from the child's output never reaches
        // end-of-file, so waiting for output would hang forever after the
        // child exits.
        _inputRead.Dispose();
        _outputWrite.Dispose();
        _inputRead = null;
        _outputWrite = null;

        _outputRead = outputRead;
        _toChild = new FileStream(inputWrite, FileAccess.Write, bufferSize: 1, isAsync: false);
        _fromChild = new FileStream(outputRead, FileAccess.Read, bufferSize: 1, isAsync: false);

        return OperationResult.Ok();
    }

    /// <summary>Closes the pseudo-console exactly once, whoever gets there first.</summary>
    private void CloseConsole()
    {
        nint console;

        lock (_gate)
        {
            console = _pseudoConsole;
            _pseudoConsole = 0;
        }

        if (console != 0)
        {
            NativeConsole.ClosePseudoConsole(console);
        }
    }

    /// <summary>
    /// Builds the attribute list that hands the pseudo-console to the child.
    /// <para>
    /// The list is unmanaged memory the child creation reads from, so it stays
    /// allocated until the terminal is disposed rather than being freed as soon
    /// as the process exists.
    /// </para>
    /// </summary>
    private OperationResult PrepareAttributeList()
    {
        nint size = 0;

        // Expected to fail: the first call reports the size it needs.
        NativeConsole.InitializeProcThreadAttributeList(0, 1, 0, ref size);

        _attributeList = Marshal.AllocHGlobal(size);

        if (!NativeConsole.InitializeProcThreadAttributeList(_attributeList, 1, 0, ref size))
        {
            return Failure("The process attribute list could not be prepared");
        }

        return NativeConsole.UpdateProcThreadAttribute(
            _attributeList,
            0,
            NativeConsole.ProcThreadAttributePseudoConsole,
            _pseudoConsole,
            Marshal.SizeOf<nint>(),
            0,
            0)
            ? OperationResult.Ok()
            : Failure("The pseudo-console could not be attached to the child process");
    }

    /// <inheritdoc />
    public async Task<OperationResult> WriteAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken ct = default)
    {
        if (_toChild is null)
        {
            return OperationResult.Fail("The pseudo-terminal has not been started.");
        }

        try
        {
            await _toChild.WriteAsync(data, ct).ConfigureAwait(false);
            await _toChild.FlushAsync(ct).ConfigureAwait(false);

            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The child closing its input is an ordinary end to a session, not
            // a fault worth a stack trace.
            return OperationResult.Fail($"The agent is no longer reading input: {ex.Message}");
        }
    }

    /// <summary>
    /// How long to wait between checks when the console has nothing to say.
    /// Short enough to be imperceptible in an interactive session.
    /// </summary>
    private const int IdlePollMilliseconds = 15;

    /// <summary>
    /// Consecutive quiet checks required before the console is closed. One is
    /// not enough: the host writes in bursts, and a single gap between two of
    /// them looks exactly like the end of the stream.
    /// </summary>
    private const int QuietChecksRequired = 3;

    /// <inheritdoc />
    /// <remarks>
    /// The reader is what decides a session is over, and it has to, because
    /// nothing else can. The write end of the output pipe belongs to the
    /// console host rather than to the child, so it stays open after the child
    /// exits and a plain blocking read never reaches end-of-file. The console
    /// must be closed to release it — but closing it the instant the child
    /// exits discards whatever the host had not yet written, which in practice
    /// means losing the last thing the agent printed.
    /// <para>
    /// So: read what is there, and once the child has gone and the pipe has
    /// stayed empty across several checks, close the console and let the read
    /// return zero. Output here can be late; it cannot be lost.
    /// </para>
    /// <para>
    /// The check runs on this thread rather than a watcher because a pipe from
    /// CreatePipe is synchronous, and a second operation on a synchronous
    /// handle waits for the first: a watcher peeking the handle would block
    /// behind the very read it was trying to release.
    /// </para>
    /// </remarks>
    public async Task<OperationResult<int>> ReadAsync(
        Memory<byte> buffer,
        CancellationToken ct = default)
    {
        if (_fromChild is null || _outputRead is null)
        {
            return OperationResult<int>.Fail("The pseudo-terminal has not been started.");
        }

        var quiet = 0;

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (!NativeConsole.PeekNamedPipe(_outputRead, 0, 0, 0, out var available, 0))
                {
                    // The pipe has gone, which is the same news a zero-length
                    // read would carry.
                    return OperationResult<int>.Ok(0);
                }

                if (available > 0)
                {
                    // Returns immediately: something is already waiting.
                    return OperationResult<int>.Ok(
                        await _fromChild.ReadAsync(buffer, ct).ConfigureAwait(false));
                }

                if (_closing is not null)
                {
                    // The console is closing. A blocking read now returns
                    // whatever the host flushes on the way out, and then zero.
                    return OperationResult<int>.Ok(
                        await _fromChild.ReadAsync(buffer, ct).ConfigureAwait(false));
                }

                if (HasExited())
                {
                    if (++quiet >= QuietChecksRequired)
                    {
                        // Closed on another thread, deliberately. The close does
                        // not return until pending output has been consumed, so
                        // calling it from the reader would leave it waiting for
                        // the very thread that is waiting for it.
                        _closing = Task.Run(CloseConsole);

                        continue;
                    }
                }
                else
                {
                    quiet = 0;
                }

                await Task.Delay(IdlePollMilliseconds, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            return OperationResult<int>.Ok(0);
        }
    }

    /// <summary>Whether the child has finished.</summary>
    private bool HasExited() =>
        _process == 0
        || (NativeConsole.GetExitCodeProcess(_process, out var code)
            && code != NativeConsole.StillActive);

    /// <inheritdoc />
    public OperationResult Resize(int columns, int rows)
    {
        nint console;

        lock (_gate)
        {
            console = _pseudoConsole;
        }

        if (console == 0)
        {
            // Also the state after the child exits, which is not an error worth
            // raising: a resize arriving as a session ends is a race the caller
            // cannot avoid and does not need to handle.
            return _process == 0
                ? OperationResult.Fail("The pseudo-terminal has not been started.")
                : OperationResult.Ok();
        }

        var size = new NativeConsole.Coord
        {
            X = (short)Math.Clamp(columns, 1, short.MaxValue),
            Y = (short)Math.Clamp(rows, 1, short.MaxValue),
        };

        var hr = NativeConsole.ResizePseudoConsole(console, size);

        return hr == 0
            ? OperationResult.Ok()
            : OperationResult.Fail($"The pseudo-console could not be resized (HRESULT 0x{hr:X8}).");
    }

    /// <inheritdoc />
    public async Task<OperationResult<int>> WaitForExitAsync(CancellationToken ct = default)
    {
        if (_process == 0)
        {
            return OperationResult<int>.Fail("The pseudo-terminal has not been started.");
        }

        var process = _process;

        // Waited for on a pool thread rather than by polling. WaitForSingleObject
        // blocks, and the caller is usually pumping output on another task.
        return await Task.Run(
            () =>
            {
                while (true)
                {
                    if (ct.IsCancellationRequested)
                    {
                        return OperationResult<int>.Fail(
                            "Waiting for the agent was cancelled.", ExitCode.GeneralFailure);
                    }

                    // A bounded wait so cancellation is noticed. An infinite one
                    // would leave the caller unable to give up on a wedged child.
                    var waited = NativeConsole.WaitForSingleObject(process, 250);

                    if (waited == 0
                        && NativeConsole.GetExitCodeProcess(process, out var code)
                        && code != NativeConsole.StillActive)
                    {
                        return OperationResult<int>.Ok(unchecked((int)code));
                    }

                    if (waited != 0 && waited != 258)
                    {
                        return OperationResult<int>.Fail(
                            "Waiting for the agent failed: "
                            + new System.ComponentModel.Win32Exception(
                                Marshal.GetLastWin32Error()).Message);
                    }
                }
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Quotes arguments the way <c>CommandLineToArgvW</c> will read them back.
    /// <para>
    /// Windows passes a command line, not an argument vector, so every process
    /// gets to decide how to split it. Matching the rules the C runtime uses is
    /// what makes a path with a space in it arrive as one argument.
    /// </para>
    /// </summary>
    internal static string BuildCommandLine(ProcessRequest request)
    {
        var builder = new StringBuilder();

        Append(builder, request.Executable);

        foreach (var argument in request.Arguments)
        {
            builder.Append(' ');
            Append(builder, argument);
        }

        return builder.ToString();

        static void Append(StringBuilder builder, string value)
        {
            if (value.Length > 0 && !value.AsSpan().ContainsAny(' ', '\t', '"'))
            {
                builder.Append(value);
                return;
            }

            builder.Append('"');

            for (var i = 0; i < value.Length; i++)
            {
                var slashes = 0;

                while (i < value.Length && value[i] == '\\')
                {
                    slashes++;
                    i++;
                }

                if (i == value.Length)
                {
                    // Trailing backslashes are doubled so they do not escape
                    // the closing quote this method is about to add.
                    builder.Append('\\', slashes * 2);
                    break;
                }

                builder.Append('\\', value[i] == '"' ? (slashes * 2) + 1 : slashes);
                builder.Append(value[i]);
            }

            builder.Append('"');
        }
    }

    /// <summary>
    /// The child's environment block: a run of NAME=VALUE strings terminated by
    /// an extra null. Null when the caller wants the launcher's own.
    /// </summary>
    private static EnvironmentBlock BuildEnvironment(ProcessRequest request)
    {
        if (request.Environment is not { Count: > 0 })
        {
            return default;
        }

        var merged = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Collections.DictionaryEntry entry in
            System.Environment.GetEnvironmentVariables())
        {
            merged[(string)entry.Key] = entry.Value?.ToString() ?? string.Empty;
        }

        foreach (var (key, value) in request.Environment)
        {
            merged[key] = value;
        }

        var builder = new StringBuilder();

        foreach (var (key, value) in merged)
        {
            builder.Append(key).Append('=').Append(value).Append('\0');
        }

        builder.Append('\0');

        return new EnvironmentBlock(Marshal.StringToHGlobalUni(builder.ToString()));
    }

    private readonly struct EnvironmentBlock(nint pointer) : IDisposable
    {
        internal nint Pointer { get; } = pointer;

        public void Dispose()
        {
            if (Pointer != 0)
            {
                Marshal.FreeHGlobal(Pointer);
            }
        }
    }

    private static OperationResult Failure(string what) =>
        OperationResult.Fail(
            $"{what}: "
            + new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message);

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

        Cleanup();
    }

    private void Cleanup()
    {
        // The pseudo-console goes first. Closing it signals the child that its
        // console has gone, which is what lets a well-behaved one exit rather
        // than being killed.
        CloseConsole();

        _toChild?.Dispose();
        _fromChild?.Dispose();
        _toChild = null;
        _fromChild = null;

        _inputRead?.Dispose();
        _outputWrite?.Dispose();
        _inputRead = null;
        _outputWrite = null;
        _outputRead = null;

        if (_attributeList != 0)
        {
            NativeConsole.DeleteProcThreadAttributeList(_attributeList);
            Marshal.FreeHGlobal(_attributeList);
            _attributeList = 0;
        }

        if (_thread != 0)
        {
            NativeConsole.CloseHandle(_thread);
            _thread = 0;
        }

        if (_process != 0)
        {
            NativeConsole.CloseHandle(_process);
            _process = 0;
        }
    }
}
