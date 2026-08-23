using Loadout.Models.Configuration;
using Loadout.Models.Results;

namespace Loadout.Core.Configuration;

/// <summary>
/// Loads and saves the two local configuration files: user preferences
/// (config.yaml) and machine-local state (machines.yaml).
/// <para>
/// These are tiers 2 and 4 of the precedence chain in spec section 90. The
/// remaining tiers — built-in defaults, central global config, project config,
/// environment profile, launch profile and CLI arguments — are layered on top
/// by the caller, with CLI arguments always winning.
/// </para>
/// </summary>
public interface IConfigurationService
{
    /// <summary>User preferences. Returns defaults when no file exists yet.</summary>
    Task<OperationResult<LauncherConfig>> LoadConfigAsync(CancellationToken ct = default);

    Task<OperationResult> SaveConfigAsync(LauncherConfig config, CancellationToken ct = default);

    /// <summary>
    /// This machine's state. Returns a new record seeded with the machine name
    /// and platform-appropriate discovery roots when no file exists yet.
    /// </summary>
    Task<OperationResult<MachineConfig>> LoadMachineAsync(CancellationToken ct = default);

    Task<OperationResult> SaveMachineAsync(MachineConfig machine, CancellationToken ct = default);
}
