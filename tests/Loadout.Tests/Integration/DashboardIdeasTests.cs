using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Results;
using Loadout.Tests.Fakes;
using Loadout.Tui;
using Spectre.Console;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The dashboard's ideas page, end to end but for the agent: what each verb the
/// page sends becomes, how a round is kept busy, and the routes that carry it.
/// </summary>
public sealed class DashboardIdeasTests : IAsyncLifetime, IDisposable
{
    private readonly TemporaryWorkspace _space = new();
    private readonly CancellationTokenSource _stopping = new();
    private DashboardServer _server = null!;
    private HttpClient _client = null!;
    private string _root = string.Empty;

    public Task InitializeAsync()
    {
        _server = new DashboardServer(new NoRuns());

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
    }

    public void Dispose()
    {
        _stopping.Dispose();
        _space.Dispose();
    }

    /// <summary>Every command asked for, and a way to hold one until the test lets it go.</summary>
    private sealed class Typed : ICommandCatalogue
    {
        private readonly TaskCompletionSource _go = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<(string Path, IReadOnlyList<string> Arguments)> Asked { get; } = [];

        public bool Hold { get; init; }

        public int Code { get; init; }

        public IReadOnlyList<CatalogueEntry> Commands => [];

        public void Release() => _go.TrySetResult();

        public async Task<int> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken ct = default)
        {
            lock (Asked)
            {
                Asked.Add((path, arguments));
            }

            if (Hold)
            {
                await _go.Task;
            }

            return Code;
        }
    }

    private DashboardIdeas Ideas(Typed commands) => new(
        commands,
        _space.Ideas,
        _space.Dumps,
        new FakeProjects("website", _space.Scratch("repo")),
        TimeProvider.System,
        new CommandOutput(AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(TextWriter.Null),
            Interactive = InteractionSupport.No,
        }), new GlobalSettings()));

    /// <summary>
    /// Every field of every verb, each given a value, so a field the page
    /// sends and the mapping drops shows up as a missing argument here.
    /// </summary>
    private static IdeaAction Asking(string verb, string? project = "website") => new(
        verb,
        Id: "status-page",
        Project: project,
        Text: "- a bullet from somewhere",
        Question: "Q1",
        Answer: "- only on the LAN",
        Layer: "L1",
        Option: "L1b",
        Pieces: ["L1", "A2"],
        Piece: "plan",
        Request: "- make it smaller",
        To: "homelab",
        NewProject: "Lab watch",
        Only: [1, 3]);

    [Fact]
    public void Each_verb_types_its_command_with_every_field_it_reads()
    {
        var expected = new Dictionary<string, (string Command, string[] Arguments)>
        {
            ["add"] = ("idea add", ["--text=- a bullet from somewhere", "--project", "website"]),
            ["refine"] = ("idea refine", ["status-page", "--project", "website"]),
            ["answer"] = ("idea answer", ["status-page", "Q1", "--answer=- only on the LAN", "--project", "website"]),
            ["choose"] = ("idea choose", ["status-page", "L1", "L1b", "--project", "website"]),
            ["keep"] = ("idea keep", ["status-page", "L1", "A2", "--project", "website"]),
            ["drop"] = ("idea drop", ["status-page", "L1", "A2", "--project", "website"]),
            ["improve"] = ("idea improve", ["status-page", "plan", "--request=- make it smaller", "--project", "website"]),
            ["accept"] = ("idea accept", ["status-page", "--project", "website", "--new-project=Lab watch"]),
            ["remove"] = ("idea remove", ["status-page", "--project", "website"]),
            ["dump"] = ("idea dump add", ["--text=- a bullet from somewhere", "--project", "website"]),
            ["apply"] = ("idea dump apply", ["status-page", "--project", "website", "--only", "1,3", "--to", "homelab"]),
        };

        expected.Keys.Should().BeEquivalentTo(DashboardIdeas.Verbs, "every verb the page can send is checked here");

        foreach (var (verb, (command, arguments)) in expected)
        {
            var (typed, line) = DashboardIdeas.Maps(Asking(verb));

            typed.Should().Be(command, verb);
            line.Should().Equal([.. arguments, "--non-interactive"], verb);
        }
    }

    [Fact]
    public void An_idea_on_no_project_is_named_as_the_workspace_wide_list_and_accept_follows_what_was_chosen()
    {
        DashboardIdeas.Maps(Asking("refine", project: null)).Arguments.Should().Contain("--global");

        DashboardIdeas.Maps(Asking("accept") with { NewProject = null }).Arguments
            .Should().ContainInConsecutiveOrder("--to", "homelab");

        DashboardIdeas.Maps(Asking("apply") with { Only = null, To = null }).Arguments
            .Should().NotContain("--only").And.NotContain("--to");

        DashboardIdeas.Maps(new IdeaAction("reticulate")).Command.Should().BeEmpty();
    }

    [Fact]
    public async Task A_round_is_started_answered_at_once_and_shown_as_going_until_it_ends()
    {
        var commands = new Typed { Hold = true };
        var ideas = Ideas(commands);

        // Bounded, because the command below is held until the test lets it
        // go: a round run in the request rather than beside it would wait for
        // ever, and a test that hangs says nothing about why.
        var started = await ideas.DoAsync(Asking("refine"), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        started.Succeeded.Should().BeTrue("the page is answered before the agent is");
        Busy(await ideas.ReadAsync(CancellationToken.None)).Should().Equal("status-page");

        (await ideas.DoAsync(Asking("refine"), CancellationToken.None)).Failed.Should().BeTrue(
            "two rounds at once would each write over the other's answer");

        commands.Release();

        await WaitUntil(async () => Busy(await ideas.ReadAsync(CancellationToken.None)).Count == 0);

        commands.Asked.Should().ContainSingle().Which.Path.Should().Be("idea refine");
    }

    [Fact]
    public async Task Anything_else_is_awaited_and_its_refusal_reaches_the_page()
    {
        var ideas = Ideas(new Typed { Code = (int)Loadout.Models.ExitCode.PolicyViolation });

        var answered = await ideas.DoAsync(Asking("answer"), CancellationToken.None);

        answered.Failed.Should().BeTrue();
        answered.Error.Should().Contain("credential");
    }

    [Fact]
    public async Task What_the_page_reads_carries_each_idea_in_full_and_the_projects_to_accept_onto()
    {
        await _space.Ideas.CaptureAsync(null, "A status page", "me");

        var read = JsonSerializer.SerializeToElement(await Ideas(new Typed()).ReadAsync(CancellationToken.None));

        var idea = read.GetProperty("ideas")[0];
        idea.GetProperty("title").GetString().Should().Be("A status page");
        idea.GetProperty("detail").GetProperty("stage").GetString().Should().Be("captured");
        read.GetProperty("projects")[0].GetString().Should().Be("website");
        read.GetProperty("dumping").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Everything_the_page_puts_in_the_body_reaches_whatever_types_the_command()
    {
        IdeaAction? asked = null;

        _server.Ideate = (action, _) =>
        {
            asked = action;

            return Task.FromResult(OperationResult.Ok());
        };

        var answer = await _client.PostAsync(
            new Uri(_root + "api/ideas?token=" + _server.Token),
            new StringContent(
                """{"verb":"apply","id":"x","project":"website","text":"t","question":"Q1","answer":"a","layer":"L1","option":"L1b","pieces":["L1","A2"],"piece":"plan","request":"r","to":"homelab","newProject":"Lab watch","only":[1,3]}""",
                System.Text.Encoding.UTF8,
                "application/json"));

        answer.StatusCode.Should().Be(HttpStatusCode.Accepted);

        asked.Should().BeEquivalentTo(new IdeaAction(
            "apply", "x", "website", "t", "Q1", "a", "L1", "L1b", ["L1", "A2"], "plan", "r", "homelab", "Lab watch", [1, 3]));
    }

    [Fact]
    public async Task A_watching_page_is_told_it_cannot_and_the_choices_say_so_before_it_asks()
    {
        var refused = await _client.PostAsync(
            new Uri(_root + "api/ideas?token=" + _server.Token),
            new StringContent("""{"verb":"add","text":"x"}""", System.Text.Encoding.UTF8, "application/json"));

        refused.StatusCode.Should().Be(HttpStatusCode.NotImplemented);

        var choices = JsonDocument.Parse(await _client.GetStringAsync(_root + "api/choices?token=" + _server.Token));
        choices.RootElement.GetProperty("ideas").GetBoolean().Should().BeFalse();

        _server.Ideate = (_, _) => Task.FromResult(OperationResult.Ok());

        choices = JsonDocument.Parse(await _client.GetStringAsync(_root + "api/choices?token=" + _server.Token));
        choices.RootElement.GetProperty("ideas").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Reading_the_ideas_is_a_get_on_the_same_path_and_needs_the_token_to_change_them()
    {
        _server.IdeasFor = _ => Task.FromResult<object>(new { ideas = new[] { new { title = "One" } } });

        var read = JsonDocument.Parse(await _client.GetStringAsync(_root + "api/ideas?token=" + _server.Token));
        read.RootElement.GetProperty("ideas")[0].GetProperty("title").GetString().Should().Be("One");

        _server.Ideate = (_, _) => Task.FromResult(OperationResult.Ok());

        var tokenless = await _client.PostAsync(
            new Uri(_root + "api/ideas"),
            new StringContent("""{"verb":"add","text":"x"}""", System.Text.Encoding.UTF8, "application/json"));

        tokenless.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void The_page_asks_for_ideas_in_the_words_the_server_reads()
    {
        var page = DashboardServer.Page();

        page.Should().Contain("/api/ideas", "the page has to ask where the server listens");

        // Only the ideas page's own script: the schedule form elsewhere sends
        // bodies with a verb as well, to a different route and record.
        var from = page.IndexOf("var ideasSeen", StringComparison.Ordinal);
        var to = page.IndexOf("function drawSettings()", from, StringComparison.Ordinal);

        from.Should().BeGreaterThan(-1);
        to.Should().BeGreaterThan(from);

        var ideas = page[from..to];

        // Every body the ideas page builds is an object literal naming its
        // verb, so collecting those collects every field it can send.
        var bodies = Regex.Matches(ideas, @"\{\s*verb:[^{}]*\}").Select(m => m.Value).ToList();

        bodies.Should().HaveCountGreaterThan(8, "the page has a body for nearly every verb");

        var sent = bodies
            // A key follows the brace or a comma. Anything else with a colon
            // after it is the far side of a conditional inside a value.
            .SelectMany(body => Regex.Matches(body, @"[{,]\s*(\w+)\s*:").Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var read = typeof(IdeaAction).GetProperties().Select(one => one.Name).ToList();

        foreach (var field in sent)
        {
            read.Should().Contain(
                one => string.Equals(one, field, StringComparison.OrdinalIgnoreCase),
                $"the page sends '{field}', so something has to read it");
        }

        var verbs = Regex.Matches(ideas, @"verb:\s*""(\w+)""").Select(m => m.Groups[1].Value).Distinct().ToList();

        verbs.Should().OnlyContain(v => DashboardIdeas.Verbs.Contains(v), "the page sends no verb the server has no command for");
        DashboardIdeas.Verbs.Should().OnlyContain(v => verbs.Contains(v), "every verb the server maps is one the page offers");
    }

    private static List<string> Busy(object read) =>
        [.. JsonSerializer.SerializeToElement(read).GetProperty("busy").EnumerateArray().Select(one => one.GetString()!)];

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);

        while (!await condition())
        {
            DateTime.UtcNow.Should().BeBefore(until, "the round should have ended once it was let go");
            await Task.Delay(20);
        }
    }

    private sealed class NoRuns : IRunJournal
    {
        public IReadOnlyList<string> List(int limit = 20) => [];

        public OperationResult<IReadOnlyList<RunEvent>> Read(string runId) =>
            OperationResult<IReadOnlyList<RunEvent>>.Fail("none");

        public OperationResult<RunSummary> Summarise(string runId) => OperationResult<RunSummary>.Fail("none");

        public string DirectoryOf(string runId) => runId;

        public OperationResult<RunForgotten> Forget(string runId, bool force = false) =>
            OperationResult<RunForgotten>.Fail("none");
    }
}
