using Loadout.Core.Configuration;
using Loadout.Core.Diagnostics;
using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Policies;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models.Diagnostics;
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
/// Covers putting right what the doctor finds.
/// <para>
/// These remedies write hooks and copy memory into a shared repository, so the
/// property that matters most is the one asserted first: a preview changes
/// nothing. Everything else in this tool previews before it mutates, and a fix
/// that acted while claiming to be describing itself would be the worst kind of
/// surprise.
/// </para>
/// </summary>
public sealed class RemediationTests : IAsyncLifetime
{
    private readonly string _root;
    private readonly ProcessLauncher _processes = new();

    private IRemediationService _remediation = null!;
    private IPolicyService _policies = null!;
    private string _repository = null!;

    public RemediationTests() =>
        _root = Path.Combine(Path.GetTempPath(), "loadout-remedy-" + Guid.NewGuid().ToString("N"));

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
        var configuration = new ConfigurationService(paths, environment, yaml);

        var workspace = new WorkspaceManager(paths, git, yaml, TimeProvider.System);
        var projects = new ProjectService(configuration, workspace, git, new PathSemantics());

        _policies = new PolicyService(workspace, git, paths, permissions, yaml);

        _remediation = new RemediationService(
            _policies,
            projects,
            workspace,
            new MemoryImporter(environment, new MemoryService(TimeProvider.System)),
            git);

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

    /// <summary>Commits an agent file, which is the state the fix exists for.</summary>
    private async Task CommitAgentFileAsync(string relative)
    {
        var full = Path.Combine(_repository, relative.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, "agent state");

        await RunGitAsync(_repository, "add", "--force", relative);
        await RunGitAsync(_repository, "commit", "-m", "committed agent state");
    }

    private async Task<string> TrackedFilesAsync()
    {
        var result = await _processes.RunAsync(
            new ProcessRequest("git", ["ls-files"], _repository),
            TimeSpan.FromSeconds(60));

        return result.Value!.StandardOutput;
    }

    private async Task<string> RevisionsAsync()
    {
        var result = await _processes.RunAsync(
            new ProcessRequest("git", ["rev-list", "--all"], _repository),
            TimeSpan.FromSeconds(60));

        return result.Value!.StandardOutput;
    }

    [Fact]
    public async Task Previewing_the_untracking_stages_nothing()
    {
        await CommitAgentFileAsync(".serena/project.yml");

        var before = await TrackedFilesAsync();

        var preview = await _remediation.PreviewAsync(
            new Remedy(RemedyKind.UntrackAgentFiles, "Untrack", _repository));

        preview.Succeeded.Should().BeTrue(preview.Error ?? string.Empty);
        preview.Value!.Applied.Should().BeFalse();

        // Says which files, so nobody has to take it on trust.
        preview.Value.Detail.Should().Contain(".serena/project.yml");

        (await TrackedFilesAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Applying_it_untracks_the_file_but_leaves_it_on_disk()
    {
        await CommitAgentFileAsync(".serena/project.yml");

        var applied = await _remediation.ApplyAsync(
            new Remedy(RemedyKind.UntrackAgentFiles, "Untrack", _repository));

        applied.Succeeded.Should().BeTrue(applied.Error ?? string.Empty);

        (await TrackedFilesAsync()).Should().NotContain(".serena/project.yml");

        // The whole point: the file is still there. Somebody's agent
        // configuration is not deleted to satisfy a policy.
        File.Exists(Path.Combine(_repository, ".serena", "project.yml")).Should().BeTrue();
    }

    [Fact]
    public async Task Untracking_does_not_touch_history()
    {
        await CommitAgentFileAsync(".serena/project.yml");

        var before = await RevisionsAsync();

        await _remediation.ApplyAsync(
            new Remedy(RemedyKind.UntrackAgentFiles, "Untrack", _repository));

        // The reason this was advice rather than a fix for so long was a belief
        // that untracking rewrites the repository. Every commit that existed
        // before still exists, with the same hash.
        (await RevisionsAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Untracking_when_there_is_nothing_to_untrack_is_harmless()
    {
        var applied = await _remediation.ApplyAsync(
            new Remedy(RemedyKind.UntrackAgentFiles, "Untrack", _repository));

        applied.Succeeded.Should().BeTrue(applied.Error ?? string.Empty);
        applied.Value!.Detail.Should().Contain("Nothing");
    }

    [Fact]
    public async Task Untracking_without_a_repository_fails_rather_than_guessing()
    {
        var applied = await _remediation.ApplyAsync(
            new Remedy(RemedyKind.UntrackAgentFiles, "Untrack"));

        applied.Failed.Should().BeTrue();
        applied.Error.Should().NotBeNullOrWhiteSpace();
    }

    private async Task RunGitAsync(string workingDirectory, params string[] arguments)
    {
        var result = await _processes.RunAsync(
            new ProcessRequest("git", arguments, workingDirectory),
            TimeSpan.FromSeconds(60));

        result.Succeeded.Should().BeTrue(string.Join(' ', arguments));
    }

    private string HookPath => Path.Combine(_repository, ".git", "hooks", "pre-commit");

    [Fact]
    public async Task Previewing_a_fix_changes_nothing()
    {
        var remedy = new Remedy(
            RemedyKind.InstallPreCommitHook,
            "Install the pre-commit hook",
            _repository);

        var preview = await _remediation.PreviewAsync(remedy);

        preview.Succeeded.Should().BeTrue(preview.Error ?? string.Empty);
        preview.Value!.Applied.Should().BeFalse();

        // The whole contract of a preview, asserted against the filesystem
        // rather than against what the result claims.
        File.Exists(HookPath).Should().BeFalse();
    }

    [Fact]
    public async Task Applying_a_fix_installs_the_hook()
    {
        var remedy = new Remedy(
            RemedyKind.InstallPreCommitHook,
            "Install the pre-commit hook",
            _repository);

        var applied = await _remediation.ApplyAsync(remedy);

        applied.Succeeded.Should().BeTrue(applied.Error ?? string.Empty);
        applied.Value!.Applied.Should().BeTrue();

        File.Exists(HookPath).Should().BeTrue();

        // The finding it was meant to clear is actually cleared. A remedy that
        // reports success without changing the verdict is the failure mode this
        // whole feature has to avoid.
        var report = await _policies.CheckAsync(_repository);

        report.Value!.HasPreCommitHook.Should().BeTrue();
    }

    [Fact]
    public async Task Applying_the_same_fix_twice_is_harmless()
    {
        var remedy = new Remedy(
            RemedyKind.InstallPreCommitHook,
            "Install the pre-commit hook",
            _repository);

        await _remediation.ApplyAsync(remedy);

        // Doctor is run repeatedly and --fix will be too, so no remedy may
        // depend on being the first to touch what it changes.
        var second = await _remediation.ApplyAsync(remedy);

        second.Succeeded.Should().BeTrue(second.Error ?? string.Empty);
        File.Exists(HookPath).Should().BeTrue();
    }

    [Fact]
    public async Task A_fix_with_no_target_fails_rather_than_guessing()
    {
        var remedy = new Remedy(RemedyKind.InstallPreCommitHook, "Install it somewhere", Target: null);

        var result = await _remediation.ApplyAsync(remedy);

        // Picking a repository on somebody's behalf would install a hook
        // wherever the shell happened to be standing.
        result.Failed.Should().BeTrue();
        result.Error.Should().Contain("repository");
    }

    [Fact]
    public async Task Importing_memory_for_an_unknown_project_fails_clearly()
    {
        var remedy = new Remedy(
            RemedyKind.ImportProjectMemory,
            "Import memory for a project that is not here",
            "no-such-project");

        var result = await _remediation.ApplyAsync(remedy);

        result.Failed.Should().BeTrue();
        result.ExitCode.Should().Be(Loadout.Models.ExitCode.ProjectNotFound);
    }

    [Fact]
    public void The_same_fix_found_twice_is_offered_once()
    {
        var remedy = new Remedy(RemedyKind.RepairGlobalExcludes, "Repair the excludes");

        var report = new DiagnosticReport(
        [
            DiagnosticCheck.Warn("Git", "Global exclude file", "missing", remedy),
            DiagnosticCheck.Warn("Git", "Global exclude file", "also missing", remedy),
            DiagnosticCheck.Ok("Git", "Installed", "git 2.54"),
        ]);

        // More than one check can notice the same underlying problem, and
        // running its fix twice would be at best wasted and at worst confusing
        // to read in the output.
        report.Remedies.Should().ContainSingle();
    }

    [Fact]
    public void A_report_with_nothing_fixable_offers_nothing()
    {
        var report = new DiagnosticReport(
        [
            DiagnosticCheck.Ok("Git", "Installed", "git 2.54"),
            DiagnosticCheck.Warn("Agents", "Capabilities", "not detected: external_prompt_file"),
        ]);

        // A finding somebody else has to decide about must not appear as
        // something the launcher will handle.
        report.Remedies.Should().BeEmpty();
    }
}
