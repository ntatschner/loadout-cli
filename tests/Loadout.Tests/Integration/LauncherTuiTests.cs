using Loadout.Agents;
using Loadout.Core.Configuration;
using Loadout.Core.Context;
using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Policies;
using Loadout.Core.Projects;
using Loadout.Core.Diagnostics;
using Loadout.Core.Sessions;
using Loadout.Core.Workspace;
using Loadout.Models.Platform;
using Loadout.Platform;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;
using Loadout.Core.Backups;
using Loadout.Platform.Linux;
using Loadout.Platform.Unix;
using Loadout.Tests.Fakes;
using Loadout.Tui;
using FluentAssertions;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Drives the interactive launcher end to end with a scripted keyboard.
/// <para>
/// The launcher was the one part of this tool nothing exercised, on the usual
/// grounds that a terminal UI is hard to test. That is exactly backwards: it is
/// the surface a person actually meets, and every claim about it up to now had
/// been reasoning rather than evidence. Its worst defect — backing out of a
/// project quit the whole launcher — would have been caught immediately by a
/// test that pressed Back and looked at what happened.
/// </para>
/// </summary>
public sealed class LauncherTuiTests : IAsyncLifetime
{
    private readonly string _root;
    private readonly ProcessLauncher _processes = new();

    private TestConsole _console = null!;
    private ILauncherTui _tui = null!;
    private IProjectService _projects = null!;
    private IConfigurationService _configuration = null!;
    private string _repository = null!;

    public LauncherTuiTests() =>
        _root = Path.Combine(Path.GetTempPath(), "loadout-tui-" + Guid.NewGuid().ToString("N"));

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

        _configuration = new ConfigurationService(paths, environment, yaml);

        var workspace = new WorkspaceManager(paths, git, yaml, TimeProvider.System);
        var semantics = new PathSemantics();

        _projects = new ProjectService(_configuration, workspace, git, semantics);

        var memory = new MemoryService(TimeProvider.System);
        var rules = new RuleService();
        var importer = new MemoryImporter(environment, memory);
        var policies = new PolicyService(workspace, git, paths, permissions, yaml);

        var overviews = new ProjectOverviewService(git, workspace, rules, memory, importer, policies);

        var config = await _configuration.LoadConfigAsync().ConfigureAwait(false);
        var agents = new AgentRegistry(resolver, _processes, config.Value!);

        // A real console that reads a scripted keyboard rather than a person.
        _console = new TestConsole().Interactive();
        _console.Profile.Width = 200;

        _tui = new LauncherTui(
            _console,
            _projects,
            workspace,
            _configuration,
            agents,
            new NoOpAgentLauncher(),
            new UnixShellProvider(environment, resolver),
            _processes,
            new ContextCompiler(permissions, rules, memory),
            overviews,
            new NoOpApplicationLauncher(),
            paths,
            new ProjectOnboarding(
                _console,
                _projects,
                new MigrationService(
                    policies,
                    workspace,
                    git,
                    new BackupService(paths, permissions, yaml, TimeProvider.System))),
            new SessionHistoryService(
                [new ClaudeSessionHistory(environment), new CodexSessionHistory(environment)],
                _projects),
            new EmptyCatalogue(),
            new DriftService(_projects, overviews, git),
            new SilentDoctor(),
            new RemediationService(policies, _projects, workspace, importer));

        _repository = await CreateRepositoryAsync("alpha").ConfigureAwait(false);
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

    private async Task<string> CreateRepositoryAsync(string name)
    {
        var path = Path.Combine(_root, "repos", name);
        Directory.CreateDirectory(path);

        await RunGitAsync(path, "init", "--initial-branch", "work");
        await RunGitAsync(path, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(path, "config", "user.name", "Loadout Tests");
        await RunGitAsync(path, "config", "core.excludesFile", "");
        await RunGitAsync(path, "remote", "add", "origin", $"https://example.com/{name}.git");

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

    /// <summary>
    /// The project list, in order. Anything past the projects is a fixed entry,
    /// so a test can count to it.
    /// </summary>
    private const int AddProjectEntry = 1;
    private const int SettingsEntry = 2;

    /// <summary>
    /// The project menu, in order. Two agents ship built in, so the actions
    /// after them sit at fixed offsets.
    /// </summary>
    /// <summary>
    /// Where an entry sits in the project menu, asked of the launcher rather
    /// than written down here.
    /// <para>
    /// These were fixed numbers, and they broke three times in one sitting:
    /// every entry added to the menu moved the ones below it, and the tests
    /// then failed somewhere unrelated to the change that broke them.
    /// </para>
    /// </summary>
    private static int Entry(string label)
    {
        // The two agents that ship built in, which is what the fixture has.
        var actions = LauncherTui.ProjectActions("claude", ["claude", "codex"], hasWarnings: true);

        var index = actions.IndexOf(label);

        index.Should().BeGreaterThanOrEqualTo(0, $"'{label}' should be in the project menu");

        return index;
    }

    private static int ReviewProblems => Entry(LauncherTui.ProblemsEntry);
    private static int BackFromProject => Entry(LauncherTui.Back);

    /// <summary>The settings menu, in order.</summary>
    private const int WorkspaceRepository = 0;

    /// <summary>Back sits below the machine check, which sits above it.</summary>
    private const int BackFromSettings = 6;

    /// <summary>
    /// Anything past the last entry. Spectre stops at the bottom rather than
    /// wrapping, so this reliably selects whatever is last without the test
    /// having to know how many projects exist.
    /// </summary>
    private const int Last = 99;

    /// <summary>Answers a yes/no confirmation.</summary>
    private void Accept() => _console.Input.PushTextWithEnter("y");

    private void Decline() => _console.Input.PushTextWithEnter("n");

    /// <summary>Presses Down n times then Enter, which is how a menu is answered.</summary>
    private void Choose(int down)
    {
        for (var i = 0; i < down; i++)
        {
            _console.Input.PushKey(ConsoleKey.DownArrow);
        }

        _console.Input.PushKey(ConsoleKey.Enter);
    }

    private string Output => _console.Output;

    [Fact]
    public async Task With_nothing_registered_it_offers_to_find_something()
    {
        // Back out of the "where should it look" question, then quit. The point
        // is that the question is asked at all: it used to print two commands
        // and exit, which is a dead end dressed up as help.
        Choose(2);

        await _tui.RunAsync();

        Output.Should().Contain("No projects are registered yet");
        Output.Should().Contain("Where should it look?");
    }

    [Fact]
    public async Task A_registered_project_is_listed_and_can_be_opened()
    {
        await _projects.AddAsync(_repository);

        Choose(0);  // the project
        Choose(Last); // Back, which is the last action
        Choose(Last); // Quit, which is the last entry in the list

        var exit = await _tui.RunAsync();

        exit.Should().Be(0);
        Output.Should().Contain("alpha");
        Output.Should().Contain("work");
    }

    [Fact]
    public async Task Backing_out_of_a_project_returns_to_the_list_rather_than_quitting()
    {
        await _projects.AddAsync(_repository);

        Choose(0);  // open the project
        Choose(Last); // Back
        Choose(Last); // Quit

        await _tui.RunAsync();

        // The list is drawn twice: once before the project and once after
        // coming back. Before this was a loop, the second one never happened
        // and there was no way to look at a second project without restarting.
        CountOf(Output, "Projects").Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task The_overview_says_what_the_session_will_start_with()
    {
        await _projects.AddAsync(_repository);

        Choose(0);
        Choose(Last);
        Choose(Last);

        await _tui.RunAsync();

        // Branch, cleanliness and what loads every session: the things somebody
        // decides on, which used to be a bare path.
        Output.Should().Contain("work");
        Output.Should().Contain("clean");
        Output.Should().Contain("loaded every session");
    }

    [Fact]
    public async Task An_unprotected_clone_is_reported_and_a_fix_is_offered()
    {
        await _projects.AddAsync(_repository);

        Choose(0);
        Choose(ReviewProblems);

        Decline();

        // Reviewing returns to the same menu rather than leaving, so a problem
        // can be read and then acted on or ignored.
        Choose(BackFromProject);
        Choose(Last);

        await _tui.RunAsync();

        Output.Should().Contain("Pre-commit protection");

        // The launcher used to print the command and leave the person to it.
        // Offering to run it is the difference between a diagnosis and a fix.
        Output.Should().Contain("can be put right now");
    }

    [Fact]
    public async Task Declining_the_offer_changes_nothing()
    {
        await _projects.AddAsync(_repository);

        var hook = Path.Combine(_repository, ".git", "hooks", "pre-commit");

        Choose(0);
        Choose(ReviewProblems);

        Decline();

        Choose(BackFromProject);
        Choose(Last);

        await _tui.RunAsync();

        Output.Should().Contain("Nothing was changed");
        File.Exists(hook).Should().BeFalse();
    }

    [Fact]
    public async Task Accepting_the_offer_actually_installs_the_hook()
    {
        await _projects.AddAsync(_repository);

        var hook = Path.Combine(_repository, ".git", "hooks", "pre-commit");

        File.Exists(hook).Should().BeFalse("the repository starts unprotected");

        Choose(0);
        Choose(ReviewProblems);

        Accept();

        Choose(BackFromProject);
        Choose(Last);

        await _tui.RunAsync();

        // The whole point: a menu that reports a problem and then fixes it,
        // asserted against the filesystem rather than against what it printed.
        File.Exists(hook).Should().BeTrue();
    }

    [Fact]
    public async Task Settings_shows_where_everything_is_kept()
    {
        await _projects.AddAsync(_repository);

        Choose(SettingsEntry);
        Choose(BackFromSettings);
        Choose(Last);

        await _tui.RunAsync();

        Output.Should().Contain("Workspace repository");
        Output.Should().Contain("config.yaml");
        Output.Should().Contain("machines.yaml");
    }

    [Fact]
    public async Task The_workspace_repository_can_be_changed_from_the_launcher()
    {
        await _projects.AddAsync(_repository);

        Choose(SettingsEntry);
        Choose(WorkspaceRepository);

        _console.Input.PushTextWithEnter("https://example.com/new-workspace.git");

        Choose(BackFromSettings);
        Choose(Last);

        await _tui.RunAsync();

        var config = await _configuration.LoadConfigAsync();

        // The question that started this: how do I point it at a different
        // private repository, without knowing a command exists.
        config.Value!.Workspace.Remote.Should().Be("https://example.com/new-workspace.git");
    }

    [Fact]
    public async Task Quitting_from_the_list_leaves_without_launching_anything()
    {
        await _projects.AddAsync(_repository);

        Choose(Last);

        var exit = await _tui.RunAsync();

        exit.Should().Be(0);
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}

/// <summary>
/// A doctor that finds nothing, because these tests drive the project screen
/// rather than the machine one. Building a real one would pull the whole
/// platform in for a screen no test here opens.
/// </summary>
internal sealed class SilentDoctor : IDoctorService
{
    public Task<Models.Results.OperationResult<Models.Diagnostics.DiagnosticReport>> RunAsync(
        CancellationToken ct = default) =>
        Task.FromResult(Models.Results.OperationResult<Models.Diagnostics.DiagnosticReport>.Ok(
            new Models.Diagnostics.DiagnosticReport([])));
}

/// <summary>
/// A catalogue with nothing in it. These tests drive the grouped menus; the
/// palette has its own tests, and building a real catalogue here would mean
/// configuring the whole command line for a screen none of them opens.
/// </summary>
internal sealed class EmptyCatalogue : ICommandCatalogue
{
    public IReadOnlyList<CatalogueEntry> Commands => [];

    public Task<int> RunAsync(
        string path,
        IReadOnlyList<string> arguments,
        CancellationToken ct = default) =>
        throw new NotSupportedException("No command is run from these tests.");
}

/// <summary>Stands in for a real agent: the launcher is under test, not Claude.</summary>
internal sealed class NoOpAgentLauncher : IAgentLauncher
{
    public Task<Models.Results.OperationResult<LaunchOutcome>> LaunchAsync(
        LaunchRequest request,
        CancellationToken ct = default) =>
        Task.FromResult(Models.Results.OperationResult<LaunchOutcome>.Ok(
            new LaunchOutcome(0, WorkspaceSyncOutcome.NotConfigured, [], null)));
}

/// <summary>A file manager that opens nothing, because a test has no desktop.</summary>
internal sealed class NoOpApplicationLauncher : IApplicationLauncher
{
    public bool IsAvailable => true;

    public Task<Models.Results.OperationResult> OpenInFileManagerAsync(
        string path,
        CancellationToken ct = default) =>
        Task.FromResult(Models.Results.OperationResult.Ok());

    public Task<Models.Results.OperationResult> OpenUrlAsync(
        string url,
        CancellationToken ct = default) =>
        Task.FromResult(Models.Results.OperationResult.Ok());
}
