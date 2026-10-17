using Loadout.Core.Configuration;
using Loadout.Core.Security;
using Loadout.Models.Results;

namespace Loadout.Core.Teams.Daemon;

/// <summary>
/// Every setting 'loadout config' knows, grouped as the terminal's Settings
/// screen groups them, for Studio's Settings page.
/// </summary>
/// <remarks>
/// <para>
/// Read here rather than through 'config list --json', because that names
/// each setting under a <c>key</c> property and a page's reads pass through
/// the secret redactor, which hides the value of any property whose name
/// says key. Every setting's name came back as [redacted]. The redactor is
/// right to fail that way, so this answers in words it leaves alone.
/// </para>
/// <para>
/// The values themselves are still redacted, as 'config list' redacts them:
/// a workspace URL can carry an embedded credential. Changing one is not
/// done here; the page runs 'config set', which owns what a valid value is.
/// </para>
/// </remarks>
public static class StudioConfig
{
    /// <summary>Where Studio reads its settings from.</summary>
    public const string Root = "/api/v1/config";

    /// <summary>The settings in the terminal screen's groups and order.</summary>
    public static async Task<OperationResult<object>> ReadAsync(IConfigurationService configuration, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var config = await configuration.LoadConfigAsync(ct).ConfigureAwait(false);
        var machine = await configuration.LoadMachineAsync(ct).ConfigureAwait(false);

        if (config.Failed || machine.Failed)
        {
            return OperationResult<object>.Fail((config.Failed ? config.Error : machine.Error)!);
        }

        // Any group the order does not name still shows, after the rest,
        // rather than a setting going missing from the page.
        var groups = ConfigKeys.Groups.InOrder
            .Concat(ConfigKeys.All.Select(entry => entry.Group).Where(group => !ConfigKeys.Groups.InOrder.Contains(group)))
            .Distinct(StringComparer.Ordinal);

        return OperationResult<object>.Ok(groups
            .Select(group => new
            {
                group,
                settings = ConfigKeys.All
                    .Where(entry => string.Equals(entry.Group, group, StringComparison.Ordinal))
                    .Select(entry => new
                    {
                        name = entry.Key,
                        value = SecretRedactor.Redact(entry.Read(config.Value!, machine.Value!) ?? string.Empty),
                        description = entry.Description,
                        machineLocal = entry.IsMachineLocal,
                        flag = entry.IsFlag,
                        whenUnset = entry.WhenUnset,
                    })
                    .ToList(),
            })
            .Where(one => one.settings.Count > 0)
            .ToList());
    }
}
