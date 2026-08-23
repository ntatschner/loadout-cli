using AgentWorkspace.Agents;
using AgentWorkspace.Core.Configuration;
using AgentWorkspace.Core.Git;
using AgentWorkspace.Core.Policies;
using AgentWorkspace.Models.Policies;
using AgentWorkspace.Core.Projects;
using AgentWorkspace.Core.Workspace;
using AgentWorkspace.Models;
using AgentWorkspace.Models.Configuration;
using AgentWorkspace.Models.Results;
using AgentWorkspace.Platform.Abstractions;
using Spectre.Console;

namespace AgentWorkspace.Tui;

/// <summary>First-run configuration (spec sections 61 to 63).</summary>
public interface ISetupWizard
{
    /// <summary>Whether the launcher has been configured on this machine.</summary>
    bool IsConfigured();

    /// <summary>
    /// Runs the wizard, asking only for what the request has not answered.
    /// Returns the exit code for the process.
    /// </summary>
    Task<int> RunAsync(SetupRequest request, CancellationToken ct = default);
}

/// <summary>
/// Walks a new user from nothing to a working launcher (spec sections 61 to 63).
/// <para>
/// This exists because the alternative is hand-authoring YAML, and spec section
/// 101 lists "a first-time user can install and configure it easily" as part of
/// the definition of done. The three choices in section 61 are offered as
/// equals: running without central storage is a legitimate way to use the tool,
/// not a degraded mode.
/// </para>
/// </summary>
public sealed class SetupWizard : ISetupWizard
{
    private readonly IAnsiConsole _console;
    private readonly IConfigurationService _configuration;
    private readonly IWorkspaceManager _workspace;
    private readonly IGitManager _git;
    private readonly IProjectService _projects;
    private readonly IAgentRegistry _agents;
    private readonly IPolicyService _policies;
    private readonly IMigrationService _migrations;
    private readonly ISecretProvider _secrets;
    private readonly IPlatformPaths _paths;
    private readonly IExecutableResolver _resolver;
    private readonly IProcessLauncher _processes;

    public SetupWizard(
        IAnsiConsole console,
        IConfigurationService configuration,
        IWorkspaceManager workspace,
        IGitManager git,
        IProjectService projects,
        IAgentRegistry agents,
        IPolicyService policies,
        IMigrationService migrations,
        ISecretProvider secrets,
        IPlatformPaths paths,
        IExecutableResolver resolver,
        IProcessLauncher processes)
    {
        _console = console;
        _configuration = configuration;
        _workspace = workspace;
        _git = git;
        _projects = projects;
        _agents = agents;
        _policies = policies;
        _migrations = migrations;
        _secrets = secrets;
        _paths = paths;
        _resolver = resolver;
        _processes = processes;
    }

    /// <inheritdoc />
    public bool IsConfigured() => File.Exists(_paths.Paths.ConfigFile);

    /// <inheritdoc />
    public async Task<int> RunAsync(SetupRequest request, CancellationToken ct = default)
    {
        // Refuse before doing anything rather than halfway through: a setup that
        // fails after creating a repository leaves the user worse off than one
        // that never started.
        if (request.MissingAnswer() is { } missing)
        {
            _console.MarkupLine($"[red]{Markup.Escape(missing)}[/]");
            return (int)ExitCode.InvalidArguments;
        }

        _console.Write(new Rule("[bold]Welcome to the AI Workspace Launcher[/]").LeftJustified());
        _console.WriteLine();

        // Nothing else works without git, so it is checked before the user is
        // asked to make any decisions.
        var gitVersion = await _git.GetVersionAsync(ct).ConfigureAwait(false);

        if (gitVersion.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(gitVersion.Error!)}[/]");
            _console.MarkupLine("[dim]Install Git, then run: agentctl setup[/]");

            return (int)ExitCode.ConfigurationInvalid;
        }

        _console.MarkupLine($"[green]+[/] {Markup.Escape(gitVersion.Value!)}");
        _console.WriteLine();

        var mode = request.Mode;

        if (mode == WorkspaceMode.Ask)
        {
            const string Existing = "Configure an existing central workspace";
            const string Create = "Create a new central workspace";

            var choice = _console.Prompt(
                new SelectionPrompt<string>()
                    .Title("No central workspace is configured. What would you like to do?")
                    .AddChoices(Existing, Create, "Run without central storage"));

            mode = choice switch
            {
                Existing => WorkspaceMode.UseExisting,
                Create => WorkspaceMode.CreateNew,
                _ => WorkspaceMode.LocalOnly,
            };
        }

        var config = new LauncherConfig();

        var outcome = mode switch
        {
            WorkspaceMode.UseExisting =>
                await ConfigureExistingAsync(config, request, ct).ConfigureAwait(false),

            WorkspaceMode.CreateNew =>
                await CreateNewAsync(config, request, ct).ConfigureAwait(false),

            _ => await ConfigureLocalOnlyAsync(config, ct).ConfigureAwait(false),
        };

        if (outcome.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(outcome.Error!)}[/]");
            return (int)outcome.ExitCode;
        }

        await ChooseSecretProviderAsync(config, ct).ConfigureAwait(false);

        var save = await _configuration.SaveConfigAsync(config, ct).ConfigureAwait(false);
        if (save.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(save.Error!)}[/]");
            return (int)save.ExitCode;
        }

        await ConfigureDiscoveryRootsAsync(request, ct).ConfigureAwait(false);
        await ShowDetectedAgentsAsync(ct).ConfigureAwait(false);

        // Migration happens inside this step, and it has to run before the
        // global excludes are installed. Installing them first would make the
        // very files migration exists to move become ignored, so setup would
        // protect the repository and then report nothing to migrate. Clean up
        // first, then stop it happening again.
        await OfferDiscoveredProjectsAsync(request, ct).ConfigureAwait(false);
        await OfferGlobalProtectionAsync(request, ct).ConfigureAwait(false);

        _console.WriteLine();
        _console.MarkupLine("[green]Setup complete.[/]");
        _console.MarkupLine("[dim]Run[/] agentctl [dim]to open the launcher, or[/] agentctl doctor "
            + "[dim]to check everything.[/]");

        return (int)ExitCode.Success;
    }

    /// <summary>Spec section 62: point at a workspace somebody has already made.</summary>
    private async Task<OperationResult> ConfigureExistingAsync(
        LauncherConfig config,
        SetupRequest request,
        CancellationToken ct)
    {
        config.Workspace.Remote = request.Remote ?? _console.Prompt(
            new TextPrompt<string>("Central workspace Git URL:")
                .Validate(value => string.IsNullOrWhiteSpace(value)
                    ? ValidationResult.Error("A URL is required.")
                    : ValidationResult.Success()));

        config.Workspace.Remote = config.Workspace.Remote.Trim();

        config.Workspace.Branch = request.Branch
            ?? (request.Interactive
                ? _console.Prompt(new TextPrompt<string>("Branch:").DefaultValue("main"))
                : "main");

        _console.WriteLine();
        _console.MarkupLine("[dim]Cloning...[/]");

        // The clone is the credential test. Doing it now rather than at first
        // launch means an authentication problem surfaces while the user is
        // still thinking about setup (spec section 62).
        var cloneResult = await _workspace.CloneAsync(config, ct).ConfigureAwait(false);

        if (cloneResult.Failed)
        {
            return OperationResult.Fail(
                $"The workspace could not be cloned: {cloneResult.Error} "
                + "Check the URL and your Git credentials, then run: agentctl setup");
        }

        var manifest = await _workspace.ReadManifestAsync(ct).ConfigureAwait(false);

        if (manifest.Succeeded
            && manifest.Value!.WorkspaceSchema > WorkspaceManager.SupportedSchemaVersion)
        {
            return OperationResult.Fail(
                $"That workspace uses schema {manifest.Value.WorkspaceSchema}, which is newer than "
                + $"this launcher supports ({WorkspaceManager.SupportedSchemaVersion}). Update agentctl.");
        }

        var registry = await _workspace.ReadRegistryAsync(ct).ConfigureAwait(false);

        _console.MarkupLine(
            $"[green]+[/] Workspace cloned  [dim]{registry.Value?.Projects.Count ?? 0} project(s)[/]");

        return OperationResult.Ok();
    }

    /// <summary>Spec section 63: create the standard structure from nothing.</summary>
    private async Task<OperationResult> CreateNewAsync(
        LauncherConfig config,
        SetupRequest request,
        CancellationToken ct)
    {
        var name = request.Name
            ?? (request.Interactive
                ? _console.Prompt(
                    new TextPrompt<string>("Workspace name:").DefaultValue("agent-workspaces"))
                : "agent-workspaces");

        config.Workspace.Branch = request.Branch
            ?? (request.Interactive
                ? _console.Prompt(new TextPrompt<string>("Default branch:").DefaultValue("main"))
                : "main");

        var initResult = await _workspace.InitialiseStructureAsync(name, ct).ConfigureAwait(false);
        if (initResult.Failed)
        {
            return initResult;
        }

        _console.MarkupLine($"[green]+[/] Created  [dim]{Markup.Escape(_workspace.LocalPath)}[/]");

        // The identity has to exist before the first commit, not after it, and
        // its absence stops setup rather than producing a commit that fails.
        var identity = await EnsureGitIdentityAsync(request, ct).ConfigureAwait(false);

        if (identity.Failed)
        {
            return identity;
        }

        // The structure has to become a real repository here. Left as a plain
        // directory it would look created while sync had nothing to fetch and
        // save-on-exit had nothing to commit into.
        var repository = await _workspace
            .InitialiseRepositoryAsync(config.Workspace.Branch, ct)
            .ConfigureAwait(false);

        if (repository.Failed)
        {
            return repository;
        }

        _console.MarkupLine("[green]+[/] Git repository initialised with the first commit");

        return await ConfigureRemoteAsync(config, request, name, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gives the new workspace somewhere to live.
    /// <para>
    /// Offers to create the repository through the GitHub CLI when it is
    /// installed and signed in, because the alternative is telling somebody to
    /// go and make an empty repository by hand and come back with a URL. The
    /// launcher stays provider-agnostic either way (spec section 10): this is a
    /// convenience for one common host, not a dependency on it, and the
    /// second option accepts any Git URL at all.
    /// </para>
    /// </summary>
    private async Task<OperationResult> ConfigureRemoteAsync(
        LauncherConfig config,
        SetupRequest request,
        string name,
        CancellationToken ct)
    {
        const string ViaGitHub = "Create a private repository on GitHub";
        const string ViaUrl = "Use a repository I have already created";
        const string Later = "Stay local for now";

        var gh = await FindAuthenticatedGitHubCliAsync(ct).ConfigureAwait(false);
        var host = request.Host;

        if (host == WorkspaceHost.GitHub && gh is null)
        {
            // Asked for explicitly, so this is an error rather than a quiet
            // fallback to a host the caller did not choose.
            return OperationResult.Fail(
                "GitHub was requested but the GitHub CLI is not installed and signed in. "
                + "Run: gh auth login");
        }

        if (host == WorkspaceHost.Ask)
        {
            var choices = new List<string>();

            if (gh is not null)
            {
                choices.Add(ViaGitHub);
            }

            choices.Add(ViaUrl);
            choices.Add(Later);

            var choice = _console.Prompt(
                new SelectionPrompt<string>()
                    .Title("Where should this workspace live?")
                    .AddChoices(choices));

            host = choice switch
            {
                ViaGitHub => WorkspaceHost.GitHub,
                ViaUrl => WorkspaceHost.Url,
                _ => WorkspaceHost.None,
            };
        }

        if (host == WorkspaceHost.None)
        {
            _console.MarkupLine(
                "[dim]Staying local. Add a remote later with:[/] "
                + "agentctl config set workspace-remote <url>");

            return OperationResult.Ok();
        }

        if (host == WorkspaceHost.Url)
        {
            config.Workspace.Remote = (request.Remote ?? _console.Prompt(
                new TextPrompt<string>("Git remote URL:")
                    .Validate(value => string.IsNullOrWhiteSpace(value)
                        ? ValidationResult.Error("A URL is required.")
                        : ValidationResult.Success()))).Trim();

            return await PushAsync(config, ct).ConfigureAwait(false);
        }

        var repositoryName = request.Interactive
            ? _console.Prompt(new TextPrompt<string>("Repository name:").DefaultValue(name))
            : name;

        // Private, and not offered as a choice. A workspace holds project
        // context, decisions and handoffs; spec section 10 calls it a private
        // repository, and making it public is an irreversible disclosure that
        // should not be one keystroke away during setup.
        _console.MarkupLine("[dim]It will be created private.[/]");

        var created = await _processes.RunAsync(
            new ProcessRequest(
                gh!,
                [
                    "repo", "create", repositoryName,
                    "--private",
                    "--source", _workspace.LocalPath,
                    "--push",
                ],
                _workspace.LocalPath),
            TimeSpan.FromMinutes(2),
            ct).ConfigureAwait(false);

        if (created.Failed || created.Value?.Succeeded != true)
        {
            var detail = created.Value?.StandardError.Trim() ?? created.Error;

            return OperationResult.Fail($"The GitHub repository could not be created: {detail}");
        }

        var remote = await _git
            .GetConfigValueAsync("remote.origin.url", _workspace.LocalPath, ct)
            .ConfigureAwait(false);

        config.Workspace.Remote = remote.Value ?? string.Empty;

        _console.MarkupLine(
            $"[green]+[/] Created and pushed  [dim]{Markup.Escape(config.Workspace.Remote)}[/]");

        return OperationResult.Ok();
    }

    private async Task<OperationResult> PushAsync(LauncherConfig config, CancellationToken ct)
    {
        var remoteResult = await _git
            .SetRemoteAsync(_workspace.LocalPath, "origin", config.Workspace.Remote, ct)
            .ConfigureAwait(false);

        if (remoteResult.Failed)
        {
            return remoteResult;
        }

        var pushResult = await _git
            .PushWithUpstreamAsync(_workspace.LocalPath, "origin", config.Workspace.Branch, ct)
            .ConfigureAwait(false);

        if (pushResult.Failed)
        {
            // The workspace exists and is committed locally, so nothing is
            // lost; only the push needs retrying. Failing setup outright here
            // would throw away everything it just built.
            _console.MarkupLine(
                $"[yellow]The workspace could not be pushed:[/] {Markup.Escape(pushResult.Error!)}");

            _console.MarkupLine("[dim]It is committed locally. Retry with:[/] agentctl workspace save");

            return OperationResult.Ok();
        }

        _console.MarkupLine("[green]+[/] Pushed");

        return OperationResult.Ok();
    }

    /// <summary>
    /// Finds the GitHub CLI only when it is also signed in. An installed but
    /// unauthenticated gh would offer a route that fails halfway through.
    /// </summary>
    private async Task<string?> FindAuthenticatedGitHubCliAsync(CancellationToken ct)
    {
        var gh = _resolver.Resolve("gh");

        if (gh is null)
        {
            return null;
        }

        var status = await _processes.RunAsync(
            new ProcessRequest(gh, ["auth", "status"]),
            TimeSpan.FromSeconds(20),
            ct).ConfigureAwait(false);

        return status.Succeeded && status.Value?.Succeeded == true ? gh : null;
    }

    /// <summary>Spec section 61's third option, offered as an equal.</summary>
    private async Task<OperationResult> ConfigureLocalOnlyAsync(
        LauncherConfig config,
        CancellationToken ct)
    {
        // The structure is still created: local-only mode uses the same layout,
        // which is what lets a user adopt a central workspace later by pushing
        // what they already have.
        var initResult = await _workspace.InitialiseStructureAsync("local", ct).ConfigureAwait(false);

        if (initResult.Failed)
        {
            return initResult;
        }

        _console.MarkupLine(
            $"[green]+[/] Local workspace created  [dim]{Markup.Escape(_workspace.LocalPath)}[/]");

        _console.MarkupLine(
            "[dim]Projects and context stay on this machine. Add a remote later to share them.[/]");

        return OperationResult.Ok();
    }

    /// <summary>
    /// Makes sure Git has a committer identity.
    /// <para>
    /// A fresh clone inherits none, and without one every workspace commit
    /// fails with "Author identity unknown" at the least convenient moment.
    /// Spec section 63 asks for this during setup for exactly that reason.
    /// </para>
    /// </summary>
    private async Task<OperationResult> EnsureGitIdentityAsync(
        SetupRequest request,
        CancellationToken ct)
    {
        // Global specifically. A plain read resolves through whatever repository
        // the process is standing in, so running setup from inside some other
        // project would find that project's identity and conclude, wrongly, that
        // the workspace had one.
        var name = await _git.GetGlobalConfigValueAsync("user.name", ct).ConfigureAwait(false);
        var email = await _git.GetGlobalConfigValueAsync("user.email", ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(name.Value) && !string.IsNullOrWhiteSpace(email.Value))
        {
            _console.MarkupLine($"[green]+[/] Git identity  [dim]{Markup.Escape(name.Value)}[/]");
            return OperationResult.Ok();
        }

        _console.MarkupLine("[yellow]No global Git identity is configured.[/]");

        if (!request.Interactive)
        {
            // Inventing one would put a fabricated author on every workspace
            // commit from here on, so this stops with the two commands needed.
            return OperationResult.Fail(
                """
                A Git identity is required before the workspace can be committed. Set one with:
                  git config --global user.name "Your Name"
                  git config --global user.email "you@example.com"
                """,
                ExitCode.ConfigurationInvalid);
        }

        if (!_console.Confirm("Set one now?", defaultValue: true))
        {
            return OperationResult.Fail(
                "The workspace cannot be committed without a Git identity. "
                + "Set one with git config --global user.name and rerun setup.",
                ExitCode.ConfigurationInvalid);
        }

        var newName = _console.Prompt(new TextPrompt<string>("Your name:"));
        var newEmail = _console.Prompt(new TextPrompt<string>("Your email:"));

        await _git.SetGlobalConfigValueAsync("user.name", newName.Trim(), ct).ConfigureAwait(false);
        await _git.SetGlobalConfigValueAsync("user.email", newEmail.Trim(), ct).ConfigureAwait(false);

        _console.MarkupLine("[green]+[/] Git identity set");

        return OperationResult.Ok();
    }

    private async Task ChooseSecretProviderAsync(LauncherConfig config, CancellationToken ct)
    {
        var availability = await _secrets.IsAvailableAsync(ct).ConfigureAwait(false);

        if (availability.Succeeded)
        {
            config.Secrets.Provider = "native";
            _console.MarkupLine($"[green]+[/] Secret provider  [dim]{Markup.Escape(_secrets.Name)}[/]");

            return;
        }

        // A headless Linux box has no Secret Service, which spec section 86
        // treats as normal rather than broken, so the environment provider is
        // offered instead of the setup simply failing.
        _console.MarkupLine(
            $"[yellow]The native secret store is unavailable:[/] {Markup.Escape(availability.Error!)}");

        config.Secrets.Provider = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("Which secret provider should be used?")
                .AddChoices("environment", "1password", "bitwarden", "vault", "native"));
    }

    private async Task ConfigureDiscoveryRootsAsync(SetupRequest request, CancellationToken ct)
    {
        var machineResult = await _configuration.LoadMachineAsync(ct).ConfigureAwait(false);

        if (machineResult.Failed)
        {
            return;
        }

        var machine = machineResult.Value!;

        if (machine.DiscoveryRoots.Count > 0)
        {
            _console.MarkupLine(
                $"[green]+[/] Discovery roots  [dim]{string.Join(", ", machine.DiscoveryRoots)}[/]");
        }
        else
        {
            // None of the conventional locations exist, so the launcher has to
            // ask rather than guess at somewhere it would then scan.
            _console.MarkupLine("[yellow]No conventional development roots were found.[/]");

            if (!request.Interactive)
            {
                // Nothing to scan is a limitation to report, not a reason to
                // invent a directory and start walking it.
                _console.MarkupLine(
                    "[dim]Set one later with:[/] agentctl config set discovery-roots <path>");

                return;
            }

            var root = _console.Prompt(
                new TextPrompt<string>("Where do you keep your repositories?")
                    .AllowEmpty());

            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                machine.DiscoveryRoots.Add(root.Trim());
                machine.DefaultCloneRoot = root.Trim();

                await _configuration.SaveMachineAsync(machine, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Offers the global Git excludes of spec section 50.</summary>
    private async Task OfferGlobalProtectionAsync(SetupRequest request, CancellationToken ct)
    {
        var existing = await _git.GetConfigValueAsync("core.excludesFile", null, ct)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(existing.Value))
        {
            _console.MarkupLine($"[green]+[/] Global Git excludes  [dim]{Markup.Escape(existing.Value)}[/]");
            return;
        }

        _console.WriteLine();

        var install = request.InstallGlobalExcludes
            ?? (request.Interactive && _console.Confirm(
                "Configure global Git excludes so agent files never enter a repository?",
                defaultValue: true));

        if (!install)
        {
            return;
        }

        var result = await _policies.InstallGlobalExcludesAsync(ct).ConfigureAwait(false);

        _console.MarkupLine(result.Succeeded
            ? $"[green]+[/] Global Git excludes  [dim]{Markup.Escape(result.Value!)}[/]"
            : $"[yellow]![/] {Markup.Escape(result.Error!)}");
    }

    private async Task ShowDetectedAgentsAsync(CancellationToken ct)
    {
        _console.WriteLine();

        var agents = await _agents.DetectAllAsync(ct).ConfigureAwait(false);

        foreach (var agent in agents)
        {
            _console.MarkupLine(agent.IsInstalled
                ? $"[green]+[/] {Markup.Escape(agent.DisplayName)}  "
                  + $"[dim]{Markup.Escape(agent.Version ?? string.Empty)}[/]"
                : $"[yellow]-[/] {Markup.Escape(agent.DisplayName)}  [dim]not installed[/]");
        }

        if (agents.All(a => !a.IsInstalled))
        {
            _console.MarkupLine(
                "[yellow]No agents were found. Install Claude Code or Codex, then run:[/] agentctl doctor");
        }
    }

    /// <summary>Spec section 96: offer to register what is already on the machine.</summary>
    private async Task OfferDiscoveredProjectsAsync(SetupRequest request, CancellationToken ct)
    {
        var discovered = await _projects.DiscoverAsync(ct).ConfigureAwait(false);

        if (discovered.Failed || discovered.Value!.Count == 0)
        {
            return;
        }

        var unregistered = discovered.Value.Where(r => !r.IsRegistered).ToList();

        if (unregistered.Count == 0)
        {
            return;
        }

        _console.WriteLine();
        _console.MarkupLine($"[bold]{unregistered.Count} repositories found[/]");

        IReadOnlyList<string> chosen;

        if (request.RegisterDiscovered)
        {
            chosen = unregistered.Select(r => r.Path).ToList();
        }
        else if (!request.Interactive)
        {
            // Registering somebody's whole disk because nobody could be asked
            // would be a poor default, so it stays opt-in.
            _console.MarkupLine(
                "[dim]Register them with:[/] agentctl project add <path>  "
                + "[dim]or rerun with --register-discovered[/]");

            return;
        }
        else
        {
            chosen = _console.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("Register any of these now? [dim](space to select, enter to confirm)[/]")
                    .NotRequired()
                    .PageSize(15)
                    .MoreChoicesText("[dim](move up and down for more)[/]")
                    .InstructionsText("[dim]Nothing is registered unless you pick it.[/]")
                    .AddChoices(unregistered.Select(r => r.Path)));
        }

        var registered = new List<Models.Projects.ProjectResolution>();

        foreach (var path in chosen)
        {
            var result = await _projects.AddAsync(path, null, ct).ConfigureAwait(false);

            if (result.Succeeded)
            {
                registered.Add(result.Value!);
                _console.MarkupLine($"[green]+[/] {Markup.Escape(result.Value!.Entry.Name)}");
            }
            else
            {
                _console.MarkupLine(
                    $"[yellow]![/] {Markup.Escape(path)}  [dim]{Markup.Escape(result.Error!)}[/]");
            }
        }

        await OfferMigrationAsync(registered, request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Offers to move existing agent configuration into the workspace
    /// (spec section 96).
    /// <para>
    /// Registering a project does nothing to the agent files already sitting in
    /// it, so without this step onboarding finishes with the repositories in
    /// exactly the state they started. The plan is always shown before anything
    /// moves, and files Git already ignores are left alone unless asked for:
    /// those are not in the repository's content and never will be, so taking
    /// them would remove a working setup to solve a problem that does not exist.
    /// </para>
    /// </summary>
    private async Task OfferMigrationAsync(
        IReadOnlyList<Models.Projects.ProjectResolution> projects,
        SetupRequest request,
        CancellationToken ct)
    {
        if (projects.Count == 0)
        {
            return;
        }

        var plans = new List<MigrationPlan>();
        var ignoredOnly = new List<string>();

        foreach (var project in projects)
        {
            if (project.LocalPath is null)
            {
                continue;
            }

            var plan = await _migrations
                .PlanAsync(project.LocalPath, project.Entry.Slug, request.IncludeIgnored, ct)
                .ConfigureAwait(false);

            if (plan.Succeeded && plan.Value!.Steps.Count > 0)
            {
                plans.Add(plan.Value);
                continue;
            }

            // Nothing to move, but there may still be agent files here that are
            // simply already excluded. Worth mentioning so the absence of a
            // migration does not look like the launcher missing them.
            var withIgnored = await _migrations
                .PlanAsync(project.LocalPath, project.Entry.Slug, includeIgnored: true, ct)
                .ConfigureAwait(false);

            if (withIgnored.Succeeded && withIgnored.Value!.Steps.Count > 0)
            {
                ignoredOnly.Add(project.Entry.Name);
            }
        }

        if (ignoredOnly.Count > 0)
        {
            _console.WriteLine();
            _console.MarkupLine(
                $"[dim]{string.Join(", ", ignoredOnly.Select(Markup.Escape))}: agent files are "
                + "already excluded from Git and were left where they are. Move them with "
                + "agentctl migrate --include-ignored if you want them shared across machines.[/]");
        }

        if (plans.Count == 0)
        {
            return;
        }

        _console.WriteLine();
        _console.MarkupLine(
            $"[bold]{plans.Count} project(s) have agent files in the repository[/]");

        foreach (var plan in plans)
        {
            _console.WriteLine();
            _console.MarkupLine($"[bold]{Markup.Escape(plan.Slug)}[/]");

            foreach (var step in plan.Steps)
            {
                var note = step.Kind == PolicyFindingKind.Tracked
                    ? "[yellow]tracked, will be copied not removed[/]"
                    : "[dim]will be moved[/]";

                _console.MarkupLine($"  {Markup.Escape(step.RepositoryRelativePath)}  {note}");
            }
        }

        _console.WriteLine();

        var migrate = request.Migrate
            || (request.Interactive
                && _console.Confirm("Migrate these into the workspace now?", defaultValue: false));

        if (!migrate)
        {
            _console.MarkupLine("[dim]Left alone. Run later with:[/] agentctl migrate <project>");
            return;
        }

        foreach (var plan in plans)
        {
            var applied = await _migrations.ApplyAsync(plan, ct).ConfigureAwait(false);

            if (applied.Failed)
            {
                _console.MarkupLine(
                    $"[yellow]![/] {Markup.Escape(plan.Slug)}  {Markup.Escape(applied.Error!)}");

                continue;
            }

            _console.MarkupLine($"[green]+[/] {Markup.Escape(plan.Slug)}");

            foreach (var path in applied.Value!.TrackedLeftInPlace)
            {
                // The one thing the user must act on themselves, so it is said
                // per project rather than buried in a summary.
                _console.MarkupLine(
                    $"    [yellow]{Markup.Escape(path)}[/] [dim]is still tracked; remove it with "
                    + $"git rm --cached and commit[/]");
            }
        }
    }
}
