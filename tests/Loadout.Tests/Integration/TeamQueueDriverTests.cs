using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Git;
using Loadout.Core.Teams;
using Loadout.Models.Results;
using Loadout.Models.Tasks;
using Loadout.Models.Teams;
using Loadout.Tests.Fakes;
using Loadout.Tests.Unit;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The daemon's side of a team's queue: starting the next item in an open
/// window, reading how it went, landing it, noticing its merge, and saying
/// what the window did.
/// </summary>
/// <remarks>
/// Against a real queue file, a real task list and real schedules; git, gh,
/// the processes and the run journal are doubles, because the thing under test
/// is what the queue does with what they say.
/// </remarks>
public sealed class TeamQueueDriverTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();
    private readonly QueueService _queues;
    private readonly ScheduleService _schedules;
    private readonly Journal _journal = new();
    private readonly FakeGit _git;
    private readonly StubProcessLauncher _launcher = new("https://github.com/o/website/pull/42\n");
    private readonly FakeProcessInspector _processes = new();
    private readonly FakePullRequests _pulls = new();
    private readonly Clock _clock = new(Local(0, 23));
    private readonly TeamQueueDriver _driver;
    private readonly List<string> _said = [];
    private readonly List<(string Title, string Detail)> _told = [];

    public TeamQueueDriverTests()
    {
        var repository = _space.Scratch("website");

        _queues = new QueueService(_space.Paths, _space.Yaml);
        _schedules = new ScheduleService(_space.Paths, _space.Yaml);
        _git = new FakeGit(repository) { AllowPush = true };
        _git.Commits["refs/remotes/origin/HEAD"] = "abc123";

        _driver = new TeamQueueDriver(
            _queues, _schedules, _space.Tasks, _journal, new FakeProjects("website", repository), _git,
            _launcher, _processes, new StubResolver("gh"), _pulls, _space.Paths, _clock);
    }

    public void Dispose() => _space.Dispose();

    private static DateTimeOffset Local(int day, int hour)
    {
        var at = DateTime.Today.AddDays(day).AddHours(hour);

        return new DateTimeOffset(at, TimeZoneInfo.Local.GetUtcOffset(at));
    }

    private async Task ReadyAsync(bool draftPullRequests = false)
    {
        await _space.ProjectAsync("website");
        await _space.Tasks.DeclareAsync(
            "website", "login", TaskState.Open, "nigel", "Fix the login", doneWhen: ["the suite passes"]);

        (await _schedules.SaveAsync(new TeamSchedule
        {
            Id = "queue-builders",
            Team = "builders",
            Kind = TeamSchedule.QueueKind,
            At = new TimeOnly(22, 0),
            Length = TimeSpan.FromHours(8),
            Autonomy = "supervised",
        })).Succeeded.Should().BeTrue();

        (await _queues.UpdateAsync("builders", queue =>
        {
            queue.Items.Add(new QueueItem { Task = "login", Project = "website" });
            queue.Settings.DraftPullRequests = draftPullRequests;

            return null;
        })).Succeeded.Should().BeTrue();
    }

    private Task PassAsync(bool firing = true) =>
        _driver.PassAsync(firing, _said.Add, (title, detail) =>
        {
            _told.Add((title, detail));

            return Task.CompletedTask;
        }, CancellationToken.None);

    private async Task<TeamQueue> QueueAsync() => (await _queues.LoadAsync("builders")).Value!;

    private async Task<TaskItem> TaskAsync() =>
        (await _space.Tasks.ListAsync("website")).Value!.Single(one => one.Id == "login");

    [Fact]
    public async Task In_an_open_window_the_next_item_starts_as_its_own_process_in_its_own_worktree()
    {
        await ReadyAsync();

        await PassAsync();

        var started = _launcher.Background!;
        started.Arguments.Should().ContainInOrder("team", "run", "builders", "Fix the login");
        started.Arguments.Should().ContainInOrder("--project", "website", "--task", "login");
        started.Arguments.Should().Contain(["--work-in", "--non-interactive"]);
        started.Arguments.Should().ContainInOrder("--autonomy", "supervised");

        var queue = await QueueAsync();
        queue.Items.Should().BeEmpty();
        queue.WindowOpened.Should().Be(Local(0, 22));

        var run = queue.Runs.Single();
        run.Pid.Should().Be(5151);
        run.Branch.Should().StartWith("queue/login-");
        run.Worktree.Should().Contain(run.Branch.Replace('/', '-'));
        started.WorkingDirectory.Should().Be(run.Worktree);
    }

    [Fact]
    public async Task A_paused_daemon_starts_nothing_but_still_marks_the_window_open()
    {
        await ReadyAsync();

        await PassAsync(firing: false);

        _launcher.Background.Should().BeNull();
        (await QueueAsync()).Items.Should().ContainSingle();
        (await QueueAsync()).WindowOpened.Should().NotBeNull();
    }

    [Fact]
    public async Task Outside_the_window_nothing_starts()
    {
        await ReadyAsync();
        _clock.Now = Local(0, 12);

        await PassAsync();

        _launcher.Background.Should().BeNull();
    }

    [Fact]
    public async Task A_run_waiting_on_a_gate_is_held_and_the_next_item_may_start()
    {
        await ReadyAsync();
        await PassAsync();

        var run = (await QueueAsync()).Runs.Single();
        _processes.MarkLive(run.Pid, run.PidStarted!.Value);
        _journal.Add(QueueSettlingTests.Summary("r1", null, finished: false, gates:
            [new PendingAsk("g", "lead", "project-lead", "Bash", "git push", _clock.Now)]) with { Started = _clock.Now });

        await _space.Tasks.DeclareAsync("website", "docs", TaskState.Open, "nigel", "Write the docs", doneWhen: ["it reads well"]);
        await _queues.UpdateAsync("builders", queue =>
        {
            queue.Items.Add(new QueueItem { Task = "docs", Project = "website" });

            return null;
        });

        await PassAsync();

        var queue = await QueueAsync();
        queue.Runs.Should().HaveCount(2);
        queue.Runs[0].State.Should().Be(QueueRunState.Held);
        queue.Runs[0].RunId.Should().Be("r1");
        queue.Runs[1].Task.Should().Be("docs");
    }

    [Fact]
    public async Task A_met_run_with_the_grant_pushes_opens_a_draft_and_leaves_the_task_blocked_pointing_at_it()
    {
        await ReadyAsync(draftPullRequests: true);
        await PassAsync();

        var run = (await QueueAsync()).Runs.Single();
        _journal.Add(QueueSettlingTests.Summary("r1", "done", finished: true) with { Started = _clock.Now });

        await PassAsync();

        _git.Pushed.Should().Equal(run.Branch);

        var gh = _launcher.Requests.Single(one => one.Executable == "gh");
        gh.Arguments.Should().ContainInOrder("pr", "create", "--draft", "--head", run.Branch);
        gh.Arguments.Should().Contain(one => one.Contains("- the suite passes", StringComparison.Ordinal));

        var landed = (await QueueAsync()).Runs.Single();
        landed.State.Should().Be(QueueRunState.Met);
        landed.PullRequest.Should().Be("https://github.com/o/website/pull/42");
        landed.Landed.Should().BeTrue();
        _git.Removed.Should().Contain(run.Worktree);

        var task = await TaskAsync();
        task.State.Should().Be(TaskState.Blocked);
        task.History.Last().Reason.Should().Contain("Draft pull request: https://github.com/o/website/pull/42");
    }

    [Fact]
    public async Task A_met_run_without_the_grant_pushes_nothing_and_says_where_the_branch_is()
    {
        await ReadyAsync(draftPullRequests: false);
        await PassAsync();

        var run = (await QueueAsync()).Runs.Single();
        _journal.Add(QueueSettlingTests.Summary("r1", "done", finished: true) with { Started = _clock.Now });

        await PassAsync();

        _git.Pushed.Should().BeEmpty();
        _launcher.Requests.Should().NotContain(one => one.Executable == "gh");
        (await TaskAsync()).History.Last().Reason.Should().Contain($"on branch {run.Branch}, kept on this machine");
    }

    [Fact]
    public async Task A_failed_run_blocks_the_task_with_the_run_and_the_reason_and_is_not_queued_again()
    {
        await ReadyAsync();
        await PassAsync();
        _journal.Add(QueueSettlingTests.Summary("r1", "over budget", finished: true) with { Started = _clock.Now });

        await PassAsync();

        var task = await TaskAsync();
        task.State.Should().Be(TaskState.Blocked);
        task.History.Last().Reason.Should().Be("Queued run r1 failed: over budget. Not tried again.");
        task.History.Last().By.Should().Be("team builders queue, run r1");
        (await QueueAsync()).Items.Should().BeEmpty();
        _launcher.Requests.Count(one => one == _launcher.Background).Should().Be(1, "nothing was started again");
    }

    [Fact]
    public async Task A_run_whose_process_died_is_failed_rather_than_stopping_the_queue_for_good()
    {
        await ReadyAsync();
        await PassAsync();
        _journal.Add(QueueSettlingTests.Summary("r1", null, finished: false) with { Started = _clock.Now });

        // The process the queue started is not marked live, so it has gone.
        await PassAsync();

        var run = (await QueueAsync()).Runs.Single();
        run.State.Should().Be(QueueRunState.Failed);
        run.Reason.Should().Be("stopped with the process running it");
    }

    [Fact]
    public async Task A_merged_draft_marks_its_task_done()
    {
        await ReadyAsync(draftPullRequests: true);
        await PassAsync();
        _journal.Add(QueueSettlingTests.Summary("r1", "done", finished: true) with { Started = _clock.Now });
        await PassAsync();

        (await TaskAsync()).State.Should().Be(TaskState.Blocked);

        _pulls.Known[42] = new PullRequestText(42, "Fix the login", "body");
        _clock.Now += TeamQueueDriver.MergeEvery;

        await PassAsync();

        var task = await TaskAsync();
        task.State.Should().Be(TaskState.Done);
        task.History.Last().Reason.Should().Contain("pull request was merged");
    }

    [Fact]
    public async Task Merges_are_not_asked_about_more_often_than_every_quarter_hour()
    {
        await ReadyAsync(draftPullRequests: true);
        await PassAsync();
        _journal.Add(QueueSettlingTests.Summary("r1", "done", finished: true) with { Started = _clock.Now });
        await PassAsync();
        await PassAsync();

        var asked = _pulls.Reads;
        _clock.Now += TimeSpan.FromMinutes(5);
        await PassAsync();

        _pulls.Reads.Should().Be(asked);
    }

    [Fact]
    public async Task When_the_window_closes_it_says_once_what_it_did()
    {
        await ReadyAsync();
        await PassAsync();
        _journal.Add(QueueSettlingTests.Summary("r1", "over budget", finished: true, cost: 4m) with { Started = _clock.Now });
        await PassAsync();

        _clock.Now = Local(1, 7);
        await PassAsync();
        await PassAsync();

        _told.Should().ContainSingle();
        _told[0].Title.Should().Be("builders' queue window: 1 run(s), $4.00");
        _told[0].Detail.Should().Contain("login: blocked - over budget (run r1)");
        (await QueueAsync()).WindowOpened.Should().BeNull();
    }

    [Fact]
    public async Task A_full_held_cap_starts_nothing_more()
    {
        await ReadyAsync();
        await _queues.UpdateAsync("builders", queue =>
        {
            queue.Settings.HeldCap = 1;
            queue.Runs.Add(new QueueRun { Task = "x", Project = "website", State = QueueRunState.Held, StartedUtc = _clock.Now, Landed = false });

            return null;
        });

        await PassAsync();

        _launcher.Background.Should().BeNull();
    }

    /// <summary>A clock the test moves.</summary>
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }

    /// <summary>A journal holding whatever runs the test says exist.</summary>
    private sealed class Journal : IRunJournal
    {
        private readonly List<RunSummary> _runs = [];

        public void Add(RunSummary run)
        {
            _runs.RemoveAll(one => one.RunId == run.RunId);
            _runs.Insert(0, run);
        }

        public IReadOnlyList<string> List(int limit = 20) => [.. _runs.Take(limit).Select(one => one.RunId)];

        public OperationResult<IReadOnlyList<RunEvent>> Read(string runId) =>
            OperationResult<IReadOnlyList<RunEvent>>.Fail("not kept");

        public OperationResult<RunSummary> Summarise(string runId) =>
            _runs.FirstOrDefault(one => one.RunId == runId) is { } run
                ? OperationResult<RunSummary>.Ok(run)
                : OperationResult<RunSummary>.Fail("no such run");

        public string DirectoryOf(string runId) => Path.Combine(Path.GetTempPath(), runId);

        public OperationResult<RunForgotten> Forget(string runId, bool force = false) =>
            OperationResult<RunForgotten>.Fail("not kept");
    }
}
