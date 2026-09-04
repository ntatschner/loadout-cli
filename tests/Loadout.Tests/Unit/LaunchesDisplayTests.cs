using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Models.Agents;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// How a recorded launch is described back to somebody.
/// </summary>
/// <remarks>
/// Two things here carry a judgement rather than a format. A launch has three
/// outcomes and not two, and collapsing them would invent a result none of them
/// has; and a task that was withheld has to read as withheld rather than as
/// absent, or the report quietly loses the difference between a session nobody
/// described and one whose description was refused.
/// </remarks>
public sealed class LaunchesDisplayTests
{
    [Fact]
    public void A_launch_that_ended_well_says_so()
    {
        LaunchesCommand.Outcome(Record(ended: true, exitCode: 0)).Should().Be("ok");
    }

    [Fact]
    public void A_launch_that_ended_badly_carries_its_code()
    {
        LaunchesCommand.Outcome(Record(ended: true, exitCode: 2)).Should().Be("exit 2");
    }

    [Fact]
    public void A_launch_with_no_ending_is_not_called_a_failure()
    {
        // Killed, or the terminal closed, or still going. None of those is a
        // failure, and the record cannot tell them apart — the running-session
        // registry is what does.
        LaunchesCommand.Outcome(Record(ended: false, exitCode: null)).Should().Be("unclosed");
    }

    [Fact]
    public void A_launch_whose_agent_never_ran_is_told_apart_from_one_still_open()
    {
        // Both have no exit code. Only the ending separates them, and calling
        // either "failed" would be a result neither has.
        LaunchesCommand.Outcome(Record(ended: true, exitCode: null)).Should().Be("never ran");
    }

    [Fact]
    public void A_task_that_was_withheld_reads_as_withheld_rather_than_missing()
    {
        var label = LaunchesCommand.Label(
            Record(ended: true, exitCode: 0) with { Task = null, TaskWithheld = "GitHub token" });

        label.Should().Contain("withheld").And.Contain("GitHub token");
    }

    [Fact]
    public void A_launch_nobody_described_says_that_instead()
    {
        LaunchesCommand.Label(Record(ended: true, exitCode: 0) with { Task = null })
            .Should().Be("(no task given)");
    }

    [Fact]
    public void A_task_that_was_recorded_is_shown_as_written()
    {
        LaunchesCommand.Label(Record(ended: true, exitCode: 0)).Should().Be("fix the upload path");
    }

    private static LaunchRecord Record(bool ended, int? exitCode)
    {
        var started = new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);

        return new LaunchRecord(
            "aa11",
            started,
            "starstats",
            "StarStats",
            "claude",
            "implement",
            "fix the upload path",
            null,
            null,
            null,
            ["foundation.change-safety"],
            2400,
            12000,
            ended ? started.AddMinutes(31) : null,
            exitCode);
    }
}
