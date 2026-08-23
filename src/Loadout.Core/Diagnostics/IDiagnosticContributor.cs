using Loadout.Models.Diagnostics;

namespace Loadout.Core.Diagnostics;

/// <summary>
/// Supplies extra checks to the doctor report (spec section 60).
/// <para>
/// This exists so core does not have to know about agents. The adapter layer
/// depends on core, not the other way round, so agent diagnostics arrive
/// through this seam rather than by core reaching upwards. It is also the
/// natural extension point for the plugin architecture of spec section 87.
/// </para>
/// </summary>
public interface IDiagnosticContributor
{
    /// <summary>Checks to fold into the report. Never throws; failures become checks.</summary>
    Task<IReadOnlyList<DiagnosticCheck>> ContributeAsync(CancellationToken ct = default);
}
