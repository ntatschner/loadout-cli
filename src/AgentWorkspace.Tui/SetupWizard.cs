using AgentWorkspace.Agents;
using AgentWorkspace.Core.Configuration;
using AgentWorkspace.Core.Git;
using AgentWorkspace.Core.Policies;
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

    /// <summary>Runs the wizard. Returns the exit code for the process.</summary>
    Task<int> RunAsync(CancellationToken ct = default);
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
        _secrets = secrets;
        _paths = paths;
        _resolver = resolver;
        _processes = processes;
    }

    /// <inheritdoc />
    public bool IsConfigured() => File.Exists(_paths.Paths.ConfigFile);

    /// <inheritdoc />
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
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

        var choice = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("No central workspace is configured. What would you like to do?")
                .AddChoices(
                    "Configure an existing central workspace",
                    "Create a new central workspace",
                    "Run without central storage"));

        var config = new LauncherConfig();

        var outcome = choice switch
        {
            "Configure an existing central workspace" =>
                await ConfigureExistingAsync(config, ct).ConfigureAwait(false),

            "Create a new central workspace" =>
                await CreateNewAsync(config, ct).ConfigureAwait(false),

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

        await ConfigureDiscoveryRootsAsync(ct).ConfigureAwait(false);
        await OfferGlobalProtectionAsync(ct).ConfigureAwait(false);
        await ShowDetectedAgentsAsync(ct).ConfigureAwait(false);
        await OfferDiscoveredProjectsAsync(ct).ConfigureAwait(false);

        _console.WriteLine();
        _console.MarkupLine("[green]Setup complete.[/]");
        _console.MarkupLine("[dim]Run[/] agentctl [dim]to open the launcher, or[/] agentctl doctor "
            + "[dim]to check everything.[/]");

        return (int)ExitCode.Success;
    }

    /// <summary>Spec section 62: point at a workspace somebody has already made.</summary>
    private async Task<OperationResult> ConfigureExistingAsync(
        LauncherConfig config,
        CancellationToken ct)
    {
        var remote = _console.Prompt(
            new TextPrompt<string>("Central workspace Git URL:")
                .Validate(value => string.IsNullOrWhiteSpace(value)
                    ? ValidationResult.Error("A URL is required.")
                    : ValidationResult.Success()));

        config.Workspace.Remote = remote.Trim();

        config.Workspace.Branch = _console.Prompt(
            new TextPrompt<string>("Branch:").DefaultValue("main"));

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
    private async Task<OperationResult> CreateNewAsync(LauncherConfig config, CancellationToken ct)
    {
        var name = _console.Prompt(
            new TextPrompt<string>("Workspace name:").DefaultValue("agent-workspaces"));

        config.Workspace.Branch = _console.Prompt(
            new TextPrompt<string>("Default branch:").DefaultValue("main"));

        var initResult = await _workspace.InitialiseStructureAsync(name, ct).ConfigureAwait(false);
        if (initResult.Failed)
        {
            return initResult;
        }

        _console.MarkupLine($"[green]+[/] Created  [dim]{Markup.Escape(_workspace.LocalPath)}[/]");

        // The identity has to exist before the first commit, not after it.
        await EnsureGitIdentityAsync(ct).ConfigureAwait(false);

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

        return await ConfigureRemoteAsync(config, name, ct).ConfigureAwait(false);
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
        string name,
        CancellationToken ct)
    {
        const string ViaGitHub = "Create a private repository on GitHub";
        const string ViaUrl = "Use a repository I have already created";
        const string Later = "Stay local for now";

        var gh = await FindAuthenticatedGitHubCliAsync(ct).ConfigureAwait(false);
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

        if (choice == Later)
        {
            _console.MarkupLine(
                "[dim]Staying local. Add a remote later with:[/] "
                + "agentctl config set workspace-remote <url>");

            return OperationResult.Ok();
        }

        if (choice == ViaUrl)
        {
            config.Workspace.Remote = _console.Prompt(
                new TextPrompt<string>("Git remote URL:")
                    .Validate(value => string.IsNullOrWhiteSpace(value)
                        ? ValidationResult.Error("A URL is required.")
                        : ValidationResult.Success())).Trim();

            return await PushAsync(config, ct).ConfigureAwait(false);
        }

        var repositoryName = _console.Prompt(
            new TextPrompt<string>("Repository name:").DefaultValue(name));

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
    private async Task EnsureGitIdentityAsync(CancellationToken ct)
    {
        var name = await _git.GetConfigValueAsync("user.name", null, ct).ConfigureAwait(false);
        var email = await _git.GetConfigValueAsync("user.email", null, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(name.Value) && !string.IsNullOrWhiteSpace(email.Value))
        {
            _console.MarkupLine($"[green]+[/] Git identity  [dim]{Markup.Escape(name.Value)}[/]");
            return;
        }

        _console.MarkupLine("[yellow]No Git identity is configured.[/]");

        if (!_console.Confirm("Set one now?", defaultValue: true))
        {
            _console.MarkupLine(
                "[dim]Workspace commits will fail until one is set with: git config --global user.name[/]");

            return;
        }

        var newName = _console.Prompt(new TextPrompt<string>("Your name:"));
        var newEmail = _console.Prompt(new TextPrompt<string>("Your email:"));

        await _git.SetGlobalConfigValueAsync("user.name", newName.Trim(), ct).ConfigureAwait(false);
        await _git.SetGlobalConfigValueAsync("user.email", newEmail.Trim(), ct).ConfigureAwait(false);

        _console.MarkupLine("[green]+[/] Git identity set");
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

    private async Task ConfigureDiscoveryRootsAsync(CancellationToken ct)
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
    private async Task OfferGlobalProtectionAsync(CancellationToken ct)
    {
        var existing = await _git.GetConfigValueAsync("core.excludesFile", null, ct)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(existing.Value))
        {
            _console.MarkupLine($"[green]+[/] Global Git excludes  [dim]{Markup.Escape(existing.Value)}[/]");
            return;
        }

        _console.WriteLine();

        if (!_console.Confirm(
            "Configure global Git excludes so agent files never enter a repository?",
            defaultValue: true))
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
    private async Task OfferDiscoveredProjectsAsync(CancellationToken ct)
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

        var chosen = _console.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("Register any of these now? [dim](space to select, enter to confirm)[/]")
                .NotRequired()
                .PageSize(15)
                .MoreChoicesText("[dim](move up and down for more)[/]")
                .InstructionsText("[dim]Nothing is registered unless you pick it.[/]")
                .AddChoices(unregistered.Select(r => r.Path)));

        foreach (var path in chosen)
        {
            var result = await _projects.AddAsync(path, null, ct).ConfigureAwait(false);

            _console.MarkupLine(result.Succeeded
                ? $"[green]+[/] {Markup.Escape(result.Value!.Entry.Name)}"
                : $"[yellow]![/] {Markup.Escape(path)}  [dim]{Markup.Escape(result.Error!)}[/]");
        }
    }
}
