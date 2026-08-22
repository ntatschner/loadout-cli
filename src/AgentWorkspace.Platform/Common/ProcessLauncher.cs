using System.Diagnostics;
using System.Text;
using AgentWorkspace.Models;
using AgentWorkspace.Models.Results;
using AgentWorkspace.Platform.Abstractions;

namespace AgentWorkspace.Platform.Common;

/// <summary>
/// Starts child processes using the .NET process APIs, which map onto native
/// process creation on all three platforms. Shared rather than per-platform
/// because the semantics genuinely are the same; only the PTY case differs,
/// and that lives behind IPseudoTerminal.
/// </summary>
public sealed class ProcessLauncher : IProcessLauncher
{
    /// <inheritdoc />
    public async Task<OperationResult<ProcessOutcome>> RunAsync(
        ProcessRequest request,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var startInfo = BuildStartInfo(request);
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.RedirectStandardInput = request.StandardInput is not null;
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
            // Written and closed immediately: credential tools such as
            // secret-tool and security read until EOF, so leaving the pipe open
            // would hang the launcher.
            await process.StandardInput.WriteAsync(request.StandardInput).ConfigureAwait(false);
            process.StandardInput.Close();
        }

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
