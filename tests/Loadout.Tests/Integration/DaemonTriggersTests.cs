using System.Collections.Concurrent;
using System.Text.Json;
using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Core.Triggers;
using Loadout.Models.Platform;
using Loadout.Models.Triggers;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Loadout.Tui;
using Spectre.Console;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The daemon with triggers switched on: finished runs and webhooks reach the
/// rules, and what the rules start goes through the command line.
/// </summary>
/// <remarks>
/// The rules here start built-ins, which need no team catalogue; whether a
/// team writes is the daemon actions' own question and is not what is being
/// pinned. The real run journal is read, so a finished run is found as the
/// daemon would find it.
/// </remarks>
public sealed class DaemonTriggersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-dtrig-" + Guid.NewGuid().ToString("N"));
    private readonly IPlatformPaths _paths;
    private readonly YamlStore _yaml = new(new NoOpFilePermissions());
    private readonly Recording _commands = new();

    public DaemonTriggersTests()
    {
        _paths = new LinuxPaths(
            Environment(null),
            new NoOpFilePermissions(),
            new HostPlatform(HostOperatingSystem.Linux, System.Runtime.InteropServices.Architecture.X64, "test", "TEST"));
        _paths.EnsureDirectoriesExist();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task A_webhook_is_handed_to_the_rules_rather_than_starting_its_team()
    {
        await Rule("audit-on-hook", TriggerEventKinds.Webhook, ("team", "docs-crew"));

        var daemon = Daemon(on: true);
        using var stop = new CancellationTokenSource();
        var running = daemon.TriggersAsync(Output(), stop.Token);

        await Eventually(Started);

        var answered = await daemon.TriggeredAsync(
            new TriggerRequest("docs-crew", "check the docs", "loadout-cli"), Output(), CancellationToken.None);

        answered.Succeeded.Should().BeTrue(answered.Error);
        await Eventually(() => _commands.Asked.Any(one => one.Path == "docs audit"));

        await stop.CancelAsync();
        await running;

        _commands.Asked.Should().ContainSingle().Which.Should().Be(("docs audit", "loadout-cli"));
    }

    [Fact]
    public async Task A_run_that_finishes_after_the_first_look_starts_what_a_rule_asks()
    {
        await Rule("audit-after-run", TriggerEventKinds.RunFinished, ("team", "alpha"));
        Run("20261022-0900-a001", "alpha", DateTimeOffset.UtcNow.AddHours(-1));

        var daemon = Daemon(on: true);
        using var stop = new CancellationTokenSource();
        var running = daemon.TriggersAsync(Output(), stop.Token);

        // The first look takes what has already finished as seen.
        await Eventually(() => File.Exists(Path.Combine(_paths.Paths.State, "triggers", "cursors", "run-finished.json")));

        Run("20261022-1000-b001", "alpha", DateTimeOffset.UtcNow);
        await Eventually(() => _commands.Asked.Count > 0);

        await stop.CancelAsync();
        await running;

        _commands.Asked.Should().ContainSingle().Which.Should().Be(("docs audit", "loadout-cli"));
    }

    [Fact]
    public async Task Documentation_that_goes_stale_after_a_commit_starts_what_a_docs_drift_rule_asks()
    {
        await Rule("audit-on-drift", TriggerEventKinds.DocsDrift, ("project", "demo"));

        var repository = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(repository, "docs"));
        File.WriteAllText(Path.Combine(repository, "docs", "guide.md"), "# Guide\n\nNothing to see.\n");

        var git = new FakeGit(repository);
        var daemon = Daemon(
            on: true,
            new FakeProjects("demo", repository),
            git,
            new Loadout.Core.Workspace.WorkspaceManager(_paths, git, _yaml, TimeProvider.System));
        using var stop = new CancellationTokenSource();
        var running = daemon.TriggersAsync(Output(), stop.Token);

        // The first look takes what is there as the baseline.
        await Eventually(() => File.Exists(Path.Combine(_paths.Paths.State, "triggers", "cursors", "docs-drift.json")));

        File.WriteAllText(Path.Combine(repository, "docs", "guide.md"), "# Guide\n\nSee [it](missing.md).\n");
        git.State = git.State with { HeadCommit = "c2" };

        await Eventually(() => _commands.Asked.Count > 0);

        await stop.CancelAsync();
        await running;

        _commands.Asked.Should().ContainSingle().Which.Should().Be(("docs audit", "demo"));
    }

    [Fact]
    public async Task With_the_switch_off_nothing_is_started_and_a_webhook_starts_its_team_as_before()
    {
        await Rule("audit-on-hook", TriggerEventKinds.Webhook, ("team", "docs-crew"));

        var daemon = Daemon(on: false);
        using var stop = new CancellationTokenSource();

        await daemon.TriggersAsync(Output(), stop.Token);
        Directory.Exists(Path.Combine(_paths.Paths.State, "triggers", "journal")).Should().BeFalse();

        await daemon.TriggeredAsync(
            new TriggerRequest("docs-crew", "check the docs", "loadout-cli"), Output(), CancellationToken.None);
        await Eventually(() => _commands.Asked.Count > 0);

        _commands.Asked.Should().ContainSingle().Which.Path.Should().Be("team run");
    }

    private bool Started() =>
        Directory.Exists(Path.Combine(_paths.Paths.State, "triggers", "cursors"));

    private async Task Rule(string id, string on, (string Field, string Value) where)
    {
        var saved = await new TriggerRules(_paths.Paths.State, _yaml).SaveAsync(new TriggerRule
        {
            Id = id,
            On = on,
            Where = new Dictionary<string, string> { [where.Field] = where.Value },
            Action = new TriggerAction { Kind = TriggerActionKinds.Builtin, Name = "docs audit" },
        });

        saved.Succeeded.Should().BeTrue(saved.Error);
    }

    private void Run(string id, string team, DateTimeOffset finished)
    {
        var directory = Path.Combine(_paths.Paths.State, "teams", "runs", id);
        Directory.CreateDirectory(directory);

        string Line(DateTimeOffset at, string kind, object data) =>
            JsonSerializer.Serialize(new { at, run = id, node = (string?)null, kind, data }) + "\n";

        File.WriteAllText(
            Path.Combine(directory, "journal.jsonl"),
            Line(finished.AddMinutes(-20), "run.started", new { team, goal = "work", autonomy = "autonomous", project = "loadout-cli" })
            + Line(finished, "run.finished", new { ended = "done", outcome = "done", cost = 1m }));
    }

    private FakeEnvironmentProvider Environment(bool? on) =>
        new(
            Path.Combine(_root, "home"),
            new Dictionary<string, string>
            {
                ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
                [TriggerSwitch.Variable] = on == true ? "1" : "0",
            });

    private TeamDaemonCommand Daemon(bool on, Loadout.Core.Projects.IProjectService? projects = null, Loadout.Core.Git.IGitManager? git = null, Loadout.Core.Workspace.IWorkspaceManager? workspace = null) =>
        new(
            secrets: null!,
            client: null!,
            configuration: null!,
            schedules: null!,
            journal: new RunJournal(_paths),
            _commands,
            _paths,
            projects: projects!,
            tasks: null!,
            git: git!,
            processes: null!,
            Quiet(),
            TimeProvider.System,
            AccessibleMode.Off,
            speech: null!,
            teams: null!,
            library: null!,
            workspace: workspace!,
            agents: null!,
            launcher: null!,
            nominations: null!,
            ideas: null!,
            dumps: null!,
            bin: null!,
            lifetime: null!,
            memory: null!,
            importer: null!,
            sheet: null!,
            environment: Environment(on),
            yaml: _yaml)
        {
            TriggerEvery = TimeSpan.FromMilliseconds(50),
            DocsDriftEvery = TimeSpan.FromMilliseconds(50),
        };

    /// <summary>Waits for the daemon to get somewhere, and when it does not, says what its journal recorded.</summary>
    private async Task Eventually(Func<bool> condition)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(15);

        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(
                until,
                "the daemon should have got there by now; its trigger journal says: {0}",
                string.Join(" | ", new TriggerJournal(_paths.Paths.State).Read(DateTimeOffset.UtcNow.AddDays(-1))
                    .Select(one => $"{one.Kind} {one.EventKind} {one.Rule} {one.Reason}")));
            await Task.Delay(25);
        }
    }

    private static CommandOutput Output() => new(Quiet(), new GlobalSettings());

    private static IAnsiConsole Quiet() =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(TextWriter.Null),
            Interactive = InteractionSupport.No,
        });

    /// <summary>Records each command asked for, with its first argument, and says it worked.</summary>
    private sealed class Recording : ICommandCatalogue
    {
        private readonly ConcurrentQueue<(string Path, string First)> _asked = new();

        public IReadOnlyList<(string Path, string First)> Asked => [.. _asked];

        public IReadOnlyList<CatalogueEntry> Commands => [];

        public Task<int> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken ct = default)
        {
            _asked.Enqueue((path, arguments.Count > 0 ? arguments[0] : string.Empty));

            return Task.FromResult(0);
        }
    }
}
