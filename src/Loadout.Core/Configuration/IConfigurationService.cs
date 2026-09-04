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

    /// <summary>
    /// Reads the launcher configuration, applies a change and writes it back
    /// without anybody else getting in between.
    /// </summary>
    /// <remarks>
    /// Load-then-save leaves a window. Two launchers each changing a different
    /// setting both read the same starting file and each writes its own change
    /// over the other's, so one setting is silently gone: the file is valid and
    /// both commands reported success. Several sessions on one machine is the
    /// ordinary case, so prefer this wherever a change is made to what was
    /// already there.
    /// </remarks>
    Task<OperationResult<LauncherConfig>> UpdateConfigAsync(
        Action<LauncherConfig> change, CancellationToken ct = default);

    /// <inheritdoc cref="UpdateConfigAsync"/>
    Task<OperationResult<MachineConfig>> UpdateMachineAsync(
        Action<MachineConfig> change, CancellationToken ct = default);
}
