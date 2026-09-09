using Loadout.Agents;
using Loadout.Core.Configuration;
using Loadout.Core.Context;
using Loadout.Core.Instructions;
using Loadout.Core.Mcp;
using Loadout.Core.Diagnostics;
using Loadout.Core.Git;
using Loadout.Core.Policies;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Core.Sessions;
using Loadout.Models.Agents;
using Loadout.Models.Configuration;
using Loadout.Models.Platform;
using Loadout.Models.Projects;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Drives the whole launch sequence of spec section 45, from resolving the
/// project through compiling context and preflight to actually starting a child
/// process and propagating its exit code.
/// <para>
/// The stand-in agent is <c>git hash-object</c> pointed at the compiled context
/// file. That is not a trick for its own sake: it proves the file genuinely
/// existed, was readable and held content at the moment the child ran, which no
/// amount of inspecting the launcher's own state can establish. Using git also
/// keeps the fixture dependency-free, since the suite already requires it.
/// </para>
/// </summary>
public sealed class LaunchPipelineTests : IAsyncLifetime
{
    private const string ProjectSlug = "starstats";

    private readonly string _root;
    private readonly ThrottledProcessLauncher _processes = new();
    private readonly SpyReaper _reaper = new();

    private IAgentLauncher _launcher = null!;
    private IProjectService _projects = null!;
    private IPlatformPaths _paths = null!;
    private string _repository = null!;

    public LaunchPipelineTests() =>
        _root = Path.Combine(Path.GetTempPath(), "loadout-launch-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);

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

        _paths = new LinuxPaths(
            environment,
            permissions,
            new HostPlatform(
                HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST-MACHINE"));

        _paths.EnsureDirectoriesExist();

        var resolver = new ExecutableResolver(environment, []);
        var git = new GitManager(_processes, resolver);
        var yaml = new YamlStore(permissions);
        var configuration = new ConfigurationService(_paths, environment, yaml);
        var workspace = new WorkspaceManager(_paths, git, yaml, TimeProvider.System);

        _projects = new ProjectService(configuration, workspace, git, new PathSemantics());

        _repository = await CreateRepositoryAsync();
        await _projects.AddAsync(_repository, ProjectSlug);

        await BuildWorkspaceAsync(workspace);

        // The stand-in agent hashes whatever file it is given. If the compiled
        // context is missing or unreadable, git exits non-zero and the launch
        // reports that exit code, so the assertion cannot pass by accident.
        var config = new LauncherConfig
        {
            DefaultAgent = "probe",
            CustomAgents =
            {
                ["probe"] = new GenericAgentDefinition
                {
                    DisplayName = "Context probe",
                    Executable = "git",
                    Arguments = { "hash-object", "${COMPILED_CONTEXT_FILE}" },
                },
            },
        };

        await configuration.SaveConfigAsync(config);

        var agents = new AgentRegistry(resolver, _processes, config);

        _launcher = new AgentLauncher(
            _projects,
            workspace,
            configuration,
            agents,
            _paths,
            _processes,
            git,
            new ContextCompiler(permissions, new RuleService(), new MemoryService(TimeProvider.System)),
            new HandoffService(workspace, TimeProvider.System),
            new InstructionService(
                new SpecialistLibrary(),
                new SpecialistResolver(),
                new RepositoryEvidenceReader(),
                configuration),
            new PreflightService(git, new FakeSecretProvider()),
            new SecurityProfileService(workspace, yaml),
            new McpService(workspace),
            new LaunchLedger(_paths, permissions, TimeProvider.System),
            new SessionRegistry(_paths, permissions, new ProcessInspector(), TimeProvider.System),
            new PolicyService(workspace, git, _paths, permissions, yaml),
            new Loadout.Tests.Fakes.QuietSpendWatch(),
            new Loadout.Core.Statusline.LoadedSpecialistStore(
                _paths, new Loadout.Core.Configuration.YamlStore(new Loadout.Tests.Fakes.NoOpFilePermissions()),
                TimeProvider.System),
            _reaper);
    }

    /// <summary>Records whether the launch asked for a collection.</summary>
    private sealed class SpyReaper : Loadout.Core.Sessions.IRuntimeReaper
    {
        public int Calls { get; private set; }

        public Task<Loadout.Models.Results.OperationResult<int>> ReapAsync(
            CancellationToken ct = default)
        {
            Calls++;

            return Task.FromResult(Loadout.Models.Results.OperationResult<int>.Ok(0));
        }
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_root))
            {
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
    public async Task A_launch_collects_what_earlier_ones_left_behind()
    {
        var result = await _launcher.LaunchAsync(
            new LaunchRequest(ProjectSlug, "probe", Offline: true));

        result.Succeeded.Should().BeTrue(result.Error);

        // The only reliable moment. A session that ends cleans up after itself;
        // one that is killed never reaches the code that would.
        _reaper.Calls.Should().Be(1);
    }

    [Fact]
    public async Task A_dry_run_changes_nothing_at_all()
    {
        // The general form of a defect this launcher keeps shipping. The dry
        // run returns from a branch about two hundred lines below where the
        // launch starts doing things, so anything added above that branch runs
        // during a dry run unless somebody remembers it must not. That has gone
        // wrong three times in shipped commands — 'workspace save' committed and
        // pushed, 'memory write' wrote the topic, 'project add' promised a
        // registration the real run refuses — and once more in review, when
        // collecting abandoned runtime directories was added above the branch.
        //
        // Each was fixed where it was found, which fixes an instance and not the
        // class. This asserts the property instead: everything the launcher owns
        // on this machine, and the repository itself, byte for byte either side
        // of a dry run. A write added above the branch fails here whatever it
        // writes, without anybody having thought of it in advance.
        //
        // Writes only. A collaborator that deletes rather than writes is stubbed
        // in this fixture and so cannot be seen from here — the collector is
        // covered by the test below, which counts whether it was asked at all.
        var before = Snapshot();

        var result = await _launcher.LaunchAsync(
            new LaunchRequest(ProjectSlug, "probe", Offline: true, DryRun: true));

        result.Succeeded.Should().BeTrue(result.Error);

        Snapshot().Should().Equal(
            before,
            "a dry run must leave the workspace, the launcher's state and the "
            + "repository exactly as it found them");
    }

    /// <summary>
    /// Every file the launcher could touch, by path and content.
    /// </summary>
    /// <remarks>
    /// Content rather than a timestamp, because a rewrite with the same length
    /// and a coarse clock would pass a comparison of sizes and dates. The tree
    /// is a handful of small files, so reading all of it costs nothing.
    /// </remarks>
    private SortedDictionary<string, string> Snapshot()
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            string content;

            try
            {
                content = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
            }
            catch (IOException)
            {
                // Held open by something else in the fixture. Recorded as such
                // so it is still compared, rather than silently skipped.
                content = "unreadable";
            }

            files[Path.GetRelativePath(_root, file)] = content;
        }

        return files;
    }

    [Fact]
    public async Task A_dry_run_collects_nothing()
    {
        var result = await _launcher.LaunchAsync(
            new LaunchRequest(ProjectSlug, "probe", Offline: true, DryRun: true));

        result.Succeeded.Should().BeTrue(result.Error);

        // The dry run reaches the line that reaps: it compiles a real context
        // into a real directory and returns well after that point. Reaping
        // there would have the one command whose whole promise is changing
        // nothing delete other sessions' contexts — which is the defect this
        // launcher has already shipped twice in other commands.
        _reaper.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_launch_compiles_context_and_the_agent_can_read_it()
    {
        var result = await _launcher.LaunchAsync(
            new LaunchRequest(ProjectSlug, "probe", Offline: true));

        result.Succeeded.Should().BeTrue(result.Error);

        // git hash-object exits zero only if it actually read the file.
        result.Value!.AgentExitCode.Should().Be(0,
            "the agent must be able to read the compiled context file");

        var compiled = result.Value.Preflight!.Checks
            .Single(c => c.Category == "Context" && c.Name == "Compilation");

        compiled.Detail.Should().Contain("source");
    }

    [Fact]
    public async Task The_runtime_directory_is_removed_once_the_agent_exits()
    {
        await _launcher.LaunchAsync(new LaunchRequest(ProjectSlug, "probe", Offline: true));

        // Spec section 82: sensitive runtime files are cleaned after use. The
        // compiled context aggregates everything the agent was told, so it must
        // not outlive the session.
        Directory.Exists(_paths.Paths.Runtime).Should().BeTrue();
        Directory.EnumerateDirectories(_paths.Paths.Runtime).Should().BeEmpty();
    }

    [Fact]
    public async Task A_profile_narrows_what_the_agent_is_given()
    {
        var full = await _launcher.LaunchAsync(
            new LaunchRequest(ProjectSlug, "probe", Offline: true));

        var narrowed = await _launcher.LaunchAsync(
            new LaunchRequest(ProjectSlug, "probe", Offline: true, Profile: "narrow"));

        var fullSources = SourceCount(full.Value!);
        var narrowedSources = SourceCount(narrowed.Value!);

        // The narrow profile excludes the global instructions, which is the
        // whole point of spec section 34: load what the task needs, not
        // everything the project has.
        narrowedSources.Should().BeLessThan(fullSources);
    }

    [Fact]
    public async Task An_unknown_profile_stops_the_launch_rather_than_using_the_wrong_context()
    {
        var result = await _launcher.LaunchAsync(
            new LaunchRequest(ProjectSlug, "probe", Offline: true, Profile: "no-such-profile"));

        result.Failed.Should().BeTrue();
        result.ExitCode.Should().Be(Models.ExitCode.InvalidArguments);
    }

    [Fact]
    public async Task A_handoff_is_folded_into_the_context_when_asked_for()
    {
        var handoffs = new HandoffService(
            new WorkspaceManager(_paths, new GitManager(_processes,
                new ExecutableResolver(new FakeEnvironmentProvider(Path.Combine(_root, "home")), [])),
                new YamlStore(new NoOpFilePermissions()), TimeProvider.System),
            TimeProvider.System);

        await handoffs.CreateAsync(ProjectSlug, "resume-here");

        var withHandoff = await _launcher.LaunchAsync(
            new LaunchRequest(ProjectSlug, "probe", Offline: true, IncludeHandoff: true));

        var without = await _launcher.LaunchAsync(
            new LaunchRequest(ProjectSlug, "probe", Offline: true));

        SourceCount(withHandoff.Value!).Should().Be(SourceCount(without.Value!) + 1);
    }

    [Fact]
    public async Task Asking_for_a_handoff_that_does_not_exist_warns_and_carries_on()
    {
        var result = await _launcher.LaunchAsync(
            new LaunchRequest("starstats", "probe", Offline: true, IncludeHandoff: true));

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.Warnings.Should().Contain(w => w.Contains("No handoff"));
    }

    [Fact]
    public async Task An_unknown_agent_fails_without_starting_anything()
    {
        var result = await _launcher.LaunchAsync(new LaunchRequest(ProjectSlug, "nonexistent-agent"));

        result.Failed.Should().BeTrue();
        result.ExitCode.Should().Be(Models.ExitCode.AgentUnavailable);
    }

    /// <summary>Reads the source count out of the preflight report.</summary>
    private static int SourceCount(LaunchOutcome outcome)
    {
        var detail = outcome.Preflight!.Checks
            .Single(c => c.Category == "Context" && c.Name == "Compilation")
            .Detail;

        return int.Parse(detail.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<string> CreateRepositoryAsync()
    {
        var path = Path.Combine(_root, "repos", ProjectSlug);
        Directory.CreateDirectory(path);

        await RunGitAsync(path, "init", "--initial-branch", "main");
        await RunGitAsync(path, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(path, "config", "user.name", "Agent Workspace Tests");

        // Neutralise whatever global exclude file the developer's machine has.
        // Without this the suite passes or fails depending on whether loadout
        // protect --global has ever been run here, which is not a property of
        // the code under test.
        await RunGitAsync(path, "config", "core.excludesFile", "");
        await RunGitAsync(path, "remote", "add", "origin", "ssh://git.internal/apps/starstats.git");

        await File.WriteAllTextAsync(Path.Combine(path, "README.md"), "# StarStats");

        await RunGitAsync(path, "add", ".");
        await RunGitAsync(path, "commit", "--message", "initial");

        return path;
    }

    /// <summary>Populates the workspace clone with context files and a manifest.</summary>
    private static async Task BuildWorkspaceAsync(IWorkspaceManager workspace)
    {
        var projectRoot = Path.Combine(workspace.LocalPath, "projects", ProjectSlug);

        Directory.CreateDirectory(Path.Combine(workspace.LocalPath, "global", "instructions"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "context"));

        await File.WriteAllTextAsync(
            Path.Combine(workspace.LocalPath, "global", "instructions", "engineering.md"),
            "Write tests. Keep secrets out of Git.");

        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "context", "architecture.md"),
            "The collector writes to Postgres.");

        var manifest = new ProjectManifest
        {
            Id = Guid.NewGuid().ToString(),
            Slug = ProjectSlug,
            Name = "StarStats",
            Repository = new ProjectRepository { Remote = "ssh://git.internal/apps/starstats.git" },
            Agents = new ProjectAgents { Default = "probe" },
            Context = new ProjectContext
            {
                Global = { "global/instructions/engineering.md" },
                Project = { "context/architecture.md" },
            },
            Profiles =
            {
                ["narrow"] = new ContextProfile
                {
                    Description = "Project context only",
                    IncludeGlobal = false,
                },
            },
        };

        await workspace.WriteProjectAsync(manifest);
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
