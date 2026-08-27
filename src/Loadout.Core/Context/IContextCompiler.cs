using Loadout.Models.Projects;
using Loadout.Models.Results;

namespace Loadout.Core.Context;

/// <summary>Where one piece of compiled context came from.</summary>
/// <param name="WorkspaceRelativePath">Path within the workspace, for diagnostics.</param>
/// <param name="Heading">Section heading written above the content.</param>
/// <param name="Bytes">Size of the content that was included.</param>
public sealed record ContextSource(string WorkspaceRelativePath, string Heading, long Bytes);

/// <summary>The result of compiling a project's context for one launch.</summary>
/// <param name="FilePath">Absolute path to the compiled file in the runtime directory.</param>
/// <param name="Sources">Files that contributed, in the order they appear.</param>
/// <param name="MissingSources">
/// Files the manifest referenced that do not exist. Reported rather than
/// ignored: a context file that silently vanished changes what the agent knows,
/// and the user should hear about it before the session rather than after.
/// </param>
/// <param name="ProfileName">Profile that was applied, or null for the base context.</param>
/// <param name="Instructions">
/// The specialists that were resolved for this launch and why, or null when the
/// specialist layer was not in play. Carried on the result so that what an agent
/// was given can be reported without resolving a second time and risking a
/// different answer.
/// </param>
public sealed record CompiledContext(
    string FilePath,
    IReadOnlyList<ContextSource> Sources,
    IReadOnlyList<string> MissingSources,
    string? ProfileName,
    Models.Instructions.EffectiveInstructions? Instructions = null)
{
    public long TotalBytes => Sources.Sum(s => s.Bytes);
}

/// <summary>
/// Assembles a project's instructions into a single file an agent can be
/// pointed at (spec section 33).
/// <para>
/// This is the component that makes project knowledge agent-independent. The
/// same global policies, architecture notes and conventions feed Claude, Codex
/// and any future adapter; only the final delivery mechanism differs, and that
/// belongs to the adapter rather than here.
/// </para>
/// </summary>
public interface IContextCompiler
{
    /// <summary>
    /// Compiles context into the launch's runtime directory (spec section 82),
    /// with owner-only permissions.
    /// </summary>
    /// <param name="manifest">The project definition supplying the file lists.</param>
    /// <param name="workspacePath">Local path to the central workspace clone.</param>
    /// <param name="runtimeDirectory">Where the compiled file is written.</param>
    /// <param name="agentName">Agent being launched, used to filter profiles.</param>
    /// <param name="profileName">Profile to apply, or null for the base context.</param>
    /// <param name="handoffPath">Optional handoff to append (spec section 69).</param>
    /// <param name="instructions">
    /// Specialists resolved for this task, or null when the specialist layer is
    /// not in play. Resolved by the caller rather than here: what is relevant
    /// depends on the task and the agent, and the compiler's job is to assemble
    /// what it is given in the right order.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<OperationResult<CompiledContext>> CompileAsync(
        ProjectManifest manifest,
        string workspacePath,
        string runtimeDirectory,
        string agentName,
        string? profileName = null,
        string? handoffPath = null,
        Models.Instructions.EffectiveInstructions? instructions = null,
        CancellationToken ct = default);

    /// <summary>
    /// Profiles available for an agent (spec section 34), always including the
    /// implicit base profile.
    /// </summary>
    IReadOnlyList<string> ListProfiles(ProjectManifest manifest, string agentName);
}
