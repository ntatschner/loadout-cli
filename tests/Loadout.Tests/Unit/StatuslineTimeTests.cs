using System.Globalization;
using System.Text.RegularExpressions;
using Loadout.Core.Configuration;
using Loadout.Core.Sessions.Hosting;
using Loadout.Core.Statusline;
using Loadout.Core.Usage;
using Loadout.Models.Configuration;
using Loadout.Models.Platform;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The time segments on the status line: what the session is waiting on, the
/// last write, the clock, the session, the last turn and the last commit.
/// <para>
/// Every test pins <c>now</c> and the zone. A time segment that read the
/// machine's clock would pass here at one moment and fail near midnight, and
/// the one that matters most, <c>due</c>, turns on an exact instant.
/// </para>
/// </summary>
public sealed class StatuslineTimeTests
{
    private static readonly DateTimeOffset Now = At("14:05:00");

    private static DateTimeOffset At(string time) =>
        DateTimeOffset.Parse("2026-10-22T" + time + "Z", CultureInfo.InvariantCulture);

    /// <summary>Colour off throughout unless a test is about colour.</summary>
    private static StatuslineSettings Plain() => new() { Colour = false };

    private static StatuslineSettings AllOn(bool colour = false) => new()
    {
        Colour = colour,
        ShowClock = true,
        ShowSession = true,
        ShowTurn = true,
        ShowLastCommit = true,
    };

    /// <summary>A turn that started at 13:58 and ended at 14:02, with every other time known.</summary>
    private static StatuslineTimes Everything() => new(
        TurnStartedAt: At("13:58:00"),
        TurnEndedAt: At("14:02:00"),
        LastWriteAt: At("14:02:00"),
        SessionLength: TimeSpan.FromMinutes(72),
        LastCommitAt: At("11:05:00"));

    private static StatuslineInputs Inputs(
        StatuslineTimes times,
        DateTimeOffset? now = null,
        TimeZoneInfo? zone = null,
        bool fixedTimes = false,
        int? width = null,
        SpendNotice? spend = null) =>
        new(
            null, null, null, null,
            Spend: spend,
            Now: now ?? Now,
            Zone: zone ?? TimeZoneInfo.Utc,
            Times: times,
            FixedTimes: fixedTimes,
            Width: width);

    private static SpendNotice Spending() => new() { Lines = ["over the weekly limit"] };

    private static string Strip(string line) => Regex.Replace(line, "\u001b\\[[0-9;]*m", string.Empty);

    [Fact]
    public void The_defaults_show_waiting_and_last_write_only()
    {
        var settings = new StatuslineSettings();

        settings.ShowWaiting.Should().BeTrue();
        settings.ShowLastWrite.Should().BeTrue();
        settings.ShowClock.Should().BeFalse();
        settings.ShowSession.Should().BeFalse();
        settings.ShowTurn.Should().BeFalse();
        settings.ShowLastCommit.Should().BeFalse();
        settings.Time.Absolute.Should().BeFalse();
        settings.Time.Hour24.Should().BeTrue();

        settings.Colour = false;

        var line = StatuslineRenderer.Render(Inputs(Everything()), settings);

        line.Should().Be("waiting for you since 14:02 | write 3m ago");
        line.Should().NotContain("14:05").And.NotContain("session")
            .And.NotContain("turn").And.NotContain("commit");
    }

    [Fact]
    public async Task An_older_config_without_the_new_keys_gets_the_defaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "loadout-statusline-" + Guid.NewGuid().ToString("N"));

        try
        {
            var environment = new FakeEnvironmentProvider(
                Path.Combine(root, "home"),
                new Dictionary<string, string>
                {
                    ["XDG_CONFIG_HOME"] = Path.Combine(root, "config"),
                    ["XDG_DATA_HOME"] = Path.Combine(root, "data"),
                    ["XDG_STATE_HOME"] = Path.Combine(root, "state"),
                    ["XDG_CACHE_HOME"] = Path.Combine(root, "cache"),
                });

            var permissions = new NoOpFilePermissions();
            var paths = new LinuxPaths(
                environment,
                permissions,
                new HostPlatform(
                    HostOperatingSystem.Linux,
                    System.Runtime.InteropServices.Architecture.X64,
                    "test",
                    "TEST-MACHINE"));

            Directory.CreateDirectory(paths.Paths.Config);

            // The statusline block as a release before the time segments
            // wrote it: the old keys only, one of them changed from its
            // default so the test can tell the block was read at all.
            await File.WriteAllTextAsync(paths.Paths.ConfigFile, """
                statusline:
                  show_project: false
                  show_directory: true
                  show_git: true
                  show_model: true
                  show_context: true
                  colour: true
                  separator: ' | '
                """);

            var configuration = new ConfigurationService(paths, environment, new YamlStore(permissions));

            var loaded = await configuration.LoadConfigAsync();

            loaded.Succeeded.Should().BeTrue();
            var statusline = loaded.Value!.Statusline;

            statusline.ShowProject.Should().BeFalse("the old block was read");
            statusline.ShowWaiting.Should().BeTrue();
            statusline.ShowLastWrite.Should().BeTrue();
            statusline.ShowClock.Should().BeFalse();
            statusline.ShowSession.Should().BeFalse();
            statusline.ShowTurn.Should().BeFalse();
            statusline.ShowLastCommit.Should().BeFalse();
            statusline.Time.Should().NotBeNull();
            statusline.Time.Absolute.Should().BeFalse();
            statusline.Time.Hour24.Should().BeTrue();
            statusline.WaitingAfter.Should().Be("10m,30m");
            statusline.LastWriteAfter.Should().Be("30m");
            statusline.LastCommitAfter.Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    public static TheoryData<string, string, Func<StatuslineSettings, object?>> NewKeys => new()
    {
        { "statusline-waiting", "false", s => s.ShowWaiting },
        { "statusline-last-write", "false", s => s.ShowLastWrite },
        { "statusline-clock", "true", s => s.ShowClock },
        { "statusline-session", "true", s => s.ShowSession },
        { "statusline-turn", "true", s => s.ShowTurn },
        { "statusline-last-commit", "true", s => s.ShowLastCommit },
        { "statusline-time-absolute", "true", s => s.Time.Absolute },
        { "statusline-time-24h", "false", s => s.Time.Hour24 },
        { "statusline-waiting-after", "5m,15m", s => s.WaitingAfter },
        { "statusline-write-after", "1h", s => s.LastWriteAfter },
        { "statusline-commit-after", "1d,3d", s => s.LastCommitAfter },
    };

    [Theory]
    [MemberData(nameof(NewKeys))]
    public void Each_new_key_reads_and_writes_its_setting(
        string key, string value, Func<StatuslineSettings, object?> setting)
    {
        var entry = ConfigKeys.Find(key);

        entry.Should().NotBeNull();
        entry!.Group.Should().Be(ConfigKeys.Groups.StatuslineTimes);

        var config = new LauncherConfig();
        var before = setting(config.Statusline);

        entry.Write(config, new MachineConfig(), value);

        // Each value is the opposite of the default, so a key writing to the
        // wrong setting leaves this one where it was.
        setting(config.Statusline).Should().NotBe(before);
        entry.Read(config, new MachineConfig()).Should().Be(value);
    }

    [Fact]
    public void The_time_segments_sit_after_the_context_figure_and_before_spend()
    {
        var payload = StatuslinePayload.Parse(
            """{ "cwd": "/tmp", "context_window": { "total_input_tokens": 84000, "context_window_size": 200000 } }""");

        var line = StatuslineRenderer.Render(
            new StatuslineInputs(
                payload, null, null, null,
                Spend: Spending(),
                Specialists: new LoadedSpecialists { Ids = ["a", "b"] },
                Now: Now,
                Zone: TimeZoneInfo.Utc,
                Times: Everything()),
            Plain());

        var context = line.IndexOf("ctx", StringComparison.Ordinal);
        var specialists = line.IndexOf("2 spec", StringComparison.Ordinal);
        var waiting = line.IndexOf("waiting", StringComparison.Ordinal);
        var spend = line.IndexOf("spend!", StringComparison.Ordinal);

        context.Should().BeGreaterThanOrEqualTo(0);
        specialists.Should().BeGreaterThan(context);
        waiting.Should().BeGreaterThan(specialists);
        spend.Should().BeGreaterThan(waiting);
    }

    [Fact]
    public void A_turn_still_running_says_working_since_it_started()
    {
        var line = StatuslineRenderer.Render(
            Inputs(new StatuslineTimes(TurnStartedAt: At("14:01:00"))), Plain());

        line.Should().Be("working since 14:01");
    }

    [Fact]
    public void A_finished_turn_with_nothing_outstanding_is_waiting_for_you()
    {
        var line = StatuslineRenderer.Render(
            Inputs(new StatuslineTimes(TurnStartedAt: At("13:58:00"), TurnEndedAt: At("14:02:00"))), Plain());

        line.Should().Be("waiting for you since 14:02");
    }

    [Fact]
    public void A_tool_call_with_no_result_is_on_a_tool_since_the_call()
    {
        var line = StatuslineRenderer.Render(
            Inputs(new StatuslineTimes(TurnStartedAt: At("13:58:00"), PendingToolSince: At("14:00:00"))),
            Plain());

        line.Should().Be("on a tool since 14:00");
    }

    [Fact]
    public void A_permission_prompt_is_named_only_when_one_was_asked()
    {
        var pending = new StatuslineTimes(
            TurnStartedAt: At("13:58:00"),
            PendingToolSince: At("14:00:00"),
            PermissionAt: At("14:00:30"));

        StatuslineRenderer.Render(Inputs(pending with { PermissionAsked = true }), Plain())
            .Should().Be("permission since 14:00");

        StatuslineRenderer.Render(Inputs(pending), Plain())
            .Should().Be("on a tool since 14:00");
    }

    [Fact]
    public void Background_work_is_since_the_turn_ended_not_since_it_was_launched()
    {
        var line = StatuslineRenderer.Render(
            Inputs(new StatuslineTimes(
                TurnStartedAt: At("13:40:00"),
                TurnEndedAt: At("13:50:00"),
                BackgroundOutstanding: true)),
            Plain());

        line.Should().Be("background since 13:50");
    }

    [Theory]
    [InlineData("14:29:00", "wakes 14:30")]
    [InlineData("14:31:59.999", "wakes 14:30")]
    [InlineData("14:32:00", "due 14:30")]
    [InlineData("14:40:00", "due 14:30")]
    public void A_wakeup_is_due_from_the_end_of_its_grace(string now, string expected)
    {
        var line = StatuslineRenderer.Render(
            Inputs(
                new StatuslineTimes(
                    TurnStartedAt: At("14:00:00"),
                    TurnEndedAt: At("14:01:00"),
                    WakeupDue: At("14:30:00")),
                now: At(now)),
            Plain());

        line.Should().Be(expected);
    }

    [Fact]
    public void Due_uses_the_shared_grace_constant()
    {
        var due = At("14:30:00");
        var times = new StatuslineTimes(TurnEndedAt: At("14:01:00"), WakeupDue: due);

        StatuslineRenderer.Render(Inputs(times, now: due + SessionSignals.WakeupGrace), Plain())
            .Should().Be("due 14:30");

        StatuslineRenderer.Render(
                Inputs(times, now: due + SessionSignals.WakeupGrace - TimeSpan.FromTicks(1)),
                Plain())
            .Should().Be("wakes 14:30");
    }

    [Theory]
    [InlineData(59, "<1m ago")]
    [InlineData(60, "1m ago")]
    [InlineData((59 * 60) + 59, "59m ago")]
    [InlineData(60 * 60, "1h00m ago")]
    [InlineData((2 * 60 * 60) + (5 * 60), "2h05m ago")]
    [InlineData(25 * 60 * 60, "1d ago")]
    public void Ages_are_written_briefly(int seconds, string expected)
    {
        TimeFormat.Relative(Now - TimeSpan.FromSeconds(seconds), Now).Should().Be(expected);
    }

    [Theory]
    [InlineData(45, "45s")]
    [InlineData(134, "2m14s")]
    [InlineData(4320, "1h12m")]
    public void Durations_are_written_briefly(int seconds, string expected)
    {
        TimeFormat.Duration(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }

    [Theory]
    [InlineData("14:05:00", true, "14:05")]
    [InlineData("14:05:00", false, "2:05pm")]
    [InlineData("00:00:00", false, "12:00am")]
    [InlineData("12:00:00", false, "12:00pm")]
    [InlineData("00:00:00", true, "00:00")]
    public void Clock_times_follow_the_hour_setting(string time, bool hour24, string expected)
    {
        TimeFormat.Clock(At(time), TimeZoneInfo.Utc, hour24).Should().Be(expected);
    }

    [Fact]
    public void The_clock_segment_follows_the_hour_setting()
    {
        var settings = new StatuslineSettings
        {
            Colour = false, ShowWaiting = false, ShowLastWrite = false, ShowClock = true,
        };

        StatuslineRenderer.Render(Inputs(Everything()), settings).Should().Be("14:05");

        settings.Time.Hour24 = false;

        StatuslineRenderer.Render(Inputs(Everything()), settings).Should().Be("2:05pm");
    }

    [Fact]
    public void Absolute_writes_a_clock_time_and_relative_an_age()
    {
        var settings = new StatuslineSettings { Colour = false, ShowWaiting = false };

        StatuslineRenderer.Render(Inputs(Everything()), settings).Should().Be("write 3m ago");

        settings.Time.Absolute = true;

        StatuslineRenderer.Render(Inputs(Everything()), settings).Should().Be("write 14:02");
    }

    [Fact]
    public void A_time_not_today_carries_its_day()
    {
        var settings = new StatuslineSettings
        {
            Colour = false, ShowWaiting = false, ShowLastWrite = false, ShowLastCommit = true,
        };
        settings.Time.Absolute = true;

        var line = StatuslineRenderer.Render(
            Inputs(new StatuslineTimes(
                LastCommitAt: DateTimeOffset.Parse("2026-10-21T11:40:00Z", CultureInfo.InvariantCulture))),
            settings);

        line.Should().Be("commit 21 Oct 11:40");
    }

    [Fact]
    public void Times_are_shown_in_the_zone_given()
    {
        var plusOne = TimeZoneInfo.CreateCustomTimeZone("plus-one", TimeSpan.FromHours(1), "plus-one", "plus-one");

        var line = StatuslineRenderer.Render(
            Inputs(new StatuslineTimes(TurnEndedAt: At("13:02:00")), zone: plusOne),
            Plain());

        line.Should().Be("waiting for you since 14:02");
    }

    [Fact]
    public void Under_redraw_never_nothing_ticks()
    {
        // The session length is what Claude reports, and it grows with the
        // clock; the moment the session started does not, and that is what
        // is shown here.
        var later = Now + TimeSpan.FromMinutes(7);
        var times = Everything();

        var first = StatuslineRenderer.Render(Inputs(times, fixedTimes: true), AllOn());
        var second = StatuslineRenderer.Render(
            Inputs(times with { SessionLength = times.SessionLength + TimeSpan.FromMinutes(7) },
                now: later,
                fixedTimes: true),
            AllOn());

        second.Should().Be(first);
        first.Should().NotContain("ago").And.NotContain("14:05").And.NotContain("session ");
        first.Should().Contain("started 12:53").And.Contain("write 14:02");
    }

    [Fact]
    public void Under_redraw_never_relative_is_forced_absolute()
    {
        var settings = new StatuslineSettings { Colour = false, ShowWaiting = false };

        settings.Time.Absolute.Should().BeFalse();

        StatuslineRenderer.Render(Inputs(Everything(), fixedTimes: true), settings)
            .Should().Be("write 14:02");
    }

    [Theory]
    [InlineData(9 * 60 + 59, "\u001b[2m")]
    [InlineData(10 * 60, "\u001b[33m")]
    [InlineData(30 * 60, "\u001b[31m")]
    public void Waiting_turns_amber_then_red_as_it_ages(int seconds, string colour)
    {
        var settings = new StatuslineSettings { Colour = true, ShowLastWrite = false };

        var line = StatuslineRenderer.Render(
            Inputs(new StatuslineTimes(TurnEndedAt: Now - TimeSpan.FromSeconds(seconds))),
            settings);

        line.Should().StartWith(colour + "waiting for you since");
    }

    [Fact]
    public void Due_is_always_the_alert_colour()
    {
        var settings = new StatuslineSettings { Colour = true, WaitingAfter = null };

        var line = StatuslineRenderer.Render(
            Inputs(new StatuslineTimes(TurnEndedAt: At("13:00:00"), WakeupDue: At("13:30:00"))),
            settings);

        line.Should().StartWith("\u001b[31mdue 13:30");
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("10")]
    [InlineData("10m,later")]
    [InlineData("1m,2m,3m")]
    [InlineData(",")]
    public void An_unreadable_threshold_changes_no_colour_and_throws_nothing(string threshold)
    {
        var settings = new StatuslineSettings { Colour = true, ShowLastWrite = false, WaitingAfter = threshold };

        var line = StatuslineRenderer.Render(
            Inputs(new StatuslineTimes(TurnEndedAt: Now - TimeSpan.FromHours(5))),
            settings);

        line.Should().StartWith("\u001b[2mwaiting for you since");
    }

    /// <summary>
    /// The width that fits everything except the clock and the session,
    /// measured rather than counted so the test does not hang on spacing.
    /// </summary>
    private static int WidthWithoutClockAndSession()
    {
        var settings = AllOn();
        settings.ShowClock = false;
        settings.ShowSession = false;

        return StatuslineRenderer.Render(Inputs(Everything(), spend: Spending()), settings).Length;
    }

    [Fact]
    public void Spend_stays_visible_when_times_do_not_fit()
    {
        var width = WidthWithoutClockAndSession();

        var line = StatuslineRenderer.Render(
            Inputs(Everything(), width: width, spend: Spending()), AllOn());

        line.Length.Should().BeLessThanOrEqualTo(width);
        line.Should().Contain("spend!");
        line.Should().Contain("waiting for you since");
        line.Should().Contain("turn 4m00s").And.Contain("commit").And.Contain("write");
        line.Should().NotContain("14:05").And.NotContain("session");
    }

    [Fact]
    public void Spend_is_never_dropped_however_narrow()
    {
        var line = StatuslineRenderer.Render(
            Inputs(Everything(), width: 5, spend: Spending()), AllOn());

        line.Should().Be("spend! (1)");
    }

    [Fact]
    public void With_no_width_nothing_is_dropped()
    {
        var line = StatuslineRenderer.Render(Inputs(Everything(), spend: Spending()), AllOn());

        line.Should().Be(
            "waiting for you since 14:02 | write 3m ago | 14:05 | session 1h12m | turn 4m00s | commit 3h00m ago | spend! (1)");
    }

    [Fact]
    public void Escape_codes_do_not_count_toward_width()
    {
        var width = WidthWithoutClockAndSession();

        var plain = StatuslineRenderer.Render(
            Inputs(Everything(), width: width, spend: Spending()), AllOn());

        var coloured = StatuslineRenderer.Render(
            Inputs(Everything(), width: width, spend: Spending()), AllOn(colour: true));

        coloured.Should().Contain("\u001b[");
        Strip(coloured).Should().Be(plain);
    }

    [Fact]
    public void The_line_never_contains_a_newline_with_every_time_segment_on()
    {
        var settings = AllOn();
        settings.Separator = " |\n ";

        var line = StatuslineRenderer.Render(
            Inputs(Everything() with { WakeupDue = At("14:30:00") }, spend: Spending()), settings);

        line.Should().Contain("wakes 14:30").And.Contain("commit");
        line.Should().NotContain("\n");
        line.Should().NotContain("\r");
    }
}
