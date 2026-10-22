using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// The trigger commands, from the built command line, with no daemon running.
/// </summary>
/// <remarks>
/// No daemon is started here, so every change takes the path a machine with
/// none running takes: written to the file, journalled, and said to take
/// effect when one starts. What a daemon does with the same changes is
/// covered where a daemon can be driven without a process of its own.
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class TriggerContractTests
{
    [BuiltCliFact]
    public async Task With_the_switch_off_every_trigger_command_refuses_and_says_how_to_turn_it_on()
    {
        using var loadout = new LoadoutProcess();
        loadout.Environment["LOADOUT_TRIGGERS"] = "0";

        var listed = await loadout.RunAsync("trigger", "list");

        listed.ExitCode.Should().Be(2, listed.StandardError);
        listed.StandardError.Should().Contain("LOADOUT_TRIGGERS").And.Contain("loadout daemon stop");
    }

    [BuiltCliFact]
    public async Task A_rule_added_with_no_daemon_is_kept_journalled_listed_tried_and_removed()
    {
        using var loadout = new LoadoutProcess();
        loadout.Environment["LOADOUT_TRIGGERS"] = "1";

        var added = await loadout.RunAsync(
            "trigger", "add", "audit-failures", "run-finished", "--builtin", "docs audit", "--where", "outcome=failed");

        added.ExitCode.Should().Be(0, added.StandardError);
        added.StandardOutput.Should().Contain("version 1").And.Contain("when one starts");

        var listed = await loadout.RunAsync("trigger", "list", "--json");

        listed.ExitCode.Should().Be(0, listed.StandardError);
        listed.Json().EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("rule").GetProperty("id").GetString().Should().Be("audit-failures");

        var journal = await loadout.RunAsync("trigger", "journal", "--json");

        journal.Json().EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("kind").GetString().Should().Be("edited");

        var tried = await loadout.RunAsync("trigger", "test", "audit-failures");

        tried.ExitCode.Should().Be(0, tried.StandardError);
        tried.StandardOutput.Should().Contain("No run-finished events");

        var removed = await loadout.RunAsync("trigger", "remove", "audit-failures");

        removed.ExitCode.Should().Be(0, removed.StandardError);
        (await loadout.RunAsync("trigger", "list", "--json")).Json().EnumerateArray().Should().BeEmpty();
    }

    [BuiltCliFact]
    public async Task A_rule_that_starts_a_team_without_a_spend_cap_is_refused_and_nothing_is_written()
    {
        using var loadout = new LoadoutProcess();
        loadout.Environment["LOADOUT_TRIGGERS"] = "1";

        var added = await loadout.RunAsync(
            "trigger", "add", "review", "run-finished", "--team", "reviewer", "--goal", "Look at {run}.");

        added.ExitCode.Should().Be(2);
        added.StandardError.Should().Contain("spend cap");

        (await loadout.RunAsync("trigger", "list", "--json")).Json().EnumerateArray().Should().BeEmpty();
    }

    [BuiltCliFact]
    public async Task A_dry_run_says_what_it_would_save_and_saves_nothing()
    {
        using var loadout = new LoadoutProcess();
        loadout.Environment["LOADOUT_TRIGGERS"] = "1";

        var added = await loadout.RunAsync(
            "trigger", "add", "audit-failures", "run-finished", "--builtin", "docs audit", "--dry-run");

        added.ExitCode.Should().Be(0, added.StandardError);
        added.StandardOutput.Should().Contain("nothing was saved");

        (await loadout.RunAsync("trigger", "list", "--json")).Json().EnumerateArray().Should().BeEmpty();
    }
}
