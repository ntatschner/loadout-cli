using Loadout.Models.Agents;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents.Generic;

/// <summary>
/// Runs an agent described entirely in configuration (spec section 88).
/// <para>
/// This is what keeps the launcher from needing a code change for every new
/// tool. It has no built-in knowledge of any agent: the executable, arguments
/// and environment all come from the definition, and placeholders are expanded
/// from the launch context.
/// </para>
/// </summary>
public sealed class GenericAgentAdapter : AgentAdapterBase
{
    private readonly GenericAgentDefinition _definition;

    public GenericAgentAdapter(
        string name,
        GenericAgentDefinition definition,
        IExecutableResolver resolver,
        IProcessLauncher processes,
        IReadOnlyList<string> configuredSearchPaths)
        : base(resolver, processes, configuredSearchPaths)
    {
        Name = name;
        _definition = definition;
    }

    /// <inheritdoc />
    public override string Name { get; }

    /// <inheritdoc />
    public override string DisplayName => _definition.DisplayName ?? Name;

    /// <inheritdoc />
    protected override string ExecutableName => _definition.Executable;

    /// <inheritdoc />
    /// <remarks>
    /// Empty because a user-defined agent has no help text the launcher knows
    /// how to read. Claiming capabilities it cannot verify would be worse than
    /// claiming none.
    /// </remarks>
    protected override IReadOnlyDictionary<string, string[]> CapabilityMarkers =>
        new Dictionary<string, string[]>();

    /// <inheritdoc />
    public override async Task<OperationResult<AgentInvocation>> BuildInvocationAsync(
        AgentLaunchContext context,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_definition.Executable))
        {
            return OperationResult<AgentInvocation>.Fail(
                $"Agent '{Name}' has no executable configured.",
                Models.ExitCode.ConfigurationInvalid);
        }

        var descriptor = await DetectAsync(ct).ConfigureAwait(false);

        if (!descriptor.IsInstalled || descriptor.ExecutablePath is null)
        {
            return OperationResult<AgentInvocation>.Fail(
                $"'{_definition.Executable}' was not found for agent '{Name}'.",
                Models.ExitCode.AgentUnavailable);
        }

        var placeholders = BuildPlaceholders(context);

        var arguments = _definition.Arguments.Select(a => Expand(a, placeholders)).ToList();
        arguments.AddRange(context.PassthroughArguments);

        // Preflight-resolved variables go in first so a definition can still
        // override one deliberately.
        var environment = context.ResolvedEnvironment is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(context.ResolvedEnvironment);

        foreach (var (key, value) in _definition.Environment)
        {
            environment[key] = Expand(value, placeholders);
        }

        return OperationResult<AgentInvocation>.Ok(
            new AgentInvocation(descriptor.ExecutablePath, arguments, environment));
    }

    private static Dictionary<string, string> BuildPlaceholders(AgentLaunchContext context) => new()
    {
        ["REPOSITORY_PATH"] = context.WorkingDirectory,
        ["WORKSPACE_PATH"] = context.WorkspacePath ?? string.Empty,
        ["RUNTIME_DIRECTORY"] = context.RuntimeDirectory,
        // Empty rather than a path to a file that was never written, so a
        // definition can test for it instead of handing the agent a dead path.
        ["COMPILED_CONTEXT_FILE"] = context.CompiledContext?.FilePath ?? string.Empty,
        ["PROJECT_SLUG"] = context.Project.Entry.Slug,
        ["PROJECT_NAME"] = context.Project.Entry.Name,
    };

    /// <summary>
    /// Replaces placeholders of the form <c>${NAME}</c>. Unknown placeholders
    /// are left alone rather than blanked, so a typo is visible in the failing
    /// command instead of silently becoming an empty argument.
    /// </summary>
    internal static string Expand(string value, IReadOnlyDictionary<string, string> placeholders)
    {
        if (!value.Contains("${", StringComparison.Ordinal))
        {
            return value;
        }

        var result = value;

        foreach (var (key, replacement) in placeholders)
        {
            result = result.Replace("${" + key + "}", replacement, StringComparison.Ordinal);
        }

        return result;
    }
}
