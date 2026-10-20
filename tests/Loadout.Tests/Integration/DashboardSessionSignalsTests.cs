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
/// A hosted session waiting for its person, as the page reads it: told by
/// the agent's own hook, cleared by the person typing, and still offering to
/// resume after the daemon that hosted it has gone.
/// </summary>
public sealed class DashboardSessionSignalsTests : IAsyncLifetime
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly CancellationTokenSource _stopping = new();
    private readonly List<ScriptedTerminal> _terminals = [];
    private readonly string _signals = Path.Combine(Path.GetTempPath(), "loadout-signals-" + Guid.NewGuid().ToString("N"));
    private HostedSessions _sessions = null!;
    private DashboardServer _server = null!;
    private HttpClient _client = null!;
    private string _root = string.Empty;

    public Task InitializeAsync()
    {
        _sessions = new HostedSessions(
            Made, () => ("loadout-host", []), signals: _signals,
            renaming: (agent, name) => agent == "claude" ? "/rename " + name : null);
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

        try
        {
            Directory.Delete(_signals, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task A_session_is_told_its_own_id_so_its_agents_hooks_know_where_to_write()
    {
        var id = await StartAsync();

        _terminals.Single().Request!.Environment.Should().Contain(SessionSignals.Variable, id);
    }

    [Fact]
    public async Task What_the_hook_said_is_shown_until_somebody_types()
    {
        var id = await StartAsync();
        var shown = await OneAsync(id);

        shown.GetProperty("needs").ValueKind.Should().Be(JsonValueKind.Null, "nothing has been said, and nothing is read off the screen");

        SessionSignals.Write(_signals, id, "approval", "5f0c1e2a-9d3b", DateTimeOffset.UtcNow);
        shown = await OneAsync(id);

        shown.GetProperty("needs").GetString().Should().Be("approval");
        shown.GetProperty("agentSession").GetString().Should().Be("5f0c1e2a-9d3b");

        await TypeAsync(id, "\u001b[I");
        (await OneAsync(id)).GetProperty("needs").GetString().Should().Be("approval", "a window's focus report is not an answer");

        await TypeAsync(id, "1");
        shown = await OneAsync(id);

        shown.GetProperty("needs").ValueKind.Should().Be(JsonValueKind.Null, "answering in the terminal is the answer");
        shown.GetProperty("agentSession").GetString().Should().Be("5f0c1e2a-9d3b");
    }

    [Fact]
    public async Task Every_open_page_hears_when_a_hook_says_a_session_is_waiting()
    {
        var id = await StartAsync();

        using var patience = new CancellationTokenSource(Patience);
        using var events = await _client.GetAsync($"{_root}/api/v1/events", HttpCompletionOption.ResponseHeadersRead, patience.Token);
        using var reader = new StreamReader(await events.Content.ReadAsStreamAsync(patience.Token));

        // The first change is the stream catching up with what is there.
        await NextChangeAsync(reader, patience.Token);

        SessionSignals.Write(_signals, id, "waiting", null, DateTimeOffset.UtcNow);

        (await NextChangeAsync(reader, patience.Token)).Should().Contain("sessions");
    }

    [Fact]
    public async Task Every_open_page_hears_when_a_session_is_renamed()
    {
        var id = await StartAsync();

        using var patience = new CancellationTokenSource(Patience);
        using var events = await _client.GetAsync($"{_root}/api/v1/events", HttpCompletionOption.ResponseHeadersRead, patience.Token);
        using var reader = new StreamReader(await events.Content.ReadAsStreamAsync(patience.Token));

        await NextChangeAsync(reader, patience.Token);
        await RenameAsync(id, "release notes");

        (await NextChangeAsync(reader, patience.Token)).Should().Contain("sessions");
    }

    [Fact]
    public async Task A_new_name_is_typed_into_the_agent_only_once_it_says_it_is_waiting()
    {
        var id = await StartAsync();
        var terminal = _terminals.Single();

        await RenameAsync(id, "first try");
        await RenameAsync(id, "release notes");
        SessionSignals.Write(_signals, id, "working", "5f0c1e2a", DateTimeOffset.UtcNow.AddSeconds(-10), agent: "claude");
        await OneAsync(id);
        await Task.Delay(300);

        terminal.Typed.Should().BeEmpty("nothing is typed into a turn that is running");

        SessionSignals.Write(_signals, id, "waiting", null, DateTimeOffset.UtcNow.AddSeconds(-5));
        var shown = await OneAsync(id);

        shown.GetProperty("namesAgent").GetBoolean().Should().BeTrue();
        await UntilTypedAsync(terminal, "/rename release notes\r");
        terminal.Typed.Should().Be("/rename release notes\r", "only the newest name goes, and only once");
        (await OneAsync(id)).GetProperty("needs").GetString().Should().Be("waiting", "the daemon typing is not somebody answering");

        await Task.Delay(300);
        terminal.Typed.Should().Be("/rename release notes\r");
    }

    [Fact]
    public async Task A_rename_while_the_agent_is_waiting_is_typed_at_once()
    {
        var id = await StartAsync();
        SessionSignals.Write(_signals, id, "waiting", "5f0c1e2a", DateTimeOffset.UtcNow, agent: "claude");

        await RenameAsync(id, "docs");

        await UntilTypedAsync(_terminals.Single(), "/rename docs\r");
    }

    [Theory]
    [InlineData("codex")]
    [InlineData(null)]
    public async Task An_agent_that_cannot_take_a_name_keeps_it_as_Loadouts_own(string? agent)
    {
        var id = await StartAsync();
        SessionSignals.Write(_signals, id, "waiting", "5f0c1e2a", DateTimeOffset.UtcNow, agent: agent);

        var shown = await RenameAsync(id, "docs");
        await Task.Delay(300);

        shown.GetProperty("title").GetString().Should().Be("docs");
        shown.GetProperty("namesAgent").GetBoolean().Should().BeFalse();
        _terminals.Single().Typed.Should().BeEmpty();
    }

    [Fact]
    public async Task A_name_waiting_for_an_agent_that_has_ended_is_never_typed()
    {
        var id = await StartAsync();
        await RenameAsync(id, "docs");

        using var ended = await _client.PostAsync($"{_root}{SessionsApi.Root}/{id}/end", null);
        SessionSignals.Write(_signals, id, "waiting", null, DateTimeOffset.UtcNow, agent: "claude");
        await OneAsync(id);
        await Task.Delay(300);

        ended.StatusCode.Should().Be(HttpStatusCode.OK);
        _terminals.Single().Typed.Should().BeEmpty();
    }

    private async Task<JsonElement> RenameAsync(string id, string name)
    {
        using var content = new StringContent(JsonSerializer.Serialize(new { title = name }), Encoding.UTF8, "application/json");
        using var renamed = await _client.PatchAsync($"{_root}{SessionsApi.Root}/{id}", content);
        renamed.StatusCode.Should().Be(HttpStatusCode.OK);

        return await renamed.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task UntilTypedAsync(ScriptedTerminal terminal, string wanted)
    {
        using var patience = new CancellationTokenSource(Patience);

        while (terminal.Typed != wanted)
        {
            await Task.Delay(20, patience.Token);
        }
    }

    private static async Task<string> NextChangeAsync(StreamReader reader, CancellationToken ct)
    {
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                return line;
            }
        }

        return string.Empty;
    }

    [Fact]
    public async Task A_session_an_earlier_daemon_hosted_is_gone_with_its_conversation_id()
    {
        SessionSignals.Write(_signals, "0000ffff", "turn", "5f0c1e2a-9d3b", DateTimeOffset.UtcNow);

        using var gone = await _client.GetAsync($"{_root}{SessionsApi.Root}/0000ffff");

        gone.StatusCode.Should().Be(HttpStatusCode.Gone);
        var body = await gone.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("lost").GetBoolean().Should().BeTrue();
        body.GetProperty("agentSession").GetString().Should().Be("5f0c1e2a-9d3b");
    }

    [Fact]
    public async Task Ending_a_session_forgets_what_its_hooks_said()
    {
        var id = await StartAsync();
        SessionSignals.Write(_signals, id, "waiting", "5f0c1e2a", DateTimeOffset.UtcNow);

        using var ended = await _client.DeleteAsync($"{_root}{SessionsApi.Root}/{id}");
        using var after = await _client.GetAsync($"{_root}{SessionsApi.Root}/{id}");

        ended.StatusCode.Should().Be(HttpStatusCode.NoContent);
        after.StatusCode.Should().Be(HttpStatusCode.NotFound, "ended from the page, it is not lost");
    }

    private async Task<string> StartAsync()
    {
        using var started = await _client.PostAsJsonAsync(_root + SessionsApi.Root, new { project = "starstats" });
        started.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private async Task<JsonElement> OneAsync(string id) =>
        await _client.GetFromJsonAsync<JsonElement>($"{_root}{SessionsApi.Root}/{id}");

    private async Task TypeAsync(string id, string text)
    {
        var terminal = _terminals.Single();
        using var window = new ClientWebSocket();
        using var patience = new CancellationTokenSource(Patience);
        await window.ConnectAsync(
            new Uri($"ws://127.0.0.1:{_server.Port}{SessionsApi.Root}/{id}/terminal?token={_server.Token}"),
            patience.Token);

        await window.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Binary, true, patience.Token);

        // Everything typed so far, so what this call typed is its end.
        while (!terminal.Typed.EndsWith(text, StringComparison.Ordinal))
        {
            await Task.Delay(20, patience.Token);
        }
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
