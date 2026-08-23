using AgentWorkspace.Core.Configuration;
using AgentWorkspace.Core.Git;
using AgentWorkspace.Core.Policies;
using AgentWorkspace.Core.Workspace;
using AgentWorkspace.Models.Platform;
using AgentWorkspace.Models.Policies;
using AgentWorkspace.Platform.Abstractions;
using AgentWorkspace.Platform.Common;
using AgentWorkspace.Platform.Linux;
using AgentWorkspace.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace AgentWorkspace.Tests.Integration;

/// <summary>
/// Exercises repository policy and migration against real Git repositories
/// (spec sections 27, 49, 50, 51 and 97).
/// <para>
/// The distinction that matters throughout is tracked versus untracked. A
/// tracked agent file is a committed violation that only a Git commit can undo;
/// an untracked one can simply be moved. Getting that wrong means either
/// deleting somebody's committed work or leaving the repository dirty while
/// claiming success.
/// </para>
/// </summary>
public sealed class PolicyAndMigrationTests : IAsyncLifetime
{
    private readonly string _root;
    private readonly ProcessLauncher _processes = new();

    private IPolicyService _policies = null!;
    private IMigrationService _migrations = null!;
    private IWorkspaceManager _workspace = null!;
    private string _repository = null!;

    public PolicyAndMigrationTests() =>
        _root = Path.Combine(Path.GetTempPath(), "agentctl-policy-" + Guid.NewGuid().ToString("N"));

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

        var paths = new LinuxPaths(
            environment,
            permissions,
            new HostPlatform(
                HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST-MACHINE"));

        paths.EnsureDirectoriesExist();

        var git = new GitManager(_processes, new ExecutableResolver(environment, []));
        var yaml = new YamlStore(permissions);

        _workspace = new WorkspaceManager(paths, git, yaml, TimeProvider.System);
        _policies = new PolicyService(_workspace, git, paths, permissions, yaml);
        _migrations = new MigrationService(_policies, _workspace, git);

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

    [Fact]
    public async Task Tracked_agent_files_are_reported_as_violations()
    {
        var report = (await _policies.CheckAsync(_repository)).Value!;

        report.IsCompliant.Should().BeFalse();
        report.Verdict.Should().Be("NON-COMPLIANT");
        report.Violations.Select(v => v.Path).Should().Contain("CLAUDE.md");
    }

    [Fact]
    public async Task Untracked_but_visible_files_warn_rather_than_fail()
    {
        var report = (await _policies.CheckAsync(_repository)).Value!;

        // One "git add ." away from being committed, which is worth saying
        // without calling the repository non-compliant for it.
        report.Warnings.Select(w => w.Path)
            .Should().Contain(p => p.Contains("settings.local.json"));
    }

    [Fact]
    public async Task An_ignored_file_is_neither_a_violation_nor_a_warning()
    {
        var report = (await _policies.CheckAsync(_repository)).Value!;

        var ignored = report.Findings
            .Where(f => f.Kind == PolicyFindingKind.Ignored)
            .Select(f => f.Path)
            .ToList();

        // .codex is in the repository's .gitignore, which is the system working
        // exactly as intended rather than a problem to report.
        ignored.Should().Contain(p => p.Contains("codex"));
        report.Violations.Should().NotContain(f => f.Path.Contains("codex"));
        report.Warnings.Should().NotContain(f => f.Path.Contains("codex"));
    }

    [Fact]
    public async Task A_clean_repository_is_reported_as_compliant()
    {
        var clean = Path.Combine(_root, "clean");
        Directory.CreateDirectory(clean);

        await RunGitAsync(clean, "init", "--initial-branch", "main");
        await RunGitAsync(clean, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(clean, "config", "user.name", "Agent Workspace Tests");
        await File.WriteAllTextAsync(Path.Combine(clean, "README.md"), "# Clean");
        await RunGitAsync(clean, "add", ".");
        await RunGitAsync(clean, "commit", "--message", "initial");

        var report = (await _policies.CheckAsync(clean)).Value!;

        report.IsCompliant.Should().BeTrue();
        report.Verdict.Should().Be("COMPLIANT");
    }

    [Fact]
    public async Task An_allowed_pattern_exempts_a_file_from_the_policy()
    {
        // Spec section 9 says a project may deliberately version something like
        // AGENTS.md, so the policy must be able to say yes.
        await WritePolicyAsync(new RepositoryPolicy
        {
            Forbidden = ["CLAUDE.md", ".claude/**"],
            Allowed = ["CLAUDE.md"],
        });

        var report = (await _policies.CheckAsync(_repository)).Value!;

        report.Violations.Should().NotContain(v => v.Path == "CLAUDE.md");
    }

    [Fact]
    public async Task Migration_plans_every_known_agent_directory()
    {
        var plan = (await _migrations.PlanAsync(_repository, "demo")).Value!;

        plan.Steps.Select(s => s.RepositoryRelativePath)
            .Should().Contain([".claude", "CLAUDE.md"]);

        plan.Steps.Single(s => s.RepositoryRelativePath == "CLAUDE.md")
            .WorkspaceRelativePath.Should().Be("projects/demo/agents/claude/instructions.md");
    }

    [Fact]
    public async Task Planning_changes_nothing_on_disk()
    {
        await _migrations.PlanAsync(_repository, "demo");

        // A plan the user has not agreed to must not have side effects.
        File.Exists(Path.Combine(_repository, "CLAUDE.md")).Should().BeTrue();
        Directory.Exists(Path.Combine(_workspace.LocalPath, "projects", "demo")).Should().BeFalse();
    }

    [Fact]
    public async Task Applying_copies_into_the_workspace()
    {
        var plan = (await _migrations.PlanAsync(_repository, "demo")).Value!;

        var applied = await _migrations.ApplyAsync(plan);

        applied.Succeeded.Should().BeTrue(applied.Error);

        var instructions = Path.Combine(
            _workspace.LocalPath, "projects", "demo", "agents", "claude", "instructions.md");

        File.Exists(instructions).Should().BeTrue();
        (await File.ReadAllTextAsync(instructions)).Should().Contain("Repository instructions");
    }

    [Fact]
    public async Task A_tracked_file_is_copied_but_never_deleted()
    {
        var plan = (await _migrations.PlanAsync(_repository, "demo")).Value!;

        var applied = (await _migrations.ApplyAsync(plan)).Value!;

        // Spec section 27 is explicit that tracked files must not be silently
        // deleted: removing one rewrites the repository and belongs in a commit
        // the user makes deliberately.
        File.Exists(Path.Combine(_repository, "CLAUDE.md")).Should().BeTrue();
        applied.TrackedLeftInPlace.Should().Contain("CLAUDE.md");
    }

    [Fact]
    public async Task An_untracked_directory_is_moved_out_of_the_repository()
    {
        var untracked = Path.Combine(_repository, ".cursor");
        Directory.CreateDirectory(untracked);
        await File.WriteAllTextAsync(Path.Combine(untracked, "rules.md"), "Cursor rules.");

        var plan = (await _migrations.PlanAsync(_repository, "demo")).Value!;
        await _migrations.ApplyAsync(plan);

        // Nothing committed it, so nothing is lost by moving it, and moving it
        // is the only way the repository actually becomes clean.
        Directory.Exists(untracked).Should().BeFalse();

        File.Exists(Path.Combine(
            _workspace.LocalPath, "projects", "demo", "agents", "cursor", "rules.md"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Files_git_already_ignores_are_left_alone_by_default()
    {
        // .codex is in this repository's .gitignore. It is not in the
        // repository's content and never will be, so the repository is already
        // compliant with respect to it. Taking it would remove a working local
        // setup to solve a problem that does not exist.
        var plan = (await _migrations.PlanAsync(_repository, "demo")).Value!;

        plan.Steps.Should().NotContain(s => s.RepositoryRelativePath == ".codex");
        Directory.Exists(Path.Combine(_repository, ".codex")).Should().BeTrue();
    }

    [Fact]
    public async Task Ignored_files_move_when_they_are_explicitly_asked_for()
    {
        var plan = (await _migrations.PlanAsync(_repository, "demo", includeIgnored: true)).Value!;

        // Sharing them across machines is a legitimate reason to want them in
        // the workspace, so it is offered rather than forbidden.
        plan.Steps.Should().Contain(s => s.RepositoryRelativePath == ".codex");
    }

    [Fact]
    public async Task Applying_the_default_plan_leaves_an_ignored_directory_in_place()
    {
        var plan = (await _migrations.PlanAsync(_repository, "demo")).Value!;

        await _migrations.ApplyAsync(plan);

        Directory.Exists(Path.Combine(_repository, ".codex")).Should().BeTrue();
        File.Exists(Path.Combine(_repository, ".codex", "config.toml")).Should().BeTrue();
    }

    [Fact]
    public async Task The_pre_commit_hook_installs_and_reports_itself()
    {
        (await _policies.InstallHookAsync(_repository)).Succeeded.Should().BeTrue();

        var hook = Path.Combine(_repository, ".git", "hooks", "pre-commit");

        File.Exists(hook).Should().BeTrue();
        (await File.ReadAllTextAsync(hook)).Should().Contain("Commit blocked");

        (await _policies.CheckAsync(_repository)).Value!.HasPreCommitHook.Should().BeTrue();
    }

    [Fact]
    public async Task A_hook_the_launcher_did_not_write_is_never_overwritten()
    {
        var hook = Path.Combine(_repository, ".git", "hooks", "pre-commit");
        Directory.CreateDirectory(Path.GetDirectoryName(hook)!);
        await File.WriteAllTextAsync(hook, "#!/bin/sh\n# somebody else's hook\nexit 0\n");

        var install = await _policies.InstallHookAsync(_repository);

        install.Failed.Should().BeTrue();

        // Clobbering a hook the launcher knows nothing about would destroy work
        // it cannot restore.
        (await File.ReadAllTextAsync(hook)).Should().Contain("somebody else");
    }

    [Fact]
    public async Task Removing_a_foreign_hook_is_refused_too()
    {
        var hook = Path.Combine(_repository, ".git", "hooks", "pre-commit");
        Directory.CreateDirectory(Path.GetDirectoryName(hook)!);
        await File.WriteAllTextAsync(hook, "#!/bin/sh\nexit 0\n");

        (await _policies.RemoveHookAsync(_repository)).Failed.Should().BeTrue();
        File.Exists(hook).Should().BeTrue();
    }

    [Fact]
    public async Task The_launchers_own_hook_can_be_removed()
    {
        await _policies.InstallHookAsync(_repository);

        (await _policies.RemoveHookAsync(_repository)).Succeeded.Should().BeTrue();
        File.Exists(Path.Combine(_repository, ".git", "hooks", "pre-commit")).Should().BeFalse();
    }

    [Fact]
    public async Task The_default_policy_does_not_forbid_a_deliberately_versioned_agents_file()
    {
        // Spec section 9 names AGENTS.md as the example of a file a project may
        // legitimately choose to version, so the shipped default must not fight
        // that choice.
        await File.WriteAllTextAsync(Path.Combine(_repository, "AGENTS.md"), "Shared agent notes.");
        await RunGitAsync(_repository, "add", "AGENTS.md");
        await RunGitAsync(_repository, "commit", "--message", "add agents file");

        var report = (await _policies.CheckAsync(_repository)).Value!;

        report.Findings.Should().NotContain(f => f.Path == "AGENTS.md");
    }

    private async Task WritePolicyAsync(RepositoryPolicy policy)
    {
        var directory = Path.Combine(_workspace.LocalPath, "policies");
        Directory.CreateDirectory(directory);

        var yaml = new YamlStore(new NoOpFilePermissions());

        await yaml.SaveAsync(
            Path.Combine(directory, "forbidden-repository-files.yaml"),
            policy,
            restrictPermissions: false);
    }

    /// <summary>
    /// Builds a repository holding one of each: a tracked agent file, an
    /// untracked one, and an ignored one.
    /// </summary>
    private async Task<string> CreateRepositoryAsync()
    {
        var path = Path.Combine(_root, "repo");
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, ".claude"));
        Directory.CreateDirectory(Path.Combine(path, ".codex"));

        await RunGitAsync(path, "init", "--initial-branch", "main");
        await RunGitAsync(path, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(path, "config", "user.name", "Agent Workspace Tests");
        await RunGitAsync(path, "remote", "add", "origin", "ssh://git.internal/apps/demo.git");

        await File.WriteAllTextAsync(Path.Combine(path, ".gitignore"), ".codex/\n");
        await File.WriteAllTextAsync(Path.Combine(path, "README.md"), "# Demo");
        await File.WriteAllTextAsync(Path.Combine(path, "CLAUDE.md"), "Repository instructions.");
        await File.WriteAllTextAsync(
            Path.Combine(path, ".claude", "settings.json"), "{\"tracked\": true}");

        await RunGitAsync(path, "add", ".gitignore", "README.md", "CLAUDE.md", ".claude/settings.json");
        await RunGitAsync(path, "commit", "--message", "initial");

        // Written after the commit so they stay untracked and ignored.
        await File.WriteAllTextAsync(
            Path.Combine(path, ".claude", "settings.local.json"), "{\"untracked\": true}");
        await File.WriteAllTextAsync(
            Path.Combine(path, ".codex", "config.toml"), "model = \"gpt\"");

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
