using Loadout.Agents;
using Loadout.Core.Configuration;
using Loadout.Core.Context;
using Loadout.Core.Diagnostics;
using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Mcp;
using Loadout.Core.Policies;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models.Agents;
using Loadout.Models.Platform;
using Loadout.Platform;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Launches a real process through the whole pipeline and inspects what arrived.
/// <para>
/// Everything else stops at the launch call and asserts the launcher meant to
/// start something. This runs it. The agent is a stub rather than Claude, which
/// is deliberate: what is under test is whether the launcher compiles the
/// context, resolves the executable, expands the arguments, sets the
/// environment and reports the exit code — none of which needs a model, an API
/// key or a network, and all of which would otherwise only be proved by
/// somebody trying it.
/// </para>
/// <para>
/// What a stub cannot prove is that Claude's own flags are the right ones. That
/// is what the capability probing of spec section 66 is for, and it is checked
/// against the installed binary rather than assumed here.
/// </para>
/// </summary>
public sealed class RealLaunchTests : IAsyncLifetime
{
    private const string Slug = "alpha";

    private readonly string _root;
    private readonly ProcessLauncher _processes = new();

    private IAgentLauncher _launcher = null!;
    private IProjectService _projects = null!;
    private IWorkspaceManager _workspace = null!;
    private string _repository = null!;
    private string _report = null!;

    public RealLaunchTests() =>
        _root = Path.Combine(Path.GetTempPath(), "loadout-launch-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);

        _report = Path.Combine(_root, "received.txt");

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

        var permissions = PlatformServices.CreateFilePermissions();

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
        var git = new GitManager(_processes, resolver);
        var yaml = new YamlStore(permissions);
        var configuration = new ConfigurationService(paths, environment, yaml);

        _workspace = new WorkspaceManager(paths, git, yaml, TimeProvider.System);
        _projects = new ProjectService(configuration, _workspace, git, new PathSemantics());

        var memory = new MemoryService(TimeProvider.System);
        var rules = new RuleService();

        var stub = WriteStubAgent();

        var config = await configuration.LoadConfigAsync().ConfigureAwait(false);

        config.Value!.CustomAgents["stub"] = new GenericAgentDefinition
        {
            DisplayName = "Stub",
            Executable = stub,
            Arguments = ["--context", "${COMPILED_CONTEXT_FILE}", "--project", "${PROJECT_SLUG}"],
            Environment = { ["LOADOUT_STUB_REPORT"] = _report },
        };

        config.Value.DefaultAgent = "stub";

        await configuration.SaveConfigAsync(config.Value).ConfigureAwait(false);

        var reloaded = await configuration.LoadConfigAsync().ConfigureAwait(false);
        var agents = new AgentRegistry(resolver, _processes, reloaded.Value!);

        _launcher = new AgentLauncher(
            _projects,
            _workspace,
            configuration,
            agents,
            paths,
            _processes,
            git,
            new ContextCompiler(permissions, rules, memory),
            new HandoffService(_workspace, TimeProvider.System),
            new InstructionService(
                new SpecialistLibrary(),
                new SpecialistResolver(),
                new RepositoryEvidenceReader(),
                configuration),
            new PreflightService(git, new FakeSecretProvider()),
            new SecurityProfileService(_workspace, yaml),
            new McpService(_workspace));

        _repository = await CreateRepositoryAsync().ConfigureAwait(false);

        await _projects.AddAsync(_repository, Slug).ConfigureAwait(false);
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

    /// <summary>
    /// Writes an agent that records what it was given and exits.
    /// <para>
    /// Two scripts because the shells differ, not because the launcher does:
    /// what is being proved is that the same pipeline delivers the same things
    /// on either platform.
    /// </para>
    /// </summary>
    private string WriteStubAgent()
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(_root, "stub.cmd");

            File.WriteAllText(path, string.Join("\r\n",
            [
                "@echo off",
                "> \"%LOADOUT_STUB_REPORT%\" echo args=%*",
                ">> \"%LOADOUT_STUB_REPORT%\" echo cwd=%CD%",
                // The agent reads the context itself. Reading it afterwards
                // cannot work: the runtime directory is deleted when the
                // session ends, which is the behaviour we want.
                ">> \"%LOADOUT_STUB_REPORT%\" echo --- context ---",
                "if exist %2 type %2 >> \"%LOADOUT_STUB_REPORT%\"",
                "exit /b 7",
            ]));

            return path;
        }

        var script = Path.Combine(_root, "stub.sh");

        File.WriteAllText(script, string.Join("\n",
        [
            "#!/bin/sh",
            "{",
            "  echo \"args=$*\"",
            "  echo \"cwd=$(pwd)\"",
            "  echo '--- context ---'",
            // The agent reads the context itself. Reading it afterwards cannot
            // work: the runtime directory is deleted when the session ends,
            // which is the behaviour we want.
            "  [ -f \"$2\" ] && cat \"$2\"",
            "} > \"$LOADOUT_STUB_REPORT\"",
            "exit 7",
        ]));

        PlatformServices.CreateFilePermissions().MakeExecutable(script);

        return script;
    }

    private async Task<string> CreateRepositoryAsync()
    {
        var path = Path.Combine(_root, "repo");
        Directory.CreateDirectory(path);

        await RunGitAsync(path, "init", "--initial-branch", "work");
        await RunGitAsync(path, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(path, "config", "user.name", "Loadout Tests");
        await RunGitAsync(path, "config", "core.excludesFile", "");
        await RunGitAsync(path, "remote", "add", "origin", "https://example.com/alpha.git");

        await File.WriteAllTextAsync(Path.Combine(path, "README.md"), "source");

        await RunGitAsync(path, "add", ".");
        await RunGitAsync(path, "commit", "-m", "first");

        return path;
    }

    private async Task RunGitAsync(string workingDirectory, params string[] arguments)
    {
        var result = await _processes.RunAsync(
            new ProcessRequest("git", arguments, workingDirectory),
            TimeSpan.FromSeconds(60));

        result.Succeeded.Should().BeTrue(string.Join(' ', arguments));
    }

    private void WriteInstruction(string relative, string contents)
    {
        var path = Path.Combine(_workspace.LocalPath, "projects", Slug, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private string Received => File.ReadAllText(_report);

    [Fact]
    public async Task The_agent_actually_runs_and_its_exit_code_is_the_launchers()
    {
        var result = await _launcher.LaunchAsync(new LaunchRequest(Slug, "stub"));

        result.Succeeded.Should().BeTrue(result.Error ?? string.Empty);

        // 7 is what the stub exits with. A launcher that swallowed it would
        // report every failed agent run as a success.
        result.Value!.AgentExitCode.Should().Be(7);
        File.Exists(_report).Should().BeTrue();
    }

    [Fact]
    public async Task A_dry_run_prepares_everything_and_starts_nothing()
    {
        var result = await _launcher.LaunchAsync(new LaunchRequest(Slug, "stub", DryRun: true));

        result.Succeeded.Should().BeTrue(result.Error ?? string.Empty);

        // The stub writes this the moment it runs. --dry-run is documented as
        // changing nothing, and the launcher read it nowhere: asked what it
        // would do, it started the agent — which on a real terminal is a
        // session opening in front of somebody who asked for a description of
        // one. The second command found doing this, after 'workspace save'.
        File.Exists(_report).Should().BeFalse("a dry run must not start the agent");

        // Everything ahead of the agent still happens, because all of it is
        // preparation and none of it changes anything: the preflight, the
        // compiled context, the specialists. Only the last step is skipped.
        result.Value!.Warnings.Should().Contain(w => w.Contains("Dry run", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_agent_starts_in_the_repository()
    {
        await _launcher.LaunchAsync(new LaunchRequest(Slug, "stub"));

        Received.Should().Contain("cwd=");

        var cwd = Received
            .Split('\n')
            .First(line => line.StartsWith("cwd=", StringComparison.Ordinal))[4..]
            .Trim();

        // Compared through the path semantics rather than as strings, which is
        // the rule this project sets for itself and the reason it has one. On
        // macOS the child reports /private/var/... for a directory created as
        // /var/..., because /var is a link; two names, one directory.
        new PathSemantics().PathsEqual(cwd, _repository).Should().BeTrue();
    }

    [Fact]
    public async Task The_compiled_context_reaches_the_agent_and_holds_the_instructions()
    {
        WriteInstruction("context/architecture.md", "The store is append-only.");

        var manifest = await _workspace.ReadProjectAsync(Slug);
        manifest.Value!.Context.Project.Add("context/architecture.md");
        await _workspace.WriteProjectAsync(manifest.Value);

        await _launcher.LaunchAsync(new LaunchRequest(Slug, "stub"));

        // The point of the whole exercise: an instruction written in the
        // workspace is readable by the agent, at a path the launcher told it
        // about, at the moment it runs.
        Received.Should().Contain("--- context ---");
        Received.Should().Contain("The store is append-only.");
    }

    [Fact]
    public async Task Environment_from_the_agent_definition_reaches_the_child()
    {
        await _launcher.LaunchAsync(new LaunchRequest(Slug, "stub"));

        // The definition sets the report path, and the child wrote to it, so
        // the environment plainly arrived. Asserted explicitly because this is
        // the path a secret travels down.
        File.Exists(_report).Should().BeTrue();
        Received.Should().Contain("args=");
    }

    [Fact]
    public async Task Passthrough_arguments_arrive_after_the_agents_own()
    {
        await _launcher.LaunchAsync(
            new LaunchRequest(Slug, "stub", PassthroughArguments: ["--verbose", "--flag"]));

        var args = Received
            .Split('\n')
            .First(line => line.StartsWith("args=", StringComparison.Ordinal));

        args.Should().Contain("--context");
        args.Should().Contain("--verbose");
        args.Should().Contain("--flag");

        args.IndexOf("--context", StringComparison.Ordinal)
            .Should().BeLessThan(args.IndexOf("--verbose", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_runtime_directory_is_cleaned_up_after_the_agent_exits()
    {
        var result = await _launcher.LaunchAsync(new LaunchRequest(Slug, "stub"));

        result.Succeeded.Should().BeTrue(result.Error ?? string.Empty);

        var runtime = Path.Combine(_root, "cache", "loadout", "runtime");

        // The compiled context aggregates everything the agent was told, so it
        // does not outlive the session that needed it (spec section 82).
        if (Directory.Exists(runtime))
        {
            Directory.EnumerateFileSystemEntries(runtime).Should().BeEmpty();
        }
    }
}
