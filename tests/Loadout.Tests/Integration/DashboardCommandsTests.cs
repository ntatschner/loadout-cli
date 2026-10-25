using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Studio's commands and jobs, against the real listener with a scripted
/// child: what is listed, what runs, what it said, and what is refused.
/// </summary>
public sealed class DashboardCommandsTests : IAsyncLifetime
{
    private const string GitHubTokenShape = "ghp_abcdefghijklmnopqrstuvwxyz0123";

    private readonly CancellationTokenSource _stopping = new();
    private readonly Scripted _commands = new();
    private readonly List<string> _ran = [];
    private DashboardServer _server = null!;
    private HttpClient _client = null!;
    private string _root = string.Empty;

    public Task InitializeAsync()
    {
        _server = new DashboardServer(new NoRuns())
        {
            Jobs = new CommandJobs(_commands, TimeProvider.System, _ran.Add),
        };

        var started = _server.Start(0);
        started.Succeeded.Should().BeTrue(started.Error);

        _root = $"http://127.0.0.1:{_server.Port}";
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

    [Fact]
    public async Task The_catalogue_is_served_with_each_command_s_reach()
    {
        var (status, text) = await SendAsync(HttpMethod.Get, "/api/v1/commands");

        status.Should().Be(HttpStatusCode.OK);

        var listed = JsonDocument.Parse(text).RootElement;

        listed.GetArrayLength().Should().Be(_commands.Catalogue.Count);
        listed[0].GetProperty("reach").GetProperty("effect").GetString().Should().Be("reads");
    }

    [Fact]
    public async Task A_job_runs_the_command_never_asking_anything_and_keeps_what_it_said_with_secrets_taken_out()
    {
        _commands.Next = new StubProcessLauncher.StubPipedProcess(
            $"{{\"topics\":1}}\ntoken {GitHubTokenShape}\n", exitCode: 0, error: "a warning");

        var (status, text) = await SendAsync(HttpMethod.Post, "/api/v1/jobs", "{\"path\":\"memory find\",\"words\":[\"build\",\"--project\",\"starstats\"]}");

        status.Should().Be(HttpStatusCode.Accepted, text);

        var id = JsonDocument.Parse(text).RootElement.GetProperty("id").GetString()!;

        _commands.Started.Should().ContainSingle().Which.Should().Equal(
            "memory", "find", "build", "--project", "starstats", "--non-interactive");
        _ran.Should().Equal(["memory find"], "the terminal serving the page hears which command ran, and nothing after it");

        var job = await EndedAsync(id);

        job.GetProperty("state").GetString().Should().Be("done");
        job.GetProperty("exitCode").GetInt32().Should().Be(0);
        job.GetProperty("line").GetString().Should().Be("loadout memory find build --project starstats --non-interactive");
        job.GetProperty("error").GetString().Should().Contain("a warning");

        var output = job.GetProperty("output").GetString()!;

        output.Should().Contain("{\"topics\":1}");
        output.Should().NotContain(GitHubTokenShape, "a secret a command printed is never shown back");

        var (_, list) = await SendAsync(HttpMethod.Get, "/api/v1/jobs");
        var listed = JsonDocument.Parse(list).RootElement;

        listed.GetArrayLength().Should().Be(1);
        listed[0].GetProperty("output").ValueKind.Should().Be(JsonValueKind.Null, "the list carries no output, only one job read on its own");
    }

    [Fact]
    public async Task A_failed_command_is_a_failed_job_with_its_exit_code()
    {
        _commands.Next = new StubProcessLauncher.StubPipedProcess(string.Empty, exitCode: 4, error: "no such topic");

        var (_, text) = await SendAsync(HttpMethod.Post, "/api/v1/jobs", "{\"path\":\"memory find\",\"words\":[\"build\"]}");
        var job = await EndedAsync(JsonDocument.Parse(text).RootElement.GetProperty("id").GetString()!);

        job.GetProperty("state").GetString().Should().Be("failed");
        job.GetProperty("exitCode").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task Cancelling_a_job_stops_its_process()
    {
        var hold = new TaskCompletionSource();
        var child = new StubProcessLauncher.StubPipedProcess("late\n", exitCode: 0, beforeFirstLine: () => hold.Task);
        _commands.Next = child;

        var (_, text) = await SendAsync(HttpMethod.Post, "/api/v1/jobs", "{\"path\":\"memory find\",\"words\":[\"build\"]}");
        var id = JsonDocument.Parse(text).RootElement.GetProperty("id").GetString()!;

        var (cancelled, _) = await SendAsync(HttpMethod.Delete, "/api/v1/jobs/" + id);

        cancelled.Should().Be(HttpStatusCode.NoContent);
        child.Killed.Should().BeTrue();

        hold.SetResult();

        (await EndedAsync(id)).GetProperty("state").GetString().Should().Be("cancelled");
    }

    [Theory]
    [InlineData("{\"path\":\"secret set\",\"words\":[\"anthropic/default\"]}", HttpStatusCode.Forbidden, "masked prompt")]
    [InlineData("{\"path\":\"memory find\",\"words\":[\"build\",\"--yes\"]}", HttpStatusCode.Forbidden, "no option")]
    [InlineData("{\"path\":\"no such thing\",\"words\":[]}", HttpStatusCode.BadRequest, "no command")]
    [InlineData("{\"words\":[]}", HttpStatusCode.BadRequest, "'path'")]
    public async Task What_the_form_could_not_have_built_is_refused_and_nothing_starts(string body, HttpStatusCode expected, string says)
    {
        var (status, text) = await SendAsync(HttpMethod.Post, "/api/v1/jobs", body);

        status.Should().Be(expected);
        text.Should().Contain(says);
        _commands.Started.Should().BeEmpty();
    }

    [Fact]
    public async Task A_watch_only_dashboard_runs_what_reads_and_refuses_what_changes()
    {
        _server.WatchOnly = true;
        _commands.Next = new StubProcessLauncher.StubPipedProcess("{}", exitCode: 0);

        var (reads, _) = await SendAsync(HttpMethod.Post, "/api/v1/jobs", "{\"path\":\"memory find\",\"words\":[\"build\"]}");
        var (changes, said) = await SendAsync(HttpMethod.Post, "/api/v1/jobs", "{\"path\":\"memory delete\",\"words\":[\"old\"]}");

        reads.Should().Be(HttpStatusCode.Accepted);
        changes.Should().Be(HttpStatusCode.Forbidden);
        said.Should().Contain("only watching");
        _commands.Started.Should().ContainSingle();
    }

    [Fact]
    public async Task A_page_reads_through_a_command_that_reads_and_nothing_is_kept_or_announced()
    {
        _commands.Next = new StubProcessLauncher.StubPipedProcess("{\"projects\":[]}\n", exitCode: 0);

        var (status, text) = await SendAsync(HttpMethod.Post, "/api/v1/read", "{\"path\":\"memory find\",\"words\":[\"build\"]}");

        status.Should().Be(HttpStatusCode.OK, text);

        var read = JsonDocument.Parse(text).RootElement;

        read.GetProperty("state").GetString().Should().Be("done", "the answer waits for the command to end");
        read.GetProperty("output").GetString().Should().Contain("{\"projects\":[]}");

        var (_, list) = await SendAsync(HttpMethod.Get, "/api/v1/jobs");

        JsonDocument.Parse(list).RootElement.GetArrayLength().Should().Be(0, "a page's own reads would bury what somebody ran");
        _ran.Should().BeEmpty();
        _server.Jobs!.Stamp.Should().Be(0, "nothing changed that the event stream should announce");
    }

    [Fact]
    public async Task A_page_cannot_read_with_a_command_that_changes_something_even_when_it_could_run_it()
    {
        var (status, text) = await SendAsync(HttpMethod.Post, "/api/v1/read", "{\"path\":\"memory delete\",\"words\":[\"old\"]}");

        status.Should().Be(HttpStatusCode.Forbidden);
        text.Should().Contain("changes something");
        _commands.Started.Should().BeEmpty();
    }

    [Fact]
    public async Task Settings_are_read_by_group_named_in_words_the_redactor_leaves_and_with_their_values_redacted()
    {
        var config = new Loadout.Models.Configuration.LauncherConfig();
        config.Workspace.Remote = $"https://{GitHubTokenShape}@github.com/example/workspace.git";
        config.Workspace.Branch = "trunk";
        _server.Configuration = new StubConfig(config);

        var (status, text) = await SendAsync(HttpMethod.Get, "/api/v1/config");

        status.Should().Be(HttpStatusCode.OK, text);

        var groups = JsonDocument.Parse(text).RootElement;
        var workspace = groups.EnumerateArray().Single(group => group.GetProperty("group").GetString() == "Workspace");
        var settings = workspace.GetProperty("settings").EnumerateArray().ToList();

        settings.Select(one => one.GetProperty("name").GetString()).Should().Contain(["workspace-remote", "workspace-branch"],
            "a setting's name must survive the page's redaction, which 'config list --json' naming it 'key' did not");
        settings.Single(one => one.GetProperty("name").GetString() == "workspace-branch").GetProperty("value").GetString().Should().Be("trunk");
        text.Should().NotContain(GitHubTokenShape, "a credential in a setting is never shown back");
    }

    [Fact]
    public async Task An_empty_word_is_passed_on_so_a_setting_can_be_cleared()
    {
        _commands.Next = new StubProcessLauncher.StubPipedProcess(string.Empty, exitCode: 0);

        var (status, text) = await SendAsync(HttpMethod.Post, "/api/v1/jobs", "{\"path\":\"memory find\",\"words\":[\"\"]}");

        status.Should().Be(HttpStatusCode.Accepted, text);
        _commands.Started.Should().ContainSingle().Which.Should().Equal("memory", "find", "", "--non-interactive");
    }

    [Fact]
    public async Task A_page_from_elsewhere_cannot_start_a_job()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _root + "/api/v1/jobs")
        {
            Content = new StringContent("{\"path\":\"memory find\",\"words\":[\"build\"]}", Encoding.UTF8, "application/json"),
        };

        request.Headers.Add("X-Loadout-Token", _server.Token);
        request.Headers.Add("Origin", "http://elsewhere.example");

        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _commands.Started.Should().BeEmpty();
    }

    [Fact]
    public async Task Nothing_is_listed_or_run_without_the_token()
    {
        using var response = await _client.GetAsync(_root + "/api/v1/commands");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<JsonElement> EndedAsync(string id)
    {
        for (var tries = 0; tries < 200; tries++)
        {
            var (_, text) = await SendAsync(HttpMethod.Get, "/api/v1/jobs/" + id);
            var job = JsonDocument.Parse(text).RootElement.Clone();

            if (job.GetProperty("state").GetString() != "running")
            {
                return job;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("The job never ended.");
    }

    private static string Version() =>
        (typeof(DashboardServer).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "unknown").Split('+')[0];

    private static string Typed(string version) =>
        JsonSerializer.Serialize(new { version, words = new[] { "memory", "find", "build" }, directory = Path.GetTempPath(), width = 120, ansi = false, colours = 0 });

    [Fact]
    public async Task A_command_from_a_terminal_comes_back_as_it_was_written_and_how_it_ended()
    {
        _commands.Next = new StubProcessLauncher.StubPipedProcess("found one\nand another\n", exitCode: 3, error: "a warning");

        var (status, text) = await SendAsync(HttpMethod.Post, RunApi.Root, Typed(Version()));

        status.Should().Be(HttpStatusCode.OK, text);

        var frames = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement).ToList();

        string.Concat(frames.Where(frame => frame.TryGetProperty("out", out _)).Select(frame => frame.GetProperty("out").GetString()))
            .Should().Be("found one\nand another\n");
        frames.Where(frame => frame.TryGetProperty("err", out _)).Select(frame => frame.GetProperty("err").GetString())
            .Should().ContainSingle().Which.Should().Be("a warning\n");
        frames[^1].GetProperty("exit").GetInt32().Should().Be(3);

        _commands.Forwarded.Should().ContainSingle().Which.Width.Should().Be(120);
        _ran.Should().BeEmpty("the page's log line is for the page's jobs");
    }

    [Fact]
    public async Task A_launcher_of_another_version_is_told_to_run_it_itself()
    {
        var (status, _) = await SendAsync(HttpMethod.Post, RunApi.Root, Typed("0.0.1"));

        status.Should().Be((HttpStatusCode)RunApi.OtherVersion);
        _commands.Forwarded.Should().BeEmpty();
    }

    [Fact]
    public async Task Running_a_command_for_the_command_line_needs_the_token_itself_in_a_header()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_root}{RunApi.Root}?token={_server.Token}")
        {
            Content = new StringContent(Typed(Version()), Encoding.UTF8, "application/json"),
        };

        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a page's link carries the token in the address, and a page must not reach this");
        _commands.Forwarded.Should().BeEmpty();
    }

    private async Task<(HttpStatusCode Status, string Text)> SendAsync(HttpMethod method, string path, string? body = null)
    {
        using var request = new HttpRequestMessage(method, _root + path);
        request.Headers.Add("X-Loadout-Token", _server.Token);

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using var response = await _client.SendAsync(request);

        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A catalogue of three commands, and a child that is whatever the test says next.</summary>
    private sealed class Scripted : IStudioCommands
    {
        private static readonly StudioOption Project = new("--project", null, "value", "SLUG", "", false, false, []);
        private static readonly StudioOption DryRun = new("--dry-run", null, "flag", null, "", false, true, []);

        public IReadOnlyList<StudioCommand> Catalogue { get; } =
        [
            new("memory find", "Find", "Memory", "", true, new StudioReach("commands", StudioEffect.Reads),
                [new StudioArgument("QUERY", 0, true, false, "")], [Project, DryRun]),
            new("memory delete", "Delete", "Memory", "", true, new StudioReach("commands", StudioEffect.Changes),
                [new StudioArgument("TOPIC", 0, true, false, "")], [Project, DryRun]),
            new("secret set", "Set", "Secrets", "", true,
                new StudioReach("commands", StudioEffect.TerminalOnly, "asks for the value at a masked prompt"),
                [new StudioArgument("REFERENCE", 0, true, false, "")], []),
        ];

        public IPipedProcess Next { get; set; } = new StubProcessLauncher.StubPipedProcess(string.Empty, exitCode: 0);

        public List<IReadOnlyList<string>> Started { get; } = [];

        public Task<OperationResult<IPipedProcess>> StartAsync(IReadOnlyList<string> words, CancellationToken ct)
        {
            Started.Add(words);

            return Task.FromResult(OperationResult<IPipedProcess>.Ok(Next));
        }

        public List<ForwardedTerminal> Forwarded { get; } = [];

        public IPipedProcess? Forward(IReadOnlyList<string> words, ForwardedTerminal terminal)
        {
            Forwarded.Add(terminal);

            return Next;
        }
    }

    private sealed class StubConfig(Loadout.Models.Configuration.LauncherConfig config) : Loadout.Core.Configuration.IConfigurationService
    {
        public Task<OperationResult<Loadout.Models.Configuration.LauncherConfig>> LoadConfigAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<Loadout.Models.Configuration.LauncherConfig>.Ok(config));

        public Task<OperationResult> SaveConfigAsync(Loadout.Models.Configuration.LauncherConfig changed, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<Loadout.Models.Configuration.MachineConfig>> LoadMachineAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<Loadout.Models.Configuration.MachineConfig>.Ok(new Loadout.Models.Configuration.MachineConfig()));

        public Task<OperationResult> SaveMachineAsync(Loadout.Models.Configuration.MachineConfig machine, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<Loadout.Models.Configuration.LauncherConfig>> UpdateConfigAsync(
            Action<Loadout.Models.Configuration.LauncherConfig> change, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<Loadout.Models.Configuration.MachineConfig>> UpdateMachineAsync(
            Action<Loadout.Models.Configuration.MachineConfig> change, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoRuns : IRunJournal
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
