using Loadout.Models.Configuration;
using Loadout.Models.Platform;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Configuration;

/// <inheritdoc />
internal sealed class ConfigurationService : IConfigurationService
{
    private readonly IPlatformPaths _paths;
    private readonly IEnvironmentProvider _environment;
    private readonly YamlStore _yaml;

    public ConfigurationService(IPlatformPaths paths, IEnvironmentProvider environment, YamlStore yaml)
    {
        _paths = paths;
        _environment = environment;
        _yaml = yaml;
    }

    /// <inheritdoc />
    public Task<OperationResult<LauncherConfig>> LoadConfigAsync(CancellationToken ct = default) =>
        _yaml.LoadAsync(_paths.Paths.ConfigFile, () => new LauncherConfig(), ct);

    /// <inheritdoc />
    public Task<OperationResult> SaveConfigAsync(LauncherConfig config, CancellationToken ct = default) =>
        _yaml.SaveAsync(_paths.Paths.ConfigFile, config, restrictPermissions: true, ct);

    /// <inheritdoc />
    public Task<OperationResult<MachineConfig>> LoadMachineAsync(CancellationToken ct = default) =>
        _yaml.LoadAsync(_paths.Paths.MachinesFile, CreateDefaultMachine, ct);

    /// <inheritdoc />
    public Task<OperationResult> SaveMachineAsync(MachineConfig machine, CancellationToken ct = default) =>
        _yaml.SaveAsync(_paths.Paths.MachinesFile, machine, restrictPermissions: true, ct);

    private MachineConfig CreateDefaultMachine() => new()
    {
        MachineName = _environment.MachineName,
        DiscoveryRoots = DefaultDiscoveryRoots().ToList(),
        DefaultCloneRoot = DefaultCloneRoot(),
    };

    /// <summary>
    /// Conventional development roots for this platform (spec section 64).
    /// <para>
    /// Only roots that actually exist are offered, so a first run does not
    /// present a list of directories the user has never created. Nothing
    /// outside these roots is ever scanned, which is what keeps the launcher
    /// clear of macOS Full Disk Access (spec section 85).
    /// </para>
    /// </summary>
    private IEnumerable<string> DefaultDiscoveryRoots()
    {
        var home = _environment.HomeDirectory;

        IEnumerable<string> candidates = _paths.Host.OperatingSystem switch
        {
            HostOperatingSystem.Windows =>
            [
                @"D:\git",
                @"C:\src",
                @"C:\dev",
                Path.Combine(home, "git"),
                Path.Combine(home, "source", "repos"),
            ],

            HostOperatingSystem.MacOS =>
            [
                Path.Combine(home, "git"),
                Path.Combine(home, "src"),
                Path.Combine(home, "dev"),
                Path.Combine(home, "Development"),
                Path.Combine(home, "Projects"),
            ],

            _ =>
            [
                Path.Combine(home, "git"),
                Path.Combine(home, "src"),
                Path.Combine(home, "dev"),
                Path.Combine(home, "Projects"),
            ],
        };

        return candidates.Where(Directory.Exists);
    }

    private string DefaultCloneRoot()
    {
        var existing = DefaultDiscoveryRoots().FirstOrDefault();
        if (existing is not null)
        {
            return existing;
        }

        // Nothing conventional exists yet, so fall back to a path under the
        // home directory that the launcher can create without permission
        // questions on any of the three platforms.
        return Path.Combine(_environment.HomeDirectory, "git");
    }
}
