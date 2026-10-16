using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Sessions.Hosting;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Results;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The hosted sessions under /api/v1/sessions, against the real listener with
/// a scripted terminal behind them: who may reach them, and what each route
/// does.
/// </summary>
public sealed class DashboardSessionsTests : IAsyncLifetime
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly CancellationTokenSource _stopping = new();
    private readonly List<ScriptedTerminal> _terminals = [];
    private HostedSessions _sessions = null!;
    private DashboardServer _server = null!;
    private HttpClient _client = null!;
    private string _root = string.Empty;

    public Task InitializeAsync()
    {
        _sessions = new HostedSessions(Made, () => ("loadout-host", []));
        _server = new DashboardServer(new EmptyJournal()) { Sessions = _sessions };

        var started = _server.Start(0);
        started.Succeeded.Should().BeTrue(started.Error);

        _root = $"http://127.0.0.1:{_server.Port}";
        _client = new HttpClient { Timeout = Patience };
        _client.DefaultRequestHeaders.Add("X-Loadout-Token", _server.Token);

        _ = _server.ListenAsync(_stopping.Token);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _stopping.CancelAsync();
        await _sessions.DisposeAsync();
        _client.Dispose();
        _server.Dispose();
        _stopping.Dispose();
    }

    [Fact]
    public async Task Without_the_token_or_a_cookie_nothing_is_listed_or_started()
    {
        using var stranger = new HttpClient { Timeout = Patience };

        using var listed = await stranger.GetAsync(_root + SessionsApi.Root);
        using var started = await stranger.PostAsJsonAsync(_root + SessionsApi.Root, new { project = "starstats" });

        listed.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        started.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _terminals.Should().BeEmpty();
    }

    [Fact]
    public async Task A_request_from_another_machine_is_refused_even_with_the_token()
    {
        _server.From = _ => false;

        using var started = await _client.PostAsJsonAsync(_root + SessionsApi.Root, new { project = "starstats" });

        started.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await started.Content.ReadAsStringAsync()).Should().Contain("only reachable from this machine");
        _terminals.Should().BeEmpty();
    }

    [Fact]
    public async Task A_dashboard_that_hosts_nothing_says_so()
    {
        _server.Sessions = null;

        using var listed = await _client.GetAsync(_root + SessionsApi.Root);

        listed.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await listed.Content.ReadAsStringAsync()).Should().Contain("hosts no sessions");
    }

    [Fact]
    public async Task Starting_one_answers_where_it_is_and_lists_it()
    {
        using var started = await _client.PostAsJsonAsync(
            _root + SessionsApi.Root,
            new { project = "starstats", agent = "codex", columns = 100, rows = 30 });

        started.StatusCode.Should().Be(HttpStatusCode.Created);
        var made = await started.Content.ReadFromJsonAsync<JsonElement>();
        var id = made.GetProperty("id").GetString()!;

        started.Headers.Location!.OriginalString.Should().Be($"{SessionsApi.Root}/{id}");
        made.GetProperty("terminal").GetString().Should().Be($"{SessionsApi.Root}/{id}/terminal");
        made.GetProperty("columns").GetInt32().Should().Be(100);
        _terminals.Single().Request!.Arguments.Should().Equal("launch", "starstats", "--agent=codex");

        var listed = await _client.GetFromJsonAsync<JsonElement>(_root + SessionsApi.Root);

        listed.EnumerateArray().Select(one => one.GetProperty("id").GetString()).Should().Equal(id);
        listed[0].GetProperty("title").GetString().Should().Be("starstats (codex)");
        listed[0].GetProperty("ended").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"agent":"codex"}""")]
    [InlineData("""{"project":"--help"}""")]
    public async Task A_start_that_names_no_usable_project_is_refused(string body)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var started = await _client.PostAsync(_root + SessionsApi.Root, content);

        started.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _terminals.Should().BeEmpty();
    }

    [Fact]
    public async Task Ending_one_stops_it_and_forgets_it()
    {
        var id = await StartAsync();

        using var ended = await _client.DeleteAsync($"{_root}{SessionsApi.Root}/{id}");
        using var after = await _client.GetAsync($"{_root}{SessionsApi.Root}/{id}");

        ended.StatusCode.Should().Be(HttpStatusCode.NoContent);
        after.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _terminals.Single().Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task A_session_that_is_not_there_is_not_found()
    {
        using var one = await _client.GetAsync($"{_root}{SessionsApi.Root}/nothing");
        using var gone = await _client.DeleteAsync($"{_root}{SessionsApi.Root}/nothing");
        using var beside = await _client.GetAsync($"{_root}{SessionsApi.Root}x");

        one.StatusCode.Should().Be(HttpStatusCode.NotFound);
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
        beside.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_terminal_is_a_websocket_that_carries_both_ways()
    {
        var id = await StartAsync();
        var terminal = _terminals.Single();
        terminal.Say("hello from the agent");

        using var window = new ClientWebSocket();
        using var patience = new CancellationTokenSource(Patience);
        await window.ConnectAsync(
            new Uri($"ws://127.0.0.1:{_server.Port}{SessionsApi.Root}/{id}/terminal?token={_server.Token}"),
            patience.Token);

        (await ReadUntilAsync(window, "hello from the agent", patience.Token)).Should().Contain("hello from the agent");

        await window.SendAsync(Encoding.UTF8.GetBytes("typed"), WebSocketMessageType.Binary, true, patience.Token);

        while (terminal.Typed != "typed")
        {
            await Task.Delay(20, patience.Token);
        }
    }

    [Fact]
    public async Task The_terminal_opened_as_a_page_says_what_it_is()
    {
        var id = await StartAsync();

        using var plain = await _client.GetAsync($"{_root}{SessionsApi.Root}/{id}/terminal");

        plain.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await plain.Content.ReadAsStringAsync()).Should().Contain("WebSocket");
    }

    [Fact]
    public async Task A_method_a_route_does_not_take_is_refused()
    {
        var id = await StartAsync();

        using var list = await _client.DeleteAsync(_root + SessionsApi.Root);
        using var one = await _client.PostAsync($"{_root}{SessionsApi.Root}/{id}", null);

        list.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        one.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    private async Task<string> StartAsync()
    {
        using var started = await _client.PostAsJsonAsync(_root + SessionsApi.Root, new { project = "starstats" });
        started.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
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

    private static async Task<string> ReadUntilAsync(WebSocket socket, string wanted, CancellationToken ct)
    {
        var seen = new StringBuilder();
        var buffer = new byte[16 * 1024];

        while (!seen.ToString().Contains(wanted, StringComparison.Ordinal))
        {
            var received = await socket.ReceiveAsync(buffer, ct);

            if (received.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (received.MessageType == WebSocketMessageType.Binary)
            {
                seen.Append(Encoding.UTF8.GetString(buffer, 0, received.Count));
            }
        }

        return seen.ToString();
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
