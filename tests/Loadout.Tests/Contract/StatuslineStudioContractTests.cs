using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Loadout.Core.Sessions.Hosting;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// The status line a Studio-hosted session runs, through the real binary: it
/// leaves Studio a snapshot, and prints what statusline-in-studio asks
/// for, the person's own line included, however that line behaves.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class StatuslineStudioContractTests
{
    private const string StudioSession = "abcd1234";

    private static readonly string Payload = JsonSerializer.Serialize(new
    {
        session_id = "contract",
        model = new { display_name = "Opus 5" },
        cost = new { total_cost_usd = 0.42, total_duration_ms = 60_000 },
        context_window = new { total_input_tokens = 84_000, context_window_size = 200_000, used_percentage = 42 },
        rate_limits = new { five_hour = new { used_percentage = 61, resets_at = 1_893_456_000 } },
    });

    [BuiltCliFact]
    public async Task By_default_the_terminal_shows_nothing_and_studio_gets_the_figures()
    {
        using var loadout = Hosted();

        var (output, _) = await RenderAsync(loadout);

        output.Should().BeEmpty("the row goes back to the terminal");

        var snapshot = Snapshot(loadout)!;
        snapshot["terminal"]!.GetValue<string>().Should().Be("off");
        snapshot["context"]!["usedPercent"]!.GetValue<double>().Should().Be(42);
        snapshot["cost"]!["usd"]!.GetValue<double>().Should().Be(0.42);
        snapshot["limits"]!["fiveHour"]!["usedPercent"]!.GetValue<double>().Should().Be(61);
        snapshot["refreshSeconds"]!.GetValue<int>().Should().Be(30);
    }

    [BuiltCliFact]
    public async Task Minimal_prints_a_short_line_of_the_launchers()
    {
        using var loadout = Hosted();

        (await loadout.RunAsync("config", "set", "statusline-in-studio", "minimal")).ExitCode.Should().Be(0);

        var (output, _) = await RenderAsync(loadout);

        output.Should().Contain("Opus 5").And.Contain("42%");
        Snapshot(loadout)!["terminal"]!.GetValue<string>().Should().Be("minimal");
    }

    [BuiltCliFact]
    public async Task Full_runs_the_persons_own_line_with_the_same_payload()
    {
        using var loadout = Hosted();

        await FullWithAsync(loadout, "printf 'mine:'; cat | head -c 20");

        var (output, _) = await RenderAsync(loadout);

        output.Should().StartWith("mine:{\"session_id\"", "their command got Claude's payload on its input");

        var snapshot = Snapshot(loadout)!;
        snapshot["terminal"]!.GetValue<string>().Should().Be("full");
        snapshot["wrapping"]!.GetValue<bool>().Should().BeTrue();
    }

    [BuiltCliFact]
    public async Task Full_without_a_line_of_their_own_draws_the_launchers()
    {
        using var loadout = Hosted();

        (await loadout.RunAsync("config", "set", "statusline-in-studio", "full")).ExitCode.Should().Be(0);

        var (output, _) = await RenderAsync(loadout);

        output.Should().Contain("Opus 5");
        Snapshot(loadout)!["wrapping"]!.GetValue<bool>().Should().BeFalse();
    }

    [BuiltCliFact]
    public async Task A_slow_line_of_their_own_still_leaves_studio_a_snapshot_within_the_deadline()
    {
        using var loadout = Hosted();

        await FullWithAsync(loadout, "sleep 8; echo late");

        var (output, took) = await RenderAsync(loadout);

        output.Should().NotContain("late");
        took.Should().BeLessThan(TimeSpan.FromSeconds(6), "the line has a deadline whatever theirs does");
        Snapshot(loadout).Should().NotBeNull();
    }

    [BuiltCliFact]
    public async Task A_failing_line_of_their_own_still_leaves_studio_a_snapshot()
    {
        using var loadout = Hosted();

        await FullWithAsync(loadout, "exit 3");

        var (output, _) = await RenderAsync(loadout);

        output.Should().BeEmpty();
        Snapshot(loadout).Should().NotBeNull();
    }

    [BuiltCliFact]
    public async Task Outside_studio_the_line_is_drawn_as_before_and_no_snapshot_is_left()
    {
        using var loadout = Hosted();

        loadout.Environment[SessionSignals.Variable] = string.Empty;

        var (output, _) = await RenderAsync(loadout, studio: false);

        output.Should().Contain("Opus 5");
        Snapshot(loadout).Should().BeNull();
    }

    [BuiltCliFact]
    public async Task Show_says_when_a_hosted_session_wraps_the_persons_own_line()
    {
        using var loadout = Hosted();

        await FullWithAsync(loadout, "echo mine");

        var run = await loadout.RunAsync("statusline", "show", "--global", "--json");

        run.ExitCode.Should().Be(0, run.StandardError);

        var studio = run.Json().GetProperty("studio");

        studio.GetProperty("terminal").GetString().Should().Be("full");
        studio.GetProperty("ownCommand").GetString().Should().Be("echo mine");
        studio.GetProperty("wrapping").GetBoolean().Should().BeTrue();

        var text = await loadout.RunAsync("statusline", "show", "--global");

        text.StandardOutput.Should().Contain("your own line, run for you");
    }

    private static LoadoutProcess Hosted()
    {
        var loadout = new LoadoutProcess();
        var claudeHome = Path.Combine(loadout.Home, ".claude");

        Directory.CreateDirectory(claudeHome);

        loadout.Environment["CLAUDE_CONFIG_DIR"] = claudeHome;
        loadout.Environment[SessionSignals.Variable] = StudioSession;
        loadout.Environment["COLUMNS"] = string.Empty;

        return loadout;
    }

    /// <summary>Gives the person a status line of their own in their Claude user settings, and asks for it in the terminal.</summary>
    private static async Task FullWithAsync(LoadoutProcess loadout, string command)
    {
        File.WriteAllText(
            Path.Combine(loadout.Home, ".claude", "settings.json"),
            new JsonObject { ["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = command } }.ToJsonString());

        (await loadout.RunAsync("config", "set", "statusline-in-studio", "full")).ExitCode.Should().Be(0);
    }

    private static JsonNode? Snapshot(LoadoutProcess loadout)
    {
        var file = Directory
            .EnumerateFiles(loadout.Home, StudioSession + ".json", SearchOption.AllDirectories)
            .SingleOrDefault(f => f.Contains(Path.Combine("studio", "status"), StringComparison.Ordinal));

        return file is null ? null : JsonNode.Parse(File.ReadAllText(file));
    }

    private static async Task<(string Output, TimeSpan Took)> RenderAsync(LoadoutProcess loadout, bool studio = true)
    {
        string[] arguments = studio ? ["statusline", "--studio", "--refresh", "30"] : ["statusline"];
        var clock = Stopwatch.StartNew();

        using var process = Process.Start(loadout.Talking(arguments))!;

        await process.StandardInput.WriteAsync(Payload);
        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));

        var took = clock.Elapsed;

        process.ExitCode.Should().Be(0, await error);

        return ((await output).TrimEnd('\r', '\n'), took);
    }
}
