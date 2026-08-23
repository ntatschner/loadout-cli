using System.Runtime.Versioning;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Linux;

/// <summary>
/// Stores secrets through the freedesktop Secret Service, which is what GNOME
/// Keyring and KDE Wallet both implement (spec section 54).
/// <para>
/// Reached through libsecret's secret-tool rather than by speaking D-Bus
/// directly. Unlike the macOS equivalent this tool is genuinely optional: a
/// headless server or a minimal container may have neither secret-tool nor a
/// running Secret Service. That is reported as an unavailable provider so the
/// user can pick another one, never as a hard failure, because spec section 86
/// requires headless machines to stay fully usable.
/// </para>
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxSecretServiceProvider : ISecretProvider
{
    private const string SecretTool = "secret-tool";
    private const string ApplicationAttribute = "loadout";

    private readonly IProcessLauncher _processes;
    private readonly IExecutableResolver _resolver;

    public LinuxSecretServiceProvider(IProcessLauncher processes, IExecutableResolver resolver)
    {
        _processes = processes;
        _resolver = resolver;
    }

    /// <inheritdoc />
    public string Name => "secret-service";

    /// <inheritdoc />
    public async Task<OperationResult> IsAvailableAsync(CancellationToken ct = default)
    {
        var tool = _resolver.Resolve(SecretTool);
        if (tool is null)
        {
            return OperationResult.Fail(
                "secret-tool was not found. Install libsecret-tools (Debian and Ubuntu) or "
                + "libsecret (Fedora and RHEL), or configure a different secret provider.");
        }

        // Presence of the binary is not enough: a Secret Service must be
        // running and unlocked for a lookup to succeed. A miss on a name that
        // will not exist proves the daemon answered.
        var result = await _processes.RunAsync(
            new ProcessRequest(tool, ["lookup", "application", ApplicationAttribute, "reference", "__availability_probe__"]),
            TimeSpan.FromSeconds(10),
            ct).ConfigureAwait(false);

        if (result.Failed || result.Value is null)
        {
            return OperationResult.Fail(result.Error ?? "secret-tool could not be run.");
        }

        // Exit code 1 with no output is "not found", which means the service
        // answered. A D-Bus or daemon failure writes to stderr instead.
        var stderr = result.Value.StandardError.Trim();
        if (stderr.Length > 0)
        {
            return OperationResult.Fail($"The Secret Service is not usable: {stderr}");
        }

        return OperationResult.Ok();
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> GetAsync(string reference, CancellationToken ct = default)
    {
        var tool = _resolver.Resolve(SecretTool);
        if (tool is null)
        {
            return OperationResult<string>.Fail("secret-tool was not found.", ExitCode.ConfigurationInvalid);
        }

        var result = await _processes.RunAsync(
            new ProcessRequest(tool, ["lookup", "application", ApplicationAttribute, "reference", reference]),
            TimeSpan.FromSeconds(30),
            ct).ConfigureAwait(false);

        if (result.Failed || result.Value is null)
        {
            return OperationResult<string>.Fail(
                result.Error ?? "The Secret Service could not be queried.",
                ExitCode.AuthenticationRequired);
        }

        if (!result.Value.Succeeded || result.Value.StandardOutput.Length == 0)
        {
            return OperationResult<string>.Fail(
                $"No stored secret for '{reference}'.",
                ExitCode.AuthenticationRequired);
        }

        // secret-tool lookup emits the value with no trailing newline, but a
        // stored value may legitimately end in one, so only the separator the
        // capture added is removed.
        return OperationResult<string>.Ok(result.Value.StandardOutput.TrimEnd('\r', '\n'));
    }

    /// <inheritdoc />
    public async Task<OperationResult> SetAsync(string reference, string value, CancellationToken ct = default)
    {
        var tool = _resolver.Resolve(SecretTool);
        if (tool is null)
        {
            return OperationResult.Fail("secret-tool was not found.", ExitCode.ConfigurationInvalid);
        }

        var result = await _processes.RunAsync(
            new ProcessRequest(
                tool,
                ["store", "--label", $"loadout: {reference}", "application", ApplicationAttribute, "reference", reference],
                StandardInput: value),
            TimeSpan.FromSeconds(30),
            ct).ConfigureAwait(false);

        if (result.Failed || result.Value is null)
        {
            return OperationResult.Fail(result.Error ?? "The Secret Service could not be written to.");
        }

        return result.Value.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(
                $"Could not store '{reference}': {result.Value.StandardError.Trim()}");
    }

    /// <inheritdoc />
    public async Task<OperationResult> RemoveAsync(string reference, CancellationToken ct = default)
    {
        var tool = _resolver.Resolve(SecretTool);
        if (tool is null)
        {
            return OperationResult.Fail("secret-tool was not found.", ExitCode.ConfigurationInvalid);
        }

        var result = await _processes.RunAsync(
            new ProcessRequest(tool, ["clear", "application", ApplicationAttribute, "reference", reference]),
            TimeSpan.FromSeconds(30),
            ct).ConfigureAwait(false);

        if (result.Failed || result.Value is null)
        {
            return OperationResult.Fail(result.Error ?? "The Secret Service could not be written to.");
        }

        return result.Value.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail($"Could not remove '{reference}': {result.Value.StandardError.Trim()}");
    }

    /// <inheritdoc />
    public async Task<OperationResult> TestAsync(string reference, CancellationToken ct = default)
    {
        var result = await GetAsync(reference, ct).ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error ?? "Unresolved.", ExitCode.AuthenticationRequired);
    }
}
