using Loadout.Models.Results;

namespace Loadout.Core.Context;

/// <summary>One stored handoff document.</summary>
/// <param name="Name">File name without its extension, used to address it on the command line.</param>
/// <param name="Path">Absolute path to the file in the workspace clone.</param>
/// <param name="WrittenUtc">Last write time, which orders the list.</param>
public sealed record HandoffDocument(string Name, string Path, DateTimeOffset WrittenUtc);

/// <summary>
/// Manages cross-agent handoffs (spec section 69).
/// <para>
/// A handoff is deliberately plain Markdown in the workspace repository rather
/// than an exported session. Agent session formats are proprietary and change,
/// and spec section 99 rules out trying to unify them; a document that a human
/// can read and edit is the one artefact that survives Claude today and Codex
/// tomorrow.
/// </para>
/// </summary>
public interface IHandoffService
{
    /// <summary>Handoffs for a project, most recent first.</summary>
    Task<OperationResult<IReadOnlyList<HandoffDocument>>> ListAsync(
        string slug,
        CancellationToken ct = default);

    /// <summary>The most recent handoff, or null when the project has none.</summary>
    Task<OperationResult<HandoffDocument?>> GetLatestAsync(string slug, CancellationToken ct = default);

    /// <summary>Reads a handoff's contents.</summary>
    Task<OperationResult<string>> ReadAsync(string slug, string? name = null, CancellationToken ct = default);

    /// <summary>
    /// Creates a handoff from the standard template, ready for a person or an
    /// agent to fill in.
    /// </summary>
    Task<OperationResult<HandoffDocument>> CreateAsync(
        string slug,
        string? name = null,
        CancellationToken ct = default);
}
