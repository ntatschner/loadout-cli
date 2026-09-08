using Loadout.Cli.Commands;
using Loadout.Core.Configuration;
using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models.Platform;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The tools the launcher serves back to the agent it started.
/// <para>
/// These had no test at all, and the one whose whole job is to say what the
/// session was given was answering from a resolution that had never been shown
/// the repository: it named the foundations and the mode and left out the
/// language and framework, which are exactly the parts chosen for this
/// repository. An agent reading that concludes it has no C# guidance while the
/// C# guidance is sitting in its context.
/// </para>
/// </summary>
public sealed class AgentToolsTests : IAsyncLifetime
{
    private readonly string _root;
    private readonly string _repositories;
    private readonly ThrottledProcessLauncher _processes = new();

    private IProjectService _projects = null!;
    private IWorkspaceManager _workspace = null!;
    private IInstructionService _instructions = null!;
    private IGitManager _git = null!;

    public AgentToolsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-tools-" + Guid.NewGuid().ToString("N"));
        _repositories = Path.Combine(_root, "repos");
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_repositories);

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

        // The Linux layout on every host, as elsewhere in the suite: it is
        // driven purely by the injected environment, so the fixture is
        // identical on the three CI legs.
        var paths = new LinuxPaths(
            environment,
            permissions,
            new HostPlatform(
                HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST-MACHINE"));

        paths.EnsureDirectoriesExist();

        _git = new GitManager(_processes, new ExecutableResolver(environment, []));

        var yaml = new YamlStore(permissions);
        var configuration = new ConfigurationService(paths, environment, yaml);

        var machine = (await configuration.LoadMachineAsync()).Value!;
        machine.DiscoveryRoots = [_repositories];
        await configuration.SaveMachineAsync(machine);

        _workspace = new WorkspaceManager(paths, _git, yaml, TimeProvider.System);
        _projects = new ProjectService(configuration, _workspace, _git, new PathSemantics());

        _instructions = new InstructionService(
            new SpecialistLibrary(),
            new SpecialistResolver(),
            new RepositoryEvidenceReader(),
            configuration);
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
    public async Task Effective_instructions_name_what_the_repository_put_there()
    {
        await RegisterCsharpRepositoryAsync("sharpish");

        var answer = await Tools("sharpish").EffectiveInstructionsAsync();

        // The foundations were never the question: they apply everywhere and
        // were reported correctly even when the repository was not read.
        answer.Should().Contain("foundation.change-safety");

        // These two are the whole point. They are chosen from what is in the
        // repository, so a resolution that never looked at the repository
        // cannot produce them.
        answer.Should().Contain(
            "language.csharp",
            "the session was given the C# specialist, so the tool has to say so");
        answer.Should().Contain(".cs", "and say what in the repository put it there");
    }

    [Fact]
    public async Task Switching_mode_keeps_what_the_repository_put_there()
    {
        await RegisterCsharpRepositoryAsync("sharpish");

        var answer = await Tools("sharpish").ModeAsync("review");

        // The tool's own answer tells the agent that the language and framework
        // specialists come from the repository and so do not change with the
        // mode. That sentence was true of the launch and false of this reply.
        answer.Should().Contain("language.csharp");
        answer.Should().Contain("mode.review");
    }

    /// <summary>
    /// Only the four the two tools under test touch are real. The rest are not
    /// reached on this path, and standing up a memory store, a task store and a
    /// symbol index to leave them unused would hide what the test is about.
    /// </summary>
    private LoadoutTools Tools(string slug) =>
        new(
            _instructions,
            memory: null!,
            _workspace,
            _projects,
            tasks: null!,
            _git,
            symbols: null!,
            TimeProvider.System,
            new LoadoutToolScope(slug));

    private async Task RegisterCsharpRepositoryAsync(string name)
    {
        var path = Path.Combine(_repositories, name);
        Directory.CreateDirectory(path);

        await RunGitAsync(path, "init", "--initial-branch", "main");
        await RunGitAsync(path, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(path, "config", "user.name", "Agent Workspace Tests");
        await RunGitAsync(path, "config", "core.excludesFile", "");
        await RunGitAsync(path, "remote", "add", "origin", "ssh://git.internal/apps/" + name + ".git");

        // Enough of a C# repository for the evidence reader to see one, laid
        // out as a real one is: the specialist's globs are '**/*.cs' and
        // '**/*.csproj', and a source tree keeps those under a directory.
        var source = Path.Combine(path, "src");
        Directory.CreateDirectory(source);

        await File.WriteAllTextAsync(
            Path.Combine(source, "Program.cs"),
            "public static class Program { public static void Main() { } }");

        await File.WriteAllTextAsync(
            Path.Combine(source, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        await RunGitAsync(path, "add", ".");
        await RunGitAsync(path, "commit", "--message", "initial");

        (await _projects.AddAsync(path)).Succeeded.Should().BeTrue();

        // Validating the fixture, not the code: if the project did not resolve
        // to a directory on disk there would be no repository to read, and both
        // tests would fail for a reason that has nothing to do with the tools.
        var resolved = await _projects.ResolveAsync(name);
        resolved.Succeeded.Should().BeTrue(resolved.Error);
        resolved.Value!.LocalPath.Should().NotBeNullOrEmpty();
        Directory.Exists(resolved.Value.LocalPath!).Should().BeTrue();
        Directory.EnumerateFiles(resolved.Value.LocalPath!, "*.cs", SearchOption.AllDirectories).Should().NotBeEmpty();
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
