using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Common;

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
    /// <summary>Prefix for the variables this provider reads.</summary>
    private const string VariablePrefix = "LOADOUT_SECRET_";

    /// <summary>
    /// What the prefix was before the tool was renamed. Still read, because a
    /// variable set in somebody's shell profile or CI configuration does not
    /// rename itself, and silently failing to find a secret they had already
    /// provided would look like the secret store was broken.
    /// </summary>
    private const string LegacyVariablePrefix = "AGENTCTL_SECRET_";

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

        // The old prefix is still read. A variable set in somebody's shell
        // profile or CI configuration does not rename itself when the tool
        // does, and failing to find a secret they had already provided would
        // look like the secret store was broken rather than renamed.
        var value = _environment.GetVariable(name)
            ?? _environment.GetVariable(LegacyVariablePrefix + name[VariablePrefix.Length..]);

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
    /// LOADOUT_SECRET_ANTHROPIC_DEFAULT. The prefix keeps the launcher from
    /// colliding with unrelated variables that happen to share a name.
    /// </summary>
    internal static string ToVariableName(string reference)
    {
        var sanitised = reference
            .Replace('/', '_')
            .Replace('-', '_')
            .Replace('.', '_')
            .ToUpperInvariant();

        return VariablePrefix + sanitised;
    }
}
