using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// When a team's queue window is open, and that a window never fires as a run.
/// </summary>
/// <remarks>
/// A window is in this machine's own time, as a daily schedule is, so the
/// moments here are made in local time from today's date, with the offset that
/// applies at each one.
/// </remarks>
public sealed class QueueWindowTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();

    public void Dispose() => _space.Dispose();

    private static DateTimeOffset Local(int day, int hour, int minute = 0)
    {
        var at = DateTime.Today.AddDays(day).AddHours(hour).AddMinutes(minute);

        return new DateTimeOffset(at, TimeZoneInfo.Local.GetUtcOffset(at));
    }

    private static TeamSchedule Window(int hour = 22, double hours = 8) => new()
    {
        Id = TeamQueueWindowCommand.Name("builders"),
        Team = "builders",
        Kind = TeamSchedule.QueueKind,
        At = new TimeOnly(hour, 0),
        Length = TimeSpan.FromHours(hours),
        Autonomy = "supervised",
    };

    [Fact]
    public void A_window_is_open_from_its_start_for_its_length()
    {
        ScheduleService.Window(Window(), Local(0, 21, 59)).Should().BeNull();
        ScheduleService.Window(Window(), Local(0, 22)).Should().Be(Local(0, 22));
        ScheduleService.Window(Window(), Local(0, 23, 30)).Should().Be(Local(0, 22));
    }

    [Fact]
    public void A_window_that_crosses_midnight_is_open_the_next_morning_from_the_night_before()
    {
        ScheduleService.Window(Window(), Local(1, 3)).Should().Be(Local(0, 22));
        ScheduleService.Window(Window(), Local(1, 6)).Should().BeNull();
    }

    [Fact]
    public void A_paused_window_is_never_open()
    {
        var paused = Window();
        paused.Enabled = false;

        ScheduleService.Window(paused, Local(0, 23)).Should().BeNull();
    }

    [Fact]
    public void A_window_is_never_due_as_a_run()
    {
        // Due would start 'team run' with an empty goal on an empty project.
        ScheduleService.IsDue(Window(), Local(0, 22, 5)).Should().BeFalse();
    }

    [Fact]
    public void Its_next_opening_is_now_while_open_and_its_next_start_otherwise()
    {
        ScheduleService.Next(Window(), Local(0, 23)).Should().Be(Local(0, 22));
        ScheduleService.Next(Window(), Local(0, 12)).Should().Be(Local(0, 22));
        ScheduleService.Next(Window(hour: 9, hours: 1), Local(0, 12)).Should().Be(Local(1, 9));
    }

    [Fact]
    public async Task A_window_is_recorded_without_a_project_or_a_goal()
    {
        var schedules = new ScheduleService(_space.Paths, _space.Yaml);

        (await schedules.SaveAsync(Window())).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(0.0, "between five minutes and a day")]
    [InlineData(25.0, "between five minutes and a day")]
    public async Task A_window_too_short_or_too_long_is_refused(double hours, string said)
    {
        var schedules = new ScheduleService(_space.Paths, _space.Yaml);

        (await schedules.SaveAsync(Window(hours: hours))).Error.Should().Contain(said);
    }

    [Fact]
    public async Task A_manual_window_is_refused()
    {
        var schedules = new ScheduleService(_space.Paths, _space.Yaml);
        var manual = Window();
        manual.Autonomy = "manual";

        (await schedules.SaveAsync(manual)).Error.Should().Contain("cannot be manual");
    }

    [Fact]
    public async Task A_kind_nobody_knows_is_refused()
    {
        var schedules = new ScheduleService(_space.Paths, _space.Yaml);

        (await schedules.SaveAsync(new TeamSchedule
        {
            Id = "odd", Project = "website", Team = "builders", Goal = "x", Every = TimeSpan.FromHours(1), Kind = "sometimes",
        })).Error.Should().Contain("is not a kind of schedule");
    }
}
