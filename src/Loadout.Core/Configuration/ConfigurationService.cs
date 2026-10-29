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
    private readonly KeptFiles<MachineConfig> _machine;

    public ConfigurationService(IPlatformPaths paths, IEnvironmentProvider environment, YamlStore yaml)
    {
        _paths = paths;
        _environment = environment;
        _yaml = yaml;
        _machine = new KeptFiles<MachineConfig>(yaml, CreateDefaultMachine, Copy);
    }

    /// <inheritdoc />
    public Task<OperationResult<LauncherConfig>> LoadConfigAsync(CancellationToken ct = default) =>
        _yaml.LoadAsync(_paths.Paths.ConfigFile, () => new LauncherConfig(), ct);

    /// <inheritdoc />
    public Task<OperationResult> SaveConfigAsync(LauncherConfig config, CancellationToken ct = default) =>
        _yaml.SaveAsync(_paths.Paths.ConfigFile, config, restrictPermissions: true, ct);

    /// <inheritdoc />
    /// <remarks>
    /// Kept parsed between reads, because the daemon asks for it hundreds of
    /// times for every time it changes.
    /// </remarks>
    public async Task<OperationResult<MachineConfig>> LoadMachineAsync(CancellationToken ct = default)
    {
        var read = await _machine.ReadAsync(_paths.Paths.MachinesFile, ct).ConfigureAwait(false);

        return read.Succeeded
            ? OperationResult<MachineConfig>.Ok(read.Value!.Value)
            : OperationResult<MachineConfig>.Fail(read.Error!, read.ExitCode);
    }

    /// <summary>A machine's settings nothing else holds, so a caller changing what it was given changes nothing kept.</summary>
    internal static MachineConfig Copy(MachineConfig machine) => new()
    {
        SchemaVersion = machine.SchemaVersion,
        MachineName = machine.MachineName,
        DiscoveryRoots = [.. machine.DiscoveryRoots],
        DefaultCloneRoot = machine.DefaultCloneRoot,
        Projects = new Dictionary<string, MachineProjectEntry>(
            machine.Projects.Select(pair => KeyValuePair.Create(pair.Key, new MachineProjectEntry
            {
                Id = pair.Value.Id,
                Path = pair.Value.Path,
                LastLaunchedUtc = pair.Value.LastLaunchedUtc,
                LaunchCount = pair.Value.LaunchCount,
                Pinned = pair.Value.Pinned,
                LastAgent = pair.Value.LastAgent,
            })),
            machine.Projects.Comparer),
        Teams = Copy(machine.Teams),
    };

    private static MachineTeams Copy(MachineTeams teams) => new()
    {
        OutwardAllowed = [.. teams.OutwardAllowed],
        WebhookTeams = [.. teams.WebhookTeams],
        ForTasks = teams.ForTasks,
        Remediation = new Dictionary<string, string>(teams.Remediation, teams.Remediation.Comparer),
        Budget = teams.Budget,
        TrustedRemedies = [.. teams.TrustedRemedies.Select(trusted => new TrustedRemedy
        {
            Team = trusted.Team,
            Remedy = trusted.Remedy,
            Fingerprint = trusted.Fingerprint,
            By = trusted.By,
            At = trusted.At,
        })],
        TrustedTools = [.. teams.TrustedTools.Select(trusted => new TrustedTool
        {
            Tool = trusted.Tool,
            Version = trusted.Version,
            Fingerprint = trusted.Fingerprint,
            By = trusted.By,
            At = trusted.At,
        })],
        WebhookListen = teams.WebhookListen,
        DashboardPort = teams.DashboardPort,
        Dashboard = teams.Dashboard,
        NotifyKind = teams.NotifyKind,
        NotifyChat = teams.NotifyChat,
        LinkAddress = teams.LinkAddress,
        PermissionWaitMinutes = teams.PermissionWaitMinutes,
        QuestionWaitMinutes = teams.QuestionWaitMinutes,
        OfficeSet = teams.OfficeSet,
        OfficeScale = teams.OfficeScale,
        OfficeCast = teams.OfficeCast,
        BinDays = teams.BinDays,
        EventsDays = teams.EventsDays,
        TriggerJournalMegabytes = teams.TriggerJournalMegabytes,
    };

    /// <inheritdoc />
    public Task<OperationResult> SaveMachineAsync(MachineConfig machine, CancellationToken ct = default) =>
        _yaml.SaveAsync(_paths.Paths.MachinesFile, machine, restrictPermissions: true, ct);

    /// <inheritdoc />
    public Task<OperationResult<LauncherConfig>> UpdateConfigAsync(
        Action<LauncherConfig> change, CancellationToken ct = default) =>
        _yaml.UpdateAsync(
            _paths.Paths.ConfigFile, () => new LauncherConfig(), change, true, ct);

    /// <inheritdoc />
    public Task<OperationResult<MachineConfig>> UpdateMachineAsync(
        Action<MachineConfig> change, CancellationToken ct = default) =>
        _yaml.UpdateAsync(
            _paths.Paths.MachinesFile, CreateDefaultMachine, change, true, ct);

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
