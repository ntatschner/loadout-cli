using System.Net;
using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Configuration;
using Loadout.Models.Platform;
using Loadout.Models.Results;
using Loadout.Models.Teams;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Loadout.Tui;
using Spectre.Console;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// A run that asks something while a schedule is running gets its notice then,
/// not when the schedule's run has finished.
/// </summary>
/// <remarks>
/// <para>
/// The notices were sent from the schedules' loop, which waits for each
/// scheduled run to end. A scheduled run that stopped to ask was told about
/// nowhere until it had finished, which for a run waiting on an answer is
/// never, or until the next run had started on top of it.
/// </para>
/// <para>
/// The run here is held, so "still going" is a fact rather than a race, and
/// it starts asking only once it has started: a notice sent before the run
/// began proves nothing about one sent during it. The wait for the notice is
/// bounded, so a daemon that never sends one fails rather than holding the
/// suite.
/// </para>
/// </remarks>
public sealed class DaemonNoticesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-notices-" + Guid.NewGuid().ToString("N"));
    private readonly IPlatformPaths _paths;

    public DaemonNoticesTests()
    {
        var environment = new FakeEnvironmentProvider(
            Path.Combine(_root, "home"),
            new Dictionary<string, string>
            {
                ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
            });

        _paths = new LinuxPaths(
            environment,
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
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task A_notice_goes_out_while_a_scheduled_run_is_still_going()
    {
        var held = new Held();
        var posted = new Posted();
        using var client = new HttpClient(posted);
        var daemon = Daemon(held, new AskingOnceStarted(held), client);

        (await daemon.TellingAsync(CancellationToken.None)).Should().NotBeNull();

        using var stopping = new CancellationTokenSource();
        var loops = daemon.LoopsAsync(Output(), stopping.Token);

        await held.Asked().WaitAsync(TimeSpan.FromSeconds(10));

        var sent = await Task.WhenAny(posted.Arrived(), Task.Delay(TimeSpan.FromSeconds(10)));

        sent.Should().Be(posted.Arrived(), "the run asked while the schedule was waiting on it");
        held.Finished.Should().BeFalse("the notice is worth something only while the run can still be answered");

        held.Release();
        await stopping.CancelAsync();
        await loops.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// The real schedule loop and notice loop; the run, the journal, the
    /// schedules and the channel are stood in for.
    /// </summary>
    private TeamDaemonCommand Daemon(ICommandCatalogue commands, IRunJournal journal, HttpClient client) =>
        new(
            new FakeSecretProvider(new Dictionary<string, string> { [Notices.Reference] = "https://chat.example/hook" }),
            client,
            new Configured(),
            new OneSchedule(),
            journal,
            commands,
            _paths,
            projects: null!,
            tasks: null!,
            git: null!,
            processes: null!,
            Quiet(),
            TimeProvider.System,
            AccessibleMode.Off,
            speech: null!,
            teams: null!,
            library: null!,
            workspace: null!,
            agents: null!,
            launcher: null!,
            new NothingNominated(),
            ideas: null!,
            dumps: null!,
            new TeamBin(_paths),
            lifetime: null!,
            memory: null!,
            importer: null!,
            sheet: null!)
        {
            NoticeEvery = TimeSpan.FromMilliseconds(50),
        };

    private static CommandOutput Output() => new(Quiet(), new GlobalSettings());

    private static IAnsiConsole Quiet() =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(TextWriter.Null),
            Interactive = InteractionSupport.No,
        });

    /// <summary>A command that starts and does not finish until it is let go.</summary>
    private sealed class Held : ICommandCatalogue
    {
        private readonly TaskCompletionSource _asked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _go = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Started => _asked.Task.IsCompleted;

        public bool Finished { get; private set; }

        public IReadOnlyList<CatalogueEntry> Commands => [];

        public Task Asked() => _asked.Task;

        public void Release() => _go.TrySetResult();

        public async Task<int> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken ct = default)
        {
            _asked.TrySetResult();

            await _go.Task.ConfigureAwait(false);

            Finished = true;

            return 0;
        }
    }

    /// <summary>One run, which has a question for somebody once the held run has started.</summary>
    private sealed class AskingOnceStarted(Held held) : IRunJournal
    {
        private const string RunId = "20261021-1200-aaaa";

        public IReadOnlyList<string> List(int limit = 20) => held.Started ? [RunId] : [];

        public OperationResult<IReadOnlyList<RunEvent>> Read(string runId) =>
            OperationResult<IReadOnlyList<RunEvent>>.Ok([]);

        public OperationResult<RunSummary> Summarise(string runId)
        {
            var now = DateTimeOffset.UtcNow;

            return OperationResult<RunSummary>.Ok(new RunSummary(
                RunId,
                Directory: "d",
                Team: "docs-crew",
                Goal: "check the docs",
                Autonomy: "supervised",
                Started: now.AddMinutes(-1),
                Finished: null,
                Ended: null,
                CostUsd: 0m,
                Rounds: 1,
                Nodes: [],
                Merged: [],
                Branches: [],
                RoundLimit: 5,
                Gates: [new PendingAsk("g1", "implementer/1", "role.implementer", "Bash", "dotnet test", now)]));
        }

        public string DirectoryOf(string runId) => "d";

        public OperationResult<RunForgotten> Forget(string runId, bool force = false) =>
            throw new NotSupportedException();
    }

    /// <summary>A schedule due now, and due once: it remembers having started.</summary>
    private sealed class OneSchedule : IScheduleService
    {
        private readonly TeamSchedule _schedule = new()
        {
            Id = "nightly",
            Project = "loadout-cli",
            Team = "docs-crew",
            Goal = "check the docs",
            Every = TimeSpan.FromHours(1),
        };

        public Task<OperationResult<IReadOnlyList<TeamSchedule>>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<TeamSchedule>>.Ok([_schedule]));

        public Task<OperationResult> StartedAsync(string id, DateTimeOffset when, string runId, CancellationToken ct = default)
        {
            _schedule.LastRun = when;

            return Task.FromResult(OperationResult.Ok());
        }

        public Task<OperationResult> SawAsync(string id, string commit, CancellationToken ct = default) =>
            Task.FromResult(OperationResult.Ok());

        public Task<OperationResult<TeamSchedule>> SaveAsync(TeamSchedule schedule, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult> RemoveAsync(string id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<IReadOnlyList<TeamSchedule>>> DueAsync(DateTimeOffset now, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>A machine that sends notices as plain webhooks.</summary>
    private sealed class Configured : IConfigurationService
    {
        public Task<OperationResult<MachineConfig>> LoadMachineAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<MachineConfig>.Ok(new MachineConfig
            {
                Teams = new MachineTeams { NotifyKind = "generic" },
            }));

        public Task<OperationResult<LauncherConfig>> LoadConfigAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<LauncherConfig>.Ok(new LauncherConfig()));

        public Task<OperationResult> SaveConfigAsync(LauncherConfig config, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult> SaveMachineAsync(MachineConfig machine, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<LauncherConfig>> UpdateConfigAsync(Action<LauncherConfig> change, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<MachineConfig>> UpdateMachineAsync(Action<MachineConfig> change, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>A channel that accepts everything and says when the first post arrives.</summary>
    private sealed class Posted : HttpMessageHandler
    {
        private readonly TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Arrived() => _arrived.Task;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _arrived.TrySetResult();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    /// <summary>A nomination pass with nothing to file.</summary>
    private sealed class NothingNominated : Loadout.Core.Tools.IToolNominationPass
    {
        public Task<IReadOnlyList<(Loadout.Core.Tools.ToolNomination Nomination, OperationResult<Loadout.Core.Tools.ToolSubmitted>? Filed)>> BeforeAsync(
            TeamSchedule schedule,
            Action<string>? log = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<(Loadout.Core.Tools.ToolNomination, OperationResult<Loadout.Core.Tools.ToolSubmitted>?)>>([]);
    }
}
