using AgentWorkspace.Models;
using AgentWorkspace.Models.Results;
using AgentWorkspace.Platform.Abstractions;

namespace AgentWorkspace.Platform.Common;

/// <summary>
/// Resolves secret references from environment variables (spec section 54).
/// <para>
/// This is the provider that keeps the launcher usable in CI, in containers
/// and on headless machines with no keystore, which is what spec section 86
/// requires. It is read-only by nature: a process cannot durably set an
/// environment variable for its parent, so writes are refused with an
/// explanation rather than appearing to succeed and silently losing the value.
/// </para>
/// </summary>
public sealed class EnvironmentSecretProvider : ISecretProvider
{
    private readonly IEnvironmentProvider _environment;

    public EnvironmentSecretProvider(IEnvironmentProvider environment) => _environment = environment;

    /// <inheritdoc />
    public string Name => "environment";

    /// <inheritdoc />
    public Task<OperationResult> IsAvailableAsync(CancellationToken ct = default) =>
        Task.FromResult(OperationResult.Ok());

    /// <inheritdoc />
    public Task<OperationResult<string>> GetAsync(string reference, CancellationToken ct = default)
    {
        var name = ToVariableName(reference);
        var value = _environment.GetVariable(name);

        return Task.FromResult(value is null
            ? OperationResult<string>.Fail(
                $"Environment variable '{name}' is not set, so '{reference}' cannot be resolved.",
                ExitCode.AuthenticationRequired)
            : OperationResult<string>.Ok(value));
    }

    /// <inheritdoc />
    public Task<OperationResult> SetAsync(string reference, string value, CancellationToken ct = default) =>
        Task.FromResult(OperationResult.Fail(
            $"The environment provider cannot store secrets. Set '{ToVariableName(reference)}' in your "
            + "environment, or configure a provider that supports writing."));

    /// <inheritdoc />
    public Task<OperationResult> RemoveAsync(string reference, CancellationToken ct = default) =>
        Task.FromResult(OperationResult.Fail(
            $"The environment provider cannot remove secrets. Unset '{ToVariableName(reference)}' yourself."));

    /// <inheritdoc />
    public async Task<OperationResult> TestAsync(string reference, CancellationToken ct = default)
    {
        var result = await GetAsync(reference, ct).ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error ?? "Unresolved.", ExitCode.AuthenticationRequired);
    }

    /// <summary>
    /// Maps a reference such as anthropic/default onto the variable name
    /// AGENTCTL_SECRET_ANTHROPIC_DEFAULT. The prefix keeps the launcher from
    /// colliding with unrelated variables that happen to share a name.
    /// </summary>
    internal static string ToVariableName(string reference)
    {
        var sanitised = reference
            .Replace('/', '_')
            .Replace('-', '_')
            .Replace('.', '_')
            .ToUpperInvariant();

        return "AGENTCTL_SECRET_" + sanitised;
    }
}
