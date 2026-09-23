using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// The resident process, from the built command line.
/// </summary>
/// <remarks>
/// <para>
/// What matters is the two things it promises: it fires what is due, and it
/// stops when it is told to. Both are about a process nobody is watching, and
/// both are the kind of thing that works in a unit test and not in a binary.
/// </para>
/// <para>
/// It is never asked to fire a real run here. That would launch an agent and
/// spend money on a test machine, so what is asserted is what it says it would
/// start - the same list the loop walks.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class DaemonContractTests
{
    /// <summary>
    /// Writes a schedule straight into the throwaway home's own file.
    /// </summary>
    /// <remarks>
    /// A throwaway home has no project, so the command that writes one cannot
    /// succeed there. What is under test is the daemon reading the file and
    /// working out what is due, and the file is the contract between them.
    /// </remarks>
    /// <summary>Where this run keeps its state, according to the run itself.</summary>
    /// <remarks>
    /// Asked rather than worked out here. This used to build
    /// <c>&lt;home&gt;/Local/loadout</c>, which is the Windows layout and only
    /// the Windows layout: on Linux and macOS state sits under XDG_DATA_HOME,
    /// so every schedule these tests wrote landed where nothing would read it
    /// and the daemon saw none at all.
    ///
    /// One test failed on that, the first time the branch was ever run on
    /// Linux. The other asserts that nothing is due and passed - for entirely
    /// the wrong reason, which is the worse of the two outcomes and the one
    /// that would have gone on hiding this.
    /// </remarks>
    private static async Task<string> StateAsync(LoadoutProcess loadout)
    {
        var doctor = await loadout.RunAsync("doctor", "--json");

        return doctor.Json()
            .GetProperty("checks")
            .EnumerateArray()
            .First(check => check.GetProperty("name").GetString() == "State")
            .GetProperty("detail")
            .GetString()!;
    }

    private static async Task ScheduleAsync(LoadoutProcess loadout, string body)
    {
        var state = Path.Combine(await StateAsync(loadout), "teams");

        Directory.CreateDirectory(state);

        await File.WriteAllTextAsync(Path.Combine(state, "schedules.yaml"), body);
    }

    private static string Never() =>
        "schema_version: 1\n"
        + "items:\n"
        + "- id: nightly\n"
        + "  project: demo\n"
        + "  team: iterating-project\n"
        + "  goal: Run the suite.\n"
        + "  autonomy: autonomous\n"
        + "  every: 01:00:00\n"
        + "  enabled: true\n";

    private static string JustRan() =>
        "schema_version: 1\n"
        + "items:\n"
        + "- id: nightly\n"
        + "  project: demo\n"
        + "  team: iterating-project\n"
        + "  goal: Run the suite.\n"
        + "  autonomy: autonomous\n"
        + "  every: 01:00:00\n"
        + $"  last_run: {DateTimeOffset.UtcNow.AddMinutes(-1):O}\n"
        + "  enabled: true\n";

    [BuiltCliFact]
    public async Task It_says_what_it_would_start_and_starts_nothing()
    {
        using var loadout = new LoadoutProcess();

        await ScheduleAsync(loadout, Never());

        var preview = await loadout.RunAsync("team", "daemon", "--dry-run");

        preview.ExitCode.Should().Be(0);
        preview.StandardOutput.Should().Contain("nothing was started or served");

        // Due, because it has never run, and named so a person reading the
        // preview knows which one it means.
        preview.StandardOutput.Should().Contain("1 schedule(s) would start now");
        preview.StandardOutput.Should().Contain("nightly");
    }

    [BuiltCliFact]
    public async Task One_that_is_not_due_is_not_offered()
    {
        using var loadout = new LoadoutProcess();

        await ScheduleAsync(loadout, JustRan());

        var preview = await loadout.RunAsync("team", "daemon", "--dry-run");

        preview.StandardOutput.Should().Contain("0 schedule(s) would start now");
    }

    [BuiltCliFact]
    public async Task A_schedule_is_read_back_with_when_it_is_next_due()
    {
        using var loadout = new LoadoutProcess();

        await ScheduleAsync(loadout, Never());

        var run = await loadout.RunAsync("team", "schedule", "list", "--json");

        run.ExitCode.Should().Be(0);

        var schedule = run.Json().GetProperty("schedules")[0];

        schedule.GetProperty("id").GetString().Should().Be("nightly");
        schedule.GetProperty("team").GetString().Should().Be("iterating-project");
        schedule.GetProperty("due").GetBoolean().Should().BeTrue("it has never run");
    }

    [BuiltCliFact]
    public async Task Nothing_is_scheduled_on_a_machine_that_has_scheduled_nothing()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("team", "schedule", "list", "--json");

        run.ExitCode.Should().Be(0);
        run.Json().GetProperty("schedules").GetArrayLength().Should().Be(0);
    }

    [BuiltCliFact]
    public async Task A_duration_nobody_can_read_is_refused()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync(
            "team", "schedule", "add", "nightly", "iterating-project", "Do a thing.",
            "--every", "soon", "--project", "demo");

        run.ExitCode.Should().NotBe(0);
    }

    [BuiltCliFact]
    public async Task The_schedule_commands_answer_in_json_like_everything_else()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("team", "schedule", "list", "--json");

        JsonDocument.Parse(run.StandardOutput).RootElement
            .TryGetProperty("schedules", out _).Should().BeTrue();
    }

    /// <summary>The address a daemon in these tests claims to be serving on.</summary>
    /// <remarks>
    /// Nothing listens on it. What is under test is which address the command
    /// answers with, and a port that is genuinely open would make the test pass
    /// for a reason it is not asserting.
    /// </remarks>
    private const string Claimed = "http://127.0.0.1:65123/?token=0123456789abcdef";

    /// <summary>
    /// Writes the note a daemon leaves about itself, straight into the
    /// throwaway home.
    /// </summary>
    /// <remarks>
    /// The process it names is this test run, because "is it still there" is
    /// answered by asking the operating system and a made-up identifier would
    /// be answered differently on a machine that had reused it. Moving the
    /// start time by an hour is the stale case exactly: the identifier is live,
    /// the process bearing it is not the one that wrote the note, and that is
    /// the reuse the start time exists to catch.
    /// </remarks>
    private static async Task DaemonNoteAsync(LoadoutProcess loadout, bool live)
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();

        var started = self.StartTime.ToUniversalTime();

        var state = Path.Combine(await StateAsync(loadout), "teams");

        Directory.CreateDirectory(state);

        await File.WriteAllTextAsync(
            Path.Combine(state, "daemon.json"),
            JsonSerializer.Serialize(new
            {
                Pid = Environment.ProcessId,
                StartedAt = live ? started : started.AddHours(-1),
                Address = Claimed,
                Since = DateTimeOffset.UtcNow.AddMinutes(-5),
            }));
    }

    /// <remarks>
    /// The reason this is a contract rather than a nicety: the daemon prints
    /// its address once, and at login it prints it into a window nobody is
    /// looking at. This command was the only other thing anybody would type,
    /// and it used to start a second server - so somebody who enabled the
    /// daemon was handed a different page and never found the one they had
    /// switched on.
    /// </remarks>
    [BuiltCliFact]
    public async Task The_dashboard_hands_back_the_address_of_a_daemon_that_is_serving()
    {
        using var loadout = new LoadoutProcess();

        await DaemonNoteAsync(loadout, live: true);

        var run = await loadout.RunAsync("team", "dashboard", "--json");

        run.ExitCode.Should().Be(0);

        var answer = run.Json();

        answer.GetProperty("address").GetString().Should().Be(
            Claimed,
            "the page somebody meant is the one already being served");

        answer.GetProperty("port").GetInt32().Should().Be(65123);
    }

    [BuiltCliFact]
    public async Task A_note_left_behind_by_a_daemon_that_has_gone_is_not_pointed_at()
    {
        using var loadout = new LoadoutProcess();

        await DaemonNoteAsync(loadout, live: false);

        var run = await loadout.RunAsync("team", "dashboard", "--dry-run");

        run.ExitCode.Should().Be(0);
        run.StandardOutput.Should().Contain(
            "A dashboard would listen on",
            "a closed port is not a page, so this serves its own");

        run.StandardOutput.Should().NotContain("A daemon is already serving");
    }

    /// <remarks>
    /// Found running on the development machine: one daemon started at login
    /// and a second from a terminal. The second overwrote the note and the
    /// first carried on holding 140 MB that nothing knew about - and both
    /// would have fired every schedule.
    /// </remarks>
    [BuiltCliFact]
    public async Task A_second_daemon_is_refused_while_the_first_is_running()
    {
        using var loadout = new LoadoutProcess();

        await DaemonNoteAsync(loadout, live: true);

        // Bounded, because the failure this guards against is a daemon that
        // starts and never returns, and that would hold the whole suite.
        using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var run = loadout.RunAsync("team", "daemon", "--no-dashboard");

        var finished = await Task.WhenAny(run, Task.Delay(Timeout.Infinite, patience.Token));

        finished.Should().Be((Task)run, "a second daemon should refuse at once, not start");

        var said = await run;

        said.ExitCode.Should().NotBe(0);
        (said.StandardOutput + said.StandardError).Should().Contain("already running");
    }

    [BuiltCliTheory]
    [InlineData("stop")]
    [InlineData("restart")]
    [InlineData("pause")]
    public async Task Controlling_a_daemon_that_is_not_there_says_so(string verb)
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("team", "daemon", verb);

        run.ExitCode.Should().NotBe(0, "nothing was controlled");
        (run.StandardOutput + run.StandardError).Should().Contain("No daemon is running");
    }

    [BuiltCliFact]
    public async Task Pausing_in_a_dry_run_writes_nothing()
    {
        using var loadout = new LoadoutProcess();

        await DaemonNoteAsync(loadout, live: true);

        var run = await loadout.RunAsync("team", "daemon", "pause", "--dry-run");

        run.ExitCode.Should().Be(0);
        run.StandardOutput.Should().Contain("Would pause");

        File.Exists(Path.Combine(await StateAsync(loadout), "teams", "daemon-pause"))
            .Should().BeFalse("a dry run changes nothing");
    }

    [BuiltCliFact]
    public async Task A_pause_is_written_where_the_daemon_looks_and_lifted_by_continue()
    {
        using var loadout = new LoadoutProcess();

        await DaemonNoteAsync(loadout, live: true);

        var hold = Path.Combine(await StateAsync(loadout), "teams", "daemon-pause");

        (await loadout.RunAsync("team", "daemon", "pause")).ExitCode.Should().Be(0);

        File.Exists(hold).Should().BeTrue();

        // "continue" as well as "resume": the word people reach for after
        // "pause" is either, and one of them being an error is a trap.
        var resumed = await loadout.RunAsync("team", "daemon", "continue");

        resumed.ExitCode.Should().Be(0);
        resumed.StandardOutput.Should().Contain("Resumed");
        File.Exists(hold).Should().BeFalse();
    }

    /// <remarks>
    /// A hold outlives the daemon on purpose, so resume has to be able to lift
    /// one with no daemon there - or the next daemon starts held for a reason
    /// nobody remembers.
    /// </remarks>
    [BuiltCliFact]
    public async Task Resume_lifts_a_hold_left_by_a_daemon_that_has_gone()
    {
        using var loadout = new LoadoutProcess();

        var teams = Path.Combine(await StateAsync(loadout), "teams");

        Directory.CreateDirectory(teams);
        await File.WriteAllTextAsync(Path.Combine(teams, "daemon-pause"), "pause");

        var run = await loadout.RunAsync("team", "daemon", "resume");

        run.ExitCode.Should().Be(0);
        run.StandardOutput.Should().Contain("hold it left is lifted");
        File.Exists(Path.Combine(teams, "daemon-pause")).Should().BeFalse();
    }

    [BuiltCliFact]
    public async Task Asking_for_a_page_that_cannot_touch_anything_still_serves_one()
    {
        using var loadout = new LoadoutProcess();

        await DaemonNoteAsync(loadout, live: true);

        var run = await loadout.RunAsync("team", "dashboard", "--watch-only", "--dry-run");

        run.ExitCode.Should().Be(0);
        run.StandardOutput.Should().Contain(
            "A dashboard would listen on",
            "the daemon's page can answer gates and stop runs, which is what --watch-only "
            + "is asking for a page without");
    }
}
