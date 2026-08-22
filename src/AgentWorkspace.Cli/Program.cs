using AgentWorkspace.Agents;
using AgentWorkspace.Cli.Commands;
using AgentWorkspace.Cli.Infrastructure;
using AgentWorkspace.Core;
using AgentWorkspace.Models;
using AgentWorkspace.Platform;
using AgentWorkspace.Platform.Abstractions;
using AgentWorkspace.Tui;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentWorkspace.Cli;

/// <summary>Composition root and entry point for agentctl.</summary>
public static class Program
{
    /// <summary>
    /// Command names that must not be mistaken for a project name when they
    /// appear first. Everything else in first position is treated as a project,
    /// which is what makes "agentctl starstats" work (spec section 35).
    /// </summary>
    private static readonly HashSet<string> KnownCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "doctor", "status", "list", "here", "launch", "project", "workspace",
        "secret", "completion", "handoff", "profile", "repo", "protect", "migrate", "setup", "config",
        "--help", "-h", "--version",
    };

    public static async Task<int> Main(string[] args)
    {
        // Split before the parser sees anything: spec section 36 forbids the
        // launcher from parsing or altering arguments after a bare separator.
        var (launcherArgs, passthrough) = PassthroughArguments.Split(args);

        var services = new ServiceCollection();

        try
        {
            services.AddPlatformServices();
        }
        catch (PlatformNotSupportedException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return (int)ExitCode.GeneralFailure;
        }

        services
            .AddCoreServices()
            .AddAgentServices();

        services.AddSingleton(AnsiConsole.Console);
        services.AddSingleton(new PassthroughArguments(passthrough));
        services.AddSingleton<ILauncherTui, LauncherTui>();
        services.AddSingleton<ISetupWizard, SetupWizard>();

        var registrar = new TypeRegistrar(services);

        // Directories are created before any command runs so no command has to
        // guess whether its storage exists (spec section 16).
        var provider = services.BuildServiceProvider();
        var paths = provider.GetRequiredService<IPlatformPaths>();

        try
        {
            paths.EnsureDirectoriesExist();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked-down or redirected profile is a real situation on a
            // managed machine, and the user needs to be told which path failed
            // rather than handed a stack trace.
            Console.Error.WriteLine(
                $"The launcher could not create its storage under '{paths.Paths.Config}' "
                + $"or '{paths.Paths.State}': {ex.Message}");

            return (int)ExitCode.ConfigurationInvalid;
        }

        // No arguments means the interactive launcher, which is the same
        // entry point the desktop shortcut uses (spec sections 17 and 21).
        if (launcherArgs.Length == 0)
        {
            return await RunInteractiveAsync(provider).ConfigureAwait(false);
        }

        var app = new CommandApp(registrar);
        app.Configure(Configure);

        return await app.RunAsync(Rewrite(launcherArgs)).ConfigureAwait(false);
    }

    private static async Task<int> RunInteractiveAsync(ServiceProvider provider)
    {
        // A redirected stream means a pipe, a script or a CI job, where spec
        // section 37 says no menu may appear. Printing usage is the honest
        // alternative to hanging on a prompt nobody can answer.
        if (Console.IsOutputRedirected || Console.IsInputRedirected)
        {
            Console.Error.WriteLine(
                "agentctl was run with no arguments and no interactive terminal. "
                + "Run 'agentctl --help' for the available commands.");

            return (int)ExitCode.InvalidArguments;
        }

        // A machine that has never been configured gets the wizard rather than
        // an empty project list, which would leave a new user with nothing to
        // do and no hint about what to do next (spec section 61).
        var wizard = provider.GetRequiredService<ISetupWizard>();

        if (!wizard.IsConfigured())
        {
            return await wizard.RunAsync().ConfigureAwait(false);
        }

        return await provider.GetRequiredService<ILauncherTui>().RunAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Turns "agentctl starstats" into "agentctl launch starstats".
    /// <para>
    /// Done here rather than with a default command so that a genuine typo
    /// still produces a clear "no project matches" error, and so the command
    /// table stays the single source of truth for what a command name is.
    /// </para>
    /// </summary>
    internal static string[] Rewrite(string[] args)
    {
        if (args.Length == 0)
        {
            return args;
        }

        var first = args[0];

        // An option in first position belongs to no command; leave it alone
        // and let the parser produce its own message.
        if (first.StartsWith('-') || KnownCommands.Contains(first))
        {
            return args;
        }

        return ["launch", .. args];
    }

    private static void Configure(IConfigurator config)
    {
        config.SetApplicationName("agentctl");
        config.UseStrictParsing();

        // Exceptions are shown in full only when asked for. A stack trace is
        // noise to someone whose workspace simply is not reachable.
        config.SetExceptionHandler((ex, _) =>
        {
            Console.Error.WriteLine(Core.Security.SecretRedactor.Redact(ex.Message));
            return (int)ExitCode.GeneralFailure;
        });

        config.AddCommand<SetupCommand>("setup");
        config.AddCommand<DoctorCommand>("doctor");
        config.AddCommand<StatusCommand>("status");
        config.AddCommand<LaunchCommand>("launch");
        config.AddCommand<HereCommand>("here");
        config.AddCommand<CompletionCommand>("completion");
        config.AddCommand<HandoffCreateCommand>("handoff");
        config.AddCommand<ProtectCommand>("protect");
        config.AddCommand<MigrateCommand>("migrate");

        config.AddBranch("config", cfg =>
        {
            cfg.SetDescription("Read and write launcher settings.");
            cfg.AddCommand<ConfigListCommand>("list");
            cfg.AddCommand<ConfigGetCommand>("get");
            cfg.AddCommand<ConfigSetCommand>("set");
            cfg.AddCommand<ConfigEditCommand>("edit");
        });

        config.AddBranch("repo", repo =>
        {
            repo.SetDescription("Inspect repository compliance.");
            repo.AddCommand<RepoCheckCommand>("check");
        });

        config.AddBranch("profile", profile =>
        {
            profile.SetDescription("Inspect context profiles.");
            profile.AddCommand<ProfileListCommand>("list");
        });

        // "list" is an alias for the most common listing, because typing
        // "project list" for the default view gets old (spec section 35).
        config.AddCommand<ProjectListCommand>("list");

        config.AddBranch("project", project =>
        {
            project.SetDescription("Register, list and inspect projects.");
            project.AddCommand<ProjectListCommand>("list");
            project.AddCommand<ProjectAddCommand>("add");
            project.AddCommand<ProjectRemoveCommand>("remove");
            project.AddCommand<ProjectDiscoverCommand>("discover");
            project.AddCommand<ProjectOpenCommand>("open");
            project.AddCommand<WorktreeListCommand>("worktrees");
            project.AddCommand<ProjectCloneCommand>("clone");
            project.AddCommand<ProjectRelocateCommand>("relocate");
            project.AddCommand<ProjectShowCommand>("show");
        });

        config.AddBranch("workspace", workspace =>
        {
            workspace.SetDescription("Manage the central workspace clone.");
            workspace.AddCommand<WorkspaceStatusCommand>("status");
            workspace.AddCommand<WorkspaceSyncCommand>("sync");
            workspace.AddCommand<WorkspaceOpenCommand>("open");
        });

        config.AddBranch("secret", secret =>
        {
            secret.SetDescription("Store and check secrets in the platform credential store.");
            secret.AddCommand<SecretSetCommand>("set");
            secret.AddCommand<SecretTestCommand>("test");
            secret.AddCommand<SecretRemoveCommand>("remove");
        });
    }
}
