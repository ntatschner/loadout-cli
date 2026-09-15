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
    private LaunchLedger _ledger = null!;
    private SessionRegistry _running = null!;

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

            // A stand-in Claude Code that speaks stream-json, for the headless
            // path. Found through the search paths the way a real one would
            // be on a machine where it is not on PATH.
            AgentSearchPaths = { await WriteFakeClaudeAsync() },
        };

        await configuration.SaveConfigAsync(config);

        var agents = new AgentRegistry(resolver, _processes, config);
        _ledger = new LaunchLedger(_paths, permissions, TimeProvider.System);
        _running = new SessionRegistry(_paths, permissions, new ProcessInspector(), TimeProvider.System);

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
            _ledger,
            _running,
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

    [Fact]
    public async Task A_headless_launch_holds_the_conversation_and_closes_its_records_when_told()
    {
        var since = DateTimeOffset.UtcNow.AddMinutes(-1);

        var started = await _launcher.StartHeadlessAsync(
            new LaunchRequest(ProjectSlug, "claude", Offline: true, Task: "say pong"),
            new HeadlessOptions(DisableHooks: false));

        started.Succeeded.Should().BeTrue(started.Error);

        await using var launch = started.Value!;

        launch.Session.Should().NotBeNull();
        launch.LaunchId.Should().NotBeNull();
        launch.Plan.Arguments.Should().ContainInOrder("-p", "--verbose", "--input-format", "stream-json");

        // Registered while it runs, under the ledger's identifier.
        (await _running.ListAsync()).Should().ContainSingle(s => s.LaunchId == launch.LaunchId);

        var turn = await launch.Session!.TurnAsync("say pong");

        turn.Completed.Should().BeTrue();
        turn.Text.Should().Be("pong");
        turn.CostUsd.Should().Be(0.01m);
        launch.Session.SessionId.Should().Be("fake-1");

        var (exit, killed) = await launch.Session.EndAsync(TimeSpan.FromSeconds(20));

        killed.Should().BeFalse("the stand-in exits when its input closes");
        exit.Should().Be(0);

        await launch.CompleteAsync(exit);

        var records = await _ledger.ReadAsync(since);
        var record = records.Value!.Should().ContainSingle(r => r.Id == launch.LaunchId).Subject;

        record.Agent.Should().Be("claude");
        record.Task.Should().Be("say pong");
        record.EndedAt.Should().NotBeNull();
        record.ExitCode.Should().Be(0);

        (await _running.ListAsync()).Should().NotContain(s => s.LaunchId == launch.LaunchId);
        Directory.Exists(Path.GetDirectoryName(launch.Plan.ContextPath!)).Should().BeFalse(
            "the runtime directory goes when the records are closed");
    }

    [Fact]
    public async Task A_launch_that_asks_for_a_worktree_gets_one_made_outside_the_repository()
    {
        const string Branch = "teams/20260916-0900-ab12/implementer-1";

        var started = await _launcher.StartHeadlessAsync(
            new LaunchRequest(ProjectSlug, "claude", Offline: true, Worktree: Branch, CreateWorktree: true),
            new HeadlessOptions(DisableHooks: false));

        started.Succeeded.Should().BeTrue(started.Error);

        await using var launch = started.Value!;

        var directory = launch.Plan.WorkingDirectory;

        directory.Should().NotBe(_repository, "the point of a worktree is that it is not the repository's own checkout");
        directory.Should().StartWith(_paths.Paths.State, "a tree inside the repository would need ignoring and would be litter");
        Directory.Exists(directory).Should().BeTrue();

        // A real worktree of that repository, on the branch asked for.
        var head = await _processes.RunAsync(
            new ProcessRequest("git", ["rev-parse", "--abbrev-ref", "HEAD"], directory), TimeSpan.FromSeconds(30));

        head.Value!.StandardOutput.Trim().Should().Be(Branch);

        var listed = await _processes.RunAsync(
            new ProcessRequest("git", ["worktree", "list"], _repository), TimeSpan.FromSeconds(30));

        listed.Value!.StandardOutput.Should().Contain(Branch);

        await launch.CompleteAsync(0);
    }

    [Fact]
    public async Task A_worktree_that_does_not_exist_is_refused_unless_the_caller_asked_for_one()
    {
        var started = await _launcher.StartHeadlessAsync(
            new LaunchRequest(ProjectSlug, "claude", Offline: true, Worktree: "no-such-tree"),
            new HeadlessOptions());

        started.Failed.Should().BeTrue();
        started.Error.Should().Contain("No worktree named 'no-such-tree'");
    }

    [Fact]
    public async Task A_dry_run_says_where_a_worktree_would_go_and_makes_none()
    {
        var before = await _processes.RunAsync(
            new ProcessRequest("git", ["worktree", "list"], _repository), TimeSpan.FromSeconds(30));

        var started = await _launcher.StartHeadlessAsync(
            new LaunchRequest(ProjectSlug, "claude", Offline: true, Worktree: "teams/dry/one", CreateWorktree: true, DryRun: true),
            new HeadlessOptions());

        started.Succeeded.Should().BeTrue(started.Error);

        await using var launch = started.Value!;

        launch.Warnings.Should().Contain(w => w.Contains("would be made at"));
        launch.Plan.WorkingDirectory.Should().Be(_repository, "the launch is described against what it would branch from");

        var after = await _processes.RunAsync(
            new ProcessRequest("git", ["worktree", "list"], _repository), TimeSpan.FromSeconds(30));

        after.Value!.StandardOutput.Should().Be(before.Value!.StandardOutput, "a dry run makes nothing");
    }

    [Fact]
    public async Task An_agent_that_cannot_be_driven_is_refused_as_a_node_before_anything_starts()
    {
        var since = DateTimeOffset.UtcNow.AddMinutes(-1);

        var started = await _launcher.StartHeadlessAsync(
            new LaunchRequest(ProjectSlug, "probe", Offline: true),
            new HeadlessOptions());

        started.Failed.Should().BeTrue();
        started.Error.Should().Contain("cannot be driven without a terminal");

        (await _ledger.ReadAsync(since)).Value.Should().BeEmpty("nothing was launched, so nothing is recorded");
    }

    [Fact]
    public async Task A_headless_dry_run_shows_the_node_line_and_starts_nothing()
    {
        var since = DateTimeOffset.UtcNow.AddMinutes(-1);

        var started = await _launcher.StartHeadlessAsync(
            new LaunchRequest(ProjectSlug, "claude", Offline: true, DryRun: true),
            new HeadlessOptions(MaxTurns: 3, BudgetUsd: 0.5m, DisableHooks: false));

        started.Succeeded.Should().BeTrue(started.Error);

        await using var launch = started.Value!;

        launch.Session.Should().BeNull();
        launch.LaunchId.Should().BeNull();
        launch.Plan.Arguments.Should().ContainInOrder("--max-turns", "3", "--max-budget-usd", "0.5");
        launch.Warnings.Should().Contain("Dry run: nothing was launched.");

        (await _ledger.ReadAsync(since)).Value.Should().BeEmpty();
        (await _running.ListAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// Writes a stand-in Claude Code and returns the directory it is in.
    /// </summary>
    /// <remarks>
    /// Answers the two probes the adapter makes, a version and a help text
    /// carrying every marker the headless path looks for, then waits for one
    /// message on its input, writes the three events a real session writes
    /// for a one-word answer, and exits when its input closes. Nothing here
    /// depends on Claude Code being installed.
    /// </remarks>
    private async Task<string> WriteFakeClaudeAsync()
    {
        var directory = Path.Combine(_root, "fake-claude");
        Directory.CreateDirectory(directory);

        const string Help = """
              --settings <file-or-json>
              --append-system-prompt-file <file>
              --mcp-config <configs...>
              --input-format <format>
              --output-format <format>
              --permission-mode <mode>
              --allowed-tools <tools...>
              --disallowed-tools <tools...>
              --permission-prompt-tool <tool>
              --json-schema <schema>
              --strict-mcp-config
            """;

        const string Init = """{"type":"system","subtype":"init","session_id":"fake-1","model":"fake","mcp_servers":[]}""";
        const string Pong = """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"pong"}]},"parent_tool_use_id":null}""";
        const string Result = """{"type":"result","subtype":"success","is_error":false,"num_turns":1,"duration_ms":1,"total_cost_usd":0.01,"usage":{},"session_id":"fake-1"}""";

        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(directory, "claude.ps1");

            await File.WriteAllTextAsync(script, $$"""
                if ($args -contains '--version') { 'fake claude 9.9.9'; exit 0 }
                if ($args -contains '--help') {
                @'
                {{Help}}
                '@
                exit 0
                }
                $null = [Console]::In.ReadLine()
                [Console]::Out.WriteLine('{{Init}}')
                [Console]::Out.WriteLine('{{Pong}}')
                [Console]::Out.WriteLine('{{Result}}')
                [Console]::Out.Flush()
                while ($null -ne [Console]::In.ReadLine()) { }
                exit 0
                """);

            await File.WriteAllTextAsync(
                Path.Combine(directory, "claude.cmd"),
                $"@powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\" %*\r\n");
        }
        else
        {
            var script = Path.Combine(directory, "claude");

            await File.WriteAllTextAsync(script, $$"""
                #!/bin/sh
                case " $* " in
                  *" --version "*) echo 'fake claude 9.9.9'; exit 0 ;;
                  *" --help "*) cat <<'HELP'
                {{Help}}
                HELP
                exit 0 ;;
                esac
                read -r _first
                echo '{{Init}}'
                echo '{{Pong}}'
                echo '{{Result}}'
                while read -r _line; do :; done
                exit 0
                """.ReplaceLineEndings("\n"));

            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        return directory;
    }
}
