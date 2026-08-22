using AgentWorkspace.Models.Results;

namespace AgentWorkspace.Platform.Abstractions;

/// <summary>
/// Writes to the system clipboard (spec section 74), backing the --clipboard
/// flag on handoff and context commands.
/// <para>
/// Optional by design. A headless Linux box has no clipboard, and that must be
/// reported as an unsupported capability rather than treated as an error.
/// </para>
/// </summary>
public interface IClipboardProvider
{
    Task<OperationResult> SetTextAsync(string text, CancellationToken ct = default);
}
