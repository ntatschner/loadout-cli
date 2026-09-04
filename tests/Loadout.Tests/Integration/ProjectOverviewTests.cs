using Loadout.Core.Configuration;
using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Policies;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models.Platform;
using Loadout.Models.Projects;
using Loadout.Platform.Abstractions;
using Loadout.Platform;
using Loadout.Platform.Common;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Loadout.Tui;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The launcher shows this before starting a session, so what it reports is
/// what somebody decides on. A number that is wrong here is worse than one that
/// is missing: it invites a launch that should not have happened.
/// </summary>
public sealed class ProjectOverviewTests : IAsyncLifetime
{
    private const string Slug = "starstats";

    private readonly string _root;
    private readonly ProcessLauncher _processes = new();

    private IProjectOverviewService _overviews = null!;
    private readonly Loadout.Tests.Fakes.QuietSessionRegistry _running = new();
    private IWorkspaceManager _workspace = null!;
    private IGitManager _git = null!;
    private string _repository = null!;

    public ProjectOverviewTests() =>
        _root = Path.Combine(Path.GetTempPath(), "loadout-overview-" + Guid.NewGuid().ToString("N"));

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
            // Without this the executable resolver cannot find git, and the
            // overview quietly reports no branch and a clean tree for every
            // repository — which is exactly the shape of a wrong answer that
            // looks like a right one.
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

        _git = new GitManager(_processes, new ExecutableResolver(environment, []));

        var yaml = new YamlStore(permissions);

        _workspace = new WorkspaceManager(paths, _git, yaml, TimeProvider.System);

        var memory = new MemoryService(TimeProvider.System);
        var configuration = new ConfigurationService(paths, environment, yaml);

        _overviews = new ProjectOverviewService(
            _git,
            _workspace,
            new RuleService(),
            memory,
            new MemoryImporter(environment, memory),
            new PolicyService(_workspace, _git, paths, permissions, yaml),
            new InstructionService(
                new SpecialistLibrary(),
                new SpecialistResolver(),
                new RepositoryEvidenceReader(),
                configuration),
            _running);

        _repository = await CreateRepositoryAsync().ConfigureAwait(false);
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

    private async Task<string> CreateRepositoryAsync()
    {
        var path = Path.Combine(_root, "repo");
        Directory.CreateDirectory(path);

        await RunGitAsync(path, "init", "--initial-branch", "work");
        await RunGitAsync(path, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(path, "config", "user.name", "Loadout Tests");
        await RunGitAsync(path, "config", "core.excludesFile", "");

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

    private ProjectResolution Project() => new(
        new ProjectRegistryEntry { Slug = Slug, Name = "StarStats", DefaultAgent = "claude" },
        _repository,
        null,
        0,
        false);

    private void WriteRule(string name, string contents)
    {
        var directory = Path.Combine(_workspace.LocalPath, "projects", Slug, "rules");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".md"), contents);
    }

    [Fact]
    public async Task It_reports_the_branch_and_a_clean_tree()
    {
        var overview = await _overviews.DescribeAsync(Project());

        overview.Succeeded.Should().BeTrue(overview.Error ?? string.Empty);
        overview.Value!.Branch.Should().Be("work");
        overview.Value.IsClean.Should().BeTrue();
    }

    [Fact]
    public async Task Uncommitted_work_is_reported_before_a_launch()
    {
        await File.WriteAllTextAsync(Path.Combine(_repository, "scratch.txt"), "in progress");

        var overview = await _overviews.DescribeAsync(Project());

        overview.Value!.IsClean.Should().BeFalse();
    }

    [Fact]
    public async Task Only_the_rules_that_load_every_session_count_towards_the_budget()
    {
        WriteRule("always", "---\nalwaysApply: true\n---\n" + new string('x', 2000));
        WriteRule("scoped", "---\nglobs: src/Data/**\n---\n" + new string('y', 9000));

        var overview = await _overviews.DescribeAsync(Project());

        // The scoped rule is free until somebody touches the paths it names, so
        // counting it would report a cost nobody is paying and push people to
        // scope things that already are.
        overview.Value!.AlwaysLoadedBytes.Should().BeGreaterThan(2000).And.BeLessThan(2200);
        overview.Value.ScopedRules.Should().Be(1);
        overview.Value.IsOverBudget.Should().BeFalse();
    }

    [Fact]
    public async Task An_oversized_instruction_layer_is_flagged()
    {
        WriteRule("huge", "---\nalwaysApply: true\n---\n" + new string('x', 30_000));

        var overview = await _overviews.DescribeAsync(Project());

        overview.Value!.IsOverBudget.Should().BeTrue();
        overview.Value.HasWarnings.Should().BeTrue();
    }

    [Fact]
    public async Task A_repository_with_no_hook_is_reported_as_unprotected()
    {
        var overview = await _overviews.DescribeAsync(Project());

        // Hooks are per-clone, so a fresh clone never has one. Saying so is the
        // difference between protection people think they have and protection
        // they do.
        overview.Value!.Protected.Should().BeFalse();
        LauncherTui.Warnings(overview.Value).Should().Contain(w => w.Contains("pre-commit"));
    }

    [Fact]
    public async Task A_project_that_is_not_on_this_machine_still_describes_itself()
    {
        var absent = new ProjectResolution(
            new ProjectRegistryEntry { Slug = Slug, Name = "StarStats" }, null, null, 0, false);

        var overview = await _overviews.DescribeAsync(absent);

        // An overview that failed because the repository is elsewhere would
        // leave the launcher with nothing to show on the one screen whose job
        // is to offer to fetch it.
        overview.Succeeded.Should().BeTrue(overview.Error ?? string.Empty);
        overview.Value!.Branch.Should().BeNull();
    }

    [Fact]
    public void The_current_repository_is_offered_first()
    {
        var first = new ProjectResolution(
            new ProjectRegistryEntry { Slug = "alpha", Name = "Alpha" }, "/a", null, 0, false);

        var here = new ProjectResolution(
            new ProjectRegistryEntry { Slug = "omega", Name = "Omega" }, "/o", null, 0, false);

        // Whatever the recency ordering says, the repository you are standing in
        // is almost always the one you meant.
        LauncherTui.Order([first, here], here)
            .Select(p => p.Entry.Slug).Should().Equal("omega", "alpha");
    }

    [Fact]
    public void Ordering_is_left_alone_when_the_directory_is_not_a_project()
    {
        var first = new ProjectResolution(
            new ProjectRegistryEntry { Slug = "alpha", Name = "Alpha" }, "/a", null, 0, false);

        var second = new ProjectResolution(
            new ProjectRegistryEntry { Slug = "omega", Name = "Omega" }, "/o", null, 0, false);

        LauncherTui.Order([first, second], null)
            .Select(p => p.Entry.Slug).Should().Equal("alpha", "omega");
    }

    [Fact]
    public async Task A_project_nobody_is_working_in_says_nothing_about_sessions()
    {
        var overview = await _overviews.DescribeAsync(Project());

        // The ordinary case, and it should cost no attention at all. A line
        // saying "0 sessions" would be noise on every project every time.
        overview.Value!.RunningSessions.Should().Be(0);
        Loadout.Tui.Terminal.ProjectDetailView.RunningNote(overview.Value).Should().BeEmpty();
    }

    [Fact]
    public void A_session_open_against_the_project_is_said_plainly()
    {
        var one = Overview(running: 1);
        var several = Overview(running: 3);

        // The only line on the panel that changes what somebody does next
        // rather than describing how things stand: launching a second agent
        // into a repository somebody is already working in is the mistake it
        // exists to prevent.
        Loadout.Tui.Terminal.ProjectDetailView.RunningNote(one).Should().Be("a session is running here");
        Loadout.Tui.Terminal.ProjectDetailView.RunningNote(several).Should().Be("3 sessions are running here");
    }

    [Fact]
    public void A_running_session_is_not_filed_as_something_wrong()
    {
        // Not in the warnings, deliberately. A session running is a fact about
        // right now, not a problem with the project, and putting it under
        // "needs attention" would train somebody to ignore that list.
        // Both surfaces, because there are two Warnings methods — the plain
        // listing's and the panel's — and a rule enforced in one of them is
        // enforced nowhere in particular.
        LauncherTui.Warnings(Overview(running: 2))
            .Should().NotContain(w => w.Contains("running", StringComparison.OrdinalIgnoreCase));

        Loadout.Tui.Terminal.ProjectDetailView.Warnings(Overview(running: 2))
            .Should().NotContain(w => w.Contains("running", StringComparison.OrdinalIgnoreCase));
    }

    private static ProjectOverview Overview(int running) =>
        new(
            new ProjectResolution(
                new ProjectRegistryEntry { Slug = Slug, Name = "StarStats" }, null, null, 0, false),
            "main",
            true,
            100,
            0,
            0,
            0,
            true,
            0,
            false,
            null,
            running);

    [Fact]
    public async Task Sessions_open_against_this_project_are_counted_and_others_are_not()
    {
        _running.Running.Add(Session(Slug));
        _running.Running.Add(Session(Slug));
        _running.Running.Add(Session("something-else"));

        var overview = await _overviews.DescribeAsync(Project());

        // Counted from the registry rather than handed in, and filtered to
        // this project: a panel that showed every session on the machine
        // would say the same thing whichever project was selected.
        overview.Value!.RunningSessions.Should().Be(2);
    }

    private static Loadout.Core.Sessions.RunningSession Session(string slug) =>
        new(
            Guid.NewGuid().ToString("N"),
            slug,
            slug,
            "claude",
            null,
            "C:/work/" + slug,
            1234,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
}
