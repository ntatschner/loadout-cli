using System.Diagnostics;
using System.Text;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Common;

/// <summary>
/// Starts child processes using the .NET process APIs, which map onto native
/// process creation on all three platforms. Shared rather than per-platform
/// because the semantics genuinely are the same; only the PTY case differs,
/// and that lives behind IPseudoTerminal.
/// </summary>
public sealed class ProcessLauncher : IProcessLauncher
{
    private readonly IChildLifetime? _lifetime;

    /// <summary>
    /// A launcher whose piped children are tied to this process, or, with no
    /// lifetime given, one whose children are nobody's responsibility.
    /// </summary>
    /// <remarks>
    /// Optional because most of what starts a process here is a question that
    /// answers in milliseconds, and because a test that runs git does not
    /// want a process-wide exit handler. What needs it is the piped path,
    /// where the child is an agent that would otherwise carry on alone.
    /// </remarks>
    public ProcessLauncher(IChildLifetime? lifetime = null) => _lifetime = lifetime;

    /// <inheritdoc />
    public async Task<OperationResult<ProcessOutcome>> RunAsync(
        ProcessRequest request,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var startInfo = BuildStartInfo(request);
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.RedirectStandardInput = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        using var process = new Process { StartInfo = startInfo };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { stdout.AppendLine(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { stderr.AppendLine(e.Data); } };

        try
        {
            if (!process.Start())
            {
                return OperationResult<ProcessOutcome>.Fail(
                    $"Could not start '{request.Executable}'.");
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // A missing executable is an ordinary outcome for probes such as
            // agent discovery, so it is reported rather than thrown.
            return OperationResult<ProcessOutcome>.Fail(
                $"Could not start '{request.Executable}': {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (request.StandardInput is not null)
        {
            await process.StandardInput.WriteAsync(request.StandardInput).ConfigureAwait(false);
        }

        // Always redirected and always closed, whether or not there was
        // anything to write. Two separate hangs come from getting this wrong.
        // Credential tools such as secret-tool and security read until EOF, so
        // leaving the pipe open after writing wedges the launcher. And a child
        // whose standard input is not redirected inherits the caller's, which
        // is fine from a terminal and fatal under 'mcp serve': the client holds
        // that pipe open and reads it, and 'git rev-parse' spawned there never
        // exited. It was killed at the timeout below, so working out which
        // project the caller was in failed, and every tool answered as though
        // the project did not exist — thirty seconds spent to say so, with no
        // error anywhere to suggest the answer was wrong.
        //
        // Nothing started here is interactive by construction: its output is
        // captured, so it has no terminal to read from either way.

        process.StandardInput.Close();

        // A timeout is its own cancellation source so that a hung child does
        // not wedge the launcher. Section 45 puts process work in front of
        // every launch, so nothing here may block indefinitely.
        using var timeoutSource = timeout is null
            ? null
            : new CancellationTokenSource(timeout.Value);
        using var linked = timeoutSource is null
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            if (timeoutSource?.IsCancellationRequested == true && !ct.IsCancellationRequested)
            {
                return OperationResult<ProcessOutcome>.Fail(
                    $"'{request.Executable}' did not finish within {timeout!.Value.TotalSeconds:0.#}s.");
            }

            throw;
        }

        return OperationResult<ProcessOutcome>.Ok(
            new ProcessOutcome(process.ExitCode, stdout.ToString(), stderr.ToString()));
    }

    /// <inheritdoc />
    public async Task<OperationResult<int>> RunInteractiveAsync(
        ProcessRequest request,
        CancellationToken ct = default)
    {
        var startInfo = BuildStartInfo(request);

        // Nothing is redirected, so the child inherits the launcher's real
        // terminal handles. That is what gives it correct Ctrl+C, signal
        // delivery, resize notification and exit status without the launcher
        // sitting in the middle of the stream (spec section 43).
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = false;
        startInfo.RedirectStandardError = false;
        startInfo.RedirectStandardInput = false;

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                return OperationResult<int>.Fail(
                    $"Could not start '{request.Executable}'.",
                    ExitCode.AgentUnavailable);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return OperationResult<int>.Fail(
                $"Could not start '{request.Executable}': {ex.Message}",
                ExitCode.AgentUnavailable);
        }

        // Ctrl+C reaches the child directly because it shares this process
        // group and console. The launcher must not race it to the exit, so
        // cancellation here waits for the child rather than killing it.
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

        return OperationResult<int>.Ok(process.ExitCode);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IPipedProcess>> StartPipedAsync(
        ProcessRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = BuildStartInfo(request);
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        // UTF-8 on every stream whatever the console code page says. The
        // agents write JSON lines in UTF-8, and a Windows console defaults to
        // a legacy code page that turns any non-ASCII character in a reply
        // into a question mark, which then fails to parse as the JSON it was.
        startInfo.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        try
        {
            if (!process.Start())
            {
                process.Dispose();

                return OperationResult<IPipedProcess>.Fail(
                    $"Could not start '{request.Executable}'.", ExitCode.AgentUnavailable);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            process.Dispose();

            return OperationResult<IPipedProcess>.Fail(
                $"Could not start '{request.Executable}': {ex.Message}", ExitCode.AgentUnavailable);
        }

        // Before anything is written to it: a child adopted after the first
        // message is a child that could outlive the launcher for as long as
        // that took.
        _lifetime?.Adopt(process.Id);

        var piped = new PipedProcess(process);

        if (request.StandardInput is not null)
        {
            await piped.Input.WriteAsync(request.StandardInput.AsMemory(), ct).ConfigureAwait(false);
            await piped.Input.FlushAsync(ct).ConfigureAwait(false);
        }

        return OperationResult<IPipedProcess>.Ok(piped);
    }

    /// <summary>The running half of <see cref="StartPipedAsync"/>.</summary>
    private sealed class PipedProcess : IPipedProcess
    {
        private readonly Process _process;
        private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _inputClosed;

        internal PipedProcess(Process process)
        {
            _process = process;

            // Recorded from the process rather than from the clock, so it is
            // the same value the inspector will compare against later.
            StartedAt = process.StartTime;

            _process.Exited += (_, _) => _exited.TrySetResult(_process.ExitCode);

            // The event can fire before the handler is attached on a child
            // that exits at once, so the state is checked as well.
            if (_process.HasExited)
            {
                _exited.TrySetResult(_process.ExitCode);
            }
        }

        /// <inheritdoc />
        public int ProcessId => _process.Id;

        /// <inheritdoc />
        public DateTimeOffset StartedAt { get; }

        /// <inheritdoc />
        public TextWriter Input => _process.StandardInput;

        /// <inheritdoc />
        public TextReader Output => _process.StandardOutput;

        /// <inheritdoc />
        public TextReader Error => _process.StandardError;

        /// <inheritdoc />
        public Task<int> Exited => _exited.Task;

        /// <inheritdoc />
        public async Task CloseInputAsync()
        {
            if (_inputClosed)
            {
                return;
            }

            _inputClosed = true;

            try
            {
                await _process.StandardInput.FlushAsync().ConfigureAwait(false);
                _process.StandardInput.Close();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The child went away first. Nothing to tell it.
            }
        }

        /// <inheritdoc />
        public void Kill() => TryKill(_process);

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await CloseInputAsync().ConfigureAwait(false);
            _process.Dispose();
        }
    }

    /// <summary>
    /// How long to let a launcher shim finish handing over before going on.
    /// Long enough for a slow disk, short enough not to be noticed.
    /// </summary>
    private const int HandoverTimeout = 15_000;

    /// <inheritdoc />
    public OperationResult StartDetached(ProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = BuildStartInfo(request);

        // Everything here is the opposite of RunAsync on purpose, and each part
        // of it was established by trying the alternative: nothing redirected,
        // no window suppressed, and no wait. An editor opened with its output
        // captured and its window suppressed came up as an empty frame with no
        // workbench in it.
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = false;
        startInfo.RedirectStandardError = false;
        startInfo.RedirectStandardInput = false;
        startInfo.CreateNoWindow = false;

        try
        {
            var process = Process.Start(startInfo);

            if (process is null)
            {
                return OperationResult.Fail(
                    $"'{request.Executable}' did not start.", ExitCode.GeneralFailure);
            }

            // Waited for, despite the name. What is started here is usually a
            // shim rather than the application: 'code' on Windows is a batch
            // file that hands the folder to the editor and returns, and the
            // editor outlives both. Letting the launcher exit while that
            // handover is still in flight is the difference between the editor
            // opening the folder and coming up as an empty frame.
            //
            // Bounded, because a command that does not return must not hold the
            // launcher: the wait is abandoned rather than the process killed,
            // since the thing being started is meant to outlive us.
            process.WaitForExit(HandoverTimeout);
            process.Dispose();

            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return OperationResult.Fail(
                $"Could not start '{request.Executable}': {ex.Message}", ExitCode.GeneralFailure);
        }
    }

    private static ProcessStartInfo BuildStartInfo(ProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.Executable,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
        };

        // ArgumentList escapes per-platform rules for us. Building a single
        // command string by hand is what breaks paths containing spaces, which
        // spec section 84 requires to work on every platform.
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.Environment is not null)
        {
            foreach (var (key, value) in request.Environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        // Withheld before the additions above are applied would be wrong: a
        // caller that removes a prefix and sets one variable under it means the
        // one it set.
        if (request.RemoveEnvironmentPrefixes is { Count: > 0 } prefixes)
        {
            var doomed = startInfo.Environment.Keys
                .Where(key => prefixes.Any(
                    prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    && !(request.Environment?.ContainsKey(key) ?? false))
                .ToList();

            foreach (var key in doomed)
            {
                startInfo.Environment.Remove(key);
            }
        }

        return startInfo;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException
            or System.ComponentModel.Win32Exception)
        {
            // The child exited between the check and the kill, or the platform
            // refused the tree kill. Either way there is nothing to recover.
        }
    }
}
