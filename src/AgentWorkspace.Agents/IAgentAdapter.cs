using AgentWorkspace.Core.Context;
using AgentWorkspace.Models.Agents;
using AgentWorkspace.Models.Diagnostics;
using AgentWorkspace.Models.Projects;
using AgentWorkspace.Models.Results;

namespace AgentWorkspace.Agents;

/// <summary>Everything an adapter needs in order to build a launch.</summary>
/// <param name="Project">The project being launched.</param>
/// <param name="WorkingDirectory">Where the agent process should start, normally the application repository.</param>
/// <param name="RuntimeDirectory">Per-launch scratch directory for generated files (spec section 82).</param>
/// <param name="WorkspacePath">Local path to the central workspace clone, or null when running without one.</param>
/// <param name="PassthroughArguments">
/// Arguments the user supplied after a bare <c>--</c>. Spec section 36 forbids
/// the launcher from parsing or altering these, so they are carried opaquely
/// and appended last.
/// </param>
/// <param name="Manifest">
/// The project manifest, or null when running without a central workspace.
/// </param>
/// <param name="CompiledContext">
/// Context assembled by the compiler for this launch (spec section 33), or null
/// when there was nothing to compile. How it reaches the agent is the adapter's
/// business; producing it is not.
/// </param>
/// <param name="ResolvedEnvironment">
/// Environment variables from preflight, with secret references already
/// resolved. Passed to the child process only, and never logged.
/// </param>
/// <param name="Security">
/// The security posture to translate into whatever this agent supports
/// (spec section 58), or null for the agent's own defaults.
/// </param>
public sealed record AgentLaunchContext(
    ProjectResolution Project,
    string WorkingDirectory,
    string RuntimeDirectory,
    string? WorkspacePath,
    IReadOnlyList<string> PassthroughArguments,
    ProjectManifest? Manifest = null,
    CompiledContext? CompiledContext = null,
    IReadOnlyDictionary<string, string>? ResolvedEnvironment = null,
    Models.Policies.SecurityProfile? Security = null);

/// <summary>A fully resolved launch, ready to be handed to the process layer.</summary>
/// <param name="Executable">Absolute path to the agent binary.</param>
/// <param name="Arguments">Complete argument list, passthrough arguments last.</param>
/// <param name="Environment">Variables set for the child process only.</param>
/// <param name="Warnings">
/// Non-fatal problems found while building the invocation, such as a context
/// that could not be attached. Shown to the user rather than swallowed: an
/// agent silently starting without its context looks like it worked.
/// </param>
public sealed record AgentInvocation(
    string Executable,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<string>? Warnings = null);

/// <summary>
/// Adapts one coding agent to the launcher (spec section 30).
/// <para>
/// All agent-specific knowledge lives behind this interface. Core never names
/// an agent, a flag or an environment variable, which is what lets a new agent
/// be added without touching the launcher itself.
/// </para>
/// <para>
/// Milestone 1 implements detection, capability probing, validation and
/// invocation building. Context compilation, session resume and security
/// profile translation are milestone 2 and are deliberately absent rather than
/// stubbed, so nothing appears to work when it does not.
/// </para>
/// </summary>
public interface IAgentAdapter
{
    /// <summary>Lowercase adapter name used in configuration and on the command line.</summary>
    string Name { get; }

    /// <summary>Human-facing name, e.g. "Claude Code".</summary>
    string DisplayName { get; }

    /// <summary>
    /// Locates the agent and probes what it can do (spec sections 65 and 66).
    /// Returns a descriptor with IsInstalled false rather than failing when the
    /// agent is simply not installed, which is an ordinary state.
    /// </summary>
    Task<AgentDescriptor> DetectAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks that this adapter can launch the given project right now, as part
    /// of preflight (spec section 59).
    /// </summary>
    Task<OperationResult> ValidateAsync(AgentLaunchContext context, CancellationToken ct = default);

    /// <summary>Builds the executable, arguments and environment for a launch.</summary>
    Task<OperationResult<AgentInvocation>> BuildInvocationAsync(
        AgentLaunchContext context,
        CancellationToken ct = default);

    /// <summary>Adapter-specific diagnostic checks for the doctor report.</summary>
    Task<IReadOnlyList<DiagnosticCheck>> RunDiagnosticsAsync(CancellationToken ct = default);
}
