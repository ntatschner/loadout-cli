using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>The run-finished schedule event: when another team's run ending starts one.</summary>
public sealed class RunFinishedScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Run_finished_fires_once_per_finished_run_and_never_for_tool_works()
    {
        var schedule = Watching(seen: "20260901-0000-0000");
        var runs = new List<RunSummary> { Finished("20260920-1000-a001", "tool-works") };

        ScheduleService.RunFinished(schedule, runs, Now).Fire.Should().BeFalse("a tool-works run never starts one");

        runs.Add(Finished("20260921-1000-b001", "alpha"));
        var (fire, seen) = ScheduleService.RunFinished(schedule, runs, Now);

        fire.Should().BeTrue();
        seen.Should().Be("20260921-1000-b001");

        schedule.LastCommit = seen!;
        schedule.LastRun = Now;

        ScheduleService.RunFinished(schedule, runs, Now.AddHours(3)).Fire.Should().BeFalse("that run has been seen");
    }

    [Fact]
    public void First_look_records_a_baseline_without_firing()
    {
        var (fire, seen) = ScheduleService.RunFinished(
            Watching(seen: string.Empty), [Finished("20260921-1000-b001", "alpha")], Now);

        fire.Should().BeFalse();
        seen.Should().Be("20260921-1000-b001");
    }

    [Fact]
    public void Debounced_to_once_an_hour()
    {
        var schedule = Watching(seen: "20260901-0000-0000");
        schedule.LastRun = Now.AddMinutes(-30);
        var runs = new[] { Finished("20260921-1000-b001", "alpha") };

        var inside = ScheduleService.RunFinished(schedule, runs, Now);
        inside.Fire.Should().BeFalse();
        inside.Seen.Should().BeNull("a run held back by the hour is not written off");

        ScheduleService.RunFinished(schedule, runs, Now.AddMinutes(31)).Fire.Should().BeTrue();
    }

    private static TeamSchedule Watching(string seen) => new()
    {
        Id = "tools-on-finish",
        // Not tool-works itself, so the own-team rule cannot stand in for the tool-works one.
        Team = "tidy-up",
        Project = "demo",
        Goal = "Look at what finished.",
        On = ScheduleService.RunFinishedEvent,
        Enabled = true,
        LastCommit = seen,
    };

    private static RunSummary Finished(string runId, string team) => new(
        runId, string.Empty, team, "goal", "autonomous",
        Now.AddHours(-5), Now.AddHours(-4), "done", 0m, 1, [], [], []);
}
