using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Results;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The page that says what the teams on this machine are doing.
/// </summary>
/// <remarks>
/// <para>
/// Run against the real listener over a real socket, because what is being
/// asserted is what a browser receives: the token check, the shape of the
/// answers, and that nothing can be changed through it.
/// </para>
/// <para>
/// The token is the part worth testing hardest. Any page in any browser tab
/// can reach a loopback port, so a dashboard without one would let a web page
/// somebody happened to open read what their agents are doing.
/// </para>
/// </remarks>
public sealed class DashboardServerTests : IAsyncLifetime
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly StubJournal _journal = new();

    private DashboardServer _server = null!;
    private HttpClient _client = null!;
    private string _root = string.Empty;

    public Task InitializeAsync()
    {
        _server = new DashboardServer(_journal);

        var started = _server.Start(0);

        started.Succeeded.Should().BeTrue(started.Error);

        _root = _server.Address[.._server.Address.IndexOf('?', StringComparison.Ordinal)];
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        _ = _server.ListenAsync(_stopping.Token);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _stopping.CancelAsync();

        _client.Dispose();
        _server.Dispose();
        _stopping.Dispose();
    }

    private Task<HttpResponseMessage> GetAsync(string path, bool withToken = true) =>
        _client.GetAsync(new Uri(
            _root.TrimEnd('/') + path + (withToken
                ? (path.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "token=" + _server.Token
                : string.Empty)));

    [Fact]
    public async Task Nothing_is_answered_without_the_token()
    {
        // The whole of the protection. A page in any tab can reach loopback.
        var page = await GetAsync("/", withToken: false);

        page.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var runs = await GetAsync("/api/runs", withToken: false);

        runs.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_wrong_token_is_no_better_than_none()
    {
        var answer = await _client.GetAsync(new Uri(_root + "?token=" + new string('0', 32)));

        answer.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_address_it_prints_carries_the_token()
    {
        var answer = await _client.GetAsync(new Uri(_server.Address));

        answer.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_server_that_only_watches_changes_nothing()
    {
        // This server has no Act, which is what `team dashboard` gives you: a
        // page opened to watch a run must not be able to stop one. The daemon
        // sets Act, and that is the only thing that can.
        var answer = await _client.PostAsync(
            new Uri(_root + "api/runs/r/stop?token=" + _server.Token),
            new StringContent(string.Empty));

        answer.StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await answer.Content.ReadAsStringAsync()).Should().Contain("only reads");
    }

    [Fact]
    public async Task Changing_a_run_needs_the_token_like_everything_else()
    {
        var answer = await _client.PostAsync(
            new Uri(_root + "api/runs/r/stop"),
            new StringContent(string.Empty));

        // Before it even asks whether anything could act: a stranger is told
        // about the token, never about what this server can do.
        answer.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task What_the_page_asks_for_is_handed_over_rather_than_done_here()
    {
        // The rule the launcher has kept since its first screen: the surface
        // never implements command behaviour. What this proves is that the ask
        // arrives intact - which run, which verb, which answer - because
        // whatever receives it is going to type it.
        RunAction? asked = null;

        _server.Act = (action, _) =>
        {
            asked = action;

            return Task.FromResult(OperationResult.Ok());
        };

        var answer = await _client.PostAsync(
            new Uri(_root + "api/runs/20260917-1116-ed59/gates?token=" + _server.Token),
            new StringContent(
                "{\"gate\":\"g1\",\"answer\":\"yes\",\"reason\":\"it needs the suite\"}",
                System.Text.Encoding.UTF8,
                "application/json"));

        answer.StatusCode.Should().Be(HttpStatusCode.Accepted);

        asked!.Run.Should().Be("20260917-1116-ed59");
        asked.Verb.Should().Be("gates");
        asked.Gate.Should().Be("g1");
        asked.Answer.Should().Be("yes");
        asked.Reason.Should().Be("it needs the suite");
    }

    [Fact]
    public async Task Reading_a_run_is_still_reading_it()
    {
        // The route split is by verb, and two of them are not verbs at all.
        // Getting this wrong would turn following a run into an action.
        _server.Act = (_, _) => Task.FromResult(OperationResult.Ok());

        (await GetAsync("/api/runs/r")).StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed);
        (await GetAsync("/api/runs/r/events")).StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Something_nobody_implemented_is_refused_by_whoever_would_do_it()
    {
        // The server does not keep a list of verbs. It hands the word over and
        // the thing that types commands decides there is no such command,
        // which is the only place that knows.
        _server.Act = (action, _) => Task.FromResult(
            OperationResult.Fail($"There is nothing called '{action.Verb}' to do to a run."));

        var answer = await _client.PostAsync(
            new Uri(_root + "api/runs/r/incinerate?token=" + _server.Token),
            new StringContent(string.Empty));

        answer.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await answer.Content.ReadAsStringAsync()).Should().Contain("incinerate");
    }

    [Fact]
    public async Task The_page_arrives_whole_and_fetches_nothing_from_anywhere()
    {
        var text = await (await GetAsync("/")).Content.ReadAsStringAsync();

        text.Should().StartWith("<!doctype html>");
        text.Should().Contain("role=\"status\"", "a live region has to be there from the first paint");
        text.Should().Contain("prefers-reduced-motion");
        text.Should().Contain("forced-colors");

        // Nothing off this machine. A dashboard that pulled a stylesheet from
        // the internet would fail on the machine it is most wanted on.
        text.Should().NotContain("http://").And.NotContain("https://");
    }

    [Fact]
    public async Task The_page_is_allowed_to_ask_the_server_things()
    {
        // The one a browser found and nothing here could. The policy said
        // default-src none, which covers connect-src, so the page loaded,
        // looked right, and said "Failed to fetch" - every assertion in this
        // file passed while the dashboard showed nothing at all.
        var answer = await GetAsync("/");

        answer.Headers.GetValues("Content-Security-Policy").Should()
            .ContainSingle().Which.Should().Contain("connect-src 'self'");
    }

    [Fact]
    public async Task The_runs_come_back_as_the_page_needs_them()
    {
        var json = JsonDocument.Parse(await (await GetAsync("/api/runs")).Content.ReadAsStringAsync());

        var runs = json.RootElement.GetProperty("runs");

        runs.GetArrayLength().Should().Be(1);

        var run = runs[0];

        run.GetProperty("id").GetString().Should().Be("20260916-1200-aaaa");
        run.GetProperty("team").GetString().Should().Be("iterating-project");
        run.GetProperty("running").GetBoolean().Should().BeTrue();
        run.GetProperty("nodes").GetArrayLength().Should().Be(1);
        run.GetProperty("nodes")[0].GetProperty("doing").GetString().Should().Be("Read docs/commands.md");
    }

    [Fact]
    public async Task A_run_nobody_has_is_a_404_rather_than_an_empty_one()
    {
        var answer = await GetAsync("/api/runs/no-such-run");

        answer.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_journal_arrives_as_it_happens()
    {
        using var reading = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await using var stream = await _client.GetStreamAsync(
            new Uri(_root + "api/runs/20260916-1200-aaaa/events?token=" + _server.Token),
            reading.Token);

        using var reader = new StreamReader(stream);

        var first = new List<string>();

        while (first.Count < 3 && !reading.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(reading.Token);

            if (line is { Length: > 0 })
            {
                first.Add(line);
            }
        }

        first.Should().Contain(line => line.StartsWith("event: line", StringComparison.Ordinal));
        first.Should().Contain(line => line.Contains("started iterating-project", StringComparison.Ordinal));
    }

    /// <summary>One run, written as a real one writes itself.</summary>
    private sealed class StubJournal : IRunJournal
    {
        private static readonly string[] Lines =
        [
            """{"at":"2026-09-16T12:00:00+00:00","run":"r","node":null,"kind":"run.started","data":{"team":"iterating-project","goal":"Add --since","autonomy":"autonomous","rounds":5}}""",
            """{"at":"2026-09-16T12:00:02+00:00","run":"r","node":"lead","kind":"node.launched","data":{"role":"role.project-lead"}}""",
            """{"at":"2026-09-16T12:00:20+00:00","run":"r","node":"lead","kind":"node.doing","data":{"doing":"Read docs/commands.md"}}""",
        ];

        public IReadOnlyList<string> List(int limit = 20) => ["20260916-1200-aaaa"];

        public OperationResult<IReadOnlyList<RunEvent>> Read(string runId) =>
            runId == "20260916-1200-aaaa"
                ? OperationResult<IReadOnlyList<RunEvent>>.Ok(
                    [.. Lines.Select(RunJournal.Parse).Where(e => e is not null)!])
                : OperationResult<IReadOnlyList<RunEvent>>.Fail($"No run named '{runId}'.");

        public OperationResult<RunSummary> Summarise(string runId)
        {
            var read = Read(runId);

            return read.Failed
                ? OperationResult<RunSummary>.Fail(read.Error!)
                : OperationResult<RunSummary>.Ok(RunJournal.Fold(runId, "C:/runs/" + runId, read.Value!));
        }

        public string DirectoryOf(string runId) => "C:/runs/" + runId;
    }
}
