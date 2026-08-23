using Loadout.Models.Agents;
using Loadout.Models.Diagnostics;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents;

/// <summary>
/// Shared adapter plumbing: locating the binary, reading its version, and
/// probing its help output for supported options.
/// <para>
/// Capability detection reads the agent's own help text rather than comparing
/// version numbers (spec sections 66 and 67). Version strings lie — a nightly
/// build, a fork, or a distribution patch can all carry a version that implies
/// support the binary does not have — whereas an option that appears in help
/// is an option the binary actually accepts.
/// </para>
/// </summary>
public abstract class AgentAdapterBase : IAgentAdapter
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);

    protected AgentAdapterBase(
        IExecutableResolver resolver,
        IProcessLauncher processes,
        IReadOnlyList<string> configuredSearchPaths)
    {
        Resolver = resolver;
        Processes = processes;
        ConfiguredSearchPaths = configuredSearchPaths;
    }

    protected IExecutableResolver Resolver { get; }

    protected IProcessLauncher Processes { get; }

    protected IReadOnlyList<string> ConfiguredSearchPaths { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string DisplayName { get; }

    /// <summary>Executable name to look for on PATH.</summary>
    protected abstract string ExecutableName { get; }

    /// <summary>
    /// Help-text markers that indicate each capability. The key is a capability
    /// name from <see cref="AgentCapabilities"/>; the value is the set of
    /// strings whose presence in help output proves it.
    /// </summary>
    protected abstract IReadOnlyDictionary<string, string[]> CapabilityMarkers { get; }

    /// <inheritdoc />
    public virtual async Task<AgentDescriptor> DetectAsync(CancellationToken ct = default)
    {
        var executable = Resolver.Resolve(ExecutableName, ConfiguredSearchPaths);

        if (executable is null)
        {
            return AgentDescriptor.NotInstalled(Name, DisplayName);
        }

        var version = await ReadVersionAsync(executable, ct).ConfigureAwait(false);
        var capabilities = await ProbeCapabilitiesAsync(executable, ct).ConfigureAwait(false);

        return new AgentDescriptor(Name, DisplayName, true, executable, version, capabilities);
    }

    /// <inheritdoc />
    public virtual async Task<OperationResult> ValidateAsync(
        AgentLaunchContext context,
        CancellationToken ct = default)
    {
        var descriptor = await DetectAsync(ct).ConfigureAwait(false);

        if (!descriptor.IsInstalled)
        {
            return OperationResult.Fail(
                $"{DisplayName} is not installed, or its executable is not on PATH.",
                Models.ExitCode.AgentUnavailable);
        }

        if (!Directory.Exists(context.WorkingDirectory))
        {
            return OperationResult.Fail(
                $"The working directory '{context.WorkingDirectory}' does not exist.",
                Models.ExitCode.RepositoryUnavailable);
        }

        return OperationResult.Ok();
    }

    /// <inheritdoc />
    public abstract Task<OperationResult<AgentInvocation>> BuildInvocationAsync(
        AgentLaunchContext context,
        CancellationToken ct = default);

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<DiagnosticCheck>> RunDiagnosticsAsync(
        CancellationToken ct = default)
    {
        var descriptor = await DetectAsync(ct).ConfigureAwait(false);

        if (!descriptor.IsInstalled)
        {
            // A missing agent is a warning rather than an error: a machine that
            // only ever runs Codex is perfectly healthy without Claude.
            return
            [
                DiagnosticCheck.Warn("Agents", DisplayName,
                    $"not found (looked for '{ExecutableName}' on PATH and the configured search paths)"),
            ];
        }

        var detail = descriptor.Version is null
            ? descriptor.ExecutablePath!
            : $"{descriptor.Version} at {descriptor.ExecutablePath}";

        var checks = new List<DiagnosticCheck>
        {
            DiagnosticCheck.Ok("Agents", DisplayName, detail),
        };

        var missing = CapabilityMarkers.Keys
            .Where(capability => !descriptor.Supports(capability))
            .ToList();

        if (missing.Count > 0)
        {
            // Surfaced rather than swallowed: a missing capability changes what
            // the launcher can do with this agent, and section 5 requires such
            // gaps to be visible.
            checks.Add(DiagnosticCheck.Warn(
                "Agents",
                $"{DisplayName} capabilities",
                "not detected: " + string.Join(", ", missing)));
        }

        return checks;
    }

    /// <summary>Reads the agent's version string, returning null when it cannot be determined.</summary>
    protected virtual async Task<string?> ReadVersionAsync(string executable, CancellationToken ct)
    {
        var result = await Processes.RunAsync(
            new ProcessRequest(executable, ["--version"]),
            ProbeTimeout,
            ct).ConfigureAwait(false);

        if (result.Failed || result.Value is null || !result.Value.Succeeded)
        {
            return null;
        }

        return result.Value.StandardOutput.Trim().Split('\n').FirstOrDefault()?.Trim();
    }

    /// <summary>Runs the agent's help command and looks for the markers of each capability.</summary>
    protected virtual async Task<IReadOnlyDictionary<string, bool>> ProbeCapabilitiesAsync(
        string executable,
        CancellationToken ct)
    {
        var capabilities = new Dictionary<string, bool>();

        var result = await Processes.RunAsync(
            new ProcessRequest(executable, ["--help"]),
            ProbeTimeout,
            ct).ConfigureAwait(false);

        if (result.Failed || result.Value is null)
        {
            // Nothing could be probed, so nothing is claimed. Reporting every
            // capability as absent is the safe direction: the launcher will
            // decline to use an option rather than pass one that is rejected.
            foreach (var capability in CapabilityMarkers.Keys)
            {
                capabilities[capability] = false;
            }

            return capabilities;
        }

        // Some CLIs print help to stderr, so both streams are searched.
        var help = result.Value.StandardOutput + "\n" + result.Value.StandardError;

        foreach (var (capability, markers) in CapabilityMarkers)
        {
            capabilities[capability] = markers.Any(
                marker => help.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        return capabilities;
    }
}
