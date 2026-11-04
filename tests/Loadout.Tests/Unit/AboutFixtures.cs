using Loadout.Core.Configuration;
using Loadout.Models.Configuration;
using Loadout.Models.Platform;
using Loadout.Models.Results;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;

namespace Loadout.Tests.Unit;

/// <summary>A throwaway home and the one setting the About tests read, shared by them.</summary>
internal sealed class AboutHome : IDisposable
{
    public AboutHome()
    {
        Root = Path.Combine(Path.GetTempPath(), "loadout-about-" + Guid.NewGuid().ToString("N"));

        var environment = new FakeEnvironmentProvider(
            Root,
            new Dictionary<string, string>
            {
                ["XDG_CONFIG_HOME"] = Path.Combine(Root, "config"),
                ["XDG_DATA_HOME"] = Path.Combine(Root, "data"),
                ["XDG_STATE_HOME"] = Path.Combine(Root, "state"),
                ["XDG_CACHE_HOME"] = Path.Combine(Root, "cache"),
            });

        Paths = new LinuxPaths(
            environment,
            new NoOpFilePermissions(),
            new HostPlatform(
                HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST-MACHINE"));

        Paths.EnsureDirectoriesExist();
    }

    public string Root { get; }

    public LinuxPaths Paths { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing the run over.
        }
    }
}

/// <summary>Only <c>updates-source</c>, which is all the About code reads.</summary>
internal sealed class SourceConfiguration(string? source) : IConfigurationService
{
    public Task<OperationResult<LauncherConfig>> LoadConfigAsync(CancellationToken ct = default) =>
        Task.FromResult(OperationResult<LauncherConfig>.Ok(new LauncherConfig
        {
            Updates = new UpdateSettings { Source = source },
        }));

    public Task<OperationResult> SaveConfigAsync(LauncherConfig config, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<OperationResult<MachineConfig>> LoadMachineAsync(CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<OperationResult> SaveMachineAsync(MachineConfig machine, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<OperationResult<LauncherConfig>> UpdateConfigAsync(Action<LauncherConfig> change, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<OperationResult<MachineConfig>> UpdateMachineAsync(Action<MachineConfig> change, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
