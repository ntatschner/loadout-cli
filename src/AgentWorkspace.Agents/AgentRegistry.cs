using AgentWorkspace.Agents.Claude;
using AgentWorkspace.Agents.Codex;
using AgentWorkspace.Agents.Generic;
using AgentWorkspace.Models;
using AgentWorkspace.Models.Agents;
using AgentWorkspace.Models.Configuration;
using AgentWorkspace.Models.Results;
using AgentWorkspace.Platform.Abstractions;

namespace AgentWorkspace.Agents;

/// <summary>Holds the adapters available in this process.</summary>
public interface IAgentRegistry
{
    /// <summary>Built-in adapters plus any configured generic ones.</summary>
    IReadOnlyList<IAgentAdapter> Adapters { get; }

    /// <summary>Finds an adapter by name. Names are matched case-insensitively.</summary>
    OperationResult<IAgentAdapter> Resolve(string name);

    /// <summary>Detects every adapter, for the doctor and status reports.</summary>
    Task<IReadOnlyList<AgentDescriptor>> DetectAllAsync(CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class AgentRegistry : IAgentRegistry
{
    public AgentRegistry(
        IExecutableResolver resolver,
        IProcessLauncher processes,
        LauncherConfig config)
    {
        var searchPaths = config.AgentSearchPaths;

        var adapters = new List<IAgentAdapter>
        {
            new ClaudeAdapter(resolver, processes, searchPaths),
            new CodexAdapter(resolver, processes, searchPaths),
        };

        // Configured agents are added after the built-ins but can override one
        // by name, so a user who needs a different invocation for Claude can
        // supply it without waiting for a launcher release.
        foreach (var (name, definition) in config.CustomAgents)
        {
            adapters.RemoveAll(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            adapters.Add(new GenericAgentAdapter(name, definition, resolver, processes, searchPaths));
        }

        Adapters = adapters;
    }

    /// <inheritdoc />
    public IReadOnlyList<IAgentAdapter> Adapters { get; }

    /// <inheritdoc />
    public OperationResult<IAgentAdapter> Resolve(string name)
    {
        var match = Adapters.FirstOrDefault(
            a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            return OperationResult<IAgentAdapter>.Ok(match);
        }

        var known = string.Join(", ", Adapters.Select(a => a.Name));

        return OperationResult<IAgentAdapter>.Fail(
            $"No agent named '{name}'. Available agents: {known}.",
            ExitCode.AgentUnavailable);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentDescriptor>> DetectAllAsync(CancellationToken ct = default)
    {
        // Probed concurrently: each detection runs the agent binary twice, and
        // doing them in sequence makes doctor and the TUI's first paint feel
        // sluggish for no reason.
        var detections = Adapters.Select(a => a.DetectAsync(ct));

        return await Task.WhenAll(detections).ConfigureAwait(false);
    }
}
