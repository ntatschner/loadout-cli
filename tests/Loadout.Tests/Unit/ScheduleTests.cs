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

    [Fact]
    public async Task A_schedule_can_wait_for_something_instead_of_a_clock()
    {
        var watching = Nightly();
        watching.At = null;
        watching.On = "commit";

        (await _schedules.SaveAsync(watching)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task An_event_nobody_knows_is_refused_with_the_ones_that_are_known()
    {
        var watching = Nightly();
        watching.At = null;
        watching.On = "sunrise";

        var saved = await _schedules.SaveAsync(watching);

        saved.Failed.Should().BeTrue();
        saved.Error.Should().Contain("commit", "the refusal has to say what it does know");
    }

    [Fact]
    public void An_event_is_not_a_clock()
    {
        // Whether a repository has moved is a question for whoever holds a git
        // manager. This only knows about time, and says so by never calling an
        // event due.
        var watching = Nightly();
        watching.At = null;
        watching.On = "commit";

        ScheduleService.IsDue(watching, DateTimeOffset.UtcNow).Should().BeFalse();
        ScheduleService.Next(watching, DateTimeOffset.UtcNow).Should().BeNull();
    }

    [Fact]
    public async Task What_a_watching_schedule_has_seen_is_written_down()
    {
        var watching = Nightly();
        watching.At = null;
        watching.On = "commit";

        await _schedules.SaveAsync(watching);

        (await _schedules.SawAsync("nightly", "abc1234")).Succeeded.Should().BeTrue();

        var listed = await _schedules.ListAsync();

        listed.Value![0].LastCommit.Should().Be("abc1234");
    }

    [Fact]
    public void The_first_look_at_a_repository_never_fires()
    {
        // Writing down a trigger and having it go off immediately, against
        // whatever happened to be checked out, is not what anybody means by
        // "when the repository moves" - and on a machine with several
        // projects it would start every one of them at once.
        var watching = Nightly();
        watching.At = null;
        watching.On = "commit";

        ScheduleService.Moved(watching, "abc1234").Should().BeFalse();

        watching.LastCommit = "abc1234";

        ScheduleService.Moved(watching, "abc1234").Should().BeFalse("it has not moved");
        ScheduleService.Moved(watching, "def5678").Should().BeTrue();
    }

    [Fact]
    public async Task A_watcher_does_not_start_itself_over_what_its_own_run_did()
    {
        /*
          The sequence the daemon follows, written out, because this is where
          the rule lives and the bug was in the order rather than in any one
          step.

          A run merges its worker's branch into the checked-out branch, which
          moves the head. The head was written down before the run and never
          after, so a minute later the watcher saw a repository that had moved -
          because of the run - and started another, which merged, which moved it
          again. Unattended that is a loop costing a team run a minute, and
          nothing in it would ever have said why.
        */
        var watching = Nightly();
        watching.At = null;
        watching.On = "commit";

        await _schedules.SaveAsync(watching);

        // The first look writes down where the repository is, and fires nothing.
        await _schedules.SawAsync(watching.Id, "aaa1111");

        watching = (await _schedules.ListAsync()).Value!.Single();
        ScheduleService.Moved(watching, "aaa1111").Should().BeFalse();

        // Somebody commits. That is a reason to run.
        ScheduleService.Moved(watching, "bbb2222").Should().BeTrue();

        await _schedules.SawAsync(watching.Id, "bbb2222");

        // The run happens and merges, so the head moves again - this time
        // because of the run. Taking the repository as seen afterwards is what
        // makes that not a reason to run again.
        await _schedules.SawAsync(watching.Id, "ccc3333");

        watching = (await _schedules.ListAsync()).Value!.Single();

        ScheduleService.Moved(watching, "ccc3333")
            .Should().BeFalse("the run moved it, and a run is not a reason to run");

        // And the next real commit still is.
        ScheduleService.Moved(watching, "ddd4444").Should().BeTrue();
    }

    [Fact]
    public void A_repository_nobody_could_read_does_not_fire_anything()
    {
        var watching = Nightly();
        watching.At = null;
        watching.On = "commit";
        watching.LastCommit = "abc1234";

        ScheduleService.Moved(watching, null).Should().BeFalse();
        ScheduleService.Moved(watching, string.Empty).Should().BeFalse();
    }

    [Fact]
    public void A_paused_watcher_watches_nothing()
    {
        var watching = Nightly();
        watching.At = null;
        watching.On = "commit";
        watching.LastCommit = "abc1234";
        watching.Enabled = false;

        ScheduleService.Moved(watching, "def5678").Should().BeFalse();
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
