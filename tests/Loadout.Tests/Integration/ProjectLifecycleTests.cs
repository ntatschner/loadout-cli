using Loadout.Core.Configuration;
using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Platform;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Integration;

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

    private IConfigurationService _configuration = null!;
    private IProjectService _projects = null!;
    private IGitManager _git = null!;
    private IWorkspaceManager _workspace = null!;
    private Loadout.Core.Tasks.ITaskService _tasks = null!;

    public ProjectLifecycleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-int-" + Guid.NewGuid().ToString("N"));
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

        _configuration = configuration;
        _tasks = new Loadout.Core.Tasks.TaskService(_workspace, yaml, TimeProvider.System);
        _projects = new ProjectService(configuration, _workspace, _git, new PathSemantics(), _tasks);
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
    public async Task Removing_a_project_says_what_it_left_in_the_workspace()
    {
        var repository = await CreateRepositoryAsync("keeper", "ssh://git.internal/apps/keeper.git");

        await _projects.AddAsync(repository);

        var definition = Path.Combine(_workspace.LocalPath, "projects", "keeper");

        Directory.CreateDirectory(Path.Combine(definition, "memory"));
        await File.WriteAllTextAsync(
            Path.Combine(definition, "memory", "architecture.md"), "what an agent learned");

        var removed = await _projects.RemoveAsync("keeper", fromWorkspace: true);

        removed.Succeeded.Should().BeTrue(removed.Error);

        // Never deleted. It is the only copy of what an agent worked out about
        // a codebase, and losing it to a command whose stated job is removing a
        // registration is a surprise there is no recovering from.
        File.Exists(Path.Combine(definition, "memory", "architecture.md"))
            .Should().BeTrue("memory is not a registration");

        // And said, because the option claimed to remove the definition and
        // never has. Keeping it quietly is the half that was wrong.
        removed.Value!.DefinitionPath.Should().NotBeNull();
        removed.Value.DefinitionFiles.Should().BeGreaterThan(0);
        removed.Value.FromWorkspace.Should().BeTrue();
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
            Path.Combine(_root, "data", "loadout", "workspace",
                "registry", "projects.yaml"));

        registryText.Should().NotContain(repository);
    }

    [Fact]
    public async Task The_project_owning_a_directory_is_found_from_inside_it()
    {
        var repository = await CreateRepositoryAsync("here-test", "ssh://git.internal/apps/here.git");
        await _projects.AddAsync(repository);

        // Spec section 24: this is what "loadout here" depends on, including
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
    public async Task Discovery_ignores_an_empty_directory()
    {
        await CreateRepositoryAsync("real", "ssh://git.internal/apps/real.git");
        Directory.CreateDirectory(Path.Combine(_repositories, "just-a-folder"));

        // An empty directory is not a project somebody forgot to initialise,
        // it is an empty directory. Offering it would fill the list with noise.
        var discovered = (await _projects.DiscoverAsync()).Value!;

        discovered.Should().ContainSingle().Which.Name.Should().Be("real");
    }

    [Fact]
    public async Task Discovery_offers_code_that_is_not_under_version_control()
    {
        await CreateRepositoryAsync("real", "ssh://git.internal/apps/real.git");

        var loose = Path.Combine(_repositories, "courtfinances");
        Directory.CreateDirectory(loose);
        await File.WriteAllTextAsync(Path.Combine(loose, "app.py"), "print('hello')");

        // 'project add' takes one of these, so the list of things worth adding
        // has to contain it. Left out, the only way to register such a
        // directory was to know the path already and type it, and the
        // launcher's own Add Project list could not show what it could add.
        var discovered = (await _projects.DiscoverAsync()).Value!;

        var found = discovered.Should().ContainSingle(r => r.Name == "courtfinances").Subject;

        found.Versioned.Should().BeFalse();
        found.IsRegistered.Should().BeFalse();
        discovered.Should().ContainSingle(r => r.Name == "real" && r.Versioned);
    }

    [Fact]
    public async Task A_folder_that_merely_holds_repositories_is_not_itself_offered()
    {
        var group = Path.Combine(_repositories, "GateConquestRepos");
        Directory.CreateDirectory(group);
        await File.WriteAllTextAsync(Path.Combine(group, "notes.txt"), "a stray file");

        await CreateRepositoryAsync(Path.Combine("GateConquestRepos", "web"), "ssh://g/web.git");
        await CreateRepositoryAsync(Path.Combine("GateConquestRepos", "api"), "ssh://g/api.git");

        var discovered = (await _projects.DiscoverAsync()).Value!;

        // It has a file of its own, so the cheap test would offer it. It holds
        // two repositories, which makes it a folder rather than a project.
        discovered.Should().NotContain(r => r.Name == "GateConquestRepos");
        discovered.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_shallowest_unversioned_directory_is_the_one_offered()
    {
        var project = Path.Combine(_repositories, "loose");
        var inner = Path.Combine(project, "docs");

        Directory.CreateDirectory(inner);
        await File.WriteAllTextAsync(Path.Combine(project, "main.go"), "package main");
        await File.WriteAllTextAsync(Path.Combine(inner, "readme.md"), "# docs");

        var discovered = (await _projects.DiscoverAsync()).Value!;

        // Offering the deepest instead would list 'loose/docs' and not 'loose',
        // which is the wrong end of the tree and not a project at all.
        discovered.Should().ContainSingle().Which.Name.Should().Be("loose");
    }

    [Fact]
    public async Task Build_output_is_not_offered_as_a_project()
    {
        foreach (var name in new[] { "__pycache__", "test-results", "screenshots", "dist" })
        {
            var directory = Path.Combine(_repositories, name);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "a.bin"), "x");
        }

        var real = Path.Combine(_repositories, "actual-code");
        Directory.CreateDirectory(real);
        await File.WriteAllTextAsync(Path.Combine(real, "main.py"), "x = 1");

        var discovered = (await _projects.DiscoverAsync()).Value!;

        // Pointed at a real machine the first time, this offered '__pycache__',
        // 'test-results' and a screenshots folder alongside the genuine ones.
        discovered.Should().ContainSingle().Which.Name.Should().Be("actual-code");
    }

    [Fact]
    public async Task Build_output_is_still_descended_into_when_looking_for_repositories()
    {
        // The name filter applies to what is offered, never to the walk.
        // Skipping 'build' while descending would hide a real repository that
        // happens to live under one, and hiding a repository is the worse
        // failure of the two.
        var buried = await CreateRepositoryAsync(
            Path.Combine("build", "shipped"), "ssh://git.internal/apps/shipped.git");

        buried.Should().NotBeNull();

        var discovered = (await _projects.DiscoverAsync()).Value!;

        discovered.Should().ContainSingle().Which.Name.Should().Be("shipped");
    }

    [Fact]
    public async Task A_discovery_root_is_never_offered_as_a_project_itself()
    {
        await File.WriteAllTextAsync(Path.Combine(_repositories, "stray.txt"), "x");

        var discovered = (await _projects.DiscoverAsync()).Value!;

        // A discovery root is where projects live, not one of them.
        discovered.Should().BeEmpty();
    }

    [Fact]
    public async Task Discovery_finds_a_linked_worktree()
    {
        var main = await CreateRepositoryAsync("trunk", "ssh://git.internal/apps/trunk.git");

        var worktree = Path.Combine(_repositories, "trunk-hotfix");

        await RunGitAsync(main, "worktree", "add", "-b", "hotfix", worktree);

        // A linked worktree keeps a .git *file* pointing at the real directory,
        // not a .git directory. The walk asked Directory.Exists, so a worktree
        // was indistinguishable from an ordinary folder and never appeared —
        // while the attribution code two files away already knew the
        // distinction and documented it.
        var discovered = (await _projects.DiscoverAsync()).Value!;

        discovered.Select(r => r.Name).Should().Contain("trunk-hotfix");
        discovered.Should().HaveCount(2);
    }

    [Fact]
    public async Task Registering_a_path_that_does_not_exist_fails_clearly()
    {
        // A directory that is not a repository is now registered deliberately.
        // A path that is not there at all is still a typo, and registering it
        // would make a project pointing at nothing.
        var missing = Path.Combine(_root, "no-such-directory");

        var result = await _projects.AddAsync(missing);

        result.Failed.Should().BeTrue();
        result.ExitCode.Should().Be(Models.ExitCode.RepositoryUnavailable);
    }

    [Fact]
    public async Task A_directory_with_no_repository_is_registered_and_says_so()
    {
        var plain = Path.Combine(_repositories, "unversioned");
        Directory.CreateDirectory(plain);
        await File.WriteAllTextAsync(Path.Combine(plain, "app.py"), "print('hello')");

        var result = await _projects.AddAsync(plain);

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.Entry.Slug.Should().Be("unversioned");

        var manifest = await _workspace.ReadProjectAsync("unversioned");

        manifest.Succeeded.Should().BeTrue();
        manifest.Value!.Repository.Versioned.Should().BeFalse(
            "the manifest has to record that there is no repository here yet");

        // Turned on for this project alone, because the task below is the
        // whole reason for registering it and a task nobody is shown is a note
        // to nobody.
        manifest.Value.Context.Tasks.Should().BeTrue();
    }

    [Fact]
    public async Task Registering_an_unversioned_directory_leaves_the_setup_work_as_a_task()
    {
        var plain = Path.Combine(_repositories, "needs-git");
        Directory.CreateDirectory(plain);

        await _projects.AddAsync(plain);

        var tasks = await _tasks.ListAsync("needs-git");

        tasks.Succeeded.Should().BeTrue(tasks.Error);

        var setup = tasks.Value!.Should().ContainSingle().Subject;

        setup.Id.Should().Be("setup-repository");
        setup.State.Should().Be(Models.Tasks.TaskState.Open);
        setup.Note.Should().Contain("Git repository");
    }

    [Fact]
    public async Task A_registered_repository_is_not_recorded_as_unversioned()
    {
        var repository = await CreateRepositoryAsync("proper", "ssh://git.internal/apps/proper.git");

        await _projects.AddAsync(repository);

        var manifest = await _workspace.ReadProjectAsync("proper");

        manifest.Value!.Repository.Versioned.Should().BeTrue();

        // And the other half: no task, and the section stays off. A repository
        // that is already a repository has nothing to be told.
        manifest.Value.Context.Tasks.Should().BeFalse();
        (await _tasks.ListAsync("proper")).Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task Here_resolves_inside_a_project_that_has_no_repository_yet()
    {
        var plain = Path.Combine(_repositories, "start-here");
        Directory.CreateDirectory(plain);

        await _projects.AddAsync(plain);

        // The session that is meant to do the initialising has to be able to
        // start in the directory it is about. Every other way of attributing a
        // directory — the marker in Git config, the remote — needs a
        // repository to read.
        var resolved = await _projects.ResolveFromDirectoryAsync(plain);

        resolved.Succeeded.Should().BeTrue(resolved.Error);
        resolved.Value!.Entry.Slug.Should().Be("start-here");
    }

    [Fact]
    public async Task Here_resolves_from_inside_an_unversioned_project_too()
    {
        var plain = Path.Combine(_repositories, "deep-start");
        var inner = Path.Combine(plain, "src", "api");

        Directory.CreateDirectory(inner);

        await _projects.AddAsync(plain);

        // A repository resolves from any directory in it, because finding the
        // root walks up. Matching only the exact path would have made this work
        // at the top of an unversioned project and fail one directory into it,
        // which is the sort of difference nobody thinks to look for.
        var resolved = await _projects.ResolveFromDirectoryAsync(inner);

        resolved.Succeeded.Should().BeTrue(resolved.Error);
        resolved.Value!.Entry.Slug.Should().Be("deep-start");
    }

    [Fact]
    public async Task A_directory_belonging_to_no_project_still_fails()
    {
        var stranger = Path.Combine(_root, "nobody's");
        Directory.CreateDirectory(stranger);

        var resolved = await _projects.ResolveFromDirectoryAsync(stranger);

        resolved.Failed.Should().BeTrue(
            "matching an unversioned project must not become a way of answering for any folder");
    }

    [Fact]
    public async Task A_preview_names_the_slug_the_registration_would_use()
    {
        var repository = await CreateRepositoryAsync(
            "previewable", "ssh://git.internal/apps/previewable.git");

        var preview = await _projects.ValidateAddAsync(repository);

        preview.Succeeded.Should().BeTrue(preview.Error);
        preview.Value!.Slug.Should().Be("previewable");
        preview.Value.Versioned.Should().BeTrue();

        // And it has to have changed nothing while working that out.
        (await _projects.ResolveAsync("previewable")).Failed.Should().BeTrue(
            "a preview that registered the project would be the defect it replaces");
    }

    [Fact]
    public async Task A_preview_says_when_there_is_no_repository_to_register()
    {
        var plain = Path.Combine(_repositories, "previewed-unversioned");
        Directory.CreateDirectory(plain);

        var preview = await _projects.ValidateAddAsync(plain);

        preview.Succeeded.Should().BeTrue(preview.Error);
        preview.Value!.Versioned.Should().BeFalse();

        // The preview and the registration have to agree about what would
        // happen, and disagree entirely about whether it has happened.
        File.Exists(Path.Combine(
                _workspace.LocalPath, "projects", "previewed-unversioned", "tasks.yaml"))
            .Should().BeFalse("a preview writes nothing, the task included");
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
        // Through the path semantics, not string equality: on macOS a temporary
        // directory has a symlinked ancestor, so the same directory has two
        // spellings.
        new PathSemantics().PathsEqual(resolved.LocalPath!, moved).Should().BeTrue();
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
    public async Task Cloning_with_no_clone_root_says_so_instead_of_throwing()
    {
        var origin = await CreateBareRemoteAsync("rootless");
        var seed = await CreateRepositoryAsync("rootless-seed", origin);

        await RunGitAsync(seed, "push", "origin", "main");
        await _projects.AddAsync(seed, "rootless");
        await _projects.RemoveAsync("rootless", fromWorkspace: false);

        // A machine that has never had one set, which is every machine on the
        // day the launcher is installed.
        var machine = (await _configuration.LoadMachineAsync()).Value!;
        machine.DefaultCloneRoot = null;
        await _configuration.SaveMachineAsync(machine);

        var cloned = await _projects.CloneAsync("rootless");

        // This used to throw InvalidOperationException from the middle of the
        // method, so it surfaced as an unhandled error with a generic exit
        // code, while every other refusal here names the fix.
        cloned.Failed.Should().BeTrue();
        cloned.ExitCode.Should().Be(ExitCode.InvalidArguments);
        cloned.Error.Should().Contain("clone-root");
        cloned.Error.Should().Contain("rootless");
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
    [Fact]
    public async Task A_registered_repository_records_which_project_it_is()
    {
        var repository = await CreateRepositoryAsync("marked", "https://example.com/marked.git");

        await _projects.AddAsync(repository);

        var marked = await _git.GetConfigValueAsync(IProjectService.ProjectMarker, repository);

        marked.Value.Should().Be("marked");

        // In .git/config, which is never committed. A tracked marker would
        // breach the rule that application repositories hold application source
        // only, and the launcher's own policy check would rightly flag it.
        File.Exists(Path.Combine(repository, ".loadout")).Should().BeFalse();
    }

    [Fact]
    public async Task A_repository_that_has_moved_is_still_recognised()
    {
        var original = await CreateRepositoryAsync("wanderer", "https://example.com/wanderer.git");
        await _projects.AddAsync(original);

        var moved = Path.Combine(_repositories, "moved-elsewhere");
        Directory.Move(original, moved);

        // The recorded path no longer matches anything, and the registry has no
        // idea where it went. The repository itself still knows, which is the
        // whole reason for writing it down there.
        var resolved = await _projects.ResolveFromDirectoryAsync(moved);

        resolved.Succeeded.Should().BeTrue(resolved.Error ?? string.Empty);
        resolved.Value!.Entry.Slug.Should().Be("wanderer");
    }

    [Fact]
    public async Task The_recorded_path_still_wins_over_a_stale_mark()
    {
        var first = await CreateRepositoryAsync("first", "https://example.com/first.git");
        var second = await CreateRepositoryAsync("second", "https://example.com/second.git");

        await _projects.AddAsync(first);
        await _projects.AddAsync(second);

        // A directory copied from elsewhere carries the mark of whatever it was
        // copied from. The machine's own record of where a project lives is the
        // stronger claim, so a stale mark cannot make one repository answer to
        // another's name.
        await _git.SetLocalConfigValueAsync(IProjectService.ProjectMarker, "first", second);

        var resolved = await _projects.ResolveFromDirectoryAsync(second);

        resolved.Value!.Entry.Slug.Should().Be("second");
    }

    [Fact]
    public async Task A_directory_of_repositories_is_told_apart_from_a_repository()
    {
        var alpha = await CreateRepositoryAsync("alpha", "https://example.com/alpha.git");
        await CreateRepositoryAsync("beta", "https://example.com/beta.git");

        var attribution = new RepositoryAttribution(
            new FakeEnvironmentProvider(_root, new Dictionary<string, string>()),
            _projects,
            new PathSemantics());

        // The GateConquest shape: work done from a parent directory, so the
        // agent recorded its memory against something that is not a repository
        // and holds several.
        attribution.RepositoriesInside(_repositories).Should().HaveCountGreaterThan(1);

        // A repository's own subdirectories are source code, not more projects.
        attribution.RepositoriesInside(alpha).Should().BeEmpty();
    }

    [Fact]
    public async Task A_directory_that_is_not_a_repository_is_not_reported_as_one()
    {
        var attribution = new RepositoryAttribution(
            new FakeEnvironmentProvider(_root, new Dictionary<string, string>()),
            _projects,
            new PathSemantics());

        var plain = Path.Combine(_root, "just-a-folder");
        Directory.CreateDirectory(plain);

        // Telling somebody to register this would send them to a command that
        // cannot succeed, which is worse than saying nothing.
        attribution.RepositoriesInside(plain).Should().BeEmpty();

        var repository = await CreateRepositoryAsync("solo", "https://example.com/solo.git");
        attribution.RepositoriesInside(repository).Should().BeEmpty();
    }

    [Theory]
    [InlineData("alpha")]
    [InlineData("has-hyphens-in-name")]
    public async Task A_path_is_recovered_from_the_agents_own_directory_name(string name)
    {
        // The transform is lossy: separators, colons and dots all became the
        // same hyphen. It is resolved against the filesystem rather than by
        // parsing, because the question that matters is whether a real
        // directory is there.
        var repository = await CreateRepositoryAsync(name, $"https://example.com/{name}.git");

        var slug = MemoryImporter.DerivedSlug(repository);

        RepositoryAttribution.RecoverPath(slug).Should().Be(repository);
    }

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
        // Without this the suite passes or fails depending on whether loadout
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
