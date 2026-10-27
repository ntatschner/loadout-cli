using System.Text.Json.Nodes;
using FluentAssertions;
using Loadout.Agents;
using Loadout.Agents.Claude;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Git;
using Loadout.Core.Sessions;
using Loadout.Models;
using Loadout.Models.Agents;
using Loadout.Models.Platform;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Spectre.Console.Testing;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// A person's conversation filed under the launch that started it, and what
/// resuming makes of that: what carries over, whether it is running, and
/// where it reopens.
/// </summary>
public sealed class SessionLinkTests : IDisposable
{
    private const string LaunchId = "1234567890abcdef1234567890abcdef";
    private const string SessionId = "0f1e2d3c-0000-4000-8000-000000000001";

    private readonly string _root;
    private readonly FakeEnvironmentProvider _environment;
    private readonly LaunchLedger _ledger;

    public SessionLinkTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _environment = Environment();
        _ledger = new LaunchLedger(Paths(_environment), new NoOpFilePermissions(), TimeProvider.System);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private FakeEnvironmentProvider Environment(IDictionary<string, string>? extra = null)
    {
        var variables = new Dictionary<string, string>
        {
            ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
            ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
            ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
            ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
        };

        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
        {
            variables[key] = value;
        }

        return new FakeEnvironmentProvider(Path.Combine(_root, "home"), variables);
    }

    private static LinuxPaths Paths(FakeEnvironmentProvider environment) => new(
        environment,
        new NoOpFilePermissions(),
        new HostPlatform(HostOperatingSystem.Linux, System.Runtime.InteropServices.Architecture.X64, "test", "TEST"));

    private static AgentSession Session(string? slug = "alpha", DateTimeOffset? lastActive = null) =>
        new("claude", SessionId, "Fix the importer", "/repos/alpha", "main",
            lastActive ?? new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), "/transcripts/x.jsonl", slug);

    private static LaunchRecord Launch(string id = LaunchId, string? task = "fix the importer", string? worktree = null, DateTimeOffset? endedAt = null) =>
        new(id, new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), "alpha", "alpha", "claude",
            "implement", task, null, "database", worktree, [], 0, 0, EndedAt: endedAt);

    // The ledger

    [Fact]
    public async Task A_link_is_read_back_and_is_neither_a_launch_nor_a_headless_session()
    {
        await _ledger.RecordLinkAsync(SessionId, "claude", LaunchId);

        var links = (await _ledger.ReadLinksAsync()).Value!;

        links.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { SessionId, Agent = "claude", LaunchId });

        // A link read as a headless session would hide the very conversation
        // it names from every resume list.
        (await _ledger.ReadHeadlessSessionsAsync()).Value!.Should().BeEmpty();
        (await _ledger.ReadAsync(DateTimeOffset.MinValue)).Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task A_link_is_written_as_a_kind_older_builds_do_not_read_as_headless()
    {
        await _ledger.RecordLinkAsync(SessionId, "claude", LaunchId);

        // Every build before links reads each "session" line as a node's and
        // hides it. The kind on disk is the contract with those builds.
        File.ReadAllText(_ledger.Path).Should().Contain("\"Kind\":\"link\"").And.NotContain("\"Kind\":\"session\"");
    }

    // The hook

    private (HookSessionStartCommand Command, TestConsole Console) Hook(string? launch, string input)
    {
        var environment = Environment(launch is null ? null : new Dictionary<string, string> { [SessionLinks.Variable] = launch });
        var console = new TestConsole();

        return (new HookSessionStartCommand(_ledger, environment, console) { Input = new StringReader(input) }, console);
    }

    [Fact]
    public async Task The_hook_files_the_conversation_under_its_launch_and_prints_nothing()
    {
        var (command, console) = Hook(LaunchId, $$"""{"session_id":"{{SessionId}}","source":"startup","transcript_path":"/x"}""");

        var exit = await command.ExecuteAsync(null!, new GlobalSettings { Agent = "claude" }, CancellationToken.None);

        exit.Should().Be(0);

        // Whatever a session-start hook prints becomes the agent's context.
        console.Output.Should().BeEmpty();

        (await _ledger.ReadLinksAsync()).Value!.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { SessionId, Agent = "claude", LaunchId });
    }

    [Theory]
    [InlineData(null, "claude")]
    [InlineData("not-a-launch", "claude")]
    [InlineData(LaunchId, null)]
    [InlineData(LaunchId, "claude; rm -rf")]
    public async Task The_hook_does_nothing_outside_a_launch_or_for_an_agent_it_cannot_name(string? launch, string? agent)
    {
        var (command, console) = Hook(launch, $$"""{"session_id":"{{SessionId}}"}""");

        var exit = await command.ExecuteAsync(null!, new GlobalSettings { Agent = agent }, CancellationToken.None);

        exit.Should().Be(0);
        console.Output.Should().BeEmpty();
        File.Exists(_ledger.Path).Should().BeFalse();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"session_id":"../../etc"}""")]
    [InlineData("""{"source":"startup"}""")]
    public async Task The_hook_ignores_input_without_a_usable_conversation_id(string input)
    {
        var (command, _) = Hook(LaunchId, input);

        (await command.ExecuteAsync(null!, new GlobalSettings { Agent = "claude" }, CancellationToken.None)).Should().Be(0);

        File.Exists(_ledger.Path).Should().BeFalse();
    }

    // Claude's settings

    private async Task<(JsonObject? Settings, IReadOnlyList<string> Warnings)> ClaudeAsync(
        bool links, HeadlessOptions? headless = null, bool settingsFlag = true)
    {
        var runtime = Path.Combine(_root, "runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runtime);

        var adapter = new ClaudeAdapter(
            new StubResolver(Path.Combine(Path.GetTempPath(), "claude")),
            new StubProcessLauncher(settingsFlag
                ? """
                  Usage: claude [options] [command] [prompt]
                    --settings <file-or-json>           Path to a settings JSON file or a JSON string
                  """
                : "Usage: claude [options] [command] [prompt]"),
            []);

        var built = await adapter.BuildInvocationAsync(new AgentLaunchContext(
            new ProjectResolution(new ProjectRegistryEntry { Slug = "demo", Name = "Demo" }, Path.GetTempPath(), null, 0, false),
            Path.GetTempPath(),
            runtime,
            null,
            [],
            Headless: headless,
            LinksSession: links,
            // Attribution kept, so the only reason for a settings file is
            // the one this test is about.
            KeepAttribution: true));

        built.Succeeded.Should().BeTrue(built.Error);

        var arguments = built.Value!.Arguments.ToList();
        var at = arguments.IndexOf("--settings");

        if (at < 0)
        {
            return (null, built.Value.Warnings ?? []);
        }

        var value = arguments[at + 1];

        return (JsonNode.Parse(File.Exists(value) ? File.ReadAllText(value) : value)!.AsObject(), built.Value.Warnings ?? []);
    }

    [Fact]
    public async Task A_linked_launch_has_Claude_report_each_conversation_as_it_starts()
    {
        var (settings, _) = await ClaudeAsync(links: true);

        var command = settings!["hooks"]!["SessionStart"]!.AsArray().Single()!["hooks"]!.AsArray().Single()!["command"]!
            .GetValue<string>();

        command.Should().EndWith("hook session-start --agent claude");
    }

    [Fact]
    public async Task A_launch_that_asks_for_no_link_is_handed_no_settings_for_one()
    {
        (await ClaudeAsync(links: false)).Settings.Should().BeNull();
    }

    [Fact]
    public async Task A_node_is_never_given_the_link_hook()
    {
        // A node's conversation is written down through its own protocol, and
        // its hooks are off from every scope.
        var (settings, _) = await ClaudeAsync(links: true, headless: new HeadlessOptions());

        settings?["hooks"]?["SessionStart"].Should().BeNull();
    }

    [Fact]
    public async Task A_Claude_without_settings_is_launched_unlinked_and_without_a_warning()
    {
        var (settings, warnings) = await ClaudeAsync(links: true, settingsFlag: false);

        settings.Should().BeNull();
        warnings.Should().NotContain(w => w.Contains("--settings", StringComparison.Ordinal));
    }

    // Whether it is running

    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 1, 0, TimeSpan.Zero);

    [Fact]
    public void A_conversation_in_a_hosted_terminal_is_hosted_there()
    {
        Resumption.Running(Session(), Launch(), new HashSet<string>(), "a1b2c3d4", Now)
            .Should().Be(new SessionRunning(RunningKind.Hosted, "a1b2c3d4"));
    }

    [Fact]
    public void A_linked_conversation_whose_launch_is_still_running_is_running_elsewhere()
    {
        Resumption.Running(Session(), Launch(), new HashSet<string> { LaunchId }, null, Now)
            .Kind.Should().Be(RunningKind.Elsewhere);
    }

    [Fact]
    public void A_linked_conversation_whose_launch_ended_is_not_running_however_recently_it_wrote()
    {
        // Written a minute ago, by a launch that has ended: somebody has just
        // closed it, and warning them it is still open would be wrong.
        Resumption.Running(Session(lastActive: Now.AddMinutes(-1)), Launch(), new HashSet<string>(), null, Now)
            .Kind.Should().Be(RunningKind.None);
    }

    [Fact]
    public void An_unlinked_conversation_written_to_a_moment_ago_counts_as_running_elsewhere()
    {
        Resumption.Running(Session(lastActive: Now.AddMinutes(-1)), null, new HashSet<string>(), null, Now)
            .Kind.Should().Be(RunningKind.Elsewhere);

        Resumption.Running(Session(lastActive: Now.AddHours(-1)), null, new HashSet<string>(), null, Now)
            .Kind.Should().Be(RunningKind.None);
    }

    [Fact]
    public void Running_is_spelled_for_the_wire_as_Studio_reads_it()
    {
        SessionRunning.None.Wire.Should().Be("none");
        new SessionRunning(RunningKind.Elsewhere).Wire.Should().Be("elsewhere");
        new SessionRunning(RunningKind.Hosted, "a1b2c3d4").Wire.Should().Be("hosted:a1b2c3d4");
    }

    // What it reopens with

    private static ResumableSession Resumable(LaunchRecord? launch, string? slug = "alpha") =>
        new(Session(slug), launch, SessionRunning.None);

    [Fact]
    public void An_unlinked_conversation_carries_nothing_over()
    {
        var values = Resumption.Values(Resumable(null), new ResumeOverrides());

        values.Task.Should().BeNull();
        values.Mode.Should().BeNull();
        values.Profile.Should().BeNull();
        values.Carried.Should().BeEmpty();
    }

    [Fact]
    public void What_is_overridden_is_not_said_to_have_been_carried()
    {
        var values = Resumption.Values(Resumable(Launch()), new ResumeOverrides(Mode: "review", Profile: "web"));

        values.Mode.Should().Be("review");
        values.Profile.Should().Be("web");
        values.Task.Should().Be("fix the importer");
        values.Carried.Should().Equal("task 'fix the importer'");
    }

    [Fact]
    public void Fresh_carries_nothing_over_and_keeps_what_was_given()
    {
        var values = Resumption.Values(Resumable(Launch(worktree: "feature-x")), new ResumeOverrides(Mode: "review", Fresh: true));

        values.Task.Should().BeNull();
        values.Worktree.Should().BeNull("the main checkout, which is how the sheet asks for it");
        values.Profile.Should().BeNull();
        values.Mode.Should().Be("review");
        values.Carried.Should().BeEmpty();
    }

    private SessionResumption Service(FakeGit git) => new(
        new NoHistory(), _ledger, new NoneRunning(), new FakeProjects("alpha", Path.Combine(_root, "alpha")), git, TimeProvider.System);

    [Fact]
    public async Task A_carried_worktree_that_has_since_gone_reopens_in_the_main_tree_and_says_so()
    {
        var planned = await Service(new FakeGit(Path.Combine(_root, "alpha")))
            .PlanAsync(Resumable(Launch(worktree: "feature-x")), new ResumeOverrides());

        planned.Value!.Worktree.Should().BeNull();
        planned.Value.Notices.Should().ContainSingle().Which.Should().Contain("'feature-x' is no longer there");
        planned.Value.Carried.Should().NotContain(c => c.StartsWith("worktree", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_carried_worktree_that_is_still_there_is_reopened()
    {
        var git = new FakeGit(Path.Combine(_root, "alpha"));
        git.Linked.Add(new GitWorktree(Path.Combine(_root, "wt", "feature-x"), "feature-x", IsPrimary: false));

        var planned = await Service(git).PlanAsync(Resumable(Launch(worktree: "feature-x")), new ResumeOverrides());

        planned.Value!.Worktree.Should().Be("feature-x");
        planned.Value.Notices.Should().BeEmpty();
    }

    [Fact]
    public async Task A_conversation_from_an_unregistered_folder_says_how_to_register_it()
    {
        var planned = await Service(new FakeGit(_root)).PlanAsync(Resumable(null, slug: null), new ResumeOverrides());

        planned.Failed.Should().BeTrue();
        planned.ExitCode.Should().Be(ExitCode.ProjectNotFound);
        planned.Error.Should().Contain("loadout project add \"/repos/alpha\"");
    }

    private sealed class NoHistory : ISessionHistoryService
    {
        public Task<OperationResult<IReadOnlyList<AgentSession>>> ListAsync(SessionQuery query, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<AgentSession>>.Ok([]));
    }

    private sealed class NoneRunning : ISessionRegistry
    {
        public string Path => "running";

        public string InProgressPath => "running/in-progress";

        public Task RegisterAsync(NewSession session, CancellationToken ct = default) => Task.CompletedTask;

        public Task ReleaseAsync(string launchId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<RunningSession>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RunningSession>>([]);
    }
}
