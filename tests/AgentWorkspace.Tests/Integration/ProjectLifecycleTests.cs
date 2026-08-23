using AgentWorkspace.Core.Configuration;
using AgentWorkspace.Core.Git;
using AgentWorkspace.Core.Projects;
using AgentWorkspace.Core.Workspace;
using AgentWorkspace.Models.Platform;
using AgentWorkspace.Platform.Abstractions;
using AgentWorkspace.Platform.Common;
using AgentWorkspace.Platform.Linux;
using AgentWorkspace.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace AgentWorkspace.Tests.Integration;

/// <summary>
/// Exercises registration, resolution and discovery against real Git
/// repositories on disk.
/// <para>
/// This is part of the shared acceptance suite of spec section 93: identical
/// assertions run on Windows, Linux and macOS, which is how the launcher can
/// claim parity rather than assert it. Everything here goes through the real
/// git binary, so it also covers the decision to shell out rather than link a
/// Git library.
/// </para>
/// </summary>
public sealed class ProjectLifecycleTests : IAsyncLifetime
{
    private readonly string _root;
    private readonly string _repositories;
    private readonly ProcessLauncher _processes = new();

    private IProjectService _projects = null!;
    private IGitManager _git = null!;
    private IWorkspaceManager _workspace = null!;

    public ProjectLifecycleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "agentctl-int-" + Guid.NewGuid().ToString("N"));
        _repositories = Path.Combine(_root, "repos");
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_repositories);

        // Launcher state is redirected into the temp tree, so the suite never
        // touches the developer's real configuration.
        var environment = new FakeEnvironmentProvider(
            Path.Combine(_root, "home"),
            new Dictionary<string, string>
            {
                ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
            })
        {
            PathDirectories = Environment.GetEnvironmentVariable("PATH")?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [],
            ExecutableExtensions = OperatingSystem.IsWindows()
                ? [".exe", ".cmd", ".bat"]
                : [string.Empty],
        };

        var permissions = new NoOpFilePermissions();

        // The Linux layout is used on every host because it is driven purely by
        // the injected environment, which keeps the fixture identical across
        // the three CI legs.
        var paths = new LinuxPaths(
            environment,
            permissions,
            new HostPlatform(
                HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST-MACHINE"));

        paths.EnsureDirectoriesExist();

        var resolver = new ExecutableResolver(environment, []);
        _git = new GitManager(_processes, resolver);

        var yaml = new YamlStore(permissions);
        var configuration = new ConfigurationService(paths, environment, yaml);

        // Discovery must look at the temp tree rather than the real machine.
        var machine = (await configuration.LoadMachineAsync()).Value!;
        machine.DiscoveryRoots = [_repositories];
        await configuration.SaveMachineAsync(machine);

        _workspace = new WorkspaceManager(paths, _git, yaml, TimeProvider.System);

        _projects = new ProjectService(configuration, _workspace, _git, new PathSemantics());
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                // Git marks objects read-only on some platforms, which blocks a
                // plain recursive delete.
                foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing the run over.
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_repository_can_be_registered_resolved_and_removed()
    {
        var repository = await CreateRepositoryAsync("starstats", "ssh://git.internal/apps/starstats.git");

        var added = await _projects.AddAsync(repository);

        added.Succeeded.Should().BeTrue(added.Error);
        added.Value!.Entry.Slug.Should().Be("starstats");
        added.Value.LocalPath.Should().NotBeNull();
        added.Value.IsAvailableLocally.Should().BeTrue();

        var resolved = await _projects.ResolveAsync("starstats");
        resolved.Succeeded.Should().BeTrue();
        resolved.Value!.Entry.Id.Should().Be(added.Value.Entry.Id);

        var removed = await _projects.RemoveAsync("starstats", fromWorkspace: true);
        removed.Succeeded.Should().BeTrue();

        // Removing a registration must never remove the code (spec section 75).
        Directory.Exists(repository).Should().BeTrue();
        Directory.Exists(Path.Combine(repository, ".git")).Should().BeTrue();
    }

    [Fact]
    public async Task The_shared_registry_holds_no_machine_specific_path()
    {
        var repository = await CreateRepositoryAsync("gateconquest", "ssh://git.internal/apps/gate.git");

        await _projects.AddAsync(repository);

        var resolved = (await _projects.ResolveAsync("gateconquest")).Value!;

        // Spec section 15: the shared definition must be portable, so the local
        // path lives only in this machine's own configuration.
        resolved.Entry.Remote.Should().Be("ssh://git.internal/apps/gate.git");
        resolved.Entry.Should().BeEquivalentTo(
            resolved.Entry,
            options => options.Excluding(e => e.Aliases));

        var registryText = await File.ReadAllTextAsync(
            Path.Combine(_root, "data", "agent-workspace-launcher", "workspace",
                "registry", "projects.yaml"));

        registryText.Should().NotContain(repository);
    }

    [Fact]
    public async Task The_project_owning_a_directory_is_found_from_inside_it()
    {
        var repository = await CreateRepositoryAsync("here-test", "ssh://git.internal/apps/here.git");
        await _projects.AddAsync(repository);

        // Spec section 24: this is what "agentctl here" depends on, including
        // from a subdirectory rather than only the repository root.
        var nested = Path.Combine(repository, "src", "deep");
        Directory.CreateDirectory(nested);

        var resolved = await _projects.ResolveFromDirectoryAsync(nested);

        resolved.Succeeded.Should().BeTrue(resolved.Error);

        // The slug comes from the remote rather than the local directory name,
        // because the remote is the shared identity and the directory name is
        // whatever this machine happened to clone into.
        resolved.Value!.Entry.Slug.Should().Be("here");
    }

    [Fact]
    public async Task A_second_clone_of_one_repository_resolves_by_remote()
    {
        var original = await CreateRepositoryAsync("original", "ssh://git.internal/apps/shared.git");
        var added = await _projects.AddAsync(original);

        added.Succeeded.Should().BeTrue(added.Error);

        // A different directory, and the remote written in the scp-like form
        // rather than as a URL. Spec section 29 requires these to be one
        // project, not two.
        var clone = await CreateRepositoryAsync("second-clone", "git@git.internal:apps/shared.git");

        var resolved = await _projects.ResolveFromDirectoryAsync(clone);

        resolved.Succeeded.Should().BeTrue(resolved.Error);
        resolved.Value!.Entry.Id.Should().Be(added.Value!.Entry.Id,
            "the two clones share a remote, so they are one project");
    }

    [Fact]
    public async Task Discovery_finds_repositories_and_marks_the_registered_ones()
    {
        var registered = await CreateRepositoryAsync("known", "ssh://git.internal/apps/known.git");
        await CreateRepositoryAsync("unknown", "ssh://git.internal/apps/unknown.git");

        await _projects.AddAsync(registered);

        var discovered = (await _projects.DiscoverAsync()).Value!;

        discovered.Should().HaveCount(2);
        discovered.Should().ContainSingle(r => r.IsRegistered && r.MatchedSlug == "known");
        discovered.Should().ContainSingle(r => !r.IsRegistered && r.Name == "unknown");
    }

    [Fact]
    public async Task Discovery_ignores_a_directory_that_is_not_a_repository()
    {
        await CreateRepositoryAsync("real", "ssh://git.internal/apps/real.git");
        Directory.CreateDirectory(Path.Combine(_repositories, "just-a-folder"));

        var discovered = (await _projects.DiscoverAsync()).Value!;

        discovered.Should().ContainSingle().Which.Name.Should().Be("real");
    }

    [Fact]
    public async Task Registering_a_directory_that_is_not_a_repository_fails_clearly()
    {
        var plain = Path.Combine(_root, "not-a-repo");
        Directory.CreateDirectory(plain);

        var result = await _projects.AddAsync(plain);

        result.Failed.Should().BeTrue();
        result.ExitCode.Should().Be(Models.ExitCode.RepositoryUnavailable);
    }

    [Fact]
    public async Task Launch_history_survives_a_relocation()
    {
        var repository = await CreateRepositoryAsync("movable", "ssh://git.internal/apps/movable.git");
        await _projects.AddAsync(repository);

        await _projects.RecordLaunchAsync("movable", "claude");

        var moved = await CreateRepositoryAsync("moved", "ssh://git.internal/apps/movable.git");
        (await _projects.RelocateAsync("movable", moved)).Succeeded.Should().BeTrue();

        var resolved = (await _projects.ResolveAsync("movable")).Value!;

        // It is the same project in a new place, so its history should not be
        // silently reset.
        resolved.LaunchCount.Should().Be(1);
        resolved.LocalPath.Should().Be(moved);
    }

    [Fact]
    public async Task Registering_a_project_never_overwrites_an_existing_manifest()
    {
        var repository = await CreateRepositoryAsync("curated", "ssh://git.internal/apps/curated.git");

        // A manifest a person hand-authored, or that another machine committed,
        // carrying exactly the material a fresh skeleton would not have.
        await _workspace.WriteProjectAsync(new Models.Projects.ProjectManifest
        {
            Id = "fixed-identity",
            Slug = "curated",
            Name = "Curated Project",
            Agents = new Models.Projects.ProjectAgents { Default = "codex" },
            Context = new Models.Projects.ProjectContext { Project = { "context/architecture.md" } },
            Profiles = { ["database"] = new Models.Projects.ContextProfile { Description = "DB work" } },
        });

        var added = await _projects.AddAsync(repository, "curated");

        added.Succeeded.Should().BeTrue(added.Error);

        var manifest = (await _workspace.ReadProjectAsync("curated")).Value!;

        // Overwriting this would silently destroy a project's whole context
        // configuration, which is the data loss spec section 47 rules out.
        manifest.Profiles.Should().ContainKey("database");
        manifest.Context.Project.Should().Contain("context/architecture.md");
        manifest.Agents.Default.Should().Be("codex");
        manifest.Id.Should().Be("fixed-identity");

        // The registry must agree with the manifest rather than inventing a
        // second identity for the same project.
        added.Value!.Entry.Id.Should().Be("fixed-identity");
        added.Value.Entry.DefaultAgent.Should().Be("codex");
    }

    [Fact]
    public async Task A_project_registered_elsewhere_can_be_cloned_here()
    {
        // Stand in for another machine: a real repository, registered, then the
        // local mapping dropped so the project is known but absent.
        var origin = await CreateBareRemoteAsync("shared");
        var seed = await CreateRepositoryAsync("seed", origin);

        await RunGitAsync(seed, "push", "origin", "main");
        await _projects.AddAsync(seed, "shared");
        await _projects.RemoveAsync("shared", fromWorkspace: false);

        var beforeClone = await _projects.ResolveAsync("shared");
        beforeClone.Value!.IsAvailableLocally.Should().BeFalse();

        var destination = Path.Combine(_repositories, "cloned-here");

        var cloned = await _projects.CloneAsync("shared", destination);

        cloned.Succeeded.Should().BeTrue(cloned.Error);
        cloned.Value!.IsAvailableLocally.Should().BeTrue();

        // Cloning must also register the local path, or the next launch would
        // still report the project as missing.
        File.Exists(Path.Combine(destination, "README.md")).Should().BeTrue();
        cloned.Value.LocalPath.Should().Be(destination);
    }

    [Fact]
    public async Task Cloning_refuses_a_destination_that_is_already_occupied()
    {
        var origin = await CreateBareRemoteAsync("occupied");
        var seed = await CreateRepositoryAsync("occupied-seed", origin);

        await RunGitAsync(seed, "push", "origin", "main");
        await _projects.AddAsync(seed, "occupied");
        await _projects.RemoveAsync("occupied", fromWorkspace: false);

        var destination = Path.Combine(_repositories, "already-there");
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(destination, "existing.txt"), "mine");

        var cloned = await _projects.CloneAsync("occupied", destination);

        // Cloning into an occupied directory would mix two repositories or fail
        // obscurely; refusing names the alternative.
        cloned.Failed.Should().BeTrue();
        cloned.Error.Should().Contain("relocate");

        (await File.ReadAllTextAsync(Path.Combine(destination, "existing.txt")))
            .Should().Be("mine");
    }

    [Fact]
    public async Task Cloning_a_project_that_is_already_here_is_refused()
    {
        var repository = await CreateRepositoryAsync("present", "ssh://git.internal/apps/present.git");
        await _projects.AddAsync(repository, "present");

        var cloned = await _projects.CloneAsync("present");

        cloned.Failed.Should().BeTrue();
        cloned.Error.Should().Contain("already present");
    }

    /// <summary>Creates a bare repository to act as a remote.</summary>
    private async Task<string> CreateBareRemoteAsync(string name)
    {
        var path = Path.Combine(_root, name + ".git");
        Directory.CreateDirectory(path);

        await RunGitAsync(_root, "init", "--bare", "--initial-branch", "main", path);

        return path.Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>Creates a real repository with one commit and an origin remote.</summary>
    private async Task<string> CreateRepositoryAsync(string name, string remote)
    {
        var path = Path.Combine(_repositories, name);
        Directory.CreateDirectory(path);

        await RunGitAsync(path, "init", "--initial-branch", "main");

        // Set locally so the suite does not depend on the machine having a
        // global Git identity, which a clean CI runner does not.
        await RunGitAsync(path, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(path, "config", "user.name", "Agent Workspace Tests");

        // Neutralise whatever global exclude file the developer's machine has.
        // Without this the suite passes or fails depending on whether agentctl
        // protect --global has ever been run here, which is not a property of
        // the code under test.
        await RunGitAsync(path, "config", "core.excludesFile", "");
        await RunGitAsync(path, "remote", "add", "origin", remote);

        await File.WriteAllTextAsync(Path.Combine(path, "README.md"), "# " + name);

        await RunGitAsync(path, "add", ".");
        await RunGitAsync(path, "commit", "--message", "initial");

        return path;
    }

    private async Task RunGitAsync(string workingDirectory, params string[] arguments)
    {
        var result = await _processes.RunAsync(
            new ProcessRequest("git", arguments, workingDirectory),
            TimeSpan.FromSeconds(60));

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.Succeeded.Should().BeTrue(
            $"git {string.Join(' ', arguments)} failed: {result.Value.StandardError}");
    }
}
