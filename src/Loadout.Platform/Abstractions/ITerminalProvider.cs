using Loadout.Models.Results;

namespace Loadout.Platform.Abstractions;

/// <summary>A terminal emulator found on this machine.</summary>
/// <param name="Id">Stable lowercase key used in configuration, e.g. "iterm2".</param>
/// <param name="DisplayName">Human-facing name.</param>
/// <param name="ExecutablePath">Path or bundle identifier used to launch it.</param>
public sealed record TerminalDescriptor(string Id, string DisplayName, string ExecutablePath);

/// <summary>
/// Discovers and spawns terminal emulators (spec section 42).
/// <para>
/// No emulator is ever a hard dependency. When the launcher is already running
/// in a terminal the correct behaviour is to reuse it, and every platform must
/// remain fully usable with no emulator installed at all, which is what keeps
/// headless Linux servers working (spec section 86).
/// </para>
/// </summary>
public interface ITerminalProvider
{
    /// <summary>Emulators detected here, best candidate first. May legitimately be empty.</summary>
    IReadOnlyList<TerminalDescriptor> DetectAvailable();

    /// <summary>True when the launcher is attached to an interactive terminal already.</summary>
    bool IsRunningInTerminal { get; }

    /// <summary>
    /// Opens a command in a new terminal window. Fails with a stated reason
    /// when no emulator is available rather than pretending to succeed.
    /// </summary>
    Task<OperationResult> LaunchInNewWindowAsync(
        TerminalDescriptor terminal,
        ProcessRequest request,
        CancellationToken ct = default);
}
