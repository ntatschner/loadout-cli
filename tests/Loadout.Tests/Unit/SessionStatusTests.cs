using System.Text.Json.Nodes;
using FluentAssertions;
using Loadout.Agents;
using Loadout.Agents.Claude;
using Loadout.Cli.Commands;
using Loadout.Core.Sessions.Hosting;
using Loadout.Core.Statusline;
using Loadout.Models.Agents;
using Loadout.Models.Projects;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// How a session Studio hosts hands over its status: what is taken from
/// Claude's payload, the file it lands in, the line the launch installs, and
/// how the person's own line is found to wrap.
/// </summary>
public sealed class SessionStatusTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-status-" + Guid.NewGuid().ToString("N"));

    private string Statuses => Path.Combine(_root, "status");

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

    /// <summary>A payload in the shape Claude Code 2.1.311 documents for its status line.</summary>
    private const string Full = """
        {
          "session_id": "5f0c1e2a",
          "model": { "id": "claude-opus-5-5", "display_name": "Opus 5.5" },
          "cost": { "total_cost_usd": 1.234, "total_duration_ms": 600000, "total_lines_added": 40, "total_lines_removed": 7 },
          "context_window": {
            "total_input_tokens": 84000, "total_output_tokens": 900, "context_window_size": 200000,
            "current_usage": { "input_tokens": 1200, "output_tokens": 900, "cache_creation_input_tokens": 3000, "cache_read_input_tokens": 79800 },
            "used_percentage": 42, "remaining_percentage": 58
          },
          "rate_limits": {
            "five_hour": { "used_percentage": 61.5, "resets_at": 1793102400 },
            "seven_day": { "used_percentage": 12, "resets_at": 1793500000 }
          }
        }
        """;

    [Fact]
    public void A_snapshot_carries_claudes_figures_for_context_cost_and_limits()
    {
        var snapshot = SessionStatusSnapshot.From(
            StatuslinePayload.Parse(Full), Noon, 30, "off", false, "demo", ["role.implementer", "language.csharp"]);

        snapshot.Model.Should().Be("Opus 5.5");
        snapshot.Context.Should().Be(new StatusContext(42, 84000, 200000, 1200, 79800, 3000, 900));
        snapshot.Cost.Should().Be(new StatusCost(1.234, 600000, 40, 7));
        snapshot.Limits!.FiveHour.Should().Be(new StatusWindow(61.5, DateTimeOffset.FromUnixTimeSeconds(1793102400)));
        snapshot.Limits.SevenDay!.UsedPercent.Should().Be(12);
        snapshot.Limits.Spend.Should().BeNull();
        snapshot.Specialists.Should().Equal("role.implementer", "language.csharp");
        snapshot.RefreshSeconds.Should().Be(30);
    }

    [Fact]
    public void What_claude_did_not_send_is_absent_rather_than_zero()
    {
        // An older Claude, or an API key: no cost, no limits, no percentage.
        var snapshot = SessionStatusSnapshot.From(
            StatuslinePayload.Parse("""{ "context_window": { "total_input_tokens": 50000, "context_window_size": 200000 } }"""),
            Noon, null, "off", false, null, null);

        snapshot.Context!.UsedPercent.Should().Be(25, "worked out from the two halves where Claude sent no figure");
        snapshot.Context.CacheReadTokens.Should().BeNull();
        snapshot.Cost.Should().BeNull();
        snapshot.Limits.Should().BeNull();
        snapshot.Model.Should().BeNull();
    }

    [Fact]
    public void Limits_with_no_window_in_them_are_no_limits()
    {
        var snapshot = SessionStatusSnapshot.From(
            StatuslinePayload.Parse("""{ "rate_limits": { "five_hour": { "resets_at": 1793102400 } } }"""),
            Noon, null, "off", false, null, null);

        snapshot.Limits.Should().BeNull("a window with no figure says nothing");
    }

    [Fact]
    public void A_gateway_spend_limit_is_kept_with_its_money()
    {
        var snapshot = SessionStatusSnapshot.From(
            StatuslinePayload.Parse("""{ "rate_limits": { "spend_limit": { "used_percentage": 104, "resets_at": 1793102400, "used_usd": 52, "limit_usd": 50, "period": "weekly" } } }"""),
            Noon, null, "off", false, null, null);

        snapshot.Limits!.Spend.Should().Be(new StatusSpend(104, DateTimeOffset.FromUnixTimeSeconds(1793102400), 52, 50, "weekly"));
    }

    [Fact]
    public void A_snapshot_is_read_back_as_it_was_written()
    {
        var snapshot = SessionStatusSnapshot.From(StatuslinePayload.Parse(Full), Noon, 30, "full", true, "demo", ["a"]);

        SessionStatus.Write(Statuses, "a1b2c3d4", snapshot).Should().BeTrue();

        var read = SessionStatus.Read(Statuses, "a1b2c3d4")!;

        read.Should().BeEquivalentTo(snapshot);
        Directory.EnumerateFiles(Statuses).Should().ContainSingle("the aside is moved, not left");
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("A1B2C3D4")]
    public void Only_an_id_the_daemon_gives_is_written(string id)
    {
        SessionStatus.Write(Statuses, id, SessionStatusSnapshot.From(null, Noon, null, "off", false, null, null)).Should().BeFalse();
        Directory.Exists(Statuses).Should().BeFalse();
    }

    [Fact]
    public void A_snapshot_from_a_newer_launcher_is_not_half_read()
    {
        Directory.CreateDirectory(Statuses);
        File.WriteAllText(Path.Combine(Statuses, "a1b2c3d4.json"), """{ "version": 2, "at": "2026-10-27T12:00:00Z", "terminal": "off" }""");

        SessionStatus.Read(Statuses, "a1b2c3d4").Should().BeNull();
    }

    [Fact]
    public void Pruning_removes_only_what_is_older_than_a_day()
    {
        SessionStatus.Write(Statuses, "aaaaaaaa", SessionStatusSnapshot.From(null, Noon, null, "off", false, null, null));
        SessionStatus.Write(Statuses, "bbbbbbbb", SessionStatusSnapshot.From(null, Noon, null, "off", false, null, null));
        File.SetLastWriteTimeUtc(Path.Combine(Statuses, "aaaaaaaa.json"), DateTime.UtcNow.AddDays(-2));

        SessionStatus.Prune(Statuses, DateTimeOffset.UtcNow);

        SessionStatus.Read(Statuses, "aaaaaaaa").Should().BeNull();
        SessionStatus.Read(Statuses, "bbbbbbbb").Should().NotBeNull();
    }

    [Fact]
    public async Task A_hosted_claude_session_is_given_the_studio_status_line()
    {
        var settings = await ClaudeSettingsAsync(studioSession: "a1b2c3d4");

        var line = settings!["statusLine"]!;

        ((string)line["type"]!).Should().Be("command");
        ((string)line["command"]!).Should().EndWith(" statusline --studio");
    }

    [Fact]
    public async Task A_session_studio_does_not_host_gets_no_status_line()
    {
        (await ClaudeSettingsAsync(studioSession: null)).Should().BeNull("no settings file is made for nothing");

        var node = await ClaudeSettingsAsync(studioSession: "a1b2c3d4", headless: new HeadlessOptions());
        node?["statusLine"].Should().BeNull("a node is nobody's to watch");
    }

    [Fact]
    public void The_redraw_interval_is_written_and_told_to_the_command()
    {
        var document = new JsonObject { ["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = "their-line" } };

        ClaudeAdapter.AddStudioStatusline(document, "loadout", 30);

        ((string)document["statusLine"]!["command"]!).Should().Be("loadout statusline --studio --refresh 30");
        ((int)document["statusLine"]!["refreshInterval"]!).Should().Be(30);

        ClaudeAdapter.AddStudioStatusline(document, "loadout", null);

        document["statusLine"]!["refreshInterval"].Should().BeNull("none under a profile that asks for nothing to redraw");
    }

    [Fact]
    public void The_persons_own_line_is_the_highest_file_that_sets_one()
    {
        var workspace = Settings("workspace.json", null);
        var local = Settings("local.json", "echo local");
        var user = Settings("user.json", "echo user");

        StatuslineWrap.FindOwnCommand([workspace, local, user]).Should().Be("echo local");
        StatuslineWrap.FindOwnCommand([Path.Combine(_root, "missing.json"), user]).Should().Be("echo user");
        StatuslineWrap.FindOwnCommand([workspace]).Should().BeNull();
    }

    [Fact]
    public void The_launchers_own_line_is_drawn_in_process_not_run()
    {
        var installed = Settings("installed.json", "\"C:\\\\Users\\\\me\\\\loadout.exe\" statusline");
        var user = Settings("user.json", "echo user");

        StatuslineWrap.FindOwnCommand([installed, user])
            .Should().BeNull("the launcher's line wins there, and it is the launcher's to draw");
        StatuslineWrap.IsLaunchers("\"dotnet\" \"D:\\loadout.dll\" statusline --studio --refresh 30").Should().BeTrue();
        StatuslineWrap.IsLaunchers("~/.claude/statusline.sh").Should().BeFalse();
    }

    [Fact]
    public void A_settings_file_with_comments_still_gives_its_line()
    {
        var path = Path.Combine(_root, "commented.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(path, """
            {
              // mine
              "statusLine": { "type": "command", "command": "echo mine", },
            }
            """);

        StatuslineWrap.FindOwnCommand([path]).Should().Be("echo mine");
    }

    [Theory]
    [InlineData(null, "off")]
    [InlineData("", "off")]
    [InlineData("Minimal", "minimal")]
    [InlineData(" full ", "full")]
    [InlineData("loud", "off")]
    public void A_hand_edited_terminal_setting_falls_back_to_off(string? value, string expected) =>
        StatuslineRenderCommand.StudioTerminal(value).Should().Be(expected);

    [Fact]
    public void The_minimal_line_keeps_nothing_that_can_be_slow()
    {
        var minimal = StatuslineRenderCommand.Minimal(new Loadout.Models.Configuration.StatuslineSettings());

        minimal.ShowGit.Should().BeFalse();
        minimal.ShowWaiting.Should().BeFalse();
        minimal.ShowLastWrite.Should().BeFalse();
        minimal.ShowSpend.Should().BeFalse();
        minimal.ShowContext.Should().BeTrue();
        minimal.ShowModel.Should().BeTrue();
    }

    private string Settings(string name, string? command)
    {
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, name);
        var document = new JsonObject { ["model"] = "opus" };

        if (command is not null)
        {
            document["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = command };
        }

        File.WriteAllText(path, document.ToJsonString());

        return path;
    }

    /// <summary>The settings Claude is handed, or null where it is handed none.</summary>
    private async Task<JsonObject?> ClaudeSettingsAsync(string? studioSession, HeadlessOptions? headless = null)
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
            StudioSession: studioSession,

            // Attribution kept, so the only thing that can make a settings
            // file here is the status line: cleared trailers make one for
            // every launch, and have tests of their own.
            KeepAttribution: true));

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
