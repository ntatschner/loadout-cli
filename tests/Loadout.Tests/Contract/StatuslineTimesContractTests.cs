using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Loadout.Core.Sessions.Hosting;
using Loadout.Core.Statusline;
using Xunit;
using Fixtures = Loadout.Tests.Unit.TranscriptTimesTests.Fixtures;

namespace Loadout.Tests.Contract;

/// <summary>
/// The time segments of the status line, drawn by the real binary from a
/// payload on its input and a transcript under the test's own home.
/// </summary>
/// <remarks>
/// The binary's clock cannot be pinned, so the transcripts are written a few
/// minutes before now and the expected times worked out from them in this
/// machine's zone, which is the zone the line is drawn in.
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class StatuslineTimesContractTests
{
    private const string StudioSession = "abcd1234";

    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddMinutes(-10);

    [BuiltCliFact]
    public async Task The_line_says_waiting_for_you_from_the_transcript()
    {
        using var loadout = Isolated();
        var ended = Start.AddMinutes(2).AddSeconds(5);

        var line = await RenderAsync(loadout, Transcript(loadout, Waiting(Start)));

        line.Should().Contain("waiting for you since " + Clock(ended));
    }

    [BuiltCliFact]
    public async Task A_hosted_session_with_a_fresh_approval_signal_says_permission()
    {
        using var loadout = Isolated();
        var signalled = Start.AddMinutes(3);

        await SignalAsync(loadout, signalled);
        loadout.Environment[SessionSignals.Variable] = StudioSession;

        var line = await RenderAsync(loadout, Transcript(loadout, Pending(Start)));

        line.Should().Contain("permission since " + Clock(signalled));
    }

    [BuiltCliFact]
    public async Task A_stale_approval_signal_is_ignored()
    {
        using var loadout = Isolated();

        // Written before the tool call the transcript ends on, so it speaks
        // for an earlier prompt that has already been answered.
        await SignalAsync(loadout, Start.AddSeconds(30));
        loadout.Environment[SessionSignals.Variable] = StudioSession;

        var line = await RenderAsync(loadout, Transcript(loadout, Pending(Start)));

        line.Should().Contain("on a tool since " + Clock(Start.AddMinutes(1)));
        line.Should().NotContain("permission");
    }

    [BuiltCliFact]
    public async Task Without_the_studio_variable_a_pending_tool_stays_neutral()
    {
        using var loadout = Isolated();

        // A fresh approval on disk, but this session is not the one it is for.
        await SignalAsync(loadout, Start.AddMinutes(3));

        var line = await RenderAsync(loadout, Transcript(loadout, Pending(Start)));

        line.Should().Contain("on a tool since " + Clock(Start.AddMinutes(1)));
        line.Should().NotContain("permission");
    }

    [BuiltCliFact]
    public async Task Redraw_never_in_config_gives_fixed_times()
    {
        using var loadout = Isolated();

        (await loadout.RunAsync("config", "set", "show-redraw", "never")).ExitCode.Should().Be(0);

        var line = await RenderAsync(loadout, Transcript(loadout, Edits(Start)));

        line.Should().Contain("write " + Clock(Start));
        line.Should().NotContain("ago");
    }

    [BuiltCliFact]
    public async Task A_narrow_COLUMNS_drops_times_first()
    {
        using var loadout = Isolated();
        var transcript = Transcript(loadout, Edits(Start));

        var wide = await RenderAsync(loadout, transcript);

        loadout.Environment["COLUMNS"] = "12";

        var narrow = await RenderAsync(loadout, transcript);

        wide.Should().Contain("waiting for you since").And.Contain("write ");
        narrow.Should().NotContain("waiting for you").And.NotContain("write ");
        narrow.Should().Contain("Opus 5", "only time segments are dropped to fit");
    }

    [BuiltCliFact]
    public async Task Show_json_lists_the_time_segments_and_previews_them()
    {
        using var loadout = Isolated();

        var run = await loadout.RunAsync("statusline", "show", "--global", "--json");

        run.ExitCode.Should().Be(0, run.StandardError);

        using var shown = JsonDocument.Parse(run.StandardOutput);
        var segments = shown.RootElement.GetProperty("segments");

        segments.GetProperty("waiting").GetBoolean().Should().BeTrue();
        segments.GetProperty("lastWrite").GetBoolean().Should().BeTrue();
        segments.GetProperty("clock").GetBoolean().Should().BeFalse();
        segments.GetProperty("session").GetBoolean().Should().BeFalse();
        segments.GetProperty("turn").GetBoolean().Should().BeFalse();
        segments.GetProperty("lastCommit").GetBoolean().Should().BeFalse();

        var preview = shown.RootElement.GetProperty("preview").GetString();

        preview.Should().Contain("waiting for you since").And.Contain("write 5m ago");
        preview.Should().NotContain("\u001b", "JSON previews are uncoloured");
    }

    // The shapes of TranscriptTimesTests' fixtures, moved to a start of the
    // test's choosing.
    private static List<string> Waiting(DateTimeOffset t) =>
    [
        Fixtures.Prompt(t),
        Fixtures.Call("toolu_a1", "Bash", t.AddMinutes(1)),
        Fixtures.Result("toolu_a1", t.AddMinutes(1).AddSeconds(3)),
        Fixtures.Said("text", "end_turn", t.AddMinutes(2).AddSeconds(5)),
        Fixtures.System("turn_duration", t.AddMinutes(2).AddSeconds(6)),
    ];

    private static List<string> Pending(DateTimeOffset t) =>
    [
        Fixtures.Prompt(t),
        Fixtures.Said("thinking", "tool_use", t.AddSeconds(50)),
        Fixtures.Call("toolu_b1", "Bash", t.AddMinutes(1)),
    ];

    private static List<string> Edits(DateTimeOffset t) =>
    [
        Fixtures.Prompt(t.AddMinutes(-1)),
        Fixtures.Call("toolu_f1", "Write", t),
        Fixtures.Result("toolu_f1", t.AddSeconds(1)),
        Fixtures.Said("text", "end_turn", t.AddMinutes(7)),
    ];

    private static LoadoutProcess Isolated()
    {
        var loadout = new LoadoutProcess();
        var claudeHome = Path.Combine(loadout.Home, ".claude");

        Directory.CreateDirectory(claudeHome);

        loadout.Environment["CLAUDE_CONFIG_DIR"] = claudeHome;

        // Whatever the test host was started inside of is not this session.
        loadout.Environment[SessionSignals.Variable] = string.Empty;
        loadout.Environment["COLUMNS"] = string.Empty;

        return loadout;
    }

    private static string Clock(DateTimeOffset at) => TimeFormat.Clock(at, TimeZoneInfo.Local, hour24: true);

    private static string Transcript(LoadoutProcess loadout, IEnumerable<string> lines)
    {
        var path = Path.Combine(loadout.Home, "transcript-" + Guid.NewGuid().ToString("N")[..8] + ".jsonl");

        File.WriteAllLines(path, lines);

        return path;
    }

    /// <summary>
    /// Records an approval the way Studio's hook does, through the binary so
    /// the file lands where this platform keeps state, then moves its time.
    /// </summary>
    private static async Task SignalAsync(LoadoutProcess loadout, DateTimeOffset at)
    {
        loadout.Environment[SessionSignals.Variable] = StudioSession;

        await TalkAsync(loadout, "{}", "dashboard", "signal", "approval");

        loadout.Environment[SessionSignals.Variable] = string.Empty;

        var file = Directory
            .EnumerateFiles(loadout.Home, StudioSession + ".json", SearchOption.AllDirectories)
            .Single(f => f.Contains(Path.Combine("studio", "signals"), StringComparison.Ordinal));

        var signal = JsonNode.Parse(File.ReadAllText(file))!;

        signal["at"] = at.ToString("o");
        File.WriteAllText(file, signal.ToJsonString());
    }

    private static async Task<string> RenderAsync(LoadoutProcess loadout, string transcript)
    {
        var payload = JsonSerializer.Serialize(new
        {
            session_id = "contract",
            transcript_path = transcript,
            cwd = loadout.Home,
            model = new { display_name = "Opus 5" },
            cost = new { total_duration_ms = 600_000 },
        });

        var output = await TalkAsync(loadout, payload, "statusline");

        // Escape codes are taken out so the assertions read the words.
        return Regex.Replace(output, "\u001b\\[[0-9;]*m", string.Empty);
    }

    private static async Task<string> TalkAsync(LoadoutProcess loadout, string input, params string[] arguments)
    {
        using var process = Process.Start(loadout.Talking(arguments))!;

        await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        await process.WaitForExitAsync(timeout.Token);

        process.ExitCode.Should().Be(0, await error);

        return await output;
    }
}
