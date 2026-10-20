using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Ideas;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Ideas;
using Loadout.Models.Results;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The ideas on one list, and one idea, over HTTP through the real service
/// against real files, and a change made elsewhere reaching the event stream.
/// </summary>
public sealed class DashboardIdeasApiTests : IAsyncLifetime
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly TemporaryWorkspace _space = new();
    private readonly CancellationTokenSource _stopping = new();
    private DashboardServer _server = null!;
    private HttpClient _client = null!;
    private string _root = string.Empty;
    private int _marked;

    private string Ideas(string list) => $"{_root}{MemoryApi.Root}/{list}/ideas";

    public async Task InitializeAsync()
    {
        await _space.ProjectAsync("website");

        _server = new DashboardServer(new EmptyJournal())
        {
            Tasks = _space.Tasks,
            Ideas = _space.Ideas,
            IdeasBusy = () => Volatile.Read(ref _marked),
        };

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

    private async Task<IdeaPlace> AskedAsync(string? project = null)
    {
        var idea = (await _space.Ideas.CaptureAsync(project, "Status page", "nigel")).Value!;
        var place = new IdeaPlace(project, idea.Id);

        (await _space.Ideas.RecordReplyAsync(place, new IdeaReply
        {
            Contract = IdeaSchema.Version,
            Questions = [new IdeaReply.ReplyQuestion { Question = "Public?" }, new IdeaReply.ReplyQuestion { Question = "Alerts?" }],
        })).Succeeded.Should().BeTrue();

        return place;
    }

    [Fact]
    public async Task Capturing_an_idea_answers_with_it_and_its_revision()
    {
        using var captured = await _client.PostAsync(Ideas("website"), Body("""{"text":"A status page for the home lab"}"""));

        captured.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await captured.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetString().Should().Be("a-status-page-for-the");
        body.GetProperty("idea").GetProperty("stage").GetString().Should().Be("captured");
        body.GetProperty("revision").GetString().Should()
            .Be((await _space.Ideas.ReadWithRevisionAsync(new IdeaPlace("website", "a-status-page-for-the"))).Value!.Revision);
    }

    [Fact]
    public async Task One_idea_is_read_with_its_revision_and_not_sent_again_when_it_has_not_changed()
    {
        var place = await AskedAsync();

        using var first = await _client.GetAsync(Ideas("-") + "/" + place.Id);
        var body = await first.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("idea").GetProperty("questions").GetArrayLength().Should().Be(2);

        using var again = new HttpRequestMessage(HttpMethod.Get, Ideas("-") + "/" + place.Id);
        again.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        using var unchanged = await _client.SendAsync(again);
        unchanged.StatusCode.Should().Be(HttpStatusCode.NotModified);

        using var missing = await _client.GetAsync(Ideas("-") + "/nothing-here");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_list_shows_only_its_own_ideas()
    {
        await AskedAsync();
        await AskedAsync("website");

        using var listed = await _client.GetAsync(Ideas("website"));
        var ideas = (await listed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ideas");

        ideas.GetArrayLength().Should().Be(1);
        ideas[0].GetProperty("idea").GetProperty("project").GetString().Should().Be("website");
        ideas[0].GetProperty("revision").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Both_answers_go_in_one_batch_or_neither_does()
    {
        var place = await AskedAsync();

        using var refused = await _client.PostAsync(Ideas("-") + ":batch", Body(
            $$"""[{"op":"answer","id":"{{place.Id}}","question":"q1","answer":"private"},{"op":"answer","id":"{{place.Id}}","question":"q9","answer":"yes"}]"""));
        var said = await refused.Content.ReadFromJsonAsync<JsonElement>();

        said.GetProperty("applied").GetBoolean().Should().BeFalse();
        said.GetProperty("results")[0].GetProperty("heldBack").GetBoolean().Should().BeTrue();
        (await _space.Ideas.ReadAsync(place)).Value!.Rounds[0].Questions[0].Answer.Should().BeEmpty();

        using var answered = await _client.PostAsync(Ideas("-") + ":batch", Body(
            $$"""[{"op":"answer","id":"{{place.Id}}","question":"q1","answer":"private"},{"op":"answer","id":"{{place.Id}}","question":"q2","answer":"yes"}]"""));

        (await answered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("applied").GetBoolean().Should().BeTrue();
        (await _space.Ideas.ReadAsync(place)).Value!.Rounds[0].Questions.Select(one => one.Answer).Should().Equal("private", "yes");
    }

    [Fact]
    public async Task Forgetting_an_idea_from_an_older_read_is_409()
    {
        var place = await AskedAsync();
        var old = (await _space.Ideas.ReadWithRevisionAsync(place)).Value!.Revision!;
        (await _space.Ideas.AnswerAsync(place, "q1", "changed since")).Succeeded.Should().BeTrue();

        using var request = new HttpRequestMessage(HttpMethod.Delete, Ideas("-") + "/" + place.Id);
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{old}\"");
        using var stale = await _client.SendAsync(request);

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _space.Ideas.ReadAsync(place)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task A_watching_dashboard_reads_ideas_and_changes_none()
    {
        _server.WatchOnly = true;

        using var refused = await _client.PostAsync(Ideas("website"), Body("""{"text":"Not from here"}"""));

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _space.Ideas.ListAsync()).Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task An_answer_written_elsewhere_and_a_round_ending_on_the_page_both_reach_the_event_stream()
    {
        var place = await AskedAsync();

        using var patience = new CancellationTokenSource(Patience);
        using var events = await _client.GetAsync($"{_root}/api/v1/events", HttpCompletionOption.ResponseHeadersRead, patience.Token);
        using var reader = new StreamReader(await events.Content.ReadAsStreamAsync(patience.Token));

        (await NextAsync(reader, "changed", patience.Token)).Should().Contain("ideas");

        var elsewhere = new IdeaService(_space.Workspace, _space.Tasks, _space.Yaml, TimeProvider.System, _space.Locks);
        (await elsewhere.AnswerAsync(place, "q1", "from a terminal")).Succeeded.Should().BeTrue();

        var said = JsonDocument.Parse(await NextAsync(reader, "ideas", patience.Token)).RootElement;
        said.EnumerateArray().Should().ContainSingle();
        said[0].GetProperty("id").GetString().Should().Be(place.Id);
        said[0].GetProperty("revision").GetString().Should().Be((await elsewhere.ReadWithRevisionAsync(place)).Value!.Revision);
        (await NextAsync(reader, "changed", patience.Token)).Should().Contain("ideas");

        // The page's own marker moving, with no file changing at all.
        Interlocked.Increment(ref _marked);

        (await NextAsync(reader, "changed", patience.Token)).Should().Contain("ideas");
    }

    [Fact]
    public async Task One_write_is_said_once_and_not_again_when_the_record_settles()
    {
        var place = await AskedAsync();

        using var patience = new CancellationTokenSource(Patience);
        using var events = await _client.GetAsync($"{_root}/api/v1/events", HttpCompletionOption.ResponseHeadersRead, patience.Token);
        using var reader = new StreamReader(await events.Content.ReadAsStreamAsync(patience.Token));

        await NextAsync(reader, "changed", patience.Token);
        (await _space.Ideas.AnswerAsync(place, "q1", "private")).Succeeded.Should().BeTrue();
        await NextAsync(reader, "ideas", patience.Token);

        (await QuietAsync(reader, "ideas", TimeSpan.FromSeconds(4))).Should().BeTrue("one write is one change");
    }

    /// <summary>Whether nothing named is said for a while, which a cancelled read means here.</summary>
    private static async Task<bool> QuietAsync(StreamReader reader, string named, TimeSpan wait)
    {
        using var waiting = new CancellationTokenSource(wait);

        try
        {
            return (await NextAsync(reader, named, waiting.Token)).Length == 0;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
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
