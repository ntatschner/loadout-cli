using AgentWorkspace.Core.Context;
using AgentWorkspace.Core.Git;
using AgentWorkspace.Core.Workspace;
using AgentWorkspace.Models;
using AgentWorkspace.Models.Agents;
using AgentWorkspace.Models.Diagnostics;
using AgentWorkspace.Models.Projects;
using AgentWorkspace.Models.Results;
using AgentWorkspace.Platform.Abstractions;

namespace AgentWorkspace.Core.Diagnostics;

/// <summary>Everything preflight needs to judge a launch.</summary>
/// <param name="Project">The resolved project.</param>
/// <param name="Manifest">Its manifest, or null when running without a central workspace.</param>
/// <param name="WorkingDirectory">Where the agent will start.</param>
/// <param name="Agent">What agent detection found.</param>
/// <param name="CompiledContext">The compiled context, or null when none was produced.</param>
/// <param name="SyncOutcome">How the workspace synchronisation went.</param>
/// <param name="Environment">
/// The resolved environment and security profile (spec sections 57 and 58), or
/// null when the project defines none.
/// </param>
public sealed record PreflightContext(
    ProjectResolution Project,
    ProjectManifest? Manifest,
    string WorkingDirectory,
    AgentDescriptor Agent,
    CompiledContext? CompiledContext,
    WorkspaceSyncOutcome SyncOutcome,
    Policies.ResolvedEnvironment? Environment = null);

/// <summary>Preflight findings and the resolved environment for the launch.</summary>
/// <param name="Checks">Everything that was verified, including what passed.</param>
/// <param name="Environment">
/// Variables to pass to the child process, with secret references already
/// resolved to values. Never logged.
/// </param>
public sealed record PreflightResult(
    IReadOnlyList<DiagnosticCheck> Checks,
    IReadOnlyDictionary<string, string> Environment)
{
    /// <summary>True when nothing blocks the launch. Warnings do not block.</summary>
    public bool CanLaunch => !Checks.Any(c => c.Severity == DiagnosticSeverity.Error);

    public IEnumerable<DiagnosticCheck> Blocking =>
        Checks.Where(c => c.Severity == DiagnosticSeverity.Error);

    public IEnumerable<DiagnosticCheck> Warnings =>
        Checks.Where(c => c.Severity == DiagnosticSeverity.Warning);
}

/// <summary>Runs the checks that must pass before an agent starts (spec section 59).</summary>
public interface IPreflightService
{
    Task<OperationResult<PreflightResult>> RunAsync(
        PreflightContext context,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class PreflightService : IPreflightService
{
    private readonly IGitManager _git;
    private readonly ISecretProvider _secrets;

    public PreflightService(IGitManager git, ISecretProvider secrets)
    {
        _git = git;
        _secrets = secrets;
    }

    /// <inheritdoc />
    public async Task<OperationResult<PreflightResult>> RunAsync(
        PreflightContext context,
        CancellationToken ct = default)
    {
        var checks = new List<DiagnosticCheck>();

        CheckRepository(checks, context);
        CheckAgent(checks, context);
        CheckWorkspace(checks, context);
        CheckContext(checks, context);

        var environment = await ResolveEnvironmentAsync(checks, context, ct).ConfigureAwait(false);

        return OperationResult<PreflightResult>.Ok(new PreflightResult(checks, environment));
    }

    private static void CheckRepository(List<DiagnosticCheck> checks, PreflightContext context)
    {
        if (!Directory.Exists(context.WorkingDirectory))
        {
            checks.Add(DiagnosticCheck.Error("Repository", "Working directory",
                $"'{context.WorkingDirectory}' does not exist"));

            return;
        }

        checks.Add(DiagnosticCheck.Ok("Repository", "Working directory", context.WorkingDirectory));
    }

    private static void CheckAgent(List<DiagnosticCheck> checks, PreflightContext context)
    {
        if (!context.Agent.IsInstalled)
        {
            checks.Add(DiagnosticCheck.Error("Agent", context.Agent.DisplayName,
                "not installed, or not on PATH"));

            return;
        }

        checks.Add(DiagnosticCheck.Ok("Agent", context.Agent.DisplayName,
            context.Agent.Version ?? context.Agent.ExecutablePath!));
    }

    private static void CheckWorkspace(List<DiagnosticCheck> checks, PreflightContext context)
    {
        var check = context.SyncOutcome switch
        {
            WorkspaceSyncOutcome.Synced =>
                DiagnosticCheck.Ok("Workspace", "Sync", "up to date"),

            // Offline is an explicitly supported mode (spec section 48), so it
            // warns rather than blocking. The user is told the context may be
            // stale and can decide.
            WorkspaceSyncOutcome.Offline =>
                DiagnosticCheck.Warn("Workspace", "Sync",
                    "running from the cached workspace; context may be out of date"),

            WorkspaceSyncOutcome.Conflict =>
                DiagnosticCheck.Warn("Workspace", "Sync",
                    "local and remote have diverged; resolve with: agentctl workspace sync"),

            _ => DiagnosticCheck.Ok("Workspace", "Sync", "no central workspace configured"),
        };

        checks.Add(check);
    }

    private static void CheckContext(List<DiagnosticCheck> checks, PreflightContext context)
    {
        if (context.CompiledContext is null)
        {
            checks.Add(DiagnosticCheck.Warn("Context", "Compilation",
                "no context was compiled; the agent starts with repository content only"));

            return;
        }

        var compiled = context.CompiledContext;

        checks.Add(DiagnosticCheck.Ok("Context", "Compilation",
            $"{compiled.Sources.Count} source(s), {compiled.TotalBytes / 1024}KB, "
            + $"profile '{compiled.ProfileName ?? ContextCompiler.DefaultProfileName}'"));

        foreach (var missing in compiled.MissingSources)
        {
            // A referenced file that is not there changes what the agent knows.
            // Surfacing it before the session beats discovering it afterwards.
            checks.Add(DiagnosticCheck.Warn("Context", "Missing source", missing));
        }

        if (compiled.Sources.Count == 0)
        {
            checks.Add(DiagnosticCheck.Warn("Context", "Content",
                "every referenced context file was missing or empty"));
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> ResolveEnvironmentAsync(
        List<DiagnosticCheck> checks,
        PreflightContext context,
        CancellationToken ct)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);

        if (context.Manifest is null)
        {
            return environment;
        }

        // The environment's merged bindings when one was selected, otherwise
        // the project's own.
        var bindings = context.Environment?.Environment ?? context.Manifest.Environment;

        if (context.Environment?.Name is not null)
        {
            checks.Add(DiagnosticCheck.Ok("Environment", "Profile",
                $"{context.Environment.Name} using security profile "
                + $"'{context.Environment.ProfileName}'"));
        }

        foreach (var (name, binding) in bindings)
        {
            ct.ThrowIfCancellationRequested();

            if (binding.Value is not null)
            {
                environment[name] = binding.Value;
                checks.Add(DiagnosticCheck.Ok("Environment", name, "literal value"));
                continue;
            }

            if (binding.Secret is null)
            {
                checks.Add(DiagnosticCheck.Warn("Environment", name,
                    "declared with neither a value nor a secret reference"));

                continue;
            }

            var resolved = await _secrets.GetAsync(binding.Secret, ct).ConfigureAwait(false);

            if (resolved.Succeeded)
            {
                environment[name] = resolved.Value!;

                // The reference is named, never the value. This check ends up
                // in logs and in the doctor report.
                checks.Add(DiagnosticCheck.Ok("Environment", name,
                    $"resolved from {binding.Secret}"));

                continue;
            }

            checks.Add(binding.Required
                ? DiagnosticCheck.Error("Environment", name,
                    $"required secret '{binding.Secret}' could not be resolved: {resolved.Error}")
                : DiagnosticCheck.Warn("Environment", name,
                    $"optional secret '{binding.Secret}' is not set"));
        }

        return environment;
    }
}
