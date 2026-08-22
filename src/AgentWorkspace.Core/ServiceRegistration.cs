using AgentWorkspace.Core.Configuration;
using AgentWorkspace.Core.Context;
using AgentWorkspace.Core.Diagnostics;
using AgentWorkspace.Core.Git;
using AgentWorkspace.Core.Policies;
using AgentWorkspace.Core.Projects;
using AgentWorkspace.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace AgentWorkspace.Core;

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
        services.AddSingleton<IPolicyService, PolicyService>();
        services.AddSingleton<IMigrationService, MigrationService>();
        services.AddSingleton<ISecurityProfileService, SecurityProfileService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDoctorService, DoctorService>();

        return services;
    }
}
