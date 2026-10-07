using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Tasks;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Tasks;
using Loadout.Tests.Fakes;
using Loadout.Tui;
using Spectre.Console;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The dashboard's tasks page: what each verb the page sends becomes, what a
/// task's run button starts, and the routes that carry them.
/// </summary>
public sealed class DashboardTasksTests : IAsyncLifetime, IDisposable
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

    /// <summary>Every command asked for, answered with one exit code.</summary>
    private sealed class Typed : ICommandCatalogue
    {
        public List<(string Path, IReadOnlyList<string> Arguments)> Asked { get; } = [];

        public int Code { get; init; }

        public IReadOnlyList<CatalogueEntry> Commands => [];

        public Task<int> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken ct = default)
        {
            lock (Asked)
            {
                Asked.Add((path, arguments));
            }

            return Task.FromResult(Code);
        }
    }

    private DashboardTasks Tasks(Typed commands) => new(
        commands,
        _space.Tasks,
        new FakeProjects("website", _space.Scratch("repo")),
        new ConfigurationService(
            _space.Paths, new FakeEnvironmentProvider(_space.Scratch("home")), _space.Yaml),
        TimeProvider.System,
        new CommandOutput(AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(TextWriter.Null),
            Interactive = InteractionSupport.No,
        }), new GlobalSettings()));

    /// <summary>
    /// Every field given a value, so a field the page sends and the mapping
    /// drops shows up as a missing argument here.
    /// </summary>
    private static TaskAction Asking(string verb, string? project = "website") => new(
        verb,
        Id: "fix-login",
        Project: project,
        Title: "- Fix the login",
        Note: "- after the cookie change",
        State: "blocked");

    [Fact]
    public void Each_verb_types_its_command_with_every_field_it_reads()
    {
        var expected = new Dictionary<string, (string Command, string[] Arguments)>
        {
            ["add"] = ("task declare",
                ["fix-login", "open", "--title=- Fix the login", "--note=- after the cookie change", "--project", "website"]),
            ["state"] = ("task declare",
                ["fix-login", "blocked", "--note=- after the cookie change", "--project", "website"]),
            ["edit"] = ("task declare",
                ["fix-login", "blocked", "--title=- Fix the login", "--note=- after the cookie change", "--project", "website"]),
            ["remove"] = ("task remove", ["fix-login", "--project", "website"]),
        };

        expected.Keys.Should().BeEquivalentTo(DashboardTasks.Verbs, "every verb the page can send is checked here");

        foreach (var (verb, (command, arguments)) in expected)
        {
            var typed = DashboardTasks.Maps(Asking(verb));

            typed.Command.Should().Be(command, verb);
            typed.Arguments.Should().Equal([.. arguments, "--non-interactive"], verb);
        }
    }

    [Fact]
    public void A_task_on_the_workspace_wide_list_is_named_as_that_list()
    {
        // The command never falls back from a project's list to this one, so
        // the page names it outright.
        DashboardTasks.Maps(Asking("remove", project: null)).Arguments
            .Should().Equal("fix-login", "--global", "--non-interactive");
    }

    [Fact]
    public void Moving_a_task_does_not_wipe_its_note_and_emptying_the_note_box_clears_it()
    {
        // A state change with no note leaves the note alone. An edit always
        // sends one, so a note somebody emptied on the page goes.
        DashboardTasks.Maps(Asking("state") with { State = "done", Note = null }).Arguments
            .Should().NotContain(one => one.StartsWith("--note", StringComparison.Ordinal));

        DashboardTasks.Maps(Asking("edit") with { Note = "  " }).Arguments.Should().Contain("--note=");
    }

    [Fact]
    public void A_verb_nothing_answers_maps_to_nothing()
    {
        DashboardTasks.Maps(Asking("archive")).Command.Should().BeEmpty();
    }

    [Fact]
    public void A_task_s_run_names_the_task_its_project_and_its_team_and_asks_for_nothing_else()
    {
        var task = new TaskItem { Id = "fix-login", Title = "Fix the login", Note = "After the cookie change." };

        var start = DashboardTasks.Running(task, "website", "bug-hunt");

        start.Should().BeEquivalentTo(new StartRequest(
            "bug-hunt", "Fix the login\n\nAfter the cookie change.", "website", Task: "fix-login"));

        var typed = DashboardActions.Starting(start);

        typed.Should().ContainInConsecutiveOrder("--task", "fix-login");
        typed.Should().ContainInConsecutiveOrder("--project", "website");
    }

    [Fact]
    public async Task Adding_a_task_names_it_from_its_title_past_any_id_already_taken()
    {
        await _space.ProjectAsync("website");
        (await _space.Tasks.DeclareAsync("website", "fix-the-login", TaskState.Open, "me", "Fix the login"))
            .Succeeded.Should().BeTrue();

        var commands = new Typed();

        (await Tasks(commands).DoAsync(new TaskAction("add", Project: "website", Title: "Fix the login"), default))
            .Succeeded.Should().BeTrue();

        commands.Asked.Should().ContainSingle()
            .Which.Arguments.Should().StartWith(["fix-the-login-2", "open"]);
    }

    [Fact]
    public async Task A_task_with_no_title_is_refused_before_anything_is_typed()
    {
        var commands = new Typed();

        var done = await Tasks(commands).DoAsync(new TaskAction("add", Project: "website", Title: "  "), default);

        done.Failed.Should().BeTrue();
        commands.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_run_on_a_task_that_is_not_there_is_refused_in_words_before_anything_is_typed()
    {
        // The page cannot see the command's words, only its exit code, so the
        // rule is checked here first and its own sentence comes back.
        await _space.ProjectAsync("website");

        var commands = new Typed();

        var done = await Tasks(commands).DoAsync(new TaskAction("run", Id: "nothing-here", Project: "website"), default);

        done.Error.Should().Contain("There is no task 'nothing-here'");
        commands.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_run_on_the_workspace_wide_list_is_refused_because_it_has_no_repository()
    {
        var commands = new Typed();

        var done = await Tasks(commands).DoAsync(new TaskAction("run", Id: "fix-login"), default);

        done.Error.Should().Contain("workspace-wide");
        commands.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_run_on_a_task_types_team_run_with_the_task_and_the_built_in_team()
    {
        await _space.ProjectAsync("website");
        (await _space.Tasks.DeclareAsync("website", "fix-login", TaskState.Open, "me", "Fix the login"))
            .Succeeded.Should().BeTrue();

        // Ending at once with a refusal, so the start does not wait out the
        // seconds it gives a real run to settle.
        var commands = new Typed { Code = (int)ExitCode.ProjectNotFound };

        await Tasks(commands).DoAsync(new TaskAction("run", Id: "fix-login", Project: "website"), default);

        var (path, arguments) = commands.Asked.Should().ContainSingle().Which;

        path.Should().Be("team run");
        arguments.Should().StartWith([DashboardTasks.DefaultTeam, "Fix the login"]);
        arguments.Should().ContainInConsecutiveOrder("--task", "fix-login");
        arguments.Should().ContainInConsecutiveOrder("--project", "website");
    }

    [Fact]
    public async Task What_the_page_reads_is_tasks_only_with_the_team_a_run_would_use()
    {
        await _space.ProjectAsync("website");
        (await _space.Tasks.DeclareAsync("website", "fix-login", TaskState.Blocked, "me", "Fix the login", "waiting on DNS"))
            .Succeeded.Should().BeTrue();
        (await _space.Tasks.DeclareAsync("website", "an-idea", TaskState.Open, "me", "An idea", kind: TaskKind.Idea))
            .Succeeded.Should().BeTrue();

        var read = JsonSerializer.SerializeToElement(await Tasks(new Typed()).ReadAsync(default));

        read.GetProperty("team").GetString().Should().Be(DashboardTasks.DefaultTeam);

        var website = read.GetProperty("lists").EnumerateArray()
            .Single(list => list.GetProperty("project").GetString() == "website");

        var task = website.GetProperty("tasks").EnumerateArray().Should().ContainSingle(
            "ideas have their own page").Which;

        task.GetProperty("id").GetString().Should().Be("fix-login");
        task.GetProperty("state").GetString().Should().Be("blocked");
        task.GetProperty("note").GetString().Should().Be("waiting on DNS");

        read.GetProperty("lists").EnumerateArray()
            .Should().Contain(list => list.GetProperty("project").ValueKind == JsonValueKind.Null,
                "the workspace-wide list is read as well");
    }

    [Fact]
    public async Task Everything_the_page_puts_in_the_body_reaches_whatever_types_the_command()
    {
        TaskAction? asked = null;

        _server.Tend = (action, _) =>
        {
            asked = action;

            return Task.FromResult(OperationResult.Ok());
        };

        var answer = await PostAsync(
            """{"verb":"edit","id":"fix-login","project":"website","title":"Fix it","note":"n","state":"doing"}""");

        answer.StatusCode.Should().Be(HttpStatusCode.Accepted);

        asked.Should().BeEquivalentTo(new TaskAction("edit", "fix-login", "website", "Fix it", "n", "doing"));
    }

    [Fact]
    public async Task A_watching_page_is_told_it_cannot_and_the_choices_say_so_before_it_asks()
    {
        (await PostAsync("""{"verb":"remove","id":"x"}""")).StatusCode.Should().Be(HttpStatusCode.NotImplemented);

        var choices = JsonDocument.Parse(await _client.GetStringAsync(_root + "api/choices?token=" + _server.Token));
        choices.RootElement.GetProperty("tasks").GetBoolean().Should().BeFalse();

        _server.Tend = (_, _) => Task.FromResult(OperationResult.Ok());

        choices = JsonDocument.Parse(await _client.GetStringAsync(_root + "api/choices?token=" + _server.Token));
        choices.RootElement.GetProperty("tasks").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Reading_the_tasks_is_a_get_on_the_same_path_and_changing_them_needs_the_token()
    {
        _server.TasksFor = _ => Task.FromResult<object>(new { lists = new[] { new { project = "website" } } });

        var read = JsonDocument.Parse(await _client.GetStringAsync(_root + "api/tasks?token=" + _server.Token));
        read.RootElement.GetProperty("lists")[0].GetProperty("project").GetString().Should().Be("website");

        _server.Tend = (_, _) => Task.FromResult(OperationResult.Ok());

        (await PostAsync("""{"verb":"remove","id":"x"}""", withToken: false))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_refusal_reaches_the_page_in_its_own_words()
    {
        _server.Tend = (_, _) => Task.FromResult(OperationResult.Fail("It is an idea.", ExitCode.InvalidArguments));

        var answer = await PostAsync("""{"verb":"run","id":"x","project":"website"}""");

        answer.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await answer.Content.ReadAsStringAsync()).Should().Contain("It is an idea.");
    }

    [Fact]
    public void The_page_asks_for_tasks_in_the_words_the_server_reads()
    {
        var page = DashboardServer.Page();

        page.Should().Contain("/api/tasks");
        page.Should().Contain("id=\"view-tasks\"").And.Contain("<section id=\"tasks\"");

        var from = page.IndexOf("var tasksDrawn", StringComparison.Ordinal);
        var to = page.IndexOf("One group of settings, headed.", from, StringComparison.Ordinal);

        from.Should().BeGreaterThan(-1);
        to.Should().BeGreaterThan(from);

        var tasks = page[from..to];

        // Every body names its verb, or is the id and list a card's verbs
        // are merged into, so their keys are every field the page can send.
        var sent = Regex.Matches(tasks, @"\{\s*(?:verb|id)\s*:[^{}]*\}")
            .SelectMany(body => Regex.Matches(body.Value, @"[{,]\s*(\w+)\s*:").Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var read = typeof(TaskAction).GetProperties().Select(one => one.Name).ToList();

        foreach (var field in sent)
        {
            read.Should().Contain(
                one => string.Equals(one, field, StringComparison.OrdinalIgnoreCase),
                $"the page sends '{field}', so something has to read it");
        }

        var verbs = Regex.Matches(tasks, @"verb:\s*""(\w+)""").Select(m => m.Groups[1].Value).Distinct().ToList();

        verbs.Should().OnlyContain(v => DashboardTasks.Verbs.Contains(v) || v == "run",
            "the page sends no verb the server has no command for");
        DashboardTasks.Verbs.Should().OnlyContain(v => verbs.Contains(v), "every verb the server maps is one the page offers");
        verbs.Should().Contain("run");
    }

    private Task<HttpResponseMessage> PostAsync(string body, bool withToken = true) =>
        _client.PostAsync(
            new Uri(_root + "api/tasks" + (withToken ? "?token=" + _server.Token : string.Empty)),
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

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
