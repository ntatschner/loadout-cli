using Loadout.Models;
using Loadout.Core.Configuration;
using Loadout.Core.Git;
using Loadout.Core.Workspace;
using Loadout.Models.Configuration;
using Loadout.Models.Platform;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Integration;

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
        _root = Path.Combine(Path.GetTempPath(), "loadout-sync-" + Guid.NewGuid().ToString("N"));

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

    [Fact]
    public async Task A_created_workspace_is_a_real_repository_with_a_first_commit()
    {
        UseEnvironmentIdentity();

        (await _workspace.InitialiseStructureAsync("local")).Succeeded.Should().BeTrue();

        // Before this existed, "create a new workspace" produced a plain
        // directory: sync had nothing to fetch and save had nothing to commit
        // into, so it looked created while doing nothing.
        _workspace.IsCloned().Should().BeFalse("nothing has initialised it yet");

        var result = await _workspace.InitialiseRepositoryAsync("main");

        result.Succeeded.Should().BeTrue(result.Error);
        _workspace.IsCloned().Should().BeTrue();

        var log = await RunGitAsync(_workspace.LocalPath, "log", "--oneline");
        log.Should().Contain("create workspace");

        // And nothing is left uncommitted, so the first save is not a surprise
        // diff of the structure itself.
        (await _workspace.GetPendingChangesAsync()).Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task A_created_workspace_settles_line_endings_in_the_repository()
    {
        UseEnvironmentIdentity();
        await _workspace.InitialiseStructureAsync("local");
        await _workspace.InitialiseRepositoryAsync("main");

        var attributes = Path.Combine(_workspace.LocalPath, ".gitattributes");

        // The workspace is shared between machines by design. Without this, an
        // instruction file written on Windows reads as modified on Linux and
        // back again: the launcher reports pending changes after every launch
        // for a file nobody edited, and saving commits the churn.
        File.Exists(attributes).Should().BeTrue();
        (await File.ReadAllTextAsync(attributes)).Should().Contain("text=auto eol=lf");
    }

    [Fact]
    public async Task A_created_workspace_ignores_transient_agent_state()
    {
        UseEnvironmentIdentity();
        await _workspace.InitialiseStructureAsync("local");
        await _workspace.InitialiseRepositoryAsync("main");

        // Spec section 12: caches, sessions and logs must never reach the
        // workspace. The launcher writes none of them, but an agent pointed at
        // the workspace might, and one careless commit is all it takes.
        Directory.CreateDirectory(Path.Combine(_workspace.LocalPath, "logs"));
        await File.WriteAllTextAsync(
            Path.Combine(_workspace.LocalPath, "logs", "session.log"), "noise");

        var pending = await _workspace.GetPendingChangesAsync();

        pending.Value!.Should().NotContain(p => p.Contains("session.log"));
    }

    [Fact]
    public async Task Initialising_an_existing_clone_is_a_no_op()
    {
        await _workspace.SyncAsync(Config());

        var head = (await RunGitAsync(_workspace.LocalPath, "rev-parse", "HEAD")).Trim();

        (await _workspace.InitialiseRepositoryAsync("main")).Succeeded.Should().BeTrue();

        // Re-running setup must not reinitialise a workspace somebody is
        // already using.
        (await RunGitAsync(_workspace.LocalPath, "rev-parse", "HEAD")).Trim().Should().Be(head);
    }

    [Fact]
    public async Task Saving_with_nothing_changed_makes_no_commit()
    {
        await _workspace.SyncAsync(Config());
        await ConfigureIdentityAsync();

        var before = (await RunGitAsync(_workspace.LocalPath, "rev-parse", "HEAD")).Trim();

        var result = await _workspace.SaveAsync("StarStats", "claude", push: false);

        // Spec section 46: do not commit meaningless changes. A session that
        // only read must not leave an empty commit behind.
        result.Succeeded.Should().BeTrue(result.Error);
        result.Value.Should().BeFalse();

        (await RunGitAsync(_workspace.LocalPath, "rev-parse", "HEAD")).Trim().Should().Be(before);
    }

    [Fact]
    public async Task Saving_refuses_a_change_that_looks_like_a_credential()
    {
        await _workspace.SyncAsync(Config());
        await ConfigureIdentityAsync();

        var before = (await RunGitAsync(_workspace.LocalPath, "rev-parse", "HEAD")).Trim();

        // A handoff is the route: the agent writes what it worked out, and the
        // exit policy commits it — under sync_exit "always", and pushes it,
        // without anybody being asked. The value is the synthetic one the
        // scanner's own tests use.
        await File.WriteAllTextAsync(
            Path.Combine(_workspace.LocalPath, "handoff.md"),
            "The deploy token is ghp_abcdefghijklmnopqrstuvwxyz0123 and it works.");

        var result = await _workspace.SaveAsync("StarStats", "claude", push: false);

        result.Failed.Should().BeTrue("the workspace is a Git repository that gets pushed");
        result.ExitCode.Should().Be(ExitCode.PolicyViolation);

        // Named, never quoted. A refusal that printed the credential to explain
        // itself would put it in the console and the scrollback.
        result.Error.Should().Contain("handoff.md").And.Contain("GitHub token");
        result.Error.Should().NotContain("ghp_abcdefghijklmnopqrstuvwxyz0123");

        // And nothing was committed, which is the whole point: an audit finding
        // after the fact does not undo a disclosure.
        (await RunGitAsync(_workspace.LocalPath, "rev-parse", "HEAD")).Trim().Should().Be(before);
    }

    [Fact]
    public async Task Saving_records_the_project_agent_and_machine()
    {
        await _workspace.SyncAsync(Config());
        await ConfigureIdentityAsync();

        await File.WriteAllTextAsync(
            Path.Combine(_workspace.LocalPath, "context-note.md"), "Something the session learned.");

        var result = await _workspace.SaveAsync("StarStats", "claude", push: false);

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value.Should().BeTrue();

        var message = await RunGitAsync(_workspace.LocalPath, "log", "-1", "--pretty=%B");

        // The format of spec section 46, so a workspace history reads as a
        // record of which machine did what to which project.
        message.Should().Contain("agent-workspace: update StarStats context");
        message.Should().Contain("Project: StarStats");
        message.Should().Contain("Agent: claude");
        message.Should().Contain("Machine: DEV-PC");
    }

    [Fact]
    public async Task Saving_and_pushing_puts_the_change_on_the_remote()
    {
        await _workspace.SyncAsync(Config());
        await ConfigureIdentityAsync();

        await File.WriteAllTextAsync(
            Path.Combine(_workspace.LocalPath, "shared-note.md"), "Visible to other machines.");

        (await _workspace.SaveAsync("StarStats", "claude", push: true))
            .Succeeded.Should().BeTrue();

        // The other clone stands in for another machine, and this is the whole
        // point of a central workspace: what one machine learns, another sees.
        await RunGitAsync(_otherClone, "pull", "origin", "main");

        File.Exists(Path.Combine(_otherClone, "shared-note.md")).Should().BeTrue();
    }

    [Fact]
    public async Task Pending_changes_are_listed_before_they_are_saved()
    {
        await _workspace.SyncAsync(Config());

        await File.WriteAllTextAsync(
            Path.Combine(_workspace.LocalPath, "pending.md"), "Not committed yet.");

        var pending = await _workspace.GetPendingChangesAsync();

        pending.Succeeded.Should().BeTrue();
        pending.Value!.Should().Contain(p => p.Contains("pending.md"));
    }

    [Fact]
    public async Task A_failed_push_still_reports_the_commit_as_made()
    {
        await _workspace.SyncAsync(Config());
        await ConfigureIdentityAsync();

        await File.WriteAllTextAsync(
            Path.Combine(_workspace.LocalPath, "local-note.md"), "Committed but unpushable.");

        await RunGitAsync(_workspace.LocalPath, "remote", "set-url", "origin",
            Path.Combine(_root, "gone.git").Replace(Path.DirectorySeparatorChar, '/'));

        var result = await _workspace.SaveAsync("StarStats", "claude", push: true);

        // The commit already happened, so nothing is lost; the message has to
        // say that rather than implying the work went nowhere.
        result.Failed.Should().BeTrue();
        result.Error.Should().Contain("committed locally");

        (await RunGitAsync(_workspace.LocalPath, "log", "-1", "--pretty=%B"))
            .Should().Contain("agent-workspace");
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

    /// <summary>
    /// Gives git a committer identity without a repository to configure.
    /// <para>
    /// The first commit of a brand new workspace happens before any repository
    /// exists to hold config, and a CI runner has no global identity either.
    /// These environment variables are what git falls back to, and they are the
    /// only way to supply one at that moment.
    /// </para>
    /// </summary>
    private static void UseEnvironmentIdentity()
    {
        Environment.SetEnvironmentVariable("GIT_AUTHOR_NAME", "Agent Workspace Tests");
        Environment.SetEnvironmentVariable("GIT_AUTHOR_EMAIL", "tests@example.invalid");
        Environment.SetEnvironmentVariable("GIT_COMMITTER_NAME", "Agent Workspace Tests");
        Environment.SetEnvironmentVariable("GIT_COMMITTER_EMAIL", "tests@example.invalid");
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
