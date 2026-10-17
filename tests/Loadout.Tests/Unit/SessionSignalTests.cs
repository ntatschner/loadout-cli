using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Loadout.Agents;
using Loadout.Agents.Claude;
using Loadout.Cli.Commands;
using Loadout.Core.Sessions.Hosting;
using Loadout.Models.Agents;
using Loadout.Models.Platform;
using Loadout.Models.Projects;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Spectre.Console.Testing;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// How an agent in a Studio session says it is waiting: the hooks the launch
/// installs, the command they run, and the file the daemon reads.
/// </summary>
/// <remarks>
/// Waiting and approval come only from the agent's own hooks, never from the
/// screen, and a hook says that a session is waiting, not what the choices
/// are. So what is tested here is the plumbing; the words the page puts on
/// it are Studio's own tests.
/// </remarks>
public sealed class SessionSignalTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-signal-" + Guid.NewGuid().ToString("N"));

    private string Signals => Path.Combine(_root, "signals");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void A_signal_is_read_back_with_the_agents_conversation_id()
    {
        SessionSignals.Write(Signals, "a1b2c3d4", "approval", "5f0c1e2a-9d3b", Noon).Should().BeTrue();

        SessionSignals.Read(Signals, "a1b2c3d4").Should().Be(new SessionSignal("approval", "5f0c1e2a-9d3b", Noon));
    }

    [Theory]
    [InlineData("../escape", "waiting")]
    [InlineData("A1B2C3D4", "waiting")]
    [InlineData("a1b2c3d4", "choose option 2")]
    public void Only_an_id_the_daemon_gives_and_a_known_state_are_written(string id, string state)
    {
        SessionSignals.Write(Signals, id, state, null, Noon).Should().BeFalse();

        Directory.Exists(Signals).Should().BeFalse("nothing was written anywhere");
    }

    [Fact]
    public void An_agent_id_that_is_not_plainly_an_id_is_dropped_and_the_last_good_one_kept()
    {
        // It goes on a command line the page shows.
        SessionSignals.Write(Signals, "a1b2c3d4", "turn", "5f0c1e2a", Noon);
        SessionSignals.Write(Signals, "a1b2c3d4", "waiting", "x; rm -rf /", Noon.AddMinutes(1));

        SessionSignals.Read(Signals, "a1b2c3d4")!.AgentSession.Should().Be("5f0c1e2a");
    }

    [Fact]
    public void A_question_counts_until_somebody_types_and_never_once_it_has_ended()
    {
        var asked = new SessionSignal("waiting", null, Noon);

        SessionSignals.Needs(asked, lastInput: null, ended: false).Should().Be("waiting");
        SessionSignals.Needs(asked, lastInput: Noon.AddSeconds(-5), ended: false).Should().Be("waiting");
        SessionSignals.Needs(asked, lastInput: Noon.AddSeconds(5), ended: false).Should().BeNull();
        SessionSignals.Needs(asked, lastInput: null, ended: true).Should().BeNull();
        SessionSignals.Needs(asked with { State = "turn" }, null, false).Should().BeNull("a reply ending says nothing about waiting");
    }

    [Fact]
    public void Pruning_removes_only_what_is_older_than_a_week()
    {
        SessionSignals.Write(Signals, "a1b2c3d4", "turn", null, Noon);
        SessionSignals.Write(Signals, "0000ffff", "turn", null, Noon);
        File.SetLastWriteTimeUtc(Path.Combine(Signals, "0000ffff.json"), DateTime.UtcNow.AddDays(-8));

        SessionSignals.Prune(Signals, DateTimeOffset.UtcNow);

        Directory.GetFiles(Signals).Select(Path.GetFileName).Should().Equal("a1b2c3d4.json");
    }

    [Theory]
    [InlineData("\u001b[I", true)]
    [InlineData("\u001b[O\u001b[I", true)]
    [InlineData("\u001b[12;40R", true)]
    [InlineData("\u001b[?1;2c", true)]
    [InlineData("\u001b[>0;276;0c", true)]
    [InlineData("\u001b[?2004;1$y", true)]
    [InlineData("\u001b]11;rgb:0b0b/1414/1c1c\u0007", true)]
    [InlineData("\u001b[<64;40;12M", true)]
    [InlineData("\u001b[<65;40;12M\u001b[<65;40;12M", true)]
    [InlineData("\u001b[<35;40;12M", true)]
    [InlineData("\u001b[M`(,", true)]
    [InlineData("\u001b[<0;40;12M\u001b[<0;40;12m", true)]
    [InlineData("\u001b[<0;40;12M1", false)]
    [InlineData("\u001b[B", false)]
    [InlineData("\r", false)]
    [InlineData("1", false)]
    [InlineData("\u001b[Iyes", false)]
    public void The_terminal_answering_the_agent_is_told_apart_from_a_person_typing(string written, bool onlyReports)
    {
        // An arrow key is an escape sequence too, and it is how an option is
        // picked, so only a write made wholly of answers is left out.
        TerminalReports.IsOnlyReports(System.Text.Encoding.Latin1.GetBytes(written))
            .Should().Be(onlyReports);
    }

    [Fact]
    public async Task The_hook_command_records_the_state_and_the_conversation_and_prints_nothing()
    {
        var (command, console, paths) = Command(new Dictionary<string, string> { [SessionSignals.Variable] = "a1b2c3d4" },
            """{"session_id":"5f0c1e2a-9d3b","hook_event_name":"Notification","notification_type":"permission_prompt","message":"Claude needs your permission to use Bash"}""");

        var exit = await command.ExecuteAsync(null!, new DashboardSignalSettings { State = "approval" }, CancellationToken.None);

        exit.Should().Be(0);
        console.Output.Should().BeEmpty("a hook's words can be put in front of the agent");

        var signal = SessionSignals.Read(SessionSignals.Directory(paths.Paths.State), "a1b2c3d4")!;
        signal.State.Should().Be("approval");
        signal.AgentSession.Should().Be("5f0c1e2a-9d3b");
        File.ReadAllText(Path.Combine(SessionSignals.Directory(paths.Paths.State), "a1b2c3d4.json"))
            .Should().NotContain("Bash", "nothing from the message is kept: it can quote what the agent was about to run");
    }

    [Fact]
    public async Task Outside_a_studio_session_the_hook_command_does_nothing()
    {
        var (command, console, paths) = Command([], """{"session_id":"5f0c1e2a"}""");

        var exit = await command.ExecuteAsync(null!, new DashboardSignalSettings { State = "waiting" }, CancellationToken.None);

        exit.Should().Be(0);
        console.Output.Should().BeEmpty();
        Directory.Exists(SessionSignals.Directory(paths.Paths.State)).Should().BeFalse();
    }

    [Fact]
    public async Task Input_that_is_not_json_still_records_the_state()
    {
        var (command, _, paths) = Command(new Dictionary<string, string> { [SessionSignals.Variable] = "a1b2c3d4" }, "not json");

        await command.ExecuteAsync(null!, new DashboardSignalSettings { State = "waiting" }, CancellationToken.None);

        SessionSignals.Read(SessionSignals.Directory(paths.Paths.State), "a1b2c3d4")
            .Should().BeEquivalentTo(new { State = "waiting", AgentSession = (string?)null });
    }

    [Fact]
    public async Task A_claude_session_hosted_for_studio_reports_waiting_and_approval_through_its_own_hooks()
    {
        var settings = await ClaudeSettingsAsync(studioSession: "a1b2c3d4", headless: null);

        var hooks = settings!["hooks"]!;
        var notification = hooks["Notification"]!.AsArray();

        notification.Select(one => (string?)one!["matcher"]).Should().Equal("idle_prompt", "permission_prompt");
        Command(notification[0]!).Should().EndWith(" dashboard signal waiting");
        Command(notification[1]!).Should().EndWith(" dashboard signal approval");
        Command(hooks["Stop"]![0]!).Should().EndWith(" dashboard signal turn");

        static string Command(JsonNode entry) => (string)entry["hooks"]![0]!["command"]!;
    }

    [Fact]
    public async Task A_launch_typed_at_a_terminal_or_a_node_gets_no_studio_hooks()
    {
        (await ClaudeSettingsAsync(studioSession: null, headless: null)).Should().BeNull("no settings file is made for nothing");

        var node = await ClaudeSettingsAsync(studioSession: "a1b2c3d4", headless: new HeadlessOptions());
        node?["hooks"].Should().BeNull("a node is nobody's to answer");
    }

    private (DashboardSignalCommand Command, TestConsole Console, IPlatformPaths Paths) Command(
        Dictionary<string, string> variables, string input)
    {
        variables["XDG_STATE_HOME"] = Path.Combine(_root, "state");
        variables["XDG_CONFIG_HOME"] = Path.Combine(_root, "config");
        variables["XDG_DATA_HOME"] = Path.Combine(_root, "data");
        variables["XDG_CACHE_HOME"] = Path.Combine(_root, "cache");

        var environment = new FakeEnvironmentProvider(Path.Combine(_root, "home"), variables);
        var paths = new LinuxPaths(
            environment,
            new NoOpFilePermissions(),
            new HostPlatform(HostOperatingSystem.Linux, System.Runtime.InteropServices.Architecture.X64, "test", "TEST"));
        var console = new TestConsole();

        return (new DashboardSignalCommand(paths, environment, console, TimeProvider.System) { Input = new StringReader(input) }, console, paths);
    }

    /// <summary>The settings Claude is handed, or null where it is handed none.</summary>
    private async Task<JsonObject?> ClaudeSettingsAsync(string? studioSession, HeadlessOptions? headless)
    {
        var runtime = Path.Combine(_root, "runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runtime);

        var adapter = new ClaudeAdapter(
            new StubResolver(Path.Combine(Path.GetTempPath(), "claude")),
            new StubProcessLauncher("""
                Usage: claude [options] [command] [prompt]
                  --settings <file-or-json>           Path to a settings JSON file or a JSON string
                """),
            []);

        var built = await adapter.BuildInvocationAsync(new AgentLaunchContext(
            new ProjectResolution(new ProjectRegistryEntry { Slug = "demo", Name = "Demo" }, Path.GetTempPath(), null, 0, false),
            Path.GetTempPath(),
            runtime,
            null,
            [],
            Headless: headless,
            StudioSession: studioSession));

        built.Succeeded.Should().BeTrue(built.Error);

        var arguments = built.Value!.Arguments.ToList();
        var at = arguments.IndexOf("--settings");

        if (at < 0)
        {
            return null;
        }

        var value = arguments[at + 1];

        return JsonNode.Parse(File.Exists(value) ? File.ReadAllText(value) : value)!.AsObject();
    }
}
