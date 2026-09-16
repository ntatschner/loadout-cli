using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Configuration;
using Loadout.Core.Teams;
using Loadout.Models.Platform;
using Loadout.Models.Teams;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Runs that happen again and again, and when each is due.
/// </summary>
/// <remarks>
/// <para>
/// A schedule fires when nobody is watching, which decides most of what is
/// here: no manual autonomy, no interval shorter than a team run takes, and a
/// missed one is not made up for.
/// </para>
/// <para>
/// Machine-local, because a schedule in the workspace would travel to every
/// machine that clones it, and three machines waking at nine to run the same
/// sweep on the same repository is one useful run and two that fight it for
/// the branch.
/// </para>
/// </remarks>
public sealed class ScheduleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-sched-" + Guid.NewGuid().ToString("N"));
    private readonly IScheduleService _schedules;

    public ScheduleTests()
    {
        var environment = new FakeEnvironmentProvider(
            Path.Combine(_root, "home"),
            new Dictionary<string, string>
            {
                ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
            });

        IPlatformPaths paths = new LinuxPaths(
            environment,
            new NoOpFilePermissions(),
            new HostPlatform(HostOperatingSystem.Linux, System.Runtime.InteropServices.Architecture.X64, "test", "TEST"));

        paths.EnsureDirectoriesExist();

        _schedules = new ScheduleService(paths, new YamlStore(new NoOpFilePermissions()));
    }

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

    private static TeamSchedule Nightly() => new()
    {
        Id = "nightly",
        Project = "demo",
        Team = "iterating-project",
        Goal = "Run the suite and fix what fails.",
        Autonomy = "autonomous",
        At = new TimeOnly(3, 0),
    };

    [Fact]
    public async Task One_is_recorded_and_read_back()
    {
        (await _schedules.SaveAsync(Nightly())).Succeeded.Should().BeTrue();

        var listed = await _schedules.ListAsync();

        listed.Value!.Should().ContainSingle().Which.Goal.Should().Be("Run the suite and fix what fails.");
    }

    [Fact]
    public async Task The_same_name_replaces_rather_than_doubles()
    {
        // Somebody correcting a schedule types the same name again. Two of one
        // name is a run that happens twice and a removal that half works.
        await _schedules.SaveAsync(Nightly());

        var corrected = Nightly();
        corrected.Goal = "Different goal.";

        await _schedules.SaveAsync(corrected);

        var listed = await _schedules.ListAsync();

        listed.Value!.Should().ContainSingle().Which.Goal.Should().Be("Different goal.");
    }

    [Fact]
    public async Task A_schedule_cannot_be_manual()
    {
        // It fires at three in the morning. A run that stops at the first
        // question has spent a session to ask something nobody will read
        // until morning.
        var manual = Nightly();
        manual.Autonomy = "manual";

        var saved = await _schedules.SaveAsync(manual);

        saved.Failed.Should().BeTrue();
        saved.Error.Should().Contain("nobody is watching");
    }

    [Fact]
    public async Task A_schedule_needs_a_when()
    {
        var whenever = Nightly();
        whenever.At = null;

        (await _schedules.SaveAsync(whenever)).Error.Should().Contain("how often");
    }

    [Fact]
    public async Task Nothing_may_run_more_often_than_a_run_takes()
    {
        var often = Nightly();
        often.At = null;
        often.Every = TimeSpan.FromSeconds(30);

        (await _schedules.SaveAsync(often)).Error.Should().Contain("five minutes");
    }

    [Fact]
    public async Task Removing_one_that_is_not_there_says_so()
    {
        (await _schedules.RemoveAsync("nothing")).Error.Should().Contain("no schedule called");
    }

    [Fact]
    public async Task Starting_one_records_which_run_it_was()
    {
        await _schedules.SaveAsync(Nightly());

        var when = DateTimeOffset.UtcNow;

        (await _schedules.StartedAsync("nightly", when, "20260916-0300-aaaa")).Succeeded.Should().BeTrue();

        var listed = await _schedules.ListAsync();

        listed.Value![0].LastRunId.Should().Be("20260916-0300-aaaa");
        listed.Value![0].LastRun.Should().BeCloseTo(when, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void An_interval_comes_round_when_the_interval_has_passed()
    {
        var every = Nightly();
        every.At = null;
        every.Every = TimeSpan.FromHours(2);

        var now = DateTimeOffset.UtcNow;

        ScheduleService.IsDue(every, now).Should().BeTrue("it has never run");

        every.LastRun = now.AddMinutes(-30);
        ScheduleService.IsDue(every, now).Should().BeFalse();

        every.LastRun = now.AddHours(-3);
        ScheduleService.IsDue(every, now).Should().BeTrue();
    }

    [Fact]
    public void A_daily_one_is_due_in_the_hour_after_its_time_and_not_before()
    {
        var daily = Nightly();
        var today = DateTimeOffset.Now.Date;

        ScheduleService.IsDue(daily, new DateTimeOffset(today.AddHours(2), DateTimeOffset.Now.Offset))
            .Should().BeFalse("it is an hour early");

        ScheduleService.IsDue(daily, new DateTimeOffset(today.AddHours(3).AddMinutes(20), DateTimeOffset.Now.Offset))
            .Should().BeTrue();
    }

    [Fact]
    public void A_missed_one_is_not_made_up_for()
    {
        // Waking a machine at noon and firing the three o'clock run is a run
        // nobody is expecting against a repository that has moved on, and
        // doing it for every day the machine was off is worse.
        var daily = Nightly();
        var noon = new DateTimeOffset(DateTimeOffset.Now.Date.AddHours(12), DateTimeOffset.Now.Offset);

        ScheduleService.IsDue(daily, noon).Should().BeFalse();
    }

    [Fact]
    public void One_that_already_ran_today_waits_for_tomorrow()
    {
        var daily = Nightly();
        var today = DateTimeOffset.Now.Date;
        var justAfter = new DateTimeOffset(today.AddHours(3).AddMinutes(10), DateTimeOffset.Now.Offset);

        daily.LastRun = justAfter.AddMinutes(-5);

        ScheduleService.IsDue(daily, justAfter).Should().BeFalse();
        ScheduleService.Next(daily, justAfter)!.Value.Date.Should().Be(today.AddDays(1));
    }

    [Fact]
    public void A_paused_one_never_comes_round()
    {
        var daily = Nightly();
        daily.Enabled = false;

        var due = new DateTimeOffset(DateTimeOffset.Now.Date.AddHours(3).AddMinutes(10), DateTimeOffset.Now.Offset);

        ScheduleService.IsDue(daily, due).Should().BeFalse();
        ScheduleService.Next(daily, due).Should().BeNull();
    }

    [Theory]
    [InlineData("30m", 30)]
    [InlineData("2h", 120)]
    [InlineData("1d", 1440)]
    [InlineData("1.5h", 90)]
    public void A_duration_is_read_the_way_people_write_one(string given, int minutes)
    {
        TeamScheduleAddCommand.Duration(given).Should().Be(TimeSpan.FromMinutes(minutes));
    }

    [Theory]
    [InlineData("")]
    [InlineData("h")]
    [InlineData("soon")]
    [InlineData("-2h")]
    [InlineData("2w")]
    public void A_duration_nobody_can_read_is_refused(string given)
    {
        TeamScheduleAddCommand.Duration(given).Should().BeNull();
    }
}
