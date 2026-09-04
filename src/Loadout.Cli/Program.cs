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
        Catalogue.Record(name, typeof(TCommand));
        config.AddCommand<TCommand>(name);
    }

    /// <summary>Registers a top-level branch and records its name.</summary>
    private static void TopBranch(IConfigurator config, string name, Action<Branch> configure)
    {
        KnownCommands.Add(name);
        config.AddBranch<CommandSettings>(name, branch => configure(new Branch(branch, name)));
    }

    /// <summary>
    /// A branch that records what is added to it.
    /// <para>
    /// Sub-commands are registered through this rather than on the configurator
    /// directly, so the catalogue the launcher reads is built from the same
    /// registration the parser uses. A second list kept by hand is the thing
    /// this exists to avoid.
    /// </para>
    /// </summary>
    internal sealed class Branch
    {
        private readonly IConfigurator<CommandSettings> _inner;
        private readonly string _name;

        internal Branch(IConfigurator<CommandSettings> inner, string name)
        {
            _inner = inner;
            _name = name;
        }

        internal void SetDescription(string description) => _inner.SetDescription(description);

        /// <summary>
        /// Describes the branch and files it under a category.
        /// </summary>
        /// <remarks>
        /// A branch is a thing somebody types — "loadout backup" is how anybody
        /// would look for restoring something — but only its sub-commands were
        /// ever recorded. Eleven whole families were invisible to any listing
        /// that showed top-level commands, which is worse than the flat help
        /// they were meant to improve on, because that at least named them.
        /// </remarks>
        internal void Describe(string description, string category, string intent = "")
        {
            _inner.SetDescription(description);

            Catalogue.RecordBranch(_name, description, category, intent);
        }

        /// <summary>
        /// The command the branch runs when named on its own. Recorded under
        /// the branch name, because that is what somebody types.
        /// </summary>
        internal void SetDefaultCommand<TCommand>()
            where TCommand : class, ICommandLimiter<CommandSettings>
        {
            Catalogue.Record(_name, typeof(TCommand));
            _inner.SetDefaultCommand<TCommand>();
        }

        internal void AddCommand<TCommand>(string name)
            where TCommand : class, ICommandLimiter<CommandSettings>
        {
            Catalogue.Record($"{_name} {name}", typeof(TCommand));
            _inner.AddCommand<TCommand>(name);
        }
    }

    /// <summary>
    /// The registered names, configuring a throwaway parser first if nothing
    /// has been registered yet. Registration only records names — it resolves
    /// no services — so doing it twice costs nothing and means the rewrite is
    /// correct even when called on its own.
    /// </summary>
    /// <summary>
    /// Every command, as the launcher sees them. Exposed so a test can assert
    /// that what the parser knows and what the launcher offers are the same.
    /// </summary>
    internal static IReadOnlyList<Loadout.Tui.CatalogueEntry> RegisteredCommands() =>
        Infrastructure.Catalogue.Commands;

    /// <summary>
    /// Fills the command set once, however many callers ask at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to be a bare "if it is empty, populate it", which is a race
    /// with a shared mutable set on the other side of it. Two callers both saw
    /// it empty, both ran the registration, and each read the set while the
    /// other was still adding to it — so a command that is registered came back
    /// as one that is not.
    /// </para>
    /// <para>
    /// Nothing in the launcher itself hits that: one process handles one
    /// command on one thread. The tests do, because they run in parallel, and
    /// it cost two runs and a wrong diagnosis before the pattern was clear —
    /// intermittent, only in the full suite, never in isolation, which is what
    /// a race looks like from outside and also what a flaky harness looks like.
    /// </para>
    /// </remarks>
    private static readonly Lazy<IReadOnlySet<string>> Names = new(
        () =>
        {
            var app = new CommandApp(new TypeRegistrar(new ServiceCollection()));

            app.Configure(config => Configure(config, showFullExceptions: false));

            return KnownCommands;
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    internal static IReadOnlySet<string> CommandNames() => Names.Value;

    /// <summary>
    /// How long a command is given to notice it has been interrupted before it
    /// is ended for it.
    /// </summary>
    private static readonly TimeSpan GraceAfterInterrupt = TimeSpan.FromSeconds(2);

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
        // The screen-based launcher. The prompt-based one it replaced could
        // only ever show one thing at a time: choosing a project meant losing
        // the list, and reading what was wrong with a project meant losing the
        // project.
        services.AddSingleton<ILauncherTui, Loadout.Tui.Terminal.TerminalLauncher>();
        services.AddSingleton<ISetupWizard, SetupWizard>();
        services.AddSingleton<WorkspaceSavePrompt>();
        services.AddSingleton<StatuslineTargets>();
        services.AddSingleton<SessionScope>();
        services.AddSingleton<McpScopeResolver>();

        var registrar = new TypeRegistrar(services);

        // Registered as a factory so it can run the very parser it is
        // registered into: the launcher hands a command back to the same
        // CommandApp rather than carrying a second implementation of any of it.
        services.AddSingleton<ICommandCatalogue>(_ =>
            new CommandCatalogue(arguments => RunParserAsync(registrar, arguments)));

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
        app.Configure(config => Configure(
            config,
            showFullExceptions,
            json: launcherArgs.Contains("--json", StringComparer.Ordinal)));

        // Commands are handed a token that Ctrl+C actually cancels. Without one
        // supplied here they receive none at all, which is what the parameter
        // added by Spectre 0.55 arrived as: present on every command and always
        // uncancellable.
        //
        // The second press is deliberately left alone. A command that does not
        // yet watch its token would otherwise swallow Ctrl+C and look like it
        // had hung, which is worse than the abrupt exit it replaced — so the
        // first press asks, and the second is the operating system's again.
        using var stopping = new CancellationTokenSource();

        ConsoleCancelEventHandler? onCancel = null;

        onCancel = (_, e) =>
        {
            if (stopping.IsCancellationRequested)
            {
                return;
            }

            e.Cancel = true;
            stopping.Cancel();

            // And left promptly whether or not anything was listening.
            //
            // Cancelling alone was a regression the moment it shipped: no
            // command watches its token yet, so the first Ctrl+C did nothing a
            // person could see and it took two to get out of something one used
            // to end. The grace period is the difference between asking and
            // insisting — a command that honours the token exits inside it and
            // this never fires, and one that does not is ended anyway rather
            // than appearing to have hung.
            //
            // Two seconds because the work being interrupted may be a file
            // write that keeps its own backups: long enough for a command to
            // finish the one it is in, far shorter than somebody waits before
            // pressing the key again.
            _ = Task.Delay(GraceAfterInterrupt).ContinueWith(
                _ => Environment.Exit((int)ExitCode.Interrupted),
                TaskScheduler.Default);
        };

        Console.CancelKeyPress += onCancel;

        try
        {
            return await app.RunAsync(Rewrite(launcherArgs), stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Asked to stop and stopped. That is not a failure to report as
            // one, but it is not success either.
            return (int)ExitCode.Interrupted;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>
    /// Runs one command through a parser configured exactly like the real one.
    /// <para>
    /// A fresh CommandApp rather than the one already running: that one is
    /// mid-invocation when the launcher is on screen, and Spectre's parser is
    /// not built to be re-entered. Configuration is cheap and records nothing
    /// twice, because the catalogue ignores a path it already holds.
    /// </para>
    /// </summary>
    private static Task<int> RunParserAsync(TypeRegistrar registrar, string[] arguments)
    {
        var app = new CommandApp(registrar);

        app.Configure(config => Configure(config, showFullExceptions: false));

        return app.RunAsync(arguments);
    }

    /// <summary>What running the launcher with no arguments should do.</summary>
    internal enum LauncherEntry
    {
        /// <summary>Nothing to draw on. Say where the help is and stop.</summary>
        NoTerminal,

        /// <summary>Never configured, so the wizard rather than an empty list.</summary>
        Setup,

        /// <summary>The screen.</summary>
        Launcher,
    }

    /// <summary>
    /// Decides which of the three happens, and registers the commands on the
    /// way to the screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separated from the doing so it can be tested. What it replaced could not
    /// be: every branch of it either wrote to the console, ran a wizard or put a
    /// full-screen application up, and the one branch a test could reach was the
    /// one that returns immediately. That is how the launcher shipped opening
    /// with an empty command list — the registration below was missing and
    /// nothing could have noticed.
    /// </para>
    /// <para>
    /// Registration is what fills the launcher's command list. Nothing else on
    /// this path does it: the parser is configured only when there are arguments
    /// to parse, and running "loadout" with none goes straight to the screen. It
    /// is cheap — names are recorded, no service is resolved, and the catalogue
    /// ignores a path it already holds.
    /// </para>
    /// <para>
    /// Whether the machine is configured is asked for rather than passed,
    /// because asking touches the disk and the redirected case must not pay for
    /// an answer it will not use.
    /// </para>
    /// </remarks>
    internal static LauncherEntry PrepareInteractive(bool interactive, Func<bool> configured)
    {
        ArgumentNullException.ThrowIfNull(configured);

        // A redirected stream means a pipe, a script or a CI job, where spec
        // section 37 says no menu may appear.
        if (!interactive)
        {
            return LauncherEntry.NoTerminal;
        }

        _ = CommandNames();

        return configured() ? LauncherEntry.Launcher : LauncherEntry.Setup;
    }

    private static async Task<int> RunInteractiveAsync(ServiceProvider provider)
    {
        var wizard = provider.GetRequiredService<ISetupWizard>();

        var entry = PrepareInteractive(
            !Console.IsOutputRedirected && !Console.IsInputRedirected,
            wizard.IsConfigured);

        switch (entry)
        {
            case LauncherEntry.NoTerminal:
                // Printing usage is the honest alternative to hanging on a
                // prompt nobody can answer.
                Console.Error.WriteLine(
                    "loadout was run with no arguments and no interactive terminal. "
                    + "Run 'loadout --help' for the available commands.");

                return (int)ExitCode.InvalidArguments;

            case LauncherEntry.Setup:
                // A machine that has never been configured gets the wizard
                // rather than an empty project list, which would leave a new
                // user with nothing to do and no hint about what to do next
                // (spec section 61).
                return await wizard.RunAsync(new SetupRequest()).ConfigureAwait(false);

            default:
                return await provider.GetRequiredService<ILauncherTui>().RunAsync().ConfigureAwait(false);
        }
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

    private static void Configure(IConfigurator config, bool showFullExceptions, bool json = false)
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
            var message = Core.Security.SecretRedactor.Redact(
                showFullExceptions ? ex.ToString() : ex.Message);

            // A command that fails answers --json with a document, and a
            // command that could not be built has to answer the same way. This
            // handler takes the failures that happen before any command exists
            // — a missing required argument, an unparseable option — and
            // without this it wrote a sentence to stderr and left stdout empty,
            // so a script asking for JSON got nothing at all to read.
            if (json)
            {
                Console.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                    new { error = message, exitCode = (int)ExitCode.GeneralFailure },
                    Infrastructure.CommandOutput.JsonOptions));

                return (int)ExitCode.GeneralFailure;
            }

            Console.Error.WriteLine(message);

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
            backup.Describe(
                "Inspect and restore snapshots taken before mutating operations.",
                CommandCategory.Workspace,
                "undo revert restore mistake recover snapshot");
            backup.AddCommand<BackupListCommand>("list");
            backup.AddCommand<BackupRestoreCommand>("restore");
        });

        TopBranch(config, "task", task =>
        {
            task.Describe(
                "Record what is being worked on, and check it against the repository.",
                CommandCategory.Workspace,
                "tasks backlog todo status where were we open work");
            task.AddCommand<TaskListCommand>("list");
            task.AddCommand<TaskDeclareCommand>("declare");
            task.AddCommand<TaskRemoveCommand>("remove");
        });

        TopBranch(config, "spend", spend =>
        {
            spend.Describe(
                "See and refresh where spending stands against your thresholds.",
                CommandCategory.Integration,
                "spend budget threshold tokens cost warning");
            spend.AddCommand<SpendRefreshCommand>("refresh");
        });

        TopBranch(config, "checkpoint", checkpoint =>
        {
            checkpoint.Describe(
                "Mark where a project stands, under a name you can return to.",
                CommandCategory.Workspace,
                "checkpoint mark milestone save point before refactor return");
            checkpoint.AddCommand<CheckpointCreateCommand>("create");
            checkpoint.AddCommand<CheckpointListCommand>("list");
            checkpoint.AddCommand<CheckpointRestoreCommand>("restore");
            checkpoint.AddCommand<CheckpointRemoveCommand>("remove");
        });

        TopBranch(config, "memory", memory =>
        {
            memory.Describe(
                "Record and check the durable facts about a project.",
                CommandCategory.AgentConfiguration,
                "facts remember notes knowledge");
            memory.AddCommand<MemoryListCommand>("list");
            memory.AddCommand<MemoryFindCommand>("find");
            memory.AddCommand<MemoryReviewCommand>("review");
            memory.AddCommand<MemoryWriteCommand>("write");
            memory.AddCommand<MemoryAuditCommand>("audit");
            memory.AddCommand<MemoryReindexCommand>("reindex");
            memory.AddCommand<MemoryImportCommand>("import");
            memory.AddCommand<MemoryCompressCommand>("compress");
        });

        TopBranch(config, "rules", rules =>
        {
            rules.Describe(
                "Inspect the path-scoped instruction rules and what they cost.",
                CommandCategory.AgentConfiguration,
                "instructions budget cost tokens scoped");
            rules.AddCommand<RulesListCommand>("list");
            rules.AddCommand<RulesBudgetCommand>("budget");
            rules.AddCommand<RulesAuditCommand>("audit");
            rules.AddCommand<RulesSplitCommand>("split");
        });

        TopBranch(config, "config", cfg =>
        {
            cfg.Describe(
                "Read and write launcher settings.",
                CommandCategory.Administration,
                "settings preferences change option value");
            cfg.AddCommand<ConfigListCommand>("list");
            cfg.AddCommand<ConfigGetCommand>("get");
            cfg.AddCommand<ConfigSetCommand>("set");
            cfg.AddCommand<ConfigEditCommand>("edit");
        });

        TopBranch(config, "mcp", mcp =>
        {
            mcp.Describe(
                "Manage the MCP servers a project loads.",
                CommandCategory.AgentConfiguration,
                "servers tools model context protocol");
            mcp.AddCommand<McpListCommand>("list");
            mcp.AddCommand<McpAddCommand>("add");
            mcp.AddCommand<McpRemoveCommand>("remove");
            mcp.AddCommand<McpServeCommand>("serve");
        });

        Top<DriftCommand>(config, "drift");
        Top<CodeCommand>(config, "code");
        Top<CommandsCommand>(config, "commands");
        Top<SessionListCommand>(config, "sessions");
        Top<SessionRunningCommand>(config, "running");
        Top<ResumeCommand>(config, "resume");
        Top<UsageCommand>(config, "usage");
        Top<LaunchesCommand>(config, "launches");

        TopBranch(config, "instructions", instructions =>
        {
            instructions.Describe(
                "Inspect the specialists an agent is given, and why.",
                CommandCategory.AgentConfiguration,
                "specialists skills expertise why these instructions explain postgresql security");
            instructions.AddCommand<InstructionsListCommand>("list");
            instructions.AddCommand<InstructionsShowCommand>("show");
            instructions.AddCommand<InstructionsExplainCommand>("explain");
            instructions.AddCommand<InstructionsAuditCommand>("audit");
            instructions.AddCommand<InstructionsStatsCommand>("stats");
            instructions.AddCommand<InstructionsExportCommand>("export");
            instructions.AddCommand<InstructionsValidateCommand>("validate");
            instructions.AddCommand<InstructionsNewCommand>("new");
        });

        TopBranch(config, "docs", docs =>
        {
            docs.Describe(
                "Whether the documentation still describes the repository.",
                CommandCategory.Health,
                "docs documentation stale links broken references audit");
            docs.AddCommand<DocsAuditCommand>("audit");
            docs.AddCommand<DocsExportCommand>("export");
            docs.AddCommand<DocsCiCommand>("ci");
        });

        TopBranch(config, "telemetry", telemetry =>
        {
            telemetry.Describe(
                "Collect what launched agents report about their own usage.",
                CommandCategory.Administration,
                "otel opentelemetry metrics collect cost tokens");
            telemetry.AddCommand<TelemetryServeCommand>("serve");
            telemetry.AddCommand<TelemetryStatusCommand>("status");
        });

        TopBranch(config, "statusline", statusline =>
        {
            statusline.Describe(
                "Show the project, branch and context usage in the agent status line.",
                CommandCategory.Integration,
                "prompt status bar agent display");

            // Rendering is the default because that is the form Claude invokes:
            // the installed command is this binary plus one word.
            statusline.SetDefaultCommand<StatuslineRenderCommand>();

            statusline.AddCommand<StatuslineInstallCommand>("install");
            statusline.AddCommand<StatuslineUninstallCommand>("uninstall");
            statusline.AddCommand<StatuslineShowCommand>("show");
        });

        TopBranch(config, "repo", repo =>
        {
            repo.Describe(
                "Inspect repository compliance.",
                CommandCategory.Safety,
                "agent files tracked committed check compliance");
            repo.AddCommand<RepoCheckCommand>("check");
        });

        TopBranch(config, "profile", profile =>
        {
            profile.Describe(
                "Inspect context profiles.",
                CommandCategory.AgentConfiguration,
                "context which instructions load");
            profile.AddCommand<ProfileListCommand>("list");
        });

        // "list" is an alias for the most common listing, because typing
        // "project list" for the default view gets old (spec section 35).
        Top<ProjectListCommand>(config, "list");

        TopBranch(config, "project", project =>
        {
            project.Describe(
                "Register, list and inspect projects.",
                CommandCategory.Projects,
                "register add remove list repositories");
            project.AddCommand<ProjectListCommand>("list");
            project.AddCommand<ProjectNewCommand>("new");
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
            workspace.Describe(
                "Manage the central workspace clone.",
                CommandCategory.Workspace,
                "central sync push pull share machines computer laptop switch move new");
            workspace.AddCommand<WorkspaceStatusCommand>("status");
            workspace.AddCommand<WorkspaceSyncCommand>("sync");
            workspace.AddCommand<WorkspaceSaveCommand>("save");
            workspace.AddCommand<WorkspaceOpenCommand>("open");
        });

        TopBranch(config, "secret", secret =>
        {
            secret.Describe(
                "Store and check secrets in the platform credential store.",
                CommandCategory.Safety,
                "credential token password keychain");
            secret.AddCommand<SecretSetCommand>("set");
            secret.AddCommand<SecretTestCommand>("test");
            secret.AddCommand<SecretRemoveCommand>("remove");
        });
    }
}
