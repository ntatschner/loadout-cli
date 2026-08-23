using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Tests.Fakes;

/// <summary>An in-memory secret store, so tests never touch a real keystore.</summary>
public sealed class FakeSecretProvider : ISecretProvider
{
    private readonly Dictionary<string, string> _secrets;

    public FakeSecretProvider(Dictionary<string, string>? secrets = null) =>
        _secrets = secrets ?? [];

    public string Name => "fake";

    /// <summary>Set to make the provider report itself unavailable.</summary>
    public string? UnavailableReason { get; init; }

    public Task<OperationResult> IsAvailableAsync(CancellationToken ct = default) =>
        Task.FromResult(UnavailableReason is null
            ? OperationResult.Ok()
            : OperationResult.Fail(UnavailableReason));

    public Task<OperationResult<string>> GetAsync(string reference, CancellationToken ct = default) =>
        Task.FromResult(_secrets.TryGetValue(reference, out var value)
            ? OperationResult<string>.Ok(value)
            : OperationResult<string>.Fail(
                $"No stored secret for '{reference}'.", ExitCode.AuthenticationRequired));

    public Task<OperationResult> SetAsync(string reference, string value, CancellationToken ct = default)
    {
        _secrets[reference] = value;
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> RemoveAsync(string reference, CancellationToken ct = default)
    {
        _secrets.Remove(reference);
        return Task.FromResult(OperationResult.Ok());
    }

    public async Task<OperationResult> TestAsync(string reference, CancellationToken ct = default)
    {
        var result = await GetAsync(reference, ct).ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error!, ExitCode.AuthenticationRequired);
    }
}
