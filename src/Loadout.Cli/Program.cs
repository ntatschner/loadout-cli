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

        /// <summary>
        /// False for an old name kept working, which parses like the new one
        /// but is not offered anywhere: the palette and the reference list
        /// each command once, by the name to type now.
        /// </summary>
        private readonly bool _recorded;

        internal Branch(IConfigurator<CommandSettings> inner, string name, bool recorded = true)
        {
            _inner = inner;
            _name = name;
            _recorded = recorded;
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

            if (_recorded)
            {
                Catalogue.RecordBranch(_name, description, category, intent);
            }
        }

        /// <summary>
        /// The command the branch runs when named on its own. Recorded under
        /// the branch name, because that is what somebody types.
        /// </summary>
        internal void SetDefaultCommand<TCommand>()
            where TCommand : class, ICommandLimiter<CommandSettings>
        {
            if (_recorded)
            {
                Catalogue.Record(_name, typeof(TCommand));
            }

            _inner.SetDefaultCommand<TCommand>();
        }

        internal void AddCommand<TCommand>(string name)
            where TCommand : class, ICommandLimiter<CommandSettings>
        {
            if (_recorded)
            {
                Catalogue.Record($"{_name} {name}", typeof(TCommand));
            }

            _inner.AddCommand<TCommand>(name);
        }

        /// <summary>
        /// An old name for a command that has moved, kept so that what was
        /// already written down still runs.
        /// </summary>
        /// <param name="name">The old name, under this branch.</param>
        /// <param name="now">What to type now, for the help line.</param>
        internal void AddOldName<TCommand>(string name, string now)
            where TCommand : class, ICommandLimiter<CommandSettings>
        {
            _inner.AddCommand<TCommand>(name)
                .WithDescription($"The old name for 'loadout {now}', which still works.");
        }

        /// <summary>
        /// An old name for a whole branch that has moved. It is configured by
        /// the same action as the new one, so the two cannot drift apart, but
        /// records nothing.
        /// </summary>
        internal void AddOldName(string name, string now, Action<Branch> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            _inner.AddBranch<CommandSettings>(
                name,
                branch =>
                {
                    var old = new Branch(branch, $"{_name} {name}", recorded: false);

                    configure(old);

                    // After the shared action, which describes the new name.
                    branch.SetDescription($"The old name for 'loadout {now}', which still works.");
                });
        }

        /// <summary>
        /// A branch inside this one, recorded under its full path.
        /// </summary>
        /// <remarks>
        /// Two levels are not new - the catalogue and the documentation check
        /// have both allowed for three words since they were written - but
        /// nothing had needed one until a family of commands arrived that
        /// belongs under a family: 'team schedule add' is a schedule command
        /// of the team's, not a team command called schedule-add.
        /// </remarks>
        internal void AddBranch(string name, Action<Branch> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            _inner.AddBranch<CommandSettings>(
                name,
                branch => configure(new Branch(branch, $"{_name} {name}", _recorded)));
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
    /// What the person asked for, from the nearest place they said it.
    /// </summary>
    /// <remarks>
    /// The configuration is read only when neither the flag nor the
    /// environment answered, and only when somebody is looking at a terminal.
    /// Output going into a pipe has no colour and no cursor to control
    /// already, and this runs on every invocation - including the status line,
    /// which runs on every prompt of every session.
    /// </remarks>
    private static AccessibleMode Accessible(string[] arguments, IServiceProvider provider)
    {
        var asked = provider.GetRequiredService<IEnvironmentProvider>()
            .GetVariable(AccessibleMode.Variable);

        var typed = arguments.Any(a =>
            string.Equals(a, AccessibleMode.Flag, StringComparison.Ordinal)
            || a.StartsWith(AccessibleMode.Flag + "=", StringComparison.Ordinal));

        if (!typed && string.IsNullOrEmpty(asked) && Console.IsOutputRedirected)
        {
            return AccessibleMode.Off;
        }

        Models.Configuration.AccessibilitySettings? settings = null;

        if (!typed && string.IsNullOrEmpty(asked))
        {
            // Blocking on one small local file, in a short-lived process with
            // no synchronisation context to deadlock against, for the same
            // reason the agent registry does it: the container has no
            // asynchronous resolution and this has to be settled before the
            // first thing is drawn.
            var loaded = provider.GetRequiredService<Loadout.Core.Configuration.IConfigurationService>()
                .LoadConfigAsync()
                .GetAwaiter()
                .GetResult();

            settings = loaded.Value?.Accessibility;
        }

        return AccessibleMode.Resolve(arguments, asked, settings);
    }

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

        // A daemon started in the background has no window to print into, so
        // everything this process says goes to its log instead. Settled before
        // the console everything writes through is made, or the dashboard's
        // address would be written into a console nobody can see.
        if (Loadout.Core.Teams.Daemon.DaemonLog.Asked(launcherArgs) is { } log)
        {
            // First, so the moment in which the terminal it came from can
            // still end it is as short as it can be made.
            Loadout.Platform.Unix.UnixSession.Leave();

            try
            {
                var writer = Loadout.Core.Teams.Daemon.DaemonLog.Open(log);

                Console.SetOut(writer);
                Console.SetError(writer);

                // Plain, and wide. A file is read in an editor as often as it
                // is followed, and colour codes are noise there; and the
                // dashboard's address, token and all, broken across two lines
                // by a width nobody chose is an address nobody can click.
                AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
                {
                    Out = new AnsiConsoleOutput(writer),
                    Ansi = AnsiSupport.No,
                    ColorSystem = ColorSystemSupport.NoColors,
                    Interactive = InteractionSupport.No,
                });

                AnsiConsole.Console.Profile.Width = 1000;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nothing to say it with: the terminal that started this is
                // watching for a daemon that never wrote a note, and says so.
                return (int)ExitCode.ConfigurationInvalid;
            }
        }

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

        // Wrapped before anything holds it, so a profile that stops redraws
        // later cannot also stop the questions.
        AnsiConsole.Console = new AnsweringConsole(AnsiConsole.Console, new AnsweringConsole.TerminalKeys());

        services.AddSingleton(AnsiConsole.Console);
        services.AddSingleton(new PassthroughArguments(passthrough));
        services.AddSingleton<IProjectOnboarding, ProjectOnboarding>();
        services.AddSingleton<TeamQueueDriver>();
        // The screen-based launcher. The prompt-based one it replaced could
        // only ever show one thing at a time: choosing a project meant losing
        // the list, and reading what was wrong with a project meant losing the
        // project.
        services.AddSingleton<ILauncherTui, Loadout.Tui.Terminal.TerminalLauncher>();
        services.AddSingleton<ISetupWizard, SetupWizard>();
        services.AddSingleton<TextLauncher>();
        services.AddSingleton<WorkspaceSavePrompt>();
        services.AddSingleton<StatuslineTargets>();
        services.AddSingleton<SessionScope>();
        services.AddSingleton<McpScopeResolver>();

        var registrar = new TypeRegistrar(services);

        // Registered as a factory so it can run the very parser it is
        // registered into: the launcher hands a command back to the same
        // CommandApp rather than carrying a second implementation of any of it.
        services.AddSingleton<ICommandCatalogue>(_ =>
            new CommandCatalogue((arguments, ct) => RunParserAsync(registrar, arguments, ct)));

        // Registered rather than resolved and handed round, because the
        // interactive launcher builds from these same services and a prompt in
        // there needs the profile as much as a command does.
        services.AddSingleton(sp => Accessible(launcherArgs, sp));
        services.AddSingleton(sp => new ReadingProfile(
            sp.GetRequiredService<AccessibleMode>() is { IsOn: true } on ? on.Profile : null));

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

        // How this person asked to be shown things, settled before the first
        // thing is drawn. One call reaches every table, tree and spinner in
        // the application, because it changes the console they all share.
        var accessible = provider.GetRequiredService<AccessibleMode>();

        accessible.Apply(AnsiConsole.Console, Environment.GetEnvironmentVariable("NO_COLOR"));

        if (accessible.IsOn
            && !launcherArgs.Contains("--json", StringComparer.Ordinal)
            && !launcherArgs.Contains("--quiet", StringComparer.Ordinal)
            && !launcherArgs.Contains("-q", StringComparer.Ordinal))
        {
            // Said once, first, so somebody can tell the setting took. A
            // profile that is on and invisible is indistinguishable from one
            // that was ignored.
            // Written straight out rather than through the console, which
            // would wrap it at the terminal width and hand a screen reader
            // half a sentence at a time.
            Console.Out.WriteLine(accessible.Line);
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
    private static Task<int> RunParserAsync(TypeRegistrar registrar, string[] arguments, CancellationToken ct)
    {
        var app = new CommandApp(registrar);

        app.Configure(config => Configure(config, showFullExceptions: false));

        return app.RunAsync(arguments, ct);
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
                // prompt nobody can answer. And it succeeds, because nothing
                // failed: a package validator runs exactly this, and reading
                // a non-zero code as a broken install held every winget
                // release for a person to check by hand.
                Console.Error.WriteLine(
                    "loadout was run with no arguments and no interactive terminal. "
                    + "Run 'loadout --help' for the available commands.");

                return (int)ExitCode.Success;

            case LauncherEntry.Setup:
                // A machine that has never been configured gets the wizard
                // rather than an empty project list, which would leave a new
                // user with nothing to do and no hint about what to do next
                // (spec section 61).
                return await wizard.RunAsync(new SetupRequest()).ConfigureAwait(false);

            default:
                // A full-screen launcher cannot be announced by anything: no
                // terminal toolkit has a screen-reader provider on Windows or
                // macOS. Somebody whose profile says so gets the same commands
                // as a numbered menu instead, rather than a screen that is
                // keyboard-operable and silent.
                var text = provider.GetRequiredService<TextLauncher>();

                return text.IsWanted
                    ? await text.RunAsync().ConfigureAwait(false)
                    : await provider.GetRequiredService<ILauncherTui>().RunAsync().ConfigureAwait(false);
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
        config.SetHelpProvider(new BranchHelp(config.Settings));

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
        Top<StartCommand>(config, "start");
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
        Top<EventsCommand>(config, "events");

        TopBranch(config, "backup", backup =>
        {
            backup.Describe(
                "Inspect and restore snapshots taken before mutating operations.",
                CommandCategory.Workspace,
                "undo revert restore mistake recover snapshot");
            backup.AddCommand<BackupListCommand>("list");
            backup.AddCommand<BackupRestoreCommand>("restore");
        });

        TopBranch(config, "share", share =>
        {
            share.Describe(
                "Find guidance that belongs to everybody, and move it deliberately.",
                CommandCategory.Workspace,
                "share promote team worthy global guidance");
            share.AddCommand<ShareCandidatesCommand>("candidates");
            share.AddCommand<SharePromoteCommand>("promote");
        });

        TopBranch(config, "pack", pack =>
        {
            pack.Describe(
                "Specialist packs fetched from a Git remote, approved per machine.",
                CommandCategory.AgentConfiguration,
                "packs house standards shared specialists trust approve");
            pack.AddCommand<PackListCommand>("list");
            pack.AddCommand<PackAddCommand>("add");
            pack.AddCommand<PackApproveCommand>("approve");
            pack.AddCommand<PackUpdateCommand>("update");
            pack.AddCommand<PackRemoveCommand>("remove");
        });

        TopBranch(config, "task", task =>
        {
            task.Describe(
                "Record what is being worked on, and check it against the repository.",
                CommandCategory.Workspace,
                "tasks backlog todo status where were we open work");
            task.AddCommand<TaskListCommand>("list");
            task.AddCommand<TaskShowCommand>("show");
            task.AddCommand<TaskDeclareCommand>("declare");
            task.AddCommand<TaskProposeDoneWhenCommand>("propose-done-when");
            task.AddCommand<TaskShortenCommand>("shorten");
            task.AddCommand<TaskMoveCommand>("move");
            task.AddCommand<TaskRemoveCommand>("remove");
            task.AddCommand<TaskApplyCommand>("apply");
        });

        TopBranch(config, "idea", idea =>
        {
            idea.Describe(
                "Drop ideas in and have an agent flesh them out into a plan, one round at a time.",
                CommandCategory.Workspace,
                "ideas brainstorm someday flesh out plan design new project questions");
            idea.AddCommand<IdeaAddCommand>("add");
            idea.AddCommand<IdeaListCommand>("list");
            idea.AddCommand<IdeaShowCommand>("show");
            idea.AddCommand<IdeaRefineCommand>("refine");
            idea.AddCommand<IdeaAnswerCommand>("answer");
            idea.AddCommand<IdeaChooseCommand>("choose");
            idea.AddCommand<IdeaKeepCommand>("keep");
            idea.AddCommand<IdeaDropCommand>("drop");
            idea.AddCommand<IdeaImproveCommand>("improve");
            idea.AddCommand<IdeaAcceptCommand>("accept");
            idea.AddCommand<IdeaDismissCommand>("dismiss");
            idea.AddCommand<IdeaMoveCommand>("move");
            idea.AddCommand<IdeaRemoveCommand>("remove");
            idea.AddCommand<IdeaApplyCommand>("apply");
            idea.AddBranch("dump", dump =>
            {
                dump.SetDescription("Notes kept elsewhere, dropped in whole and split by an agent into ideas and tasks.");
                dump.AddCommand<IdeaDumpAddCommand>("add");
                dump.AddCommand<IdeaDumpListCommand>("list");
                dump.AddCommand<IdeaDumpShowCommand>("show");
                dump.AddCommand<IdeaDumpSplitCommand>("split");
                dump.AddCommand<IdeaDumpApplyCommand>("apply");
            });
        });

        TopBranch(config, "tools", tools =>
        {
            tools.Describe(
                "The machine's catalogue of tools shared by every team: search it, submit to it, and decide what may run.",
                CommandCategory.Start,
                "tools catalogue registry shared reusable global tool creator refiner");
            tools.AddCommand<ToolSearchCommand>("search");
            tools.AddCommand<ToolShowCommand>("show");
            tools.AddCommand<ToolSubmitCommand>("submit");
            tools.AddCommand<ToolUsedCommand>("used");
            tools.AddCommand<ToolAuditCommand>("audit");
            tools.AddCommand<ToolHealthCommand>("health");
            tools.AddCommand<ToolVerifyCommand>("verify");
            tools.AddCommand<ToolPromoteCommand>("promote");
            tools.AddCommand<ToolDeprecateCommand>("deprecate");
            tools.AddCommand<ToolRetireCommand>("retire");
            tools.AddCommand<ToolTrustCommand>("trust");
        });

        TopBranch(config, "team", team =>
        {
            team.Describe(
                "Run a team of agents against a project: a lead that briefs workers and reports to you.",
                CommandCategory.Start,
                "team teams agents orchestration swarm crew multi-agent autonomous");
            team.AddCommand<TeamListCommand>("list");
            team.AddCommand<TeamShowCommand>("show");
            team.AddCommand<TeamNewCommand>("new");
            team.AddCommand<TeamEditCommand>("edit");
            team.AddCommand<TeamRemoveCommand>("remove");
            team.AddCommand<TeamRunCommand>("run");
            team.AddCommand<TeamResumeCommand>("resume");

            // A branch whose default is the listing, so 'team runs' means what
            // it always did and 'team runs remove' is a runs command rather
            // than a team command called runs-remove.
            team.AddBranch("runs", runs =>
            {
                runs.SetDescription("What has run on this machine, and clearing out what you are done with.");
                runs.SetDefaultCommand<TeamRunsCommand>();
                runs.AddCommand<TeamRunsRemoveCommand>("remove");
                runs.AddCommand<TeamRunsPruneCommand>("prune");
                runs.AddCommand<TeamRunsRestoreCommand>("restore");
            });

            team.AddCommand<TeamRestoreCommand>("restore");

            // The same shape as 'runs': 'team bin' lists, and emptying it is a
            // bin command rather than a team command called bin-empty.
            team.AddBranch("bin", bin =>
            {
                bin.SetDescription("Removed runs and teams, kept for a while in case they are wanted back.");
                bin.SetDefaultCommand<TeamBinCommand>();
                bin.AddCommand<TeamBinEmptyCommand>("empty");
            });

            team.AddCommand<TeamStatusCommand>("status");
            team.AddCommand<TeamLogCommand>("log");
            team.AddCommand<TeamOutboxCommand>("outbox");
            team.AddCommand<TeamRemediesCommand>("remedies");
            team.AddCommand<TeamCapabilitiesCommand>("capabilities");

            team.AddBranch("remedy", remedy =>
            {
                remedy.SetDescription("What a team has worked out how to fix, and whether it may do it again on its own.");
                remedy.AddCommand<TeamRemedyShowCommand>("show");
                remedy.AddCommand<TeamRemedyTrustCommand>("trust");
                remedy.AddCommand<TeamRemedyRequestsCommand>("requests");
            });
            team.AddOldName<TeamDashboardCommand>("dashboard", "dashboard");

            team.AddBranch("office", office =>
            {
                office.SetDescription("The art the dashboard's office is drawn with.");
                office.AddCommand<TeamOfficeCheckCommand>("check");
            });

            // Where these lived before the daemon came to serve more than
            // teams. Scheduled tasks, hooks, login entries and printed
            // instructions all say 'team daemon', so the old names still run.
            team.AddOldName("daemon", "daemon", Daemon);

            team.AddBranch("schedule", schedule =>
            {
                schedule.AddCommand<TeamScheduleAddCommand>("add");
                schedule.AddCommand<TeamScheduleListCommand>("list");
                schedule.AddCommand<TeamScheduleRemoveCommand>("remove");
            });

            team.AddBranch("queue", queue =>
            {
                queue.SetDescription("A team's queue of fully specified tasks, worked through one run at a time.");
                queue.AddCommand<TeamQueueAddCommand>("add");
                queue.AddCommand<TeamQueueListCommand>("list");
                queue.AddCommand<TeamQueueMoveCommand>("move");
                queue.AddCommand<TeamQueueRemoveCommand>("remove");
                queue.AddCommand<TeamQueuePlanCommand>("plan");
                queue.AddCommand<TeamQueueSettingsCommand>("settings");
                queue.AddCommand<TeamQueueRunNextCommand>("run-next");
                queue.AddCommand<TeamQueueWindowCommand>("window");
            });

            team.AddCommand<TeamGateCommand>("gate");
            team.AddCommand<TeamMessageCommand>("message");
            team.AddCommand<TeamHaltCommand>("halt");
            team.AddCommand<TeamBudgetCommand>("budget");
            team.AddCommand<TeamNameCommand>("name");
            team.AddCommand<TeamPrCommand>("pr");
            team.AddCommand<TeamSayCommand>("say");

            team.AddBranch("attach", attach =>
            {
                attach.AddCommand<TeamAttachSetCommand>("set");
                attach.AddCommand<TeamAttachClearCommand>("clear");
                attach.AddCommand<TeamAttachShowCommand>("show");
            });

            team.AddOldName("autostart", "daemon autostart", Autostart);

            team.AddBranch("notify", notify =>
            {
                notify.AddCommand<TeamNotifySetCommand>("set");
                notify.AddCommand<TeamNotifyTestCommand>("test");
                notify.AddCommand<TeamNotifyShowCommand>("show");
                notify.AddCommand<TeamNotifyClearCommand>("clear");
            });

            team.AddBranch("webhook", webhook =>
            {
                webhook.AddCommand<TeamWebhookEnableCommand>("enable");
                webhook.AddCommand<TeamWebhookDisableCommand>("disable");
                webhook.AddCommand<TeamWebhookShowCommand>("show");
            });
        });

        // Top level rather than under 'team', because what they serve is not
        // only about teams: memory, ideas and settings will be served from the
        // same page.
        //
        // A command for now, and a group once it has a second command to hold:
        // a branch with only a default hides that command's options from its
        // help.
        TopBranch(config, "dashboard", Dashboard);

        TopBranch(config, "daemon", Daemon);

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
            memory.AddCommand<MemoryShowCommand>("show");
            memory.AddCommand<MemoryReplaceCommand>("replace");
            memory.AddCommand<MemoryAuditCommand>("audit");
            memory.AddCommand<MemoryReindexCommand>("reindex");
            memory.AddCommand<MemoryImportCommand>("import");
            memory.AddCommand<MemoryCompressCommand>("compress");
            memory.AddCommand<MemoryDeleteCommand>("delete");
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

        TopBranch(config, "session", session =>
        {
            session.Describe(
                "List, rename and end the sessions the daemon hosts for Studio.",
                CommandCategory.Start,
                "studio hosted sessions terminals tabs");
            session.AddCommand<HostedSessionListCommand>("list");
            session.AddCommand<HostedSessionRenameCommand>("rename");
            session.AddCommand<HostedSessionEndCommand>("end");
        });
        Top<ResumeCommand>(config, "resume");
        Top<FindCommand>(config, "find");
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
            instructions.AddCommand<InstructionsProbeCommand>("probe");
            instructions.AddCommand<InstructionsExportCommand>("export");
            instructions.AddCommand<InstructionsValidateCommand>("validate");
            instructions.AddCommand<InstructionsNewCommand>("new");
        });

        TopBranch(config, "style", style =>
        {
            style.Describe(
                "Your coding styles, and which of them a project uses.",
                CommandCategory.AgentConfiguration,
                "coding style conventions house style personal named codebase patterns");
            style.AddCommand<StyleListCommand>("list");
            style.AddCommand<StyleShowCommand>("show");
            style.AddCommand<StyleUseCommand>("use");
        });

        // Top level rather than under 'team': a rule can start a built-in
        // command as well as a team, and it watches every project at once.
        TopBranch(config, "trigger", trigger =>
        {
            trigger.Describe(
                "Rules that start something when something happens. Unfinished, and off unless LOADOUT_TRIGGERS is set.",
                CommandCategory.Integration,
                "trigger event rule when happens automation webhook run finished");
            trigger.AddCommand<TriggerListCommand>("list");
            trigger.AddCommand<TriggerAddCommand>("add");
            trigger.AddCommand<TriggerRemoveCommand>("remove");
            trigger.AddCommand<TriggerResumeCommand>("resume");
            trigger.AddCommand<TriggerJournalCommand>("journal");
            trigger.AddCommand<TriggerTestCommand>("test");
            trigger.AddCommand<TriggerSuggestedCommand>("suggested");
            trigger.AddCommand<TriggerAcceptCommand>("accept");
            trigger.AddCommand<TriggerPruneCommand>("prune");
        });

        TopBranch(config, "voice", voice =>
        {
            voice.Describe(
                "How you write, learned from your own writing and checked against it.",
                CommandCategory.AgentConfiguration,
                "voice writing style prose commit messages docs hand-written marks");
            voice.AddCommand<VoiceShowCommand>("show");
            voice.AddCommand<VoiceCheckCommand>("check");
            voice.AddCommand<VoiceMarkCommand>("mark");
            voice.AddCommand<VoiceLearnCommand>("learn");
            voice.AddCommand<VoiceRunsCommand>("runs");
            voice.AddCommand<VoiceRenderCommand>("render");

            // A branch of its own: 'voice guide draft' drafts a guide, which is
            // a different thing from learning a voice with one.
            voice.AddBranch("guide", guide =>
            {
                guide.SetDescription("The guide definitions voices are learned against: list, show, or draft a new one.");
                guide.AddCommand<VoiceGuideListCommand>("list");
                guide.AddCommand<VoiceGuideShowCommand>("show");
                guide.AddCommand<VoiceGuideDraftCommand>("draft");
            });

            // Settings, not learning: the time guide's rules are typed in.
            voice.AddBranch("time", time =>
            {
                time.SetDescription("How dates are written and estimates given on each surface: show, set or unset.");
                time.AddCommand<VoiceTimeShowCommand>("show");
                time.AddCommand<VoiceTimeSetCommand>("set");
                time.AddCommand<VoiceTimeUnsetCommand>("unset");
            });
        });

        TopBranch(config, "docs", docs =>
        {
            docs.Describe(
                "Whether the documentation still describes the repository.",
                CommandCategory.Health,
                "docs documentation stale links broken references audit");
            docs.AddCommand<DocsAuditCommand>("audit");
            docs.AddCommand<DocsExportCommand>("export");
            docs.AddCommand<DocsFindCommand>("find");
            docs.AddCommand<DocsRefreshCommand>("refresh");
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

        TopBranch(config, "hook", hook =>
        {
            hook.SetDescription("Commands the agents' own hooks run. Not for a person.");

            // Named for the event that runs it, as the agent's settings spell it.
            hook.AddCommand<HookSessionStartCommand>("session-start");
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
            project.AddCommand<ProjectContextCommand>("context");
            project.AddCommand<ProjectOnboardCommand>("onboard");
            project.AddCommand<ProjectProposalCommand>("proposal");
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

    /// <summary>
    /// The daemon's commands, under 'daemon' and under its old name.
    /// </summary>
    /// <remarks>
    /// A branch whose default starts it, so 'daemon' on its own starts it and
    /// the controls are daemon commands rather than commands called
    /// daemon-stop.
    /// </remarks>
    /// <summary>The page, and signing browsers out of it.</summary>
    private static void Dashboard(Branch dashboard)
    {
        // The command first, for the reason Daemon gives.
        dashboard.SetDefaultCommand<TeamDashboardCommand>();
        dashboard.Describe(
            "Show what the team runs are doing on a page: the one a daemon is already serving, or a new one on this machine.",
            CommandCategory.Start,
            "dashboard web page browser watch runs live team");
        dashboard.AddCommand<DashboardSignOutCommand>("sign-out");
        dashboard.AddCommand<DashboardSignalCommand>("signal");
    }

    private static void Daemon(Branch daemon)
    {
        // The command first. The catalogue keeps whichever entry for a path
        // arrives first, and recorded as a bare branch 'daemon' lost the
        // command's word that it changes something: the suite that runs every
        // command ran it without --dry-run and started a real daemon.
        daemon.SetDefaultCommand<TeamDaemonCommand>();
        daemon.Describe(
            "Stay running: fire the scheduled team runs and serve the dashboard.",
            CommandCategory.Start,
            "daemon resident service schedules fire background");
        daemon.AddCommand<TeamDaemonStopCommand>("stop");
        daemon.AddCommand<TeamDaemonRestartCommand>("restart");
        daemon.AddCommand<TeamDaemonPauseCommand>("pause");
        daemon.AddCommand<TeamDaemonResumeCommand>("resume");

        // The word somebody reaches for after "pause" as often as
        // "resume", so it answers too rather than being an error.
        daemon.AddCommand<TeamDaemonResumeCommand>("continue");

        daemon.AddBranch("autostart", Autostart);
    }

    /// <summary>Starting the daemon at login.</summary>
    private static void Autostart(Branch autostart)
    {
        autostart.SetDescription("Whether the daemon starts when you log in.");
        autostart.AddCommand<TeamAutostartEnableCommand>("enable");
        autostart.AddCommand<TeamAutostartDisableCommand>("disable");
        autostart.AddCommand<TeamAutostartShowCommand>("show");
    }
}
