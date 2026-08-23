using Loadout.Core.Backups;
using Loadout.Core.Configuration;
using Loadout.Core.Context;
using Loadout.Core.Diagnostics;
using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Policies;
using Loadout.Core.Projects;
using Loadout.Core.Updates;
using Loadout.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Loadout.Core;

/// <summary>
/// Registers the platform-neutral services. Every one of these depends only on
/// the platform abstractions, never on a concrete implementation, which is what
/// keeps the cross-platform contract of spec section 5 enforceable.
/// </summary>
public static class ServiceRegistration
{
    /// <summary>Adds the core services. Requires the platform services to be registered first.</summary>
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        // Singleton throughout: the launcher is a short-lived process handling
        // one command, so there is nothing per-request to scope.
        services.AddSingleton<YamlStore>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<IGitManager, GitManager>();
        services.AddSingleton<IWorkspaceManager, WorkspaceManager>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IContextCompiler, ContextCompiler>();
        services.AddSingleton<IHandoffService, HandoffService>();
        services.AddSingleton<IPreflightService, PreflightService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IRuleService, RuleService>();
        services.AddSingleton<IMemoryService, MemoryService>();
        services.AddSingleton<IMemoryImporter, MemoryImporter>();
        services.AddSingleton<IRepositoryAttribution, RepositoryAttribution>();
        services.AddSingleton<IPolicyService, PolicyService>();
        services.AddSingleton<IMigrationService, MigrationService>();
        services.AddSingleton<ISecurityProfileService, SecurityProfileService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDiagnosticContributor, InstructionDiagnosticContributor>();
        services.AddSingleton<IDoctorService, DoctorService>();

        // One client for the process, which is what HttpClient is designed for.
        // The update check is the only thing in the launcher that reaches the
        // network on its own behalf; everything else goes through git.
        services.AddSingleton(_ => new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5),
        });

        services.AddSingleton<IUpdateService>(provider => new UpdateService(
            provider.GetRequiredService<IConfigurationService>(),
            provider.GetRequiredService<Platform.Abstractions.IPlatformPaths>(),
            provider.GetRequiredService<Platform.Abstractions.IFilePermissions>(),
            provider.GetRequiredService<HttpClient>()));

        return services;
    }
}
