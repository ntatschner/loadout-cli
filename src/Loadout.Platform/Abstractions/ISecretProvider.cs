using Loadout.Models.Results;

namespace Loadout.Platform.Abstractions;

/// <summary>
/// Resolves secret references such as "anthropic/default" against a real
/// credential store (spec sections 53 to 55).
/// <para>
/// Secret values never enter either Git repository, a log, a handoff or a
/// compiled context file (spec section 52). Configuration holds the reference;
/// this interface is the only thing that ever sees the value.
/// </para>
/// </summary>
public interface ISecretProvider
{
    /// <summary>Provider name for diagnostics, e.g. "keychain" or "credential-manager".</summary>
    string Name { get; }

    /// <summary>Whether this provider can be used on this machine right now.</summary>
    Task<OperationResult> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>Reads a secret. Returns a failure rather than throwing when the reference is absent.</summary>
    Task<OperationResult<string>> GetAsync(string reference, CancellationToken ct = default);

    /// <summary>Creates or replaces a secret.</summary>
    Task<OperationResult> SetAsync(string reference, string value, CancellationToken ct = default);

    /// <summary>Deletes a secret.</summary>
    Task<OperationResult> RemoveAsync(string reference, CancellationToken ct = default);

    /// <summary>
    /// Checks that a reference resolves without returning or logging the value
    /// (spec section 55). Backs the "loadout secret test" command.
    /// </summary>
    Task<OperationResult> TestAsync(string reference, CancellationToken ct = default);
}
