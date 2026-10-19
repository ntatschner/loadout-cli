using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Sessions;
using Loadout.Core.Sessions.Hosting;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Platform;
using Loadout.Models.Results;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Studio's history of past conversations, and resuming one in a hosted
/// terminal: what is offered, what is refused, and what the page is asked
/// before anything starts an agent.
/// </summary>
public sealed class DashboardResumeTests : IAsyncLifetime
{
    private const string Mine = "11111111-0000-4000-8000-000000000001";
    private const string Elsewhere = "22222222-0000-4000-8000-000000000002";
    private const string Stray = "33333333-0000-4000-8000-000000000003";
    private const string Node = "44444444-0000-4000-8000-000000000004";
    private const string LaunchId = "1234567890abcdef1234567890abcdef";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly CancellationTokenSource _stopping = new();
    private readonly List<ScriptedTerminal> _terminals = [];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-resume-api-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _registered = [];
    private HostedSessions _sessions = null!;
    private DashboardServer _server = null!;
    private HttpClient _client = null!;
    private string _address = string.Empty;
    private string _signals = string.Empty;
    private string _strayFolder = string.Empty;

    public Task InitializeAsync() => StartServerAsync();

    private async Task StartServerAsync()
    {
        Directory.CreateDirectory(_root);
        _signals = Path.Combine(_root, "signals");
        _strayFolder = Path.Combine(_root, "stray");

        var environment = new FakeEnvironmentProvider(
            Path.Combine(_root, "home"),
            new Dictionary<string, string>
            {
                ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
            });

        var ledger = new LaunchLedger(
            new LinuxPaths(environment, new NoOpFilePermissions(),
                new HostPlatform(HostOperatingSystem.Linux, System.Runtime.InteropServices.Architecture.X64, "test", "TEST")),
            new NoOpFilePermissions(),
            TimeProvider.System);

        Directory.CreateDirectory(Path.GetDirectoryName(ledger.Path)!);
        await File.WriteAllLinesAsync(ledger.Path,
        [
            $$"""{"Kind":"start","Id":"{{LaunchId}}","When":"2026-10-01T09:00:00.0000000+00:00","Slug":"alpha","Project":"alpha","Agent":"claude","Mode":"implement","Task":"fix the importer"}""",
            $$"""{"Kind":"end","Id":"{{LaunchId}}","When":"2026-10-01T10:00:00.0000000+00:00","Exit":0}""",
            $$"""{"Kind":"link","Id":"{{Mine}}","When":"2026-10-01T09:00:05.0000000+00:00","Agent":"claude","Launch":"{{LaunchId}}"}""",
        ]);

        var history = new History(
            [
                new AgentSession("claude", Mine, "Fix the importer", "/repos/alpha", "main",
                    new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero), "/t/1.jsonl", "alpha"),

                // Written to a moment ago with no launch on record: somebody
                // has it open in a terminal of their own.
                new AgentSession("claude", Elsewhere, "Still going", "/repos/alpha", "main",
                    DateTimeOffset.UtcNow.AddSeconds(-30), "/t/2.jsonl", "alpha"),

                new AgentSession("claude", Stray, "Somewhere else", _strayFolder, null,
                    new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero), "/t/3.jsonl"),
            ],
            [
                new AgentSession("claude", Node, "A node", "/repos/alpha", "main",
                    new DateTimeOffset(2026, 10, 1, 11, 0, 0, TimeSpan.Zero), "/t/4.jsonl", "alpha"),
            ]);

        var resumption = new SessionResumption(
            history, ledger, new NoneRunning(), new FakeProjects("alpha", "/repos/alpha"), new FakeGit("/repos/alpha"), TimeProvider.System);

        _sessions = new HostedSessions(Made, () => ("loadout-host", []), signals: _signals);
        _server = new DashboardServer(new EmptyJournal())
        {
            Sessions = _sessions,
            Resumption = resumption,
            Register = (folder, _) =>
            {
                _registered.Add(folder);

                return Task.FromResult(OperationResult<string>.Ok("stray"));
            },
        };

        var started = _server.Start(0);
        started.Succeeded.Should().BeTrue(started.Error);

        _address = $"http://127.0.0.1:{_server.Port}";
        _client = new HttpClient { Timeout = Patience };
        _client.DefaultRequestHeaders.Add("X-Loadout-Token", _server.Token);

        _ = _server.ListenAsync(_stopping.Token);
    }

    public async Task DisposeAsync()
    {
        await _stopping.CancelAsync();
        await _sessions.DisposeAsync();
        _client.Dispose();
        _server.Dispose();
        _stopping.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private Task<HttpResponseMessage> ResumeAsync(object resume, object? sheet = null)
    {
        var body = new Dictionary<string, object?> { ["resume"] = resume };

        foreach (var property in sheet?.GetType().GetProperties() ?? [])
        {
            body[property.Name] = property.GetValue(sheet);
        }

        return _client.PostAsJsonAsync(_address + SessionsApi.Root, body);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task The_history_shows_what_each_conversation_was_launched_for_and_whether_it_is_running()
    {
        var listed = await _client.GetFromJsonAsync<JsonElement>(_address + HistoryApi.Root);

        var rows = listed.EnumerateArray().ToDictionary(row => row.GetProperty("id").GetString()!);

        rows.Keys.Should().BeEquivalentTo([Mine, Elsewhere, Stray], "a node's conversation is offered from its run's page");

        rows[Mine].GetProperty("launchedFor").GetProperty("task").GetString().Should().Be("fix the importer");
        rows[Mine].GetProperty("running").GetString().Should().Be("none");
        rows[Mine].GetProperty("registered").GetBoolean().Should().BeTrue();

        rows[Elsewhere].GetProperty("launchedFor").ValueKind.Should().Be(JsonValueKind.Null, "it has no link, and nothing is guessed");
        rows[Elsewhere].GetProperty("running").GetString().Should().Be("elsewhere");

        rows[Stray].GetProperty("registered").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task The_history_takes_the_query_every_list_takes_and_keeps_what_each_was_launched_for()
    {
        var listed = await _client.GetFromJsonAsync<JsonElement>(_address + HistoryApi.Root + "/query?q=" + Uri.EscapeDataString("project:alpha sort:id"));

        var ids = listed.GetProperty("conversations").EnumerateArray().Select(row => row.GetProperty("id").GetString()).ToList();

        ids.Should().Equal(new[] { Mine, Elsewhere }.Order(StringComparer.Ordinal), "the stray is on no project, and sort:id orders the rest");
        listed.GetProperty("recorded").GetInt32().Should().Be(3);
        listed.GetProperty("query").GetProperty("text").GetString().Should().Be("project:alpha sort:id");

        var mine = listed.GetProperty("conversations").EnumerateArray().Single(row => row.GetProperty("id").GetString() == Mine);
        mine.GetProperty("launchedFor").GetProperty("task").GetString().Should().Be("fix the importer");
    }

    [Fact]
    public async Task A_history_query_naming_a_field_it_lacks_is_refused_in_its_words()
    {
        using var refused = await _client.GetAsync(_address + HistoryApi.Root + "/query?q=colour:red");

        refused.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("'colour' is not something sessions can be filtered on");
    }

    [Fact]
    public async Task Resuming_opens_loadout_resume_in_a_hosted_terminal_shown_as_the_session_it_reopens()
    {
        using var started = await ResumeAsync(new { agentSession = Mine });

        started.StatusCode.Should().Be(HttpStatusCode.Created);

        var shown = await BodyAsync(started);
        shown.GetProperty("kind").GetString().Should().Be("launch");
        shown.GetProperty("project").GetString().Should().Be("alpha");
        shown.GetProperty("agent").GetString().Should().Be("claude");
        shown.GetProperty("task").GetString().Should().Be("fix the importer", "carried over from its launch");
        shown.GetProperty("agentSession").GetString().Should().Be(Mine);

        // Built from the history's record of it, not from what the page sent.
        _terminals.Single().Request!.Arguments.Should().Equal(
            "resume", Mine, "--project=alpha", "--agent=claude");
    }

    [Fact]
    public async Task What_the_launch_sheet_changed_goes_to_the_resume_as_its_options()
    {
        // An empty field is no option at all: the parser refuses an option
        // with an empty value, so the first draft's '--worktree=' would have
        // opened a terminal that failed at once.
        using var started = await ResumeAsync(new { agentSession = Mine, fresh = true }, new { mode = "review", worktree = "", task = "", offline = true });

        started.StatusCode.Should().Be(HttpStatusCode.Created);
        _terminals.Single().Request!.Arguments.Should().Equal(
            "resume", Mine, "--project=alpha", "--agent=claude", "--fresh", "--mode=review", "--offline");

        var shown = await BodyAsync(started);
        shown.GetProperty("task").ValueKind.Should().Be(JsonValueKind.Null, "fresh carries nothing over");
        shown.GetProperty("mode").GetString().Should().Be("review");
    }

    [Fact]
    public async Task Resuming_a_conversation_this_daemon_is_already_hosting_sends_the_page_there()
    {
        using var first = await ResumeAsync(new { agentSession = Mine });
        var id = (await BodyAsync(first)).GetProperty("id").GetString();

        // Before its agent has taken a turn and said its id, which is when a
        // second press is likeliest.
        using var second = await ResumeAsync(new { agentSession = Mine });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await BodyAsync(second);
        body.GetProperty("conflict").GetString().Should().Be("hosted");
        body.GetProperty("session").GetString().Should().Be(id);
        _terminals.Should().ContainSingle();
    }

    [Fact]
    public async Task A_conversation_running_elsewhere_is_resumed_only_once_the_person_says_so()
    {
        using var asked = await ResumeAsync(new { agentSession = Elsewhere });

        asked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyAsync(asked)).GetProperty("conflict").GetString().Should().Be("elsewhere");
        _terminals.Should().BeEmpty();

        using var confirmed = await ResumeAsync(new { agentSession = Elsewhere, confirmRunning = true });

        confirmed.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_conversation_from_an_unregistered_folder_asks_before_registering_that_folder_and_no_other()
    {
        using var asked = await ResumeAsync(new { agentSession = Stray });

        asked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await BodyAsync(asked);
        body.GetProperty("conflict").GetString().Should().Be("unregistered");
        body.GetProperty("directory").GetString().Should().Be(_strayFolder);

        using var other = await ResumeAsync(new { agentSession = Stray, registerFolder = Path.Combine(_root, "elsewhere") });

        other.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _registered.Should().BeEmpty("resuming is not a way to register any folder the page names");

        using var confirmed = await ResumeAsync(new { agentSession = Stray, registerFolder = _strayFolder });

        confirmed.StatusCode.Should().Be(HttpStatusCode.Created);
        _registered.Should().Equal(_strayFolder);
        _terminals.Single().Request!.Arguments.Should().Contain("--project=stray");
    }

    [Theory]
    [InlineData(Node)]
    [InlineData("11111111")]
    [InlineData("--all")]
    [InlineData("no such conversation")]
    public async Task Only_a_whole_id_from_the_persons_own_history_is_resumed(string id)
    {
        using var refused = await ResumeAsync(new { agentSession = id });

        refused.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
        _terminals.Should().BeEmpty();
    }

    [Fact]
    public async Task A_watching_dashboard_cannot_resume_or_let_go_of_a_lost_session()
    {
        _server.WatchOnly = true;

        using var refused = await ResumeAsync(new { agentSession = Mine });
        using var dismissed = await _client.DeleteAsync($"{_address}{HistoryApi.Root}/lost/0000ffff");

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        dismissed.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _terminals.Should().BeEmpty();
    }

    [Fact]
    public async Task Sessions_a_restart_ended_are_offered_with_their_conversation_until_one_is_resumed()
    {
        SessionSignals.Write(_signals, "0000ffff", "turn", Mine, DateTimeOffset.UtcNow);
        SessionSignals.Write(_signals, "0000eeee", "turn", null, DateTimeOffset.UtcNow);

        var lost = await _client.GetFromJsonAsync<JsonElement>($"{_address}{HistoryApi.Root}/lost");

        // One said its conversation's id; the other never got as far as a
        // turn, and there is nothing of it to resume.
        var only = lost.EnumerateArray().Should().ContainSingle().Subject;
        only.GetProperty("id").GetString().Should().Be("0000ffff");
        only.GetProperty("conversation").GetProperty("project").GetString().Should().Be("alpha");

        using var resumed = await ResumeAsync(new { agentSession = Mine, lost = "0000ffff" });
        resumed.StatusCode.Should().Be(HttpStatusCode.Created);

        (await _client.GetFromJsonAsync<JsonElement>($"{_address}{HistoryApi.Root}/lost"))
            .GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_lost_session_can_be_let_go_of_without_resuming_it()
    {
        SessionSignals.Write(_signals, "0000ffff", "turn", Mine, DateTimeOffset.UtcNow);

        using var dismissed = await _client.DeleteAsync($"{_address}{HistoryApi.Root}/lost/0000ffff");

        dismissed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetFromJsonAsync<JsonElement>($"{_address}{HistoryApi.Root}/lost"))
            .GetArrayLength().Should().Be(0);
        _terminals.Should().BeEmpty();
    }

    private ScriptedTerminal Made()
    {
        var terminal = new ScriptedTerminal();

        lock (_terminals)
        {
            _terminals.Add(terminal);
        }

        return terminal;
    }

    /// <summary>The person's conversations, and a node's, which only a listing that asks for nodes sees.</summary>
    private sealed class History(IReadOnlyList<AgentSession> mine, IReadOnlyList<AgentSession> nodes) : ISessionHistoryService
    {
        public Task<OperationResult<IReadOnlyList<AgentSession>>> ListAsync(SessionQuery query, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<AgentSession>>.Ok(
                (query.IncludeHeadless ? [.. mine, .. nodes] : mine)
                    .Where(s => query.ProjectSlug is null || s.ProjectSlug == query.ProjectSlug)
                    .OrderByDescending(s => s.LastActive)
                    .ToList()));
    }

    private sealed class NoneRunning : ISessionRegistry
    {
        public string Path => "running";

        public string InProgressPath => "running/in-progress";

        public Task RegisterAsync(NewSession session, CancellationToken ct = default) => Task.CompletedTask;

        public Task ReleaseAsync(string launchId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<RunningSession>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RunningSession>>([]);
    }

    private sealed class EmptyJournal : IRunJournal
    {
        public IReadOnlyList<string> List(int limit = 20) => [];

        public OperationResult<IReadOnlyList<RunEvent>> Read(string runId) =>
            OperationResult<IReadOnlyList<RunEvent>>.Fail("no such run");

        public OperationResult<RunSummary> Summarise(string runId) => OperationResult<RunSummary>.Fail("no such run");

        public string DirectoryOf(string runId) => Path.Combine(Path.GetTempPath(), runId);

        public OperationResult<RunForgotten> Forget(string runId, bool force = false) =>
            OperationResult<RunForgotten>.Fail("no such run");
    }
}
