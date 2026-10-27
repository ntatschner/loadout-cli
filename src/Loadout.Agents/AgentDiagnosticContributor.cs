using Loadout.Core.Configuration;
using Loadout.Core.Diagnostics;
using Loadout.Models.Agents;
using Loadout.Models.Diagnostics;

namespace Loadout.Agents;

/// <summary>
/// Folds every adapter's diagnostics into the doctor report, so core never has
/// to know which agents exist.
/// </summary>
public sealed class AgentDiagnosticContributor : IDiagnosticContributor
{
    private readonly IAgentRegistry _registry;
    private readonly IConfigurationService _configuration;

    public AgentDiagnosticContributor(IAgentRegistry registry, IConfigurationService configuration)
    {
        _registry = registry;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiagnosticCheck>> ContributeAsync(CancellationToken ct = default)
    {
        var perAdapter = await Task.WhenAll(
            _registry.Adapters.Select(a => a.RunDiagnosticsAsync(ct))).ConfigureAwait(false);

        var checks = perAdapter.SelectMany(checks => checks).ToList();

        if (await RemoteControlAsync(ct).ConfigureAwait(false) is { } remote)
        {
            checks.Add(remote);
        }

        return checks;
    }

    /// <summary>
    /// Whether sessions start with remote control, and which installed agents
    /// cannot do it.
    /// </summary>
    /// <remarks>
    /// Remote control cannot be seen once a session is running, so this is
    /// where somebody finds out before relying on it. A warning only when the
    /// default is on and the default agent cannot do it: another agent that
    /// is installed and never used is not worth a warning.
    /// </remarks>
    private async Task<DiagnosticCheck?> RemoteControlAsync(CancellationToken ct)
    {
        var loaded = await _configuration.LoadConfigAsync(ct).ConfigureAwait(false);

        if (loaded.Failed)
        {
            // The configuration itself is already reported on by the doctor.
            return null;
        }

        var config = loaded.Value!;

        if (!config.RemoteControl.Enabled)
        {
            return DiagnosticCheck.Ok(
                "Agents", "Remote control",
                "off by default; --remote turns it on for one session");
        }

        var installed = (await _registry.DetectAllAsync(ct).ConfigureAwait(false))
            .Where(d => d.IsInstalled)
            .ToList();

        var can = installed.Where(d => d.Supports(AgentCapabilities.RemoteControl)).Select(d => d.DisplayName).ToList();
        var cannot = installed.Where(d => !d.Supports(AgentCapabilities.RemoteControl)).Select(d => d.DisplayName).ToList();

        var detail = "on by default; --no-remote turns it off for one session."
            + (can.Count > 0 ? $" Starts with it: {string.Join(", ", can)}." : string.Empty)
            + (cannot.Count > 0 ? $" Cannot, so starts without it: {string.Join(", ", cannot)}." : string.Empty);

        var defaultCannot = installed.Any(d =>
            string.Equals(d.Name, config.DefaultAgent, StringComparison.OrdinalIgnoreCase)
            && !d.Supports(AgentCapabilities.RemoteControl));

        return defaultCannot
            ? DiagnosticCheck.Warn("Agents", "Remote control", detail)
            : DiagnosticCheck.Ok("Agents", "Remote control", detail);
    }
}
