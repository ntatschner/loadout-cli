using Loadout.Core.Diagnostics;
using Loadout.Models.Diagnostics;

namespace Loadout.Agents;

/// <summary>
/// Folds every adapter's diagnostics into the doctor report, so core never has
/// to know which agents exist.
/// </summary>
public sealed class AgentDiagnosticContributor : IDiagnosticContributor
{
    private readonly IAgentRegistry _registry;

    public AgentDiagnosticContributor(IAgentRegistry registry) => _registry = registry;

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiagnosticCheck>> ContributeAsync(CancellationToken ct = default)
    {
        var perAdapter = await Task.WhenAll(
            _registry.Adapters.Select(a => a.RunDiagnosticsAsync(ct))).ConfigureAwait(false);

        return perAdapter.SelectMany(checks => checks).ToList();
    }
}
