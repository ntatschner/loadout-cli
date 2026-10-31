using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// The requires link from outside the process: a team queue passes over a task
/// whose requirement is not done, naming it, takes it once it is, and holds
/// nothing with the guard turned off.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class TaskRequiresContractTests
{
    private const string Team = "iterating-project";

    [BuiltCliFact]
    public async Task A_queue_holds_a_task_until_what_it_requires_is_done_and_the_guard_can_be_turned_off()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");

        (await loadout.RunAsync("task", "declare", "ci", "open", "--title", "Add CI", "--done-when", "a workflow runs", "--project", project))
            .ExitCode.Should().Be(0);
        var linked = await loadout.RunAsync(
            "task", "declare", "notes", "open", "--title", "Write the notes", "--done-when", "they read well",
            "--requires", "ci", "--project", project);
        linked.ExitCode.Should().Be(0, linked.StandardError);

        (await loadout.RunAsync("team", "queue", "add", Team, "notes", "--project", project)).ExitCode.Should().Be(0);

        var held = (await loadout.RunAsync("team", "queue", "plan", Team, "--json")).Json();

        held.GetProperty("order").GetArrayLength().Should().Be(0);
        held.GetProperty("left").EnumerateArray().Single().GetProperty("reason").GetString()
            .Should().Be("'notes' waits for ci, which is open.");

        var off = await loadout.RunAsync("config", "set", "requires-guard", "off");
        off.ExitCode.Should().Be(0, off.StandardError);

        Order(await loadout.RunAsync("team", "queue", "plan", Team, "--json")).Should().Equal("notes");

        (await loadout.RunAsync("config", "set", "requires-guard", "on")).ExitCode.Should().Be(0);
        (await loadout.RunAsync("task", "declare", "ci", "done", "--project", project)).ExitCode.Should().Be(0);

        Order(await loadout.RunAsync("team", "queue", "plan", Team, "--json")).Should().Equal("notes");
    }

    [BuiltCliFact]
    public async Task Both_ends_of_a_link_are_shown_and_dropping_the_one_waited_for_says_what_still_waits()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");

        (await loadout.RunAsync("task", "declare", "ci", "open", "--title", "Add CI", "--project", project)).ExitCode.Should().Be(0);
        (await loadout.RunAsync("task", "declare", "notes", "open", "--title", "Notes", "--project", project)).ExitCode.Should().Be(0);
        (await loadout.RunAsync("task", "requires", "notes", "ci", "--project", project)).ExitCode.Should().Be(0);

        var waiting = (await loadout.RunAsync("task", "requires", "notes", "--project", project, "--json")).Json();
        waiting.GetProperty("held").GetBoolean().Should().BeTrue();
        waiting.GetProperty("requires").EnumerateArray().Single().GetProperty("task").GetString().Should().Be($"{project}/ci");

        var target = (await loadout.RunAsync("task", "requires", "ci", "--project", project, "--json")).Json();
        target.GetProperty("requiredBy").EnumerateArray().Select(one => one.GetString()).Should().Equal($"{project}/notes");

        var dropped = await loadout.RunAsync("task", "declare", "ci", "dropped", "--project", project);

        dropped.ExitCode.Should().Be(0, dropped.StandardError);
        string.Join(' ', dropped.StandardOutput.Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .Should().Contain("notes requires ci, and will keep waiting");

        var loop = await loadout.RunAsync("task", "requires", "ci", "notes", "--project", project);

        loop.ExitCode.Should().NotBe(0);
        loop.StandardError.Should().Contain("loop");
    }

    [BuiltCliFact]
    public async Task A_run_started_for_a_held_task_with_nobody_to_ask_is_refused_and_says_how_to_start_it_anyway()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");

        (await loadout.RunAsync("task", "declare", "ci", "open", "--title", "Add CI", "--project", project)).ExitCode.Should().Be(0);
        (await loadout.RunAsync(
                "task", "declare", "notes", "open", "--title", "Notes", "--done-when", "they read well", "--requires", "ci", "--project", project))
            .ExitCode.Should().Be(0);

        var run = await loadout.RunAsync(
            "team", "run", Team, "Write the notes", "--project", project, "--task", "notes", "--non-interactive");

        run.ExitCode.Should().NotBe(0);
        string.Join(' ', run.StandardError.Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .Should().Contain("'notes' waits for ci, which is open").And.Contain("--despite-requires");
    }

    private static IEnumerable<string?> Order(LoadoutRun plan) =>
        plan.Json().GetProperty("order").EnumerateArray().Select(one => one.GetProperty("task").GetString());
}
