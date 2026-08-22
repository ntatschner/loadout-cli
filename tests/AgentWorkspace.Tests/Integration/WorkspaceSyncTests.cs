using AgentWorkspace.Core.Configuration;
using AgentWorkspace.Core.Git;
using AgentWorkspace.Core.Workspace;
using AgentWorkspace.Models.Configuration;
using AgentWorkspace.Models.Platform;
using AgentWorkspace.Platform.Abstractions;
using AgentWorkspace.Platform.Common;
using AgentWorkspace.Platform.Linux;
using AgentWorkspace.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace AgentWorkspace.Tests.Integration;

/// <summary>
/// Exercises workspace synchronisation against a real remote
/// (spec sections 45, 47 and 48).
/// <para>
/// The divergence case is the one that matters most. Spec section 47 says no
/// data loss is acceptable, and a fast-forward that silently became a merge or
/// a reset is exactly how local work disappears.
/// </para>
/// </summary>
public sealed class WorkspaceSyncTests : IAsyncLifetime
{
    private readonly string _root;
    private readonly ProcessLauncher _processes = new();

    private IWorkspaceManager _workspace = null!;
    private string _remote = null!;
    private string _otherClone = null!;

    public WorkspaceSyncTests() =>
        _root = Path.Combine(Path.GetTempPath(), "agentctl-sync-" + Guid.NewGuid().ToString("N"));

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
                "DEV-PC"));

        paths.EnsureDirectoriesExist();

        var git = new GitManager(_processes, new ExecutableResolver(environment, []));

        _workspace = new WorkspaceManager(paths, git, new YamlStore(permissions), TimeProvider.System);

        await BuildRemoteAsync().ConfigureAwait(false);
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
    public async Task An_unconfigured_workspace_reports_local_only_rather_than_failing()
    {
        var result = await _workspace.SyncAsync(new LauncherConfig());

        // Spec section 61 offers "run without central storage" as a real
        // choice, so this must not look like an error.
        result.Succeeded.Should().BeTrue();
        result.Value!.Outcome.Should().Be(WorkspaceSyncOutcome.NotConfigured);
    }

    [Fact]
    public async Task A_first_sync_clones_the_workspace()
    {
        var result = await _workspace.SyncAsync(Config());

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.Outcome.Should().Be(WorkspaceSyncOutcome.Synced);

        _workspace.IsCloned().Should().BeTrue();
        _workspace.IsAvailable().Should().BeTrue();
    }

    [Fact]
    public async Task A_second_sync_fast_forwards_a_remote_change()
    {
        await _workspace.SyncAsync(Config());

        await CommitToRemoteAsync("added by another machine");

        var result = await _workspace.SyncAsync(Config());

        result.Value!.Outcome.Should().Be(WorkspaceSyncOutcome.Synced);

        File.Exists(Path.Combine(_workspace.LocalPath, "from-other-machine.md"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task An_unreachable_remote_degrades_to_offline_with_the_cache_age()
    {
        await _workspace.SyncAsync(Config());

        // Point at somewhere that cannot answer. The launcher must carry on
        // with the cached workspace rather than blocking the user
        // (spec section 48).
        var config = Config();
        config.Workspace.Remote = Path.Combine(_root, "no-such-remote.git");

        await RunGitAsync(_workspace.LocalPath, "remote", "set-url", "origin",
            config.Workspace.Remote.Replace('\\', '/'));

        var result = await _workspace.SyncAsync(config);

        result.Succeeded.Should().BeTrue();
        result.Value!.Outcome.Should().Be(WorkspaceSyncOutcome.Offline);
        result.Value.CachedAtUtc.Should().NotBeNull("the user needs to know how stale the cache is");
    }

    [Fact]
    public async Task A_divergence_preserves_local_work_on_a_recovery_branch()
    {
        await _workspace.SyncAsync(Config());
        await ConfigureIdentityAsync();

        // Both sides move: the local clone gains a commit, and so does the
        // remote. A fast-forward is now impossible.
        await File.WriteAllTextAsync(
            Path.Combine(_workspace.LocalPath, "local-only.md"), "Written on this machine.");

        await RunGitAsync(_workspace.LocalPath, "add", ".");
        await RunGitAsync(_workspace.LocalPath, "commit", "--message", "local work");

        await CommitToRemoteAsync("remote work");

        var result = await _workspace.SyncAsync(Config());

        result.Succeeded.Should().BeTrue();
        result.Value!.Outcome.Should().Be(WorkspaceSyncOutcome.Conflict);

        // The branch is what makes the promise of section 47 real: whatever
        // happens next, the local commit is still reachable by name.
        result.Value.RecoveryBranch.Should().StartWith("recovery/DEV-PC/");

        var branches = await RunGitAsync(_workspace.LocalPath, "branch", "--list");
        branches.Should().Contain("recovery/DEV-PC/");

        // And the local commit itself is untouched.
        File.Exists(Path.Combine(_workspace.LocalPath, "local-only.md")).Should().BeTrue();
    }

    [Fact]
    public async Task A_divergence_never_discards_the_local_commit()
    {
        await _workspace.SyncAsync(Config());
        await ConfigureIdentityAsync();

        await File.WriteAllTextAsync(
            Path.Combine(_workspace.LocalPath, "precious.md"), "Do not lose this.");

        await RunGitAsync(_workspace.LocalPath, "add", ".");
        await RunGitAsync(_workspace.LocalPath, "commit", "--message", "precious work");

        var localHead = (await RunGitAsync(_workspace.LocalPath, "rev-parse", "HEAD")).Trim();

        await CommitToRemoteAsync("remote work");
        await _workspace.SyncAsync(Config());

        var headAfter = (await RunGitAsync(_workspace.LocalPath, "rev-parse", "HEAD")).Trim();

        // The sync must not have moved HEAD. Anything else would mean the
        // launcher rewrote the user's work to make its own life easier.
        headAfter.Should().Be(localHead);
    }

    /// <summary>
    /// Gives the cloned workspace a committer identity.
    /// <para>
    /// A fresh clone inherits none, and a CI runner has no global one either.
    /// The launcher itself does not yet commit to the workspace; when it does,
    /// spec section 63 has the setup wizard ask for an identity, and this is
    /// the gap that makes that necessary.
    /// </para>
    /// </summary>
    private async Task ConfigureIdentityAsync()
    {
        await RunGitAsync(_workspace.LocalPath, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(_workspace.LocalPath, "config", "user.name", "Agent Workspace Tests");
    }

    private LauncherConfig Config() => new()
    {
        Workspace = new WorkspaceSettings
        {
            Remote = _remote.Replace('\\', '/'),
            Branch = "main",
        },
    };

    /// <summary>Creates a bare remote plus a second clone used to push to it.</summary>
    private async Task BuildRemoteAsync()
    {
        _remote = Path.Combine(_root, "remote.git");
        _otherClone = Path.Combine(_root, "other");

        Directory.CreateDirectory(_remote);
        await RunGitAsync(_root, "init", "--bare", "--initial-branch", "main", _remote);

        await RunGitAsync(_root, "clone", _remote.Replace('\\', '/'), _otherClone);
        await RunGitAsync(_otherClone, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(_otherClone, "config", "user.name", "Agent Workspace Tests");

        Directory.CreateDirectory(Path.Combine(_otherClone, "registry"));

        await File.WriteAllTextAsync(
            Path.Combine(_otherClone, "workspace.yaml"),
            "workspace_schema: 1\nname: test\n");

        await File.WriteAllTextAsync(
            Path.Combine(_otherClone, "registry", "projects.yaml"),
            "schema_version: 1\nprojects: []\n");

        await RunGitAsync(_otherClone, "add", ".");
        await RunGitAsync(_otherClone, "commit", "--message", "initial workspace");
        await RunGitAsync(_otherClone, "push", "origin", "main");
    }

    private async Task CommitToRemoteAsync(string message)
    {
        await File.WriteAllTextAsync(
            Path.Combine(_otherClone, "from-other-machine.md"), message);

        await RunGitAsync(_otherClone, "add", ".");
        await RunGitAsync(_otherClone, "commit", "--message", message);
        await RunGitAsync(_otherClone, "push", "origin", "main");
    }

    private async Task<string> RunGitAsync(string workingDirectory, params string[] arguments)
    {
        var result = await _processes.RunAsync(
            new ProcessRequest("git", arguments, workingDirectory),
            TimeSpan.FromSeconds(60));

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.Succeeded.Should().BeTrue(
            $"git {string.Join(' ', arguments)} failed: {result.Value.StandardError}");

        return result.Value.StandardOutput;
    }
}
