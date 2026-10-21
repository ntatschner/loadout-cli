using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Events;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Results;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The event history end to end: the daemon's watch writing it from what the
/// journals say, and the routes Studio reads it through.
/// </summary>
public sealed class EventLogTests : IAsyncLifetime, IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly string _state = Path.Combine(Path.GetTempPath(), "loadout-eventlog-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _stopping = new();
    private readonly Journal _journal = new();
    private readonly Clock _clock = new(Noon);
    private DashboardServer _server = null!;
    private HttpClient _client = null!;
    private string _root = string.Empty;

    private EventStore Store => new(_state);

    public Task InitializeAsync()
    {
        _server = new DashboardServer(_journal);

        var started = _server.Start(0);
        started.Succeeded.Should().BeTrue(started.Error);

        _root = $"http://127.0.0.1:{_server.Port}{EventLogApi.Root}";
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _client.DefaultRequestHeaders.Add("X-Loadout-Token", _server.Token);

        _ = _server.ListenAsync(_stopping.Token);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _stopping.CancelAsync();
        _client.Dispose();
        _server.Dispose();
    }

    public void Dispose()
    {
        _stopping.Dispose();

        if (Directory.Exists(_state))
        {
            Directory.Delete(_state, recursive: true);
        }
    }

    [Fact]
    public void The_watch_records_a_question_once_clears_it_once_and_survives_a_restart()
    {
        var watch = new EventWatch(Store, _journal, null, _clock);

        _journal.Asking = true;
        watch.Start(30);
        _clock.Now = Noon.AddSeconds(1);
        watch.Look();
        watch.Look();

        // A new daemon, the question still open.
        _clock.Now = Noon.AddMinutes(10);
        var again = new EventWatch(Store, _journal, null, _clock);
        again.Start(30);
        again.Look();

        _journal.Asking = false;
        _clock.Now = Noon.AddMinutes(14);
        again.Look();

        var kinds = Store.Read().Select(one => one.Kind).ToList();

        kinds.Count(one => one == EventKinds.Asking).Should().Be(1, "neither a second look nor a restart is the question appearing again");
        kinds.Count(one => one == EventKinds.Cleared).Should().Be(1);
        kinds.Count(one => one == EventKinds.RunStarted).Should().Be(1, "a journal entry is copied once, across daemons too");
        kinds.Count(one => one == EventKinds.DaemonStarted).Should().Be(2);

        var asked = EventFeed.Query(Store, EventFilter.Everything, _clock.Now).Items.Single(item => item.Event.Kind == EventKinds.Asking);
        asked.Cleared.Should().Be(Noon.AddMinutes(14));
        asked.Read.Should().BeFalse("only what was in the journals before the very first start is marked read");
    }

    [Fact]
    public void What_the_journals_held_before_the_first_start_counts_as_read()
    {
        _journal.Finished = true;

        var watch = new EventWatch(Store, _journal, null, _clock);
        watch.Start(30);
        _clock.Now = Noon.AddSeconds(1);
        watch.Look();

        var page = EventFeed.Query(Store, EventFilter.Parse("direction=any").Value!, _clock.Now);

        page.Items.Should().Contain(item => item.Event.Kind == EventKinds.RunFinished);
        page.Items.Where(item => item.Event.Source == EventSources.Run).Should().OnlyContain(item => item.Read);
    }

    [Fact]
    public void A_finished_run_picked_up_again_is_watched_again()
    {
        _journal.At = Path.Combine(_state, "run");
        _journal.Finished = true;

        var watch = new EventWatch(Store, _journal, null, _clock);
        watch.Start(30);
        _clock.Now = Noon.AddSeconds(1);
        watch.Look();
        watch.Look();

        // Picked up again, and asking: its journal has moved.
        _journal.Finished = false;
        _journal.Asking = true;
        _clock.Now = Noon.AddMinutes(1);
        watch.Look();

        Store.Read().Should().ContainSingle(one => one.Kind == EventKinds.Asking);
    }

    [Fact]
    public void A_notice_that_keeps_failing_is_said_once_until_it_lands()
    {
        var watch = new EventWatch(Store, _journal, null, _clock);
        var run = _journal.Run();
        var reason = new Attention(AttentionKind.Asking, "may it merge?", "you answer it", "g1");

        watch.Told("slack", run, reason, landed: false);
        watch.Told("slack", run, reason, landed: false);
        watch.Told("slack", run, reason, landed: true);

        Store.Read().Select(one => one.Kind).Should().Equal(EventKinds.NoticeFailed, EventKinds.NoticeSent);
    }

    [Fact]
    public async Task A_dashboard_with_no_history_says_where_one_is()
    {
        using var answer = await _client.GetAsync(_root);

        answer.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await answer.Content.ReadAsStringAsync()).Should().Contain("loadout daemon");
    }

    [Fact]
    public async Task The_route_filters_answers_the_canonical_string_and_names_what_it_cannot_read()
    {
        Seed();

        var read = await _client.GetFromJsonAsync<JsonElement>(_root + "?since=1d&level=problem,needs&limit=10");

        read.GetProperty("filter").GetString().Should().Be("level=needs%2Cproblem&since=1d");
        read.GetProperty("items").EnumerateArray().Select(one => one.GetProperty("event").GetProperty("id").GetString())
            .Should().Equal("e-new");
        read.GetProperty("unread").GetInt32().Should().Be(2);

        using var refused = await _client.GetAsync(_root + "?level=urgent");
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("'urgent' is not a level");
    }

    [Fact]
    public async Task Marking_read_moves_the_count_and_a_watching_page_may_not()
    {
        Seed();

        using var one = await _client.PostAsJsonAsync(_root + "/read", new { ids = new[] { "e-new" } });
        (await one.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("unread").GetInt32().Should().Be(1);

        using var all = await _client.PostAsJsonAsync(_root + "/read", new { all = true });
        (await all.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("unread").GetInt32().Should().Be(0);

        (await _client.GetFromJsonAsync<JsonElement>(_root + "/unread")).GetProperty("unread").GetInt32().Should().Be(0);

        _server.WatchOnly = true;
        using var watching = await _client.PostAsJsonAsync(_root + "/read", new { all = true });
        watching.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var nothing = await _client.GetAsync(_root + "/read");
        nothing.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    /// <summary>Two unread problems, one recent and one old, and something read.</summary>
    private void Seed()
    {
        var now = DateTimeOffset.UtcNow;
        var store = Store;

        store.Append(
        [
            new UserEvent("e-old", now.AddDays(-3), EventLevels.Problem, EventSources.Run, EventDirections.ToMe, EventKinds.Quiet, "docs: lead has gone quiet"),
            new UserEvent("e-new", now.AddHours(-1), EventLevels.Needs, EventSources.Run, EventDirections.ToMe, EventKinds.Asking, "docs asks: may it merge?"),
            new UserEvent("e-info", now.AddHours(-1), EventLevels.Info, EventSources.Daemon, EventDirections.ToMe, EventKinds.DaemonStarted, "The daemon started"),
        ]);

        _server.Events = store;
    }

    /// <summary>One run, asking a question or not, finished or not.</summary>
    private sealed class Journal : IRunJournal
    {
        private bool _asking;
        private bool _finished;

        /// <summary>Where its journal file is, for a test that needs it to change; null for none.</summary>
        public string? At { get; set; }

        public bool Asking
        {
            get => _asking;
            set
            {
                _asking = value;
                Wrote();
            }
        }

        public bool Finished
        {
            get => _finished;
            set
            {
                _finished = value;
                Wrote();
            }
        }

        /// <summary>A line on the journal, as a run changing writes one.</summary>
        private void Wrote()
        {
            if (At is not null)
            {
                Directory.CreateDirectory(At);
                File.AppendAllText(Path.Combine(At, "journal.jsonl"), "{}" + Environment.NewLine);
            }
        }

        public RunSummary Run() =>
            new(
                "20261021-1200-ab12",
                Directory: "d",
                Team: "docs",
                Goal: "write it",
                Autonomy: "supervised",
                Started: Noon.AddMinutes(-30),
                Finished: Finished ? Noon.AddMinutes(-5) : null,
                Ended: Finished ? "done" : null,
                CostUsd: 0m,
                Rounds: 1,
                Nodes: [],
                Merged: [],
                Branches: [],
                Project: "p",
                Gates: Asking ? [new PendingAsk("g1", "implementer/1", "role.implementer", "Bash", "git merge", Noon)] : []);

        public IReadOnlyList<string> List(int limit = 20) => ["20261021-1200-ab12"];

        public OperationResult<IReadOnlyList<RunEvent>> Read(string runId) =>
            OperationResult<IReadOnlyList<RunEvent>>.Ok(
            [
                new RunEvent(Noon.AddMinutes(-30), null, "run.started", JsonSerializer.SerializeToElement(new { team = "docs" })),
                .. Finished
                    ? new[] { new RunEvent(Noon.AddMinutes(-5), null, "run.finished", JsonSerializer.SerializeToElement(new { ended = "done", outcome = "done" })) }
                    : [],
            ]);

        public OperationResult<RunSummary> Summarise(string runId) => OperationResult<RunSummary>.Ok(Run());

        public string DirectoryOf(string runId) => At ?? runId;

        public OperationResult<RunForgotten> Forget(string runId, bool force = false) => OperationResult<RunForgotten>.Fail("no");
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
