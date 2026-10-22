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
    public async Task A_team_rule_without_a_cap_is_refused_with_a_proposal_that_one_word_accepts()
    {
        using var loadout = new LoadoutProcess();
        loadout.Environment["LOADOUT_TRIGGERS"] = "1";

        var refused = await loadout.RunAsync(
            "trigger", "add", "review", "run-finished", "--team", "iterating-project", "--goal", "Look at {run}.");

        refused.ExitCode.Should().Be(2, refused.StandardOutput);
        refused.StandardError.Should().Contain("Proposed: $2 a run").And.Contain("--cap-usd proposed");
        (await loadout.RunAsync("trigger", "list", "--json")).Json().EnumerateArray().Should().BeEmpty();

        // A throwaway home has no runs, so what is taken is the starter figure.
        var accepted = await loadout.RunAsync(
            "trigger", "add", "review", "run-finished", "--team", "iterating-project", "--goal", "Look at {run}.",
            "--cap-usd", "proposed", "--budget-usd", "proposed");

        accepted.ExitCode.Should().Be(0, accepted.StandardError);

        var rule = (await loadout.RunAsync("trigger", "list", "--json")).Json().EnumerateArray().Single().GetProperty("rule");
        rule.GetProperty("budgetUsd").GetDecimal().Should().Be(2m);
        rule.GetProperty("capUsd").GetDecimal().Should().Be(10m);
    }

    [BuiltCliFact]
    public async Task A_rule_for_a_team_that_does_not_exist_is_refused()
    {
        using var loadout = new LoadoutProcess();
        loadout.Environment["LOADOUT_TRIGGERS"] = "1";

        var added = await loadout.RunAsync(
            "trigger", "add", "review", "run-finished", "--team", "nobody-has-this", "--goal", "Look.", "--cap-usd", "5");

        added.ExitCode.Should().Be(2);
        added.StandardError.Should().Contain("no team called 'nobody-has-this'");
    }

    [BuiltCliFact]
    public async Task A_team_suggestion_is_offered_accepted_kept_as_accepted_when_it_changes_and_taken_again()
    {
        using var loadout = new LoadoutProcess();
        loadout.Environment["LOADOUT_TRIGGERS"] = "1";

        var team = await WatcherAsync(loadout, cap: "6");

        var offered = (await loadout.RunAsync("trigger", "suggested", "--json")).Json().EnumerateArray().Single();
        offered.GetProperty("rule").GetString().Should().Be("watcher-after-failure");
        offered.GetProperty("accepted").GetBoolean().Should().BeFalse();

        var accepted = await loadout.RunAsync("trigger", "accept", "watcher", "after-failure");
        accepted.ExitCode.Should().Be(0, accepted.StandardError);

        Rule().Should().Be((1, 6m));

        // The team's author raises the cap. The rule stays as accepted, and
        // the change is shown rather than taken.
        await File.WriteAllTextAsync(team, Watcher(cap: "8"));

        var changed = (await loadout.RunAsync("trigger", "suggested", "--json")).Json().EnumerateArray().Single();
        changed.GetProperty("changed").GetBoolean().Should().BeTrue();
        Rule().Should().Be((1, 6m));

        (await loadout.RunAsync("trigger", "accept", "watcher", "after-failure")).ExitCode.Should().Be(0);
        Rule().Should().Be((2, 8m));

        (int, decimal) Rule()
        {
            var rule = loadout.RunAsync("trigger", "list", "--json").GetAwaiter().GetResult()
                .Json().EnumerateArray().Single().GetProperty("rule");

            return (rule.GetProperty("version").GetInt32(), rule.GetProperty("capUsd").GetDecimal());
        }
    }

    /// <summary>
    /// Puts a team that suggests a trigger in this run's own workspace, and
    /// returns its file.
    /// </summary>
    /// <remarks>
    /// The workspace is asked for its path rather than worked out, for the
    /// reason the daemon tests ask for the state directory: the layout differs
    /// between Windows and the XDG platforms. It counts as a workspace once it
    /// has a projects directory.
    /// </remarks>
    private static async Task<string> WatcherAsync(LoadoutProcess loadout, string cap)
    {
        var root = (await loadout.RunAsync("workspace", "status", "--json")).Json().GetProperty("localPath").GetString()!;
        var teams = Path.Combine(root, "global", "teams");

        Directory.CreateDirectory(Path.Combine(root, "projects"));
        Directory.CreateDirectory(teams);

        var file = Path.Combine(teams, "watcher.yaml");
        await File.WriteAllTextAsync(file, Watcher(cap));

        return file;
    }

    /// <summary>A small team with no worktree, so it only reads, that suggests one rule.</summary>
    private static string Watcher(string cap) =>
        "name: watcher\n"
        + "description: Looks at failed runs.\n"
        + "lead: lead\n"
        + "nodes:\n"
        + "  lead:\n"
        + "    role: role.project-lead\n"
        + "rules:\n"
        + "  autonomy: autonomous\n"
        + "  budget:\n"
        + "    usd: 3\n"
        + "triggers:\n"
        + "  - name: after-failure\n"
        + "    on: run-finished\n"
        + "    where:\n"
        + "      outcome: failed\n"
        + "    goal: Find out why {run} failed.\n"
        + $"    cap_usd: {cap}\n";

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
