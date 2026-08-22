using AgentWorkspace.Models.Results;
using AgentWorkspace.Platform.Abstractions;
using AgentWorkspace.Platform.Common;

namespace AgentWorkspace.Platform.Windows;

/// <summary>Terminal hosts available on Windows (spec section 42).</summary>
public sealed class WindowsTerminalProvider : TerminalProviderBase
{
    public WindowsTerminalProvider(IProcessLauncher processes, IExecutableResolver resolver)
        : base(processes, resolver)
    {
    }

    /// <inheritdoc />
    protected override IReadOnlyList<TerminalCandidate> Candidates =>
    [
        new("windows-terminal", "Windows Terminal", "wt"),
        new("pwsh", "PowerShell", "pwsh"),
        new("powershell", "Windows PowerShell", "powershell"),
    ];

    /// <inheritdoc />
    public override async Task<OperationResult> LaunchInNewWindowAsync(
        TerminalDescriptor terminal,
        ProcessRequest request,
        CancellationToken ct = default)
    {
        var arguments = new List<string>();

        if (terminal.Id == "windows-terminal")
        {
            // Windows Terminal sets its own working directory rather than
            // inheriting one, so passing it here is what opens the new tab in
            // the right repository.
            if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            {
                arguments.Add("-d");
                arguments.Add(request.WorkingDirectory);
            }

            arguments.Add(request.Executable);
            arguments.AddRange(request.Arguments);
        }
        else
        {
            arguments.Add("-NoLogo");
            arguments.Add("-Command");
            arguments.Add(request.Executable);
            arguments.AddRange(request.Arguments);
        }

        var result = await Processes.RunAsync(
            new ProcessRequest(terminal.ExecutablePath, arguments, request.WorkingDirectory, request.Environment),
            TimeSpan.FromSeconds(30),
            ct).ConfigureAwait(false);

        if (result.Failed || result.Value is null)
        {
            return OperationResult.Fail(result.Error ?? $"{terminal.DisplayName} could not be started.");
        }

        return result.Value.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail($"{terminal.DisplayName} exited with code {result.Value.ExitCode}.");
    }
}
