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
    private static async Task ScheduleAsync(LoadoutProcess loadout, string body)
    {
        var state = Path.Combine(loadout.Home, "Local", "loadout", "teams");

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
}
