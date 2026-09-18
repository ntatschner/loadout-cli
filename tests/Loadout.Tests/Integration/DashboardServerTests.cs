using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Tests.Fakes;
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
    private readonly FakeGit _git = new("D:/repo");

    private DashboardServer _server = null!;
    private HttpClient _client = null!;
    private string _root = string.Empty;

    public Task InitializeAsync()
    {
        _git.Commits["teams-r-lead"] = "2222222222222222222222222222222222222222";
        _git.Diffs["1111111111111111111111111111111111111111..2222222222222222222222222222222222222222"] =
            "diff --git a/docs/commands.md b/docs/commands.md\n+--since\n";
        _git.Diffs["1111111111111111111111111111111111111111..2222222222222222222222222222222222222222 --stat"] =
            " docs/commands.md | 1 +\n";

        _server = new DashboardServer(_journal, _git);

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
        (await GetAsync("/api/runs/r/stream/lead")).StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed);

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
        // The SVG namespace is not a fetch. http://www.w3.org/2000/svg is an
        // identifier a browser never resolves - the inline favicon needs it as
        // an xmlns to render at all, and createElementNS needs the same string
        // to make an element that is an SVG rather than an unknown tag. So it
        // is removed before asking, rather than the question being softened:
        // everything that could actually be fetched is still banned outright.
        var fetchable = text.Replace(
            "http://www.w3.org/2000/svg", string.Empty, StringComparison.Ordinal);

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
    public async Task Everything_a_node_did_comes_back_as_steps()
    {
        var root = Papers();
        var run = Path.Combine(root, "20260916-1200-aaaa");

        File.WriteAllLines(NodeStream.PathFor(run, "lead"),
        [
            NodeStream.Line(new NodeStep(DateTimeOffset.UtcNow, "tool", "Read", "docs/teams.md")),
            NodeStream.Line(new NodeStep(DateTimeOffset.UtcNow, "said", null, null, "Starting on the docs.")),
        ]);

        var json = JsonDocument.Parse(
            await (await GetAsync("/api/runs/20260916-1200-aaaa/stream/lead")).Content.ReadAsStringAsync());

        var steps = json.RootElement.GetProperty("steps");

        steps.GetArrayLength().Should().Be(2);
        steps[0].GetProperty("tool").GetString().Should().Be("Read");
        steps[1].GetProperty("text").GetString().Should().Contain("Starting");
    }

    [Fact]
    public async Task A_node_with_no_stream_says_so_rather_than_showing_an_empty_one()
    {
        Papers();

        var answer = await GetAsync("/api/runs/20260916-1200-aaaa/stream/lead");

        answer.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await answer.Content.ReadAsStringAsync()).Should().Contain("recorded no stream");
    }

    [Fact]
    public async Task What_a_node_changed_comes_back_as_a_patch_and_a_summary()
    {
        var json = JsonDocument.Parse(
            await (await GetAsync("/api/runs/20260916-1200-aaaa/diff/lead")).Content.ReadAsStringAsync());

        json.RootElement.GetProperty("branch").GetString().Should().Be("teams-r-lead");
        json.RootElement.GetProperty("summary").GetString().Should().Contain("docs/commands.md");
        json.RootElement.GetProperty("patch").GetString().Should().Contain("+--since");

        // Measured from where the branch started, which the run wrote down.
        json.RootElement.GetProperty("from").GetString()
            .Should().Be("1111111111111111111111111111111111111111");
    }

    [Fact]
    public async Task A_node_nobody_has_is_a_404_rather_than_an_empty_patch()
    {
        (await GetAsync("/api/runs/20260916-1200-aaaa/diff/nobody")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_dashboard_with_no_git_says_so_rather_than_showing_nothing()
    {
        // The journal is readable on a machine whose repository has since
        // moved, and most of the page still works there. What a node changed
        // is the one thing that needs the repository itself.
        using var bare = new DashboardServer(_journal);

        var started = bare.Start(0);

        started.Succeeded.Should().BeTrue(started.Error);

        using var stopping = new CancellationTokenSource();

        _ = bare.ListenAsync(stopping.Token);

        var root = bare.Address[..bare.Address.IndexOf('?', StringComparison.Ordinal)];

        var answer = await _client.GetAsync(new Uri(
            root.TrimEnd('/') + "/api/runs/20260916-1200-aaaa/diff/lead?token=" + bare.Token));

        answer.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
        (await answer.Content.ReadAsStringAsync()).Should().Contain("run git");

        await stopping.CancelAsync();
    }

    [Fact]
    public async Task Asking_what_a_node_changed_is_reading_and_needs_the_token()
    {
        (await GetAsync("/api/runs/20260916-1200-aaaa/diff/lead", withToken: false))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var answer = await _client.PostAsync(
            new Uri(_root + "api/runs/20260916-1200-aaaa/diff/lead?token=" + _server.Token),
            new StringContent(string.Empty));

        // Never routed as a verb: a change routed as a read is a change that
        // skipped the check, and the other way round is only a 405.
        answer.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Asking_for_the_papers_of_a_run_nobody_has_is_a_404()
    {
        (await GetAsync("/api/runs/no-such-run/documents")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_detail_pane_has_four_depths_and_says_which_key_reaches_each()
    {
        // A tab strip, so what a screen reader announces matches what the page
        // looks like: three tabs, one selected, three panels below them.
        var text = await (await GetAsync("/")).Content.ReadAsStringAsync();

        text.Should().Contain("""<div class="depths" role="tablist" aria-label="How much detail">""");

        foreach (var tab in new[] { "messages", "turns", "papers", "changes" })
        {
            // With the role on it, not merely near it: taking role="tab" off one
            // of them left every one of these assertions passing.
            text.Should().Contain($"role=\"tab\" id=\"tab-{tab}\" aria-controls=\"depth-{tab}\"");
            text.Should().Contain($"id=\"depth-{tab}\" role=\"tabpanel\" aria-labelledby=\"tab-{tab}\"");
        }

        // Written on the tabs rather than left for somebody to discover. A
        // shortcut nobody is told about is a shortcut nobody uses.
        text.Should().Contain("<kbd>1</kbd>").And.Contain("<kbd>2</kbd>")
            .And.Contain("<kbd>3</kbd>").And.Contain("<kbd>4</kbd>");

        // Two of the three are not the selected one. Counted on "false"
        // rather than on "true" because the styles select on "true" as well,
        // and a test that counts those is counting its own stylesheet.
        System.Text.RegularExpressions.Regex.Matches(text, "aria-selected=\"false\"")
            .Should().HaveCount(3);
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
    public async Task A_run_carries_what_it_is_costing_and_not_only_what_it_has_cost()
    {
        var json = JsonDocument.Parse(await (await GetAsync("/api/runs")).Content.ReadAsStringAsync());

        var numbers = json.RootElement.GetProperty("runs")[0].GetProperty("numbers");

        // The rate in words as well as the figure, because the figure is often
        // a fraction of a penny and nobody has a feel for those.
        numbers.GetProperty("money").GetProperty("rate").GetString()
            .Should().MatchRegex(@"^\$\d+\.\d\d an? (minute|hour)$");

        numbers.GetProperty("rounds").ValueKind.Should().Be(JsonValueKind.Array);
        numbers.GetProperty("nodes").ValueKind.Should().Be(JsonValueKind.Array);
        numbers.GetProperty("trouble").GetProperty("any").ValueKind
            .Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
    }

    private Task<HttpResponseMessage> StartAsync(string body, bool withToken = true) =>
        _client.PostAsync(
            new Uri(_root + "api/start" + (withToken ? "?token=" + _server.Token : string.Empty)),
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

    [Fact]
    public async Task Starting_a_team_types_the_command_somebody_would_have_typed()
    {
        StartRequest? asked = null;

        _server.Begin = (request, _) =>
        {
            asked = request;

            return Task.FromResult(OperationResult.Ok());
        };

        var answer = await StartAsync(
            """{"team":"docs-crew","goal":"check the docs","project":"loadout-cli","rounds":3,"autonomy":"supervised"}""");

        // Accepted rather than done: a team run takes twenty minutes, and a
        // browser holding a request open that long has already given up.
        answer.StatusCode.Should().Be(HttpStatusCode.Accepted);

        asked!.Team.Should().Be("docs-crew");
        asked.Goal.Should().Be("check the docs");
        asked.Project.Should().Be("loadout-cli");
        asked.Rounds.Should().Be(3);
        asked.Autonomy.Should().Be("supervised");
    }

    [Fact]
    public async Task Starting_a_team_needs_the_token()
    {
        _server.Begin = (_, _) => Task.FromResult(OperationResult.Ok());

        (await StartAsync("""{"team":"docs-crew","goal":"go"}""", withToken: false))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Starting_a_team_is_never_a_GET()
    {
        // A link somebody could be sent, or a page could prefetch, must not
        // start something that spends money.
        _server.Begin = (_, _) => Task.FromResult(OperationResult.Ok());

        (await GetAsync("/api/start")).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task A_run_with_nothing_to_do_is_refused_before_anything_is_typed()
    {
        var typed = false;

        _server.Begin = (_, _) =>
        {
            typed = true;

            return Task.FromResult(OperationResult.Ok());
        };

        (await StartAsync("""{"team":"docs-crew","goal":"   "}""")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        typed.Should().BeFalse();
    }

    [Fact]
    public async Task Whatever_the_command_line_says_about_it_reaches_the_page()
    {
        // Nothing here knows what a team is. Whether that one exists is the
        // command line's question, and its answer is the useful one.
        _server.Begin = (_, _) => Task.FromResult(
            OperationResult.Fail("There is no team called 'docs-crew'. Run: loadout team list"));

        var answer = await StartAsync("""{"team":"docs-crew","goal":"go"}""");

        answer.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await answer.Content.ReadAsStringAsync()).Should().Contain("loadout team list");
    }

    [Fact]
    public async Task A_dashboard_that_cannot_run_commands_says_so_rather_than_failing_quietly()
    {
        // The fixture's server has no Begin unless a test sets one, which is
        // the same state a dashboard started on its own is in.
        var answer = await StartAsync("""{"team":"docs-crew","goal":"go"}""");

        answer.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
        (await answer.Content.ReadAsStringAsync()).Should().Contain("command line");
    }

    [Fact]
    public async Task The_page_offers_a_way_to_start_one()
    {
        var text = await (await GetAsync("/")).Content.ReadAsStringAsync();

        // Folded away, because the page is for watching what is already going
        // and a form that is always open is one somebody fills in by accident.
        text.Should().Contain("<details class=\"start\">");
        text.Should().Contain("<summary>Start a team</summary>");

        // Every field the command line takes, and nothing it does not.
        foreach (var field in new[] { "start-team", "start-goal", "start-project", "start-rounds", "start-autonomy" })
        {
            text.Should().Contain($"id=\"{field}\"");
        }
    }

    [Fact]
    public void A_dashboard_on_this_machine_says_it_is_on_this_machine()
    {
        // The fixture's own server, bound to loopback like every other one in
        // this file.
        _server.Beyond.Should().BeFalse();
        _server.Reachable().Should().ContainSingle().Which.Should().Be(_server.Address);
    }

    [Fact]
    public void A_wildcard_becomes_an_address_somebody_could_type()
    {
        // A server bound to 0.0.0.0 prints an address nothing can open. What
        // somebody wants is the one to type into a phone.
        var spread = DashboardServer.Spread(
            "http://0.0.0.0:8477/?token=abc", beyond: true, () => ["192.168.1.9", "10.0.0.4"]);

        spread.Should().Equal(
            "http://192.168.1.9:8477/?token=abc",
            "http://10.0.0.4:8477/?token=abc");
    }

    [Theory]
    [InlineData("http://+:8477/?token=abc")]
    [InlineData("http://*:8477/?token=abc")]
    public void Every_shape_of_wildcard_a_listener_takes(string address)
    {
        DashboardServer.Spread(address, beyond: true, () => ["192.168.1.9"])
            .Should().ContainSingle().Which.Should().Be("http://192.168.1.9:8477/?token=abc");
    }

    [Fact]
    public void An_address_that_is_already_typeable_is_left_exactly_as_it_was()
    {
        const string Given = "http://192.168.1.9:8477/?token=abc";

        DashboardServer.Spread(Given, beyond: true, () => ["10.0.0.4"])
            .Should().ContainSingle().Which.Should().Be(Given);
    }

    [Fact]
    public void A_machine_that_cannot_say_what_it_is_called_still_says_something()
    {
        // Unusual rather than broken, and the wildcard is still true even when
        // nobody can type it. Silence would read as "it is not listening".
        DashboardServer.Spread("http://0.0.0.0:8477/?token=abc", beyond: true, () => [])
            .Should().ContainSingle().Which.Should().Be("http://0.0.0.0:8477/?token=abc");
    }

    [Fact]
    public void A_server_that_never_started_offers_no_address_at_all()
    {
        DashboardServer.Spread(string.Empty, beyond: false, () => ["10.0.0.4"]).Should().BeEmpty();
    }

    [Fact]
    public async Task Every_run_carries_a_room_to_be_in()
    {
        var json = JsonDocument.Parse(await (await GetAsync("/api/runs")).Content.ReadAsStringAsync());

        var room = json.RootElement.GetProperty("runs")[0].GetProperty("room").GetString();

        room.Should().Be(RoomNames.For("20260916-1200-aaaa"));
        room.Should().StartWith("The ");
    }

    [Fact]
    public async Task There_are_four_ways_to_look_at_the_same_state()
    {
        var text = await (await GetAsync("/")).Content.ReadAsStringAsync();

        text.Should().Contain("<div class=\"views\" role=\"group\" aria-label=\"How to look at them\">");

        foreach (var view in new[] { "list", "office", "graph", "when" })
        {
            text.Should().Contain($"id=\"view-{view}\"");
        }

        // One of them is on, the other two are not. A group where every button
        // claims to be pressed announces as three pressed buttons.
        System.Text.RegularExpressions.Regex.Matches(text, "aria-pressed=\"false\"")
            .Should().HaveCount(3);
    }

    [Fact]
    public async Task None_of_the_other_views_can_do_anything_to_a_run()
    {
        // Every control lives in the detail pane, so answering a gate is
        // implemented once rather than three times. The office and the graph
        // are boxes to be filled in, and what fills them only ever opens a run.
        var text = await (await GetAsync("/")).Content.ReadAsStringAsync();

        text.Should().Contain("<ul class=\"rooms\" id=\"office\" hidden></ul>");
        text.Should().Contain("<div id=\"graph\" hidden></div>");
        text.Should().Contain("<div id=\"when\" hidden></div>");
    }

    [Fact]
    public async Task Everything_the_page_puts_in_the_body_reaches_whatever_types_the_command()
    {
        // Every field on its own rather than one happy path, because the way
        // this goes wrong is silent: a field added at one end and not the
        // other arrives as null, the command runs with a default, and the page
        // is told it worked. Renaming a room did exactly that - the body
        // carried a name, the server never read it, and "--clear" on a run
        // with no name succeeds.
        RunAction? asked = null;

        _server.Act = (action, _) =>
        {
            asked = action;

            return Task.FromResult(OperationResult.Ok());
        };

        await _client.PostAsync(
            new Uri(_root + "api/runs/r/anything?token=" + _server.Token),
            new StringContent(
                """{"gate":"toolu_1","answer":"yes","reason":"it needs the suite","message":"leave the tests alone","room":"The Haunted Meeting Room","node":"implementer/1","instead":"do something else"}""",
                System.Text.Encoding.UTF8,
                "application/json"));

        asked!.Run.Should().Be("r");
        asked.Verb.Should().Be("anything");
        asked.Gate.Should().Be("toolu_1");
        asked.Answer.Should().Be("yes");
        asked.Reason.Should().Be("it needs the suite");
        asked.Message.Should().Be("leave the tests alone");
        asked.Room.Should().Be("The Haunted Meeting Room");
        asked.Node.Should().Be("implementer/1");
        asked.Instead.Should().Be("do something else");
    }

    [Fact]
    public async Task The_page_offers_a_way_to_type_at_a_node_and_hides_it_by_default()
    {
        var text = await (await GetAsync("/")).Content.ReadAsStringAsync();

        // Hidden until somebody opens a live node's own stream, which is the
        // only moment at which saying something to it rather than to the lead
        // makes any sense.
        text.Should().Contain("<div class=\"controls\" id=\"steer\" hidden>");
        text.Should().Contain("<label for=\"steer-said\"");
        text.Should().Contain("id=\"steer-send\"");

        // And it carries the second credential rather than the first.
        text.Should().Contain("X-Loadout-Attach");
    }

    [Fact]
    public async Task A_brief_offered_for_changing_gets_a_box_rather_than_two_buttons()
    {
        var text = await (await GetAsync("/")).Content.ReadAsStringAsync();

        // The one question with a third answer. Yes and no would make somebody
        // choose between the wrong brief and no brief.
        text.Should().Contain("gate.kind === \"brief\"");
        text.Should().Contain("createElement(\"textarea\")");

        // And the box is labelled, like every other box on the page.
        text.Should().Contain("label.htmlFor = \"instead-\" + gate.id");
    }

    [Fact]
    public async Task A_finished_run_can_be_run_again()
    {
        var text = await (await GetAsync("/")).Content.ReadAsStringAsync();

        text.Should().Contain("id=\"again\">Run it again</button>");

        // It fills the form rather than starting anything, because a run that
        // has been run before is exactly the one somebody wants to change one
        // thing about before running again.
        text.Should().Contain("again.onclick");
        text.Should().Contain("form.open = true");
    }

    [Fact]
    public async Task Refusing_a_gate_is_not_one_thing_and_the_page_does_not_say_it_is()
    {
        var text = await (await GetAsync("/")).Content.ReadAsStringAsync();

        // Refusing a permission tells the node no and the run carries on;
        // refusing a brief or a confirmation stops it, and so does choosing to
        // stop one outright. A warning that said "that stops the run" over
        // every refusal was wrong about the commonest of the three, and a
        // warning that is wrong teaches somebody to click past it.
        text.Should().Contain("is told no and carries on without it");
        text.Should().Contain("That stops the run");
        text.Should().Contain("gate.kind === \"confirm\" || gate.kind === \"brief\"");
        text.Should().Contain("option === \"Stop the run\"");
    }

    [Fact]
    public async Task A_question_that_already_ends_in_one_does_not_get_a_second()
    {
        var text = await (await GetAsync("/")).Content.ReadAsStringAsync();

        // A permission is written without a mark so whoever asks can add one -
        // the first real run asked somebody "Let it??" and that is why. A
        // lead's own question arrives with one, and appending another brought
        // the same defect back on a different gate.
        text.Should().Contain("function asking(question)");

        text.Should().NotContain("gate.question + \"?\"",
            "the mark goes on only where there is not one already");
    }

    [Fact]
    public async Task Hidden_means_hidden()
    {
        var text = await (await GetAsync("/")).Content.ReadAsStringAsync();

        // A browser hides [hidden] through its own stylesheet, and any author
        // rule that sets display beats it. `.controls { display: flex }` left
        // the box for typing at a live node on screen at all times - 664 by 78
        // pixels of it, and in the tab order - while its hidden attribute said
        // otherwise, and every test passed because the attribute was in the
        // markup, which is what they look at.
        //
        // This one cannot see it either. What it can do is insist the rule that
        // settles it is still there.
        text.Should().Contain("[hidden] { display: none !important; }");
    }

    [Fact]
    public async Task The_page_offers_a_way_to_rename_a_room()
    {
        var text = await (await GetAsync("/")).Content.ReadAsStringAsync();

        // In the detail pane with the other controls, because there is one
        // place anything is done from and three views that only look.
        text.Should().Contain("id=\"room\"");
        text.Should().Contain("<label for=\"room\"");
        text.Should().Contain("id=\"rename\"");
    }

    private Task<HttpResponseMessage> SayAsync(string? grant)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(_root + "api/runs/20260916-1200-aaaa/say?token=" + _server.Token))
        {
            Content = new StringContent(
                """{"node":"lead","message":"stop, wrong file"}""",
                System.Text.Encoding.UTF8,
                "application/json"),
        };

        if (grant is not null)
        {
            request.Headers.Add("X-Loadout-Attach", grant);
        }

        return _client.SendAsync(request);
    }

    [Fact]
    public async Task The_token_that_gets_you_here_does_not_let_you_type_at_a_node()
    {
        // The whole of the split. Reading a run, answering a gate it asked and
        // stopping it are things the run offered to have decided. Typing at a
        // live node is not: it puts words into a process running with somebody's
        // file access, at a moment nobody chose.
        var typed = false;

        _server.Act = (_, _) =>
        {
            typed = true;

            return Task.FromResult(OperationResult.Ok());
        };

        _server.Attach = new Attaching(new NoSecrets());

        var answer = await SayAsync(grant: null);

        answer.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await answer.Content.ReadAsStringAsync()).Should().Contain("separate grant");

        typed.Should().BeFalse();
    }

    [Fact]
    public async Task A_grant_from_the_passphrase_does()
    {
        RunAction? asked = null;

        _server.Act = (action, _) =>
        {
            asked = action;

            return Task.FromResult(OperationResult.Ok());
        };

        var attach = new Attaching(new NoSecrets { Kept = "a passphrase worth having" });

        _server.Attach = attach;

        var got = await _client.PostAsync(
            new Uri(_root + "api/attach?token=" + _server.Token),
            new StringContent(
                """{"passphrase":"a passphrase worth having"}""",
                System.Text.Encoding.UTF8,
                "application/json"));

        got.StatusCode.Should().Be(HttpStatusCode.OK);

        var grant = JsonDocument.Parse(await got.Content.ReadAsStringAsync())
            .RootElement.GetProperty("grant").GetString();

        (await SayAsync(grant)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        asked!.Verb.Should().Be("say");
        asked.Node.Should().Be("lead");
        asked.Message.Should().Be("stop, wrong file");
    }

    [Fact]
    public async Task A_grant_somebody_made_up_is_not_one()
    {
        _server.Act = (_, _) => Task.FromResult(OperationResult.Ok());
        _server.Attach = new Attaching(new NoSecrets { Kept = "a passphrase worth having" });

        (await SayAsync(new string('a', 64))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Asking_to_attach_needs_the_dashboard_token_as_well()
    {
        _server.Attach = new Attaching(new NoSecrets { Kept = "a passphrase worth having" });

        var answer = await _client.PostAsync(
            new Uri(_root + "api/attach"),
            new StringContent("""{"passphrase":"a passphrase worth having"}""",
                System.Text.Encoding.UTF8, "application/json"));

        // No reason for anything without the first credential to be guessing at
        // the second.
        answer.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_dashboard_that_cannot_attach_at_all_says_so()
    {
        // The fixture's server has no Attaching unless a test sets one, which
        // is the state a dashboard started on its own is in.
        var answer = await _client.PostAsync(
            new Uri(_root + "api/attach?token=" + _server.Token),
            new StringContent("""{"passphrase":"anything"}""",
                System.Text.Encoding.UTF8, "application/json"));

        answer.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
        (await answer.Content.ReadAsStringAsync()).Should().Contain("team attach set");
    }

    /// <summary>A credential store holding one thing, or nothing.</summary>
    private sealed class NoSecrets : Loadout.Platform.Abstractions.ISecretProvider
    {
        public string? Kept { get; init; }

        public string Name => "none";

        public Task<OperationResult> IsAvailableAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult.Ok());

        public Task<OperationResult<string>> GetAsync(string reference, CancellationToken ct = default) =>
            Task.FromResult(Kept is { Length: > 0 }
                ? OperationResult<string>.Ok(Kept)
                : OperationResult<string>.Fail("nothing kept", Loadout.Models.ExitCode.GeneralFailure));

        public Task<OperationResult> SetAsync(string reference, string value, CancellationToken ct = default) =>
            Task.FromResult(OperationResult.Ok());

        public Task<OperationResult> RemoveAsync(string reference, CancellationToken ct = default) =>
            Task.FromResult(OperationResult.Ok());

        public Task<OperationResult> TestAsync(string reference, CancellationToken ct = default) =>
            Task.FromResult(OperationResult.Ok());
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
            """{"at":"2026-09-16T12:00:00+00:00","run":"r","node":null,"kind":"run.started","data":{"team":"iterating-project","goal":"Add --since","autonomy":"autonomous","rounds":5,"path":"D:/repo"}}""",
            """{"at":"2026-09-16T12:00:02+00:00","run":"r","node":"lead","kind":"node.launched","data":{"role":"role.project-lead","worktree":"teams-r-lead","base":"1111111111111111111111111111111111111111"}}""",
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
