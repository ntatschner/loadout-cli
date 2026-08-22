using System.Runtime.Versioning;
using AgentWorkspace.Models.Results;
using AgentWorkspace.Platform.Abstractions;

namespace AgentWorkspace.Platform.MacOS;

/// <summary>
/// Placeholder for the macOS application bundle (spec sections 20 and 44).
/// <para>
/// Bundle creation, signing and notarisation were explicitly deferred out of
/// the first milestone, so this reports the capability as unavailable with the
/// reason rather than silently doing nothing. That is the behaviour spec
/// section 5 demands of a gap: documented, detectable, surfaced by diagnostics
/// and handled gracefully.
/// </para>
/// <para>
/// The launcher itself runs natively on macOS regardless; only the graphical
/// entry point is missing, and the CLI and TUI reach every feature without it.
/// </para>
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacOSDesktopIntegration : IDesktopIntegration
{
    internal const string DeferralReason =
        "The macOS application bundle is not built yet, so there is no Spotlight or Launchpad "
        + "entry to install. Run agentctl from a terminal; every feature is reachable there.";

    /// <inheritdoc />
    public OperationResult<bool> IsInstalled() => OperationResult<bool>.Ok(false);

    /// <inheritdoc />
    public Task<OperationResult> InstallAsync(string executablePath, CancellationToken ct = default) =>
        Task.FromResult(OperationResult.Fail(DeferralReason));

    /// <inheritdoc />
    public Task<OperationResult> UninstallAsync(CancellationToken ct = default) =>
        Task.FromResult(OperationResult.Ok());
}
