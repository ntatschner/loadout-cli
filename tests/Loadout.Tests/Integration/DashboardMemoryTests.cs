using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models;
using Loadout.Models.Instructions;
using Loadout.Models.Results;
using Loadout.Tests.Fakes;
using Loadout.Tui;
using Spectre.Console;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Studio's memory page: what a save types, what it does when the topic has
/// changed or would be refused, and the routes that carry it.
/// </summary>
public sealed class DashboardMemoryTests : IAsyncLifetime, IDisposable
{
    private const string Description = "why installers fail with 1603 over a running app";

    private readonly TemporaryWorkspace _space = new();
    private readonly MemoryService _memory = new(TimeProvider.System);
    private readonly CancellationTokenSource _stopping = new();
    private DashboardServer _server = null!;
    private HttpClient _client = null!;
    private string _root = string.Empty;

    public Task InitializeAsync()
    {
        _server = new DashboardServer(new NoRuns());

        var started = _server.Start(0);
        started.Succeeded.Should().BeTrue(started.Error);

        _root = $"http://127.0.0.1:{_server.Port}";
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _client.DefaultRequestHeaders.Add("X-Loadout-Token", _server.Token);

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

    [Fact]
    public async Task A_save_types_memory_replace_with_every_field_the_page_sent()
    {
        var read = await TopicAsync();
        var commands = new Typed(_space.Workspace.LocalPath, _memory);
        var edited = read.Text + "- Close the launcher before an upgrade.\n";

        var saved = await Memory(commands).SaveAsync("website", MemoryScope.User, "restart-manager", edited, read.Version, CancellationToken.None);

        commands.Asked.Should().ContainSingle();
        var (path, arguments) = commands.Asked[0];
        path.Should().Be("memory replace");
        arguments[0].Should().Be("restart-manager");
        arguments.Should().Contain("--project=website").And.Contain("--scope=user").And.Contain($"--expect={read.Version}");
        arguments.Should().ContainSingle(one => one.StartsWith("--file=", StringComparison.Ordinal));
        commands.FileText.Should().Be(edited, "the command is handed exactly what the page sent");
        File.Exists(arguments.Single(one => one.StartsWith("--file=", StringComparison.Ordinal))["--file=".Length..])
            .Should().BeFalse("the copy handed to the command is removed afterwards");

        saved.Succeeded.Should().BeTrue(saved.Error);
        saved.Value!.Conflicted.Should().BeFalse();
        saved.Value.Topic.Text.Should().Contain("Close the launcher before an upgrade.");
    }

    [Fact]
    public async Task A_topic_changed_since_it_was_read_runs_nothing_and_comes_back_as_it_now_is()
    {
        var read = await TopicAsync();
        await _memory.AppendAsync(_space.Workspace.LocalPath, "website", "restart-manager", ["Somebody else's fact."], MemoryScope.User);
        var commands = new Typed(_space.Workspace.LocalPath, _memory);

        var saved = await Memory(commands).SaveAsync("website", MemoryScope.User, "restart-manager", read.Text + "- Mine.\n", read.Version, CancellationToken.None);

        commands.Asked.Should().BeEmpty();
        saved.Value!.Conflicted.Should().BeTrue();
        saved.Value.Topic.Text.Should().Contain("Somebody else's fact.");
    }

    [Fact]
    public async Task A_refusal_reaches_the_page_in_the_service_s_own_words_and_runs_nothing()
    {
        var read = await TopicAsync();
        var commands = new Typed(_space.Workspace.LocalPath, _memory);

        var saved = await Memory(commands).SaveAsync(
            "website", MemoryScope.User, "restart-manager", read.Text + "The token is ghp_" + new string('A', 36) + ".\n", read.Version, CancellationToken.None);

        commands.Asked.Should().BeEmpty();
        saved.Succeeded.Should().BeFalse();
        saved.Error.Should().Contain("credential").And.NotContain("ghp_");
    }

    [Fact]
    public async Task The_routes_list_read_save_and_say_when_it_changed()
    {
        var read = await TopicAsync();
        _server.Memory = Memory(new Typed(_space.Workspace.LocalPath, _memory));
        var topic = $"{_root}/api/v1/projects/website/memory/user/restart-manager";

        var projects = await _client.GetFromJsonAsync<JsonElement>(_root + "/api/v1/projects");
        projects.EnumerateArray().Select(one => one.GetProperty("slug").GetString()).Should().Equal("website");

        var listed = await _client.GetFromJsonAsync<JsonElement>(_root + "/api/v1/projects/website/memory");
        listed.GetProperty("topics").EnumerateArray().Should().ContainSingle(one =>
            one.GetProperty("name").GetString() == "restart-manager" && one.GetProperty("scope").GetString() == "user");

        var shown = await _client.GetFromJsonAsync<JsonElement>(topic);
        shown.GetProperty("version").GetString().Should().Be(read.Version);

        using var saved = await _client.PutAsJsonAsync(topic, new { text = read.Text + "- Saved from the page.\n", version = read.Version });
        saved.StatusCode.Should().Be(HttpStatusCode.OK);

        using var stale = await _client.PutAsJsonAsync(topic, new { text = read.Text + "- From a stale window.\n", version = read.Version });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("text").GetString()
            .Should().Contain("Saved from the page.").And.NotContain("From a stale window.");
    }

    [Theory]
    [InlineData("PUT", "/api/v1/projects/website/memory/machine/restart-manager", HttpStatusCode.Forbidden)]
    [InlineData("GET", "/api/v1/projects/website/memory/elsewhere/restart-manager", HttpStatusCode.NotFound)]
    [InlineData("GET", "/api/v1/projects/website/memory/1/restart-manager", HttpStatusCode.NotFound)]
    [InlineData("DELETE", "/api/v1/projects/website/memory/user/restart-manager", HttpStatusCode.MethodNotAllowed)]
    [InlineData("GET", "/api/v1/projectsx", HttpStatusCode.NotFound)]
    [InlineData("GET", "/api/v1/nothing", HttpStatusCode.NotFound)]
    public async Task What_the_routes_do_not_offer_they_refuse(string method, string path, HttpStatusCode expected)
    {
        await TopicAsync();
        _server.Memory = Memory(new Typed(_space.Workspace.LocalPath, _memory));

        using var request = new HttpRequestMessage(new HttpMethod(method), _root + path)
        {
            Content = method == "PUT" ? new StringContent("""{"text":"x","version":"y"}""", Encoding.UTF8, "application/json") : null,
        };
        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task Memory_is_not_reachable_from_another_machine_even_with_the_token()
    {
        await TopicAsync();
        _server.Memory = Memory(new Typed(_space.Workspace.LocalPath, _memory));
        _server.From = _ => false;

        using var response = await _client.GetAsync(_root + "/api/v1/projects/website/memory");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<MemoryTopicText> TopicAsync()
    {
        var written = await _memory.WriteAsync(
            _space.Workspace.LocalPath, "website", "restart-manager", Description, MemoryKind.Lesson,
            ["Restart Manager is disabled by policy on this machine."], scope: MemoryScope.User);
        written.Succeeded.Should().BeTrue(written.Error);

        return (await _memory.ReadTextAsync(_space.Workspace.LocalPath, "website", "restart-manager", MemoryScope.User)).Value!;
    }

    private DashboardMemory Memory(Typed commands) => new(
        commands,
        _memory,
        new NoAgentStore(),
        new FakeProjects("website", _space.Scratch("repo")),
        _space.Workspace,
        TimeProvider.System,
        new CommandOutput(AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(TextWriter.Null),
            Interactive = InteractionSupport.No,
        }), new GlobalSettings()));

    /// <summary>
    /// Every command asked for, with the file it was handed read while it
    /// still existed, and a replace carried out as the real command would.
    /// </summary>
    private sealed class Typed(string workspace, MemoryService memory) : ICommandCatalogue
    {
        public List<(string Path, IReadOnlyList<string> Arguments)> Asked { get; } = [];

        public string? FileText { get; private set; }

        public IReadOnlyList<CatalogueEntry> Commands => [];

        public async Task<int> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken ct = default)
        {
            Asked.Add((path, arguments));

            string Value(string option) => arguments.Single(one => one.StartsWith(option + "=", StringComparison.Ordinal))[(option.Length + 1)..];

            FileText = await File.ReadAllTextAsync(Value("--file"), ct);

            var replaced = await memory.ReplaceTextAsync(
                workspace, Value("--project"), arguments[0], Enum.Parse<MemoryScope>(Value("--scope"), ignoreCase: true),
                FileText, Value("--expect"), ct: ct);

            return replaced.Failed ? (int)replaced.ExitCode : replaced.Value!.Conflicted ? (int)ExitCode.ChangedSinceRead : 0;
        }
    }

    private sealed class NoAgentStore : IMemoryImporter
    {
        public string? Discover(string repositoryPath) => null;

        public Task<OperationResult<MemoryImport>> ImportAsync(
            string workspaceRoot, string slug, string sourceDirectory, bool apply, CancellationToken ct = default) =>
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
