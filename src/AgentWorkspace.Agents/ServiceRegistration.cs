using AgentWorkspace.Core.Configuration;
using AgentWorkspace.Core.Diagnostics;
using AgentWorkspace.Models.Configuration;
using AgentWorkspace.Platform.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AgentWorkspace.Agents;

/// <summary>Registers the agent adapters and the launch pipeline.</summary>
public static class ServiceRegistration
{
    /// <summary>Adds the agent services. Requires platform and core services first.</summary>
    public static IServiceCollection AddAgentServices(this IServiceCollection services)
    {
        services.AddSingleton<IAgentRegistry>(provider =>
        {
            // The registry needs configuration in order to know about
            // user-defined agents and extra search paths, and the container
            // has no asynchronous resolution. Blocking here is safe and
            // bounded: it reads one small local file, and this is a
            // short-lived CLI process with no synchronisation context to
            // deadlock against.
            var configuration = provider.GetRequiredService<IConfigurationService>();
            var configResult = configuration.LoadConfigAsync().GetAwaiter().GetResult();

            // A broken config must not stop the agents from being listed:
            // doctor is exactly what a user runs to find out why, and it needs
            // the registry to report on. Defaults are used instead.
            var config = configResult.Value ?? new LauncherConfig();

            return new AgentRegistry(
                provider.GetRequiredService<IExecutableResolver>(),
                provider.GetRequiredService<IProcessLauncher>(),
                config);
        });

        services.AddSingleton<IAgentLauncher, AgentLauncher>();
        services.AddSingleton<IDiagnosticContributor, AgentDiagnosticContributor>();

        return services;
    }
}
