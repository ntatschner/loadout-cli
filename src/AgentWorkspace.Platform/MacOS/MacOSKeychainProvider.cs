using System.Runtime.Versioning;
using AgentWorkspace.Models;
using AgentWorkspace.Models.Results;
using AgentWorkspace.Platform.Abstractions;

namespace AgentWorkspace.Platform.MacOS;

/// <summary>
/// Stores secrets in the login Keychain, the native macOS provider
/// (spec section 54).
/// <para>
/// Driven through the platform's own /usr/bin/security tool rather than
/// linking Security.framework. That keeps the launcher free of native interop
/// on a platform where the tool is guaranteed present, and it inherits the
/// system's ACL and unlock prompting behaviour rather than reimplementing it.
/// </para>
/// <para>
/// Secret values are passed on stdin, never in argv, so they cannot be read
/// out of a process listing.
/// </para>
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacOSKeychainProvider : ISecretProvider
{
    private const string SecurityTool = "/usr/bin/security";
    private const string ServiceName = "AgentWorkspaceLauncher";

    private readonly IProcessLauncher _processes;

    public MacOSKeychainProvider(IProcessLauncher processes) => _processes = processes;

    /// <inheritdoc />
    public string Name => "keychain";

    /// <inheritdoc />
    public async Task<OperationResult> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!File.Exists(SecurityTool))
        {
            return OperationResult.Fail($"The macOS security tool was not found at {SecurityTool}.");
        }

        var result = await _processes.RunAsync(
            new ProcessRequest(SecurityTool, ["help"]),
            TimeSpan.FromSeconds(10),
            ct).ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error ?? "The macOS security tool could not be run.");
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> GetAsync(string reference, CancellationToken ct = default)
    {
        var result = await _processes.RunAsync(
            new ProcessRequest(SecurityTool,
                ["find-generic-password", "-s", ServiceName, "-a", reference, "-w"]),
            TimeSpan.FromSeconds(30),
            ct).ConfigureAwait(false);

        if (result.Failed || result.Value is null)
        {
            return OperationResult<string>.Fail(
                result.Error ?? "The Keychain could not be queried.",
                ExitCode.AuthenticationRequired);
        }

        if (!result.Value.Succeeded)
        {
            return OperationResult<string>.Fail(
                $"No Keychain item for '{reference}'.",
                ExitCode.AuthenticationRequired);
        }

        // -w prints the value alone, with a trailing newline the tool adds.
        return OperationResult<string>.Ok(result.Value.StandardOutput.TrimEnd('\r', '\n'));
    }

    /// <inheritdoc />
    public async Task<OperationResult> SetAsync(string reference, string value, CancellationToken ct = default)
    {
        // -U updates an existing item instead of failing; -w with no inline
        // value makes the tool read the secret from stdin.
        var result = await _processes.RunAsync(
            new ProcessRequest(
                SecurityTool,
                ["add-generic-password", "-s", ServiceName, "-a", reference, "-U", "-w"],
                StandardInput: value + "\n"),
            TimeSpan.FromSeconds(30),
            ct).ConfigureAwait(false);

        if (result.Failed || result.Value is null)
        {
            return OperationResult.Fail(result.Error ?? "The Keychain could not be written to.");
        }

        return result.Value.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail($"Could not store '{reference}' in the Keychain: {Summarise(result.Value)}");
    }

    /// <inheritdoc />
    public async Task<OperationResult> RemoveAsync(string reference, CancellationToken ct = default)
    {
        var result = await _processes.RunAsync(
            new ProcessRequest(SecurityTool,
                ["delete-generic-password", "-s", ServiceName, "-a", reference]),
            TimeSpan.FromSeconds(30),
            ct).ConfigureAwait(false);

        if (result.Failed || result.Value is null)
        {
            return OperationResult.Fail(result.Error ?? "The Keychain could not be written to.");
        }

        return result.Value.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail($"No Keychain item for '{reference}'.");
    }

    /// <inheritdoc />
    public async Task<OperationResult> TestAsync(string reference, CancellationToken ct = default)
    {
        var result = await GetAsync(reference, ct).ConfigureAwait(false);

        // Deliberately discards the value: a test must confirm resolution
        // without the secret reaching a caller that might print it.
        return result.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error ?? "Unresolved.", ExitCode.AuthenticationRequired);
    }

    private static string Summarise(ProcessOutcome outcome)
    {
        var text = string.IsNullOrWhiteSpace(outcome.StandardError)
            ? outcome.StandardOutput
            : outcome.StandardError;

        return text.Trim().Length == 0 ? $"exit code {outcome.ExitCode}" : text.Trim();
    }
}
