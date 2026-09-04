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
        services.AddSingleton<Projects.IProjectTemplateService, Projects.ProjectTemplateService>();
        services.AddSingleton<IContextCompiler, ContextCompiler>();
        services.AddSingleton<IHandoffService, HandoffService>();
        services.AddSingleton<IPreflightService, PreflightService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IRuleService, RuleService>();
        // The machine root comes from the platform rather than the workspace,
        // because the whole point of the machine scope is that it is somewhere
        // the workspace cannot carry away.
        services.AddSingleton<IMemoryService>(provider => new MemoryService(
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<Loadout.Platform.Abstractions.IPlatformPaths>()
                .Paths.State));
        services.AddSingleton<IMemoryImporter, MemoryImporter>();
        services.AddSingleton<Instructions.MemoryCompressor>();
        services.AddSingleton<IRepositoryAttribution, RepositoryAttribution>();
        services.AddSingleton<IProjectOverviewService, ProjectOverviewService>();
        services.AddSingleton<IDriftService, DriftService>();
        services.AddSingleton<Mcp.IMcpService, Mcp.McpService>();
        services.AddSingleton<Mcp.IInstalledMcpReader, Mcp.InstalledMcpReader>();
        services.AddSingleton<IPolicyService, PolicyService>();
        services.AddSingleton<IMigrationService, MigrationService>();
        services.AddSingleton<ISecurityProfileService, SecurityProfileService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDiagnosticContributor, InstructionDiagnosticContributor>();

        services.AddSingleton<Editors.IEditorService, Editors.EditorService>();
        services.AddSingleton<IDiagnosticContributor, Editors.EditorDiagnosticContributor>();
        services.AddSingleton<IDoctorService, DoctorService>();
        services.AddSingleton<IRemediationService, RemediationService>();

        // Both are registered whether or not the agent is installed: each
        // reports its own availability, so a machine with only one of them
        // simply lists fewer sessions rather than failing.
        services.AddSingleton<Sessions.ISessionHistory, Sessions.ClaudeSessionHistory>();
        services.AddSingleton<Sessions.ISessionHistory, Sessions.CodexSessionHistory>();
        // Readers for agents nobody compiled in. How many there are is only
        // known once configuration has been read, and the container is built
        // before that, so this is resolved rather than registered N times.
        // Blocking on the read is the same trade the agent registry makes for
        // the same reason: one small local file, in a short-lived process.
        services.AddSingleton<Sessions.IDeclaredSessionHistories>(provider =>
        {
            var loaded = provider.GetRequiredService<IConfigurationService>()
                .LoadConfigAsync().GetAwaiter().GetResult();

            // A broken config must not cost somebody their session listing.
            return new Sessions.DeclaredSessionHistories(
                loaded.Value ?? new Models.Configuration.LauncherConfig(),
                provider.GetRequiredService<Loadout.Platform.Abstractions.IEnvironmentProvider>());
        });

        services.AddSingleton<Sessions.ISessionHistoryService, Sessions.SessionHistoryService>();

        // What the launcher gave a session, which the transcripts cannot say.
        // Written by the launcher rather than reconstructed afterwards, because
        // a launch nobody recorded cannot be recovered later.
        services.AddSingleton<Sessions.ILaunchLedger, Sessions.LaunchLedger>();

        // And which of them are still going. Separate from the ledger because
        // the questions differ: one is a history that is only ever added to,
        // the other is a claim about right now that has to be checked against
        // the process that made it.
        services.AddSingleton<Sessions.ISessionRegistry, Sessions.SessionRegistry>();

        // The same transcripts read again, for what they cost rather than what
        // they were about. Separate readers from the session ones because the
        // two want opposite behaviour from a malformed line: a listing skips it
        // and carries on, whereas a total that skipped something has to say so.
        services.AddSingleton<Usage.IUsageHistory, Usage.ClaudeUsageHistory>();
        services.AddSingleton<Usage.IUsageHistory, Usage.CodexUsageHistory>();
        // The counting counterpart of the described session readers, resolved
        // the same way and for the same reason.
        services.AddSingleton<Usage.IDeclaredUsageHistories>(provider =>
        {
            var loaded = provider.GetRequiredService<IConfigurationService>()
                .LoadConfigAsync().GetAwaiter().GetResult();

            return new Usage.DeclaredUsageHistories(
                loaded.Value ?? new Models.Configuration.LauncherConfig(),
                provider.GetRequiredService<Loadout.Platform.Abstractions.IEnvironmentProvider>());
        });

        services.AddSingleton<Usage.IUsageService, Usage.UsageService>();
        services.AddSingleton<Usage.ITelemetryStore, Usage.TelemetryStore>();
        services.AddSingleton<Usage.IPlanHeadroomReader, Usage.CodexPlanHeadroom>();
        services.AddSingleton<Usage.ISpendWatch, Usage.SpendWatch>();
        services.AddSingleton<Usage.ISpendNoticeStore, Usage.SpendNoticeStore>();
        services.AddSingleton<Tasks.ITaskService, Tasks.TaskService>();
        services.AddSingleton<Statusline.ILoadedSpecialistStore, Statusline.LoadedSpecialistStore>();
        services.AddSingleton<Checkpoints.ICheckpointService, Checkpoints.CheckpointService>();

        // The specialist layer: what an agent is told, and why. The library and
        // resolver hold no state of their own, so a singleton each is enough.
        services.AddSingleton<ISpecialistLibrary, SpecialistLibrary>();
        services.AddSingleton<ISpecialistResolver, SpecialistResolver>();
        services.AddSingleton<IRepositoryEvidenceReader, RepositoryEvidenceReader>();
        services.AddSingleton<IInstructionService, InstructionService>();
        services.AddSingleton<IDiagnosticContributor, SpecialistDiagnosticContributor>();

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
