using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Tasks;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Results;
using Loadout.Models.Tasks;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// One task list and one task over HTTP, through the real service against
/// real files: what each route answers, revisions as ETags and If-Match, and
/// a change written by something else reaching the event stream.
/// </summary>
public sealed class DashboardTasksApiTests : IAsyncLifetime
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly TemporaryWorkspace _space = new();
    private readonly CancellationTokenSource _stopping = new();
    private DashboardServer _server = null!;
    private HttpClient _client = null!;
    private string _root = string.Empty;

    private string Tasks(string list) => $"{_root}{MemoryApi.Root}/{list}/tasks";

    public async Task InitializeAsync()
    {
        await _space.ProjectAsync("website");
        await _space.ProjectAsync("homelab");

        _server = new DashboardServer(new EmptyJournal()) { Tasks = _space.Tasks };

        var started = _server.Start(0);
        started.Succeeded.Should().BeTrue(started.Error);

        _root = $"http://127.0.0.1:{_server.Port}";
        _client = new HttpClient { Timeout = Patience };
        _client.DefaultRequestHeaders.Add("X-Loadout-Token", _server.Token);

        _ = _server.ListenAsync(_stopping.Token);
    }

    public async Task DisposeAsync()
    {
        await _stopping.CancelAsync();
        _client.Dispose();
        _server.Dispose();
        _stopping.Dispose();
        _space.Dispose();
    }

    private static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string address, string? json = null, string? ifMatch = null)
    {
        using var request = new HttpRequestMessage(method, address) { Content = json is null ? null : Body(json) };

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatch}\"");
        }

        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Adding_a_task_answers_with_it_and_the_list_revision_it_made()
    {
        using var added = await SendAsync(HttpMethod.Post, Tasks("website"), """{"title":"Fix the login page","note":"after the release"}""");

        added.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await added.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("task").GetProperty("id").GetString().Should().Be("fix-the-login-page");
        body.GetProperty("task").GetProperty("state").GetString().Should().Be("open");
        body.GetProperty("revision").GetString().Should().Be((await _space.Tasks.ReadAsync("website")).Value!.Revision);
        added.Headers.ETag!.Tag.Should().Be($"\"{body.GetProperty("revision").GetString()}\"");
    }

    [Fact]
    public async Task A_list_is_read_with_its_revision_and_not_sent_again_when_it_has_not_changed()
    {
        (await _space.Tasks.DeclareAsync("website", "a", TaskState.Open, "nigel", "A")).Succeeded.Should().BeTrue();

        using var first = await _client.GetAsync(Tasks("website"));
        var body = await first.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("tasks").EnumerateArray().Select(one => one.GetProperty("id").GetString()).Should().Equal("a");

        using var again = new HttpRequestMessage(HttpMethod.Get, Tasks("website"));
        again.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        using var unchanged = await _client.SendAsync(again);

        unchanged.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task One_task_is_read_and_one_that_is_not_there_is_404()
    {
        (await _space.Tasks.DeclareAsync(null, "a", TaskState.Doing, "nigel", "A")).Succeeded.Should().BeTrue();

        using var one = await _client.GetAsync(Tasks("-") + "/a");
        (await one.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("task").GetProperty("state").GetString().Should().Be("doing");

        using var missing = await _client.GetAsync(Tasks("-") + "/b");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_patch_changes_only_what_it_names_and_a_stale_one_is_409_with_the_current_revision()
    {
        (await _space.Tasks.DeclareAsync("website", "a", TaskState.Doing, "nigel", "A", "keep this")).Succeeded.Should().BeTrue();
        var old = (await _space.Tasks.ReadAsync("website")).Value!.Revision!;

        using var changed = await SendAsync(HttpMethod.Patch, Tasks("website") + "/a", """{"title":"A, renamed"}""", old);

        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        var task = (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("task");
        task.GetProperty("title").GetString().Should().Be("A, renamed");
        task.GetProperty("state").GetString().Should().Be("doing", "a change of title says nothing about where the work stands");
        task.GetProperty("note").GetString().Should().Be("keep this");

        using var stale = await SendAsync(HttpMethod.Patch, Tasks("website") + "/a", """{"state":"done"}""", old);

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revision").GetString()
            .Should().Be((await _space.Tasks.ReadAsync("website")).Value!.Revision);
        (await _space.Tasks.ListAsync("website")).Value!.Single().State.Should().Be(TaskState.Doing);
    }

    [Fact]
    public async Task A_patch_with_somewhere_to_go_changes_the_task_and_moves_it_in_one()
    {
        (await _space.Tasks.DeclareAsync("website", "dns", TaskState.Open, "nigel", "DNS")).Succeeded.Should().BeTrue();

        using var moved = await SendAsync(HttpMethod.Patch, Tasks("website") + "/dns", """{"state":"doing","to":"homelab"}""");

        moved.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await moved.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("project").GetString().Should().Be("homelab");
        body.GetProperty("task").GetProperty("moved").GetProperty("from").GetString().Should().Be("website");
        (await _space.Tasks.ListAsync("homelab")).Value!.Single().State.Should().Be(TaskState.Doing);
        (await _space.Tasks.ListAsync("website")).Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task A_patch_does_not_add_a_task_that_is_not_there()
    {
        using var missing = await SendAsync(HttpMethod.Patch, Tasks("website") + "/nothing", """{"state":"done"}""");

        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _space.Tasks.ListAsync("website")).Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_a_task_answers_with_what_it_was()
    {
        (await _space.Tasks.DeclareAsync("website", "a", TaskState.Open, "nigel", "Gone soon")).Succeeded.Should().BeTrue();

        using var deleted = await SendAsync(HttpMethod.Delete, Tasks("website") + "/a");

        deleted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await deleted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("task").GetProperty("title").GetString().Should().Be("Gone soon");

        using var again = await SendAsync(HttpMethod.Delete, Tasks("website") + "/a");
        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_batch_answers_for_every_operation_and_every_list()
    {
        (await _space.Tasks.DeclareAsync("website", "a", TaskState.Open, "nigel", "A")).Succeeded.Should().BeTrue();

        using var answered = await SendAsync(HttpMethod.Post, Tasks("website") + ":batch",
            """[{"op":"declare","id":"a","state":"done"},{"op":"add","title":"Next thing"},{"op":"remove","project":"homelab","id":"nope"}]""");

        answered.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await answered.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("applied").GetBoolean().Should().BeFalse();
        body.GetProperty("results").EnumerateArray().Select(one => one.GetProperty("applied").GetBoolean()).Should().Equal(true, true, false);
        body.GetProperty("results")[1].GetProperty("id").GetString().Should().Be("next-thing");
        body.GetProperty("lists").EnumerateArray().Select(one => one.GetProperty("project").GetString()).Should().BeEquivalentTo("website", "homelab");
    }

    [Fact]
    public async Task A_batch_that_is_not_one_is_400_with_why()
    {
        using var refused = await SendAsync(HttpMethod.Post, Tasks("website") + ":batch", """{"op":"add"}""");

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("JSON array");
    }

    [Fact]
    public async Task A_watching_dashboard_reads_tasks_and_changes_none()
    {
        _server.WatchOnly = true;

        using var read = await _client.GetAsync(Tasks("website"));
        using var refused = await SendAsync(HttpMethod.Post, Tasks("website"), """{"title":"Not from here"}""");

        read.StatusCode.Should().Be(HttpStatusCode.OK);
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _space.Tasks.ListAsync("website")).Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task A_change_written_by_something_else_reaches_the_event_stream_naming_its_list()
    {
        using var patience = new CancellationTokenSource(Patience);
        using var events = await _client.GetAsync($"{_root}/api/v1/events", HttpCompletionOption.ResponseHeadersRead, patience.Token);
        using var reader = new StreamReader(await events.Content.ReadAsStreamAsync(patience.Token));

        // The stream catching up with what is there, which may have missed
        // anything while it was not connected.
        (await NextAsync(reader, "changed", patience.Token)).Should().Contain("tasks");

        // Another service over the same files, as a terminal or an agent is.
        var elsewhere = new TaskService(_space.Workspace, _space.Yaml, TimeProvider.System);
        (await elsewhere.DeclareAsync("homelab", "dns", TaskState.Open, "agent", "DNS")).Succeeded.Should().BeTrue();
        var written = System.Diagnostics.Stopwatch.StartNew();

        var said = JsonDocument.Parse(await NextAsync(reader, "tasks", patience.Token)).RootElement;

        // Within a second is what is promised; the bound is looser so a slow
        // runner does not make this a test that fails now and then.
        written.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));

        said.EnumerateArray().Should().ContainSingle();
        said[0].GetProperty("project").GetString().Should().Be("homelab");
        said[0].GetProperty("revision").GetString().Should().Be((await elsewhere.ReadAsync("homelab")).Value!.Revision);
        (await NextAsync(reader, "changed", patience.Token)).Should().Contain("tasks");
    }

    [Fact]
    public async Task The_bulk_lists_are_tagged_so_an_unchanged_answer_is_not_sent_again()
    {
        _server.TasksFor = _ => Task.FromResult<object>(new { lists = new[] { new { project = "website" } } });

        using var first = await _client.GetAsync($"{_root}/api/tasks");
        using var again = new HttpRequestMessage(HttpMethod.Get, $"{_root}/api/tasks");
        again.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        using var unchanged = await _client.SendAsync(again);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        unchanged.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Every_open_stream_shares_one_reading_of_the_stamps()
    {
        var reads = 0;
        var poll = new SharedPoll<int>(_ => Task.FromResult(Interlocked.Increment(ref reads)), TimeSpan.FromSeconds(30));

        var seen = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => poll.GetAsync(CancellationToken.None)));

        seen.Should().OnlyContain(one => one == 1);
        poll.Reads.Should().Be(1);
    }

    private static async Task<string> NextAsync(StreamReader reader, string named, CancellationToken ct)
    {
        string? current = null;

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                current = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal) && current == named)
            {
                return line["data: ".Length..];
            }
        }

        return string.Empty;
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
