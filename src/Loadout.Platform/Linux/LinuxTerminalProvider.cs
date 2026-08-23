using Loadout.Models.Results;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;

namespace Loadout.Platform.Linux;

/// <summary>
/// Terminal emulators commonly found on Linux (spec section 42). Ordered by
/// how likely a developer is to prefer it rather than alphabetically, and none
/// of them is required.
/// </summary>
public sealed class LinuxTerminalProvider : TerminalProviderBase
{
    public LinuxTerminalProvider(IProcessLauncher processes, IExecutableResolver resolver)
        : base(processes, resolver)
    {
    }

    /// <inheritdoc />
    protected override IReadOnlyList<TerminalCandidate> Candidates =>
    [
        new("ghostty", "Ghostty", "ghostty"),
        new("wezterm", "WezTerm", "wezterm"),
        new("kitty", "kitty", "kitty"),
        new("alacritty", "Alacritty", "alacritty"),
        new("gnome-terminal", "GNOME Terminal", "gnome-terminal"),
        new("konsole", "Konsole", "konsole"),
        new("xfce4-terminal", "Xfce Terminal", "xfce4-terminal"),
        new("xterm", "xterm", "xterm"),
    ];

    /// <inheritdoc />
    public override async Task<OperationResult> LaunchInNewWindowAsync(
        TerminalDescriptor terminal,
        ProcessRequest request,
        CancellationToken ct = default)
    {
        // A trailing -e followed by the command is the one invocation form all
        // of these emulators genuinely agree on.
        var arguments = new List<string> { "-e", request.Executable };
        arguments.AddRange(request.Arguments);

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
            : OperationResult.Fail(
                $"{terminal.DisplayName} exited with code {result.Value.ExitCode}: "
                + result.Value.StandardError.Trim());
    }
}
