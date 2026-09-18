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
    public async Task What_you_just_did_is_said_somewhere_the_refresh_does_not_overwrite()
    {
        // Found by clicking the buttons: the confirmation went into the same
        // live region the runs list rewrites twice a second, so it survived
        // under half a second and a screen reader could miss it entirely.
        var text = await (await GetAsync("/")).Content.ReadAsStringAsync();

        text.Should().Contain("id=\"said\"");
        text.Should().Contain("aria-live=\"assertive\"",
            "a reply to something somebody clicked is not ambient status");

        // And the ambient one is still there and still polite.
        text.Should().Contain("id=\"status\"");
        text.Should().Contain("aria-live=\"polite\"");
    }

    [Fact]
    public async Task The_page_is_one_column_until_there_is_room_for_three()
    {
        // The narrow case is a phone answering a gate from the sofa, and a
        // layout that starts wide and is squeezed reads as an afterthought
        // there. So the columns are what a wide screen adds, not what a narrow
        // one takes away.
        var text = await (await GetAsync("/")).Content.ReadAsStringAsync();

        text.Should().Contain(".panes { display: grid;");
        text.Should().Contain("@media (min-width: 64rem)");

        // The three-column rule must live inside that query and nowhere else.
        var columns = text.IndexOf("grid-template-columns", StringComparison.Ordinal);
        var breakpoint = text.IndexOf("@media (min-width: 64rem)", StringComparison.Ordinal);

        columns.Should().BeGreaterThan(breakpoint, "columns are added by width, not removed by it");
    }

    [Fact]
    public async Task Every_pane_says_what_it_is()
    {
        // Three unnamed regions announce as "region, region, region". The
        // detail pane was exactly that until somebody looked.
        var text = await (await GetAsync("/")).Content.ReadAsStringAsync();

        // On the sections themselves, not merely somewhere in the page: the
        // log inside the detail pane carries the same label, so asking
        // whether the string exists anywhere passed with the pane unnamed.
        text.Should().Contain("<section id=\"rail\" class=\"pane\" hidden aria-labelledby=\"rail-heading\">");
        text.Should().Contain("<section class=\"pane\" aria-labelledby=\"runs\">");
        text.Should().Contain("<section id=\"detail\" class=\"pane\" hidden aria-labelledby=\"detail-heading\">");
    }

    [Fact]
    public async Task Reading_a_run_is_still_reading_it()
    {
        // The route split is by verb, and two of them are not verbs at all.
        // Getting this wrong would turn following a run into an action.
        _server.Act = (_, _) => Task.FromResult(OperationResult.Ok());

        (await GetAsync("/api/runs/r")).StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed);
        (await GetAsync("/api/runs/r/events")).StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed);
        (await GetAsync("/api/runs/r/documents")).StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed);

        // And the one underneath it, which is a name rather than a fixed word.
        (await GetAsync("/api/runs/r/documents/brief-lead.json")).StatusCode
            .Should().NotBe(HttpStatusCode.MethodNotAllowed);
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
        //
        // An XML namespace is not a fetch: `xmlns="http://www.w3.org/2000/svg"`
        // is an identifier a browser never resolves, and the inline SVG
        // favicon needs it to render at all. So it is removed before asking,
        // rather than the question being softened - everything that could
        // actually be fetched is still banned outright.
        var fetchable = text.Replace(
            "xmlns='http://www.w3.org/2000/svg'", string.Empty, StringComparison.Ordinal)
            .Replace("xmlns=\"http://www.w3.org/2000/svg\"", string.Empty, StringComparison.Ordinal);

        fetchable.Should().NotContain("http://").And.NotContain("https://");
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

    /// <summary>Gives the stub a real directory with the papers named in it.</summary>
    private string Papers(params string[] names)
    {
        var root = Path.Combine(Path.GetTempPath(), "loadout-dash-" + Guid.NewGuid().ToString("N")[..8]);
        var run = Path.Combine(root, "20260916-1200-aaaa");

        Directory.CreateDirectory(run);

        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(run, name), """{"said":"something"}""");
        }

        _journal.Root = root;

        return root;
    }

    [Fact]
    public async Task What_a_run_wrote_down_is_listed_without_its_contents()
    {
        // The listing, not the contents. A run with eight nodes has twenty of
        // these and most of them are not the one somebody wants.
        Papers("brief-lead.json", "report-lead.json", "journal.jsonl");

        var json = JsonDocument.Parse(
            await (await GetAsync("/api/runs/20260916-1200-aaaa/documents")).Content.ReadAsStringAsync());

        var papers = json.RootElement.GetProperty("documents");

        papers.GetArrayLength().Should().Be(2, "the journal itself is not one of these");
        papers[0].GetProperty("kind").GetString().Should().Be("brief");
        papers[0].GetProperty("subject").GetString().Should().Be("lead");
        papers[0].TryGetProperty("text", out _).Should().BeFalse("a listing is not twenty documents");
    }

    [Fact]
    public async Task One_of_them_can_be_read()
    {
        Papers("brief-lead.json");

        var json = JsonDocument.Parse(
            await (await GetAsync("/api/runs/20260916-1200-aaaa/documents/brief-lead.json"))
                .Content.ReadAsStringAsync());

        json.RootElement.GetProperty("kind").GetString().Should().Be("brief");
        json.RootElement.GetProperty("text").GetString().Should().Contain("something");
    }

    [Fact]
    public async Task A_name_that_climbs_out_of_the_run_gets_nothing()
    {
        // The name arrives from a browser. Everything below the run's own
        // directory is somebody's home directory.
        Papers("brief-lead.json");

        var answer = await GetAsync("/api/runs/20260916-1200-aaaa/documents/..%2F..%2Fsettings.json");

        answer.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await answer.Content.ReadAsStringAsync()).Should().Contain("not one of the documents");
    }

    [Fact]
    public async Task The_papers_need_the_token_like_everything_else()
    {
        Papers("brief-lead.json");

        (await GetAsync("/api/runs/20260916-1200-aaaa/documents", withToken: false))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await GetAsync("/api/runs/20260916-1200-aaaa/documents/brief-lead.json", withToken: false))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Asking_for_the_papers_of_a_run_nobody_has_is_a_404()
    {
        (await GetAsync("/api/runs/no-such-run/documents")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_detail_pane_has_three_depths_and_says_which_key_reaches_each()
    {
        // A tab strip, so what a screen reader announces matches what the page
        // looks like: three tabs, one selected, three panels below them.
        var text = await (await GetAsync("/")).Content.ReadAsStringAsync();

        text.Should().Contain("""<div class="depths" role="tablist" aria-label="How much detail">""");

        foreach (var tab in new[] { "messages", "turns", "papers" })
        {
            // With the role on it, not merely near it: taking role="tab" off one
            // of them left every one of these assertions passing.
            text.Should().Contain($"role=\"tab\" id=\"tab-{tab}\" aria-controls=\"depth-{tab}\"");
            text.Should().Contain($"id=\"depth-{tab}\" role=\"tabpanel\" aria-labelledby=\"tab-{tab}\"");
        }

        // Written on the tabs rather than left for somebody to discover. A
        // shortcut nobody is told about is a shortcut nobody uses.
        text.Should().Contain("<kbd>1</kbd>").And.Contain("<kbd>2</kbd>").And.Contain("<kbd>3</kbd>");

        // Two of the three are not the selected one. Counted on "false"
        // rather than on "true" because the styles select on "true" as well,
        // and a test that counts those is counting its own stylesheet.
        System.Text.RegularExpressions.Regex.Matches(text, "aria-selected=\"false\"")
            .Should().HaveCount(2);
    }

    [Fact]
    public async Task The_preferences_are_wired_once_and_not_every_four_seconds()
    {
        // This sat inside refresh(), which runs every four seconds, so it
        // added another change handler and another click handler each time.
        // After five minutes one tick of the Sound box chimed seventy-odd
        // times: measured in a browser at thirteen seconds, one click wrote
        // the setting five times, and one after the fix.
        var text = await (await GetAsync("/")).Content.ReadAsStringAsync();

        var refresh = text.IndexOf("function refresh()", StringComparison.Ordinal);

        refresh.Should().BeGreaterThan(0);

        text[refresh..].Should().NotContain("addEventListener",
            "nothing refresh does should leave anything behind it");
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

        /// <summary>Where the run's papers are, when a test has written some.</summary>
        public string? Root { get; set; }

        public string DirectoryOf(string runId) =>
            Root is null ? "C:/runs/" + runId : Path.Combine(Root, runId);
    }
}
