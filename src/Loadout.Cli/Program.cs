using Loadout.Agents;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Core;
using Loadout.Models;
using Loadout.Platform;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli;

/// <summary>Composition root and entry point for loadout.</summary>
public static class Program
{
    /// <summary>
    /// Command names that must not be mistaken for a project name when they
    /// appear first. Everything else in first position is treated as a project,
    /// which is what makes "loadout starstats" work (spec section 35).
    /// <para>
    /// Recorded as each command is registered rather than written out by hand.
    /// The hand-written version was a standing trap: a new command that nobody
    /// remembered to add here did not fail loudly, it silently became a project
    /// name and reported that no project matched it.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> KnownCommands = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a top-level command and records its name.</summary>
    private static void Top<TCommand>(IConfigurator config, string name)
        where TCommand : class, ICommandLimiter<CommandSettings>
    {
        KnownCommands.Add(name);
        config.AddCommand<TCommand>(name);
    }

    /// <summary>Registers a top-level branch and records its name.</summary>
    private static void TopBranch(
        IConfigurator config,
        string name,
        Action<IConfigurator<CommandSettings>> configure)
    {
        KnownCommands.Add(name);
        config.AddBranch(name, configure);
    }

    /// <summary>
    /// The registered names, configuring a throwaway parser first if nothing
    /// has been registered yet. Registration only records names — it resolves
    /// no services — so doing it twice costs nothing and means the rewrite is
    /// correct even when called on its own.
    /// </summary>
    internal static IReadOnlySet<string> CommandNames()
    {
        if (KnownCommands.Count == 0)
        {
            var app = new CommandApp(new TypeRegistrar(new ServiceCollection()));
            app.Configure(config => Configure(config, showFullExceptions: false));
        }

        return KnownCommands;
    }

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
        services.AddSingleton<IProjectOnboarding, ProjectOnboarding>();
        services.AddSingleton<ILauncherTui, LauncherTui>();
        services.AddSingleton<ISetupWizard, SetupWizard>();
        services.AddSingleton<WorkspaceSavePrompt>();
        services.AddSingleton<StatuslineTargets>();
        services.AddSingleton<SessionScope>();
        services.AddSingleton<McpScopeResolver>();

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

        // Read straight from argv because the exception handler runs outside any
        // command, so it never sees the parsed settings.
        var showFullExceptions = launcherArgs.Contains("--debug", StringComparer.Ordinal);

        var app = new CommandApp(registrar);
        app.Configure(config => Configure(config, showFullExceptions));

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
                "loadout was run with no arguments and no interactive terminal. "
                + "Run 'loadout --help' for the available commands.");

            return (int)ExitCode.InvalidArguments;
        }

        // A machine that has never been configured gets the wizard rather than
        // an empty project list, which would leave a new user with nothing to
        // do and no hint about what to do next (spec section 61).
        var wizard = provider.GetRequiredService<ISetupWizard>();

        if (!wizard.IsConfigured())
        {
            return await wizard.RunAsync(new SetupRequest()).ConfigureAwait(false);
        }

        return await provider.GetRequiredService<ILauncherTui>().RunAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Turns "loadout starstats" into "loadout launch starstats".
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
        if (first.StartsWith('-') || CommandNames().Contains(first))
        {
            return args;
        }

        return ["launch", .. args];
    }

    private static void Configure(IConfigurator config, bool showFullExceptions)
    {
        config.SetApplicationName("loadout");

        // Without this, --version is rejected as an unknown option. It is the
        // first thing anyone types against an unfamiliar binary, and the first
        // thing a bug report asks for.
        config.SetApplicationVersion(
            typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

        config.UseStrictParsing();

        // Exceptions are shown in full only when asked for. A stack trace is
        // noise to someone whose workspace simply is not reachable, and it is
        // exactly what is needed when the failure is a defect.
        config.SetExceptionHandler((ex, _) =>
        {
            Console.Error.WriteLine(Core.Security.SecretRedactor.Redact(
                showFullExceptions ? ex.ToString() : ex.Message));

            if (!showFullExceptions)
            {
                Console.Error.WriteLine("Run again with --debug for the full detail.");
            }

            return (int)ExitCode.GeneralFailure;
        });

        Top<SetupCommand>(config, "setup");
        Top<DoctorCommand>(config, "doctor");
        Top<StatusCommand>(config, "status");
        Top<LaunchCommand>(config, "launch");
        Top<HereCommand>(config, "here");
        Top<CompletionCommand>(config, "completion");
        Top<HandoffCreateCommand>(config, "handoff");
        Top<ProtectCommand>(config, "protect");
        Top<DesktopCommand>(config, "desktop");
        Top<UpdateCommand>(config, "update");
        Top<MigrateCommand>(config, "migrate");

        TopBranch(config, "backup", backup =>
        {
            backup.SetDescription("Inspect and restore snapshots taken before mutating operations.");
            backup.AddCommand<BackupListCommand>("list");
            backup.AddCommand<BackupRestoreCommand>("restore");
        });

        TopBranch(config, "memory", memory =>
        {
            memory.SetDescription("Record and check the durable facts about a project.");
            memory.AddCommand<MemoryListCommand>("list");
            memory.AddCommand<MemoryWriteCommand>("write");
            memory.AddCommand<MemoryAuditCommand>("audit");
            memory.AddCommand<MemoryReindexCommand>("reindex");
            memory.AddCommand<MemoryImportCommand>("import");
            memory.AddCommand<MemoryCompressCommand>("compress");
        });

        TopBranch(config, "rules", rules =>
        {
            rules.SetDescription("Inspect the path-scoped instruction rules and what they cost.");
            rules.AddCommand<RulesListCommand>("list");
            rules.AddCommand<RulesBudgetCommand>("budget");
            rules.AddCommand<RulesAuditCommand>("audit");
            rules.AddCommand<RulesSplitCommand>("split");
        });

        TopBranch(config, "config", cfg =>
        {
            cfg.SetDescription("Read and write launcher settings.");
            cfg.AddCommand<ConfigListCommand>("list");
            cfg.AddCommand<ConfigGetCommand>("get");
            cfg.AddCommand<ConfigSetCommand>("set");
            cfg.AddCommand<ConfigEditCommand>("edit");
        });

        TopBranch(config, "mcp", mcp =>
        {
            mcp.SetDescription("Manage the MCP servers a project loads.");
            mcp.AddCommand<McpListCommand>("list");
            mcp.AddCommand<McpAddCommand>("add");
            mcp.AddCommand<McpRemoveCommand>("remove");
        });

        Top<DriftCommand>(config, "drift");
        Top<SessionListCommand>(config, "sessions");
        Top<ResumeCommand>(config, "resume");

        TopBranch(config, "statusline", statusline =>
        {
            statusline.SetDescription("Show the project, branch and context usage in the agent status line.");

            // Rendering is the default because that is the form Claude invokes:
            // the installed command is this binary plus one word.
            statusline.SetDefaultCommand<StatuslineRenderCommand>();

            statusline.AddCommand<StatuslineInstallCommand>("install");
            statusline.AddCommand<StatuslineUninstallCommand>("uninstall");
            statusline.AddCommand<StatuslineShowCommand>("show");
        });

        TopBranch(config, "repo", repo =>
        {
            repo.SetDescription("Inspect repository compliance.");
            repo.AddCommand<RepoCheckCommand>("check");
        });

        TopBranch(config, "profile", profile =>
        {
            profile.SetDescription("Inspect context profiles.");
            profile.AddCommand<ProfileListCommand>("list");
        });

        // "list" is an alias for the most common listing, because typing
        // "project list" for the default view gets old (spec section 35).
        Top<ProjectListCommand>(config, "list");

        TopBranch(config, "project", project =>
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
            project.AddCommand<ProjectSurveyCommand>("survey");
            project.AddCommand<ProjectLinkCommand>("link");
        });

        TopBranch(config, "workspace", workspace =>
        {
            workspace.SetDescription("Manage the central workspace clone.");
            workspace.AddCommand<WorkspaceStatusCommand>("status");
            workspace.AddCommand<WorkspaceSyncCommand>("sync");
            workspace.AddCommand<WorkspaceSaveCommand>("save");
            workspace.AddCommand<WorkspaceOpenCommand>("open");
        });

        TopBranch(config, "secret", secret =>
        {
            secret.SetDescription("Store and check secrets in the platform credential store.");
            secret.AddCommand<SecretSetCommand>("set");
            secret.AddCommand<SecretTestCommand>("test");
            secret.AddCommand<SecretRemoveCommand>("remove");
        });
    }
}
