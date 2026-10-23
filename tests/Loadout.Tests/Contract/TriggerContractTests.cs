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
    public async Task A_clock_rule_is_added_with_its_time_and_a_schedule_that_is_not_there_cannot_be_adopted()
    {
        using var loadout = new LoadoutProcess();
        loadout.Environment["LOADOUT_TRIGGERS"] = "1";

        var added = await loadout.RunAsync("trigger", "add", "hourly-audit", "clock", "--every", "1h", "--builtin", "docs audit");
        added.ExitCode.Should().Be(0, added.StandardError);

        var rule = (await loadout.RunAsync("trigger", "list", "--json")).Json().EnumerateArray().Single().GetProperty("rule");
        rule.GetProperty("on").GetString().Should().Be("clock");
        (await loadout.RunAsync("trigger", "list")).StandardOutput.Should().Contain("hourly-audit v1: every 1h");

        var both = await loadout.RunAsync("trigger", "add", "both", "clock", "--every", "1h", "--at", "09:00", "--builtin", "docs audit");
        both.ExitCode.Should().Be(2);
        both.StandardError.Should().Contain("exactly one of them");

        var adopted = await loadout.RunAsync("trigger", "adopt", "no-such-schedule");
        adopted.ExitCode.Should().NotBe(0);
        adopted.StandardError.Should().Contain("no schedule called 'no-such-schedule'");
    }

    [BuiltCliFact]
    public async Task The_stale_list_starts_empty_and_a_release_with_no_daemon_is_refused()
    {
        using var loadout = new LoadoutProcess();
        loadout.Environment["LOADOUT_TRIGGERS"] = "1";

        var listed = await loadout.RunAsync("trigger", "stale");
        listed.ExitCode.Should().Be(0, listed.StandardError);
        listed.StandardOutput.Should().Contain("Nothing is on the stale list");
        (await loadout.RunAsync("trigger", "stale", "--json")).Json().EnumerateArray().Should().BeEmpty();

        var nothing = await loadout.RunAsync("trigger", "release", "audit-failures");
        nothing.ExitCode.Should().Be(0, nothing.StandardError);
        nothing.StandardOutput.Should().Contain("nothing of that on the stale list");

        // What a daemon writes when it resumes a rule that held an event for
        // longer than the catch-up window.
        var doctor = (await loadout.RunAsync("doctor", "--json")).Json();
        var state = doctor.GetProperty("checks").EnumerateArray()
            .First(check => check.GetProperty("name").GetString() == "State").GetProperty("detail").GetString()!;
        var journal = Path.Combine(state, "triggers", "journal");
        var now = DateTimeOffset.UtcNow;

        Directory.CreateDirectory(journal);
        await File.AppendAllTextAsync(
            Path.Combine(journal, now.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ".jsonl"),
            string.Join('\n',
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    at = now.AddHours(-30), kind = "event", @event = "e1", subject = "run:1", source = "daemon",
                    eventKind = "run-finished", fields = new { team = "alpha", outcome = "failed" },
                }),
                System.Text.Json.JsonSerializer.Serialize(new { at = now, kind = "stale", @event = "e1", rule = "audit-failures", subject = "run:1" }))
            + "\n");

        var stale = (await loadout.RunAsync("trigger", "stale", "--json")).Json().EnumerateArray().Single();
        stale.GetProperty("subject").GetString().Should().Be("run:1");
        stale.GetProperty("agesOut").GetDateTimeOffset().Should().BeCloseTo(now.AddDays(7), TimeSpan.FromSeconds(1));
        (await loadout.RunAsync("trigger", "stale")).StandardOutput.Should().Contain("audit-failures: run-finished about run:1");

        var released = await loadout.RunAsync("trigger", "release", "audit-failures");
        released.ExitCode.Should().Be(2, released.StandardOutput);
        released.StandardError.Should().Contain("Only a running daemon can decide the 1 stale event(s)");
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
        SuggestionChanged().Should().BeTrue("the rules list says so too, not only 'trigger suggested'");
        (await loadout.RunAsync("trigger", "list")).StandardOutput.Should().Contain("Changed since you accepted it");
        (await loadout.RunAsync("trigger", "accept", "watcher", "after-failure", "--dry-run")).StandardOutput
            .Should().Contain("spend cap: $6.00 -> $8.00", "a dry run says what taking the change would change");

        (await loadout.RunAsync("trigger", "accept", "watcher", "after-failure")).ExitCode.Should().Be(0);
        Rule().Should().Be((2, 8m));
        SuggestionChanged().Should().BeFalse();

        bool SuggestionChanged() =>
            loadout.RunAsync("trigger", "list", "--json").GetAwaiter().GetResult()
                .Json().EnumerateArray().Single().GetProperty("suggestionChanged").GetBoolean();

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
    public async Task With_no_daemon_a_rule_that_held_events_while_paused_is_not_resumed()
    {
        using var loadout = new LoadoutProcess();
        loadout.Environment["LOADOUT_TRIGGERS"] = "1";

        (await loadout.RunAsync("trigger", "add", "audit-failures", "run-finished", "--builtin", "docs audit"))
            .ExitCode.Should().Be(0);

        // What a daemon writes when a rule reaches its cap and goes on matching.
        var doctor = (await loadout.RunAsync("doctor", "--json")).Json();
        var state = doctor.GetProperty("checks").EnumerateArray()
            .First(check => check.GetProperty("name").GetString() == "State").GetProperty("detail").GetString()!;
        var journal = Path.Combine(state, "triggers", "journal");
        var now = DateTimeOffset.UtcNow;

        Directory.CreateDirectory(journal);
        await File.AppendAllTextAsync(
            Path.Combine(journal, now.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ".jsonl"),
            string.Join('\n',
                System.Text.Json.JsonSerializer.Serialize(new { at = now, kind = "paused", rule = "audit-failures" }),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    at = now, kind = "event", @event = "e1", subject = "run:1", source = "daemon",
                    eventKind = "run-finished", fields = new { team = "alpha", outcome = "failed" },
                }),
                System.Text.Json.JsonSerializer.Serialize(new { at = now, kind = "held", @event = "e1", rule = "audit-failures" }))
            + "\n");

        var resumed = await loadout.RunAsync("trigger", "resume", "audit-failures");

        resumed.ExitCode.Should().Be(2, resumed.StandardOutput);
        resumed.StandardError.Should().Contain("held 1 event(s)").And.Contain("only a running daemon");
    }

    [BuiltCliFact]
    public async Task Prune_says_what_it_would_delete_then_deletes_it()
    {
        using var loadout = new LoadoutProcess();
        loadout.Environment["LOADOUT_TRIGGERS"] = "1";

        var doctor = (await loadout.RunAsync("doctor", "--json")).Json();
        var state = doctor.GetProperty("checks").EnumerateArray()
            .First(check => check.GetProperty("name").GetString() == "State").GetProperty("detail").GetString()!;
        var journal = Path.Combine(state, "triggers", "journal");
        var old = Path.Combine(journal, DateTimeOffset.UtcNow.AddDays(-120).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ".jsonl");

        Directory.CreateDirectory(journal);
        await File.WriteAllTextAsync(old, "{\"at\":\"2026-06-01T00:00:00+00:00\",\"kind\":\"event\"}\n");

        var dry = await loadout.RunAsync("trigger", "prune", "--dry-run");

        dry.ExitCode.Should().Be(0, dry.StandardError);
        dry.StandardOutput.Should().Contain("1 day(s) would go");
        File.Exists(old).Should().BeTrue();

        (await loadout.RunAsync("trigger", "prune")).ExitCode.Should().Be(0);
        File.Exists(old).Should().BeFalse();
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

    [BuiltCliFact]
    public async Task An_account_is_trusted_by_id_listed_with_its_scope_and_taken_off_with_the_journal_saying_so()
    {
        using var loadout = new LoadoutProcess();
        loadout.Environment["LOADOUT_TRIGGERS"] = "1";

        var both = await loadout.RunAsync("trigger", "allow", "bot", "--id", "7", "--repository", "a/b", "--owner", "a");
        both.ExitCode.Should().Be(2);
        both.StandardError.Should().Contain("not both");

        var dry = await loadout.RunAsync("trigger", "allow", "dependabot[bot]", "--id", "49699333", "--dry-run");
        dry.ExitCode.Should().Be(0, dry.StandardError);
        // Short phrases: redirected output wraps at 80 columns.
        dry.StandardOutput.Should().Contain("nothing was changed").And.Contain("49699333");
        (await loadout.RunAsync("trigger", "allowed", "--json")).Json().EnumerateArray().Should().BeEmpty();

        (await loadout.RunAsync("trigger", "allow", "dependabot[bot]", "--id", "49699333")).ExitCode.Should().Be(0);
        (await loadout.RunAsync("trigger", "allow", "pat", "--id", "5", "--repository", "acme/app")).ExitCode.Should().Be(0);

        var listed = (await loadout.RunAsync("trigger", "allowed", "--json")).Json().EnumerateArray().ToList();
        listed.Select(one => one.GetProperty("scope").GetString()).Should().Equal("everywhere", "acme/app");

        var taken = await loadout.RunAsync("trigger", "disallow", "pat");
        taken.ExitCode.Should().Be(0, taken.StandardError);
        taken.StandardOutput.Should().Contain("pat is off the allow-list").And.Contain("0 waiting");

        (await loadout.RunAsync("trigger", "allowed")).StandardOutput.Should().Contain("dependabot[bot] (49699333)")
            .And.NotContain("pat");
        (await loadout.RunAsync("trigger", "journal", "--json")).StandardOutput.Should().Contain("taken off the allow-list");

        var again = await loadout.RunAsync("trigger", "disallow", "pat");
        again.ExitCode.Should().NotBe(0);
        again.StandardError.Should().Contain("not on the allow-list");
    }

    [BuiltCliFact]
    public async Task A_github_rule_can_ask_about_trust_and_the_sources_say_nothing_has_been_read_yet()
    {
        using var loadout = new LoadoutProcess();
        loadout.Environment["LOADOUT_TRIGGERS"] = "1";

        var added = await loadout.RunAsync(
            "trigger", "add", "triage-outside", "issue-opened", "--builtin", "docs audit", "--where", "trust=outside");
        added.ExitCode.Should().Be(0, added.StandardError);

        var wrong = await loadout.RunAsync(
            "trigger", "add", "nonsense", "pr-opened", "--builtin", "docs audit", "--where", "workflow=CI");
        wrong.ExitCode.Should().Be(2);
        wrong.StandardError.Should().Contain("carries no 'workflow'");

        var sources = await loadout.RunAsync("trigger", "sources");
        sources.ExitCode.Should().Be(0, sources.StandardError);
        sources.StandardOutput.Should().Contain("GitHub has not been read yet");

        var clear = await loadout.RunAsync("trigger", "token", "clear", "--dry-run");
        clear.ExitCode.Should().Be(0, clear.StandardError);
        clear.StandardOutput.Should().Contain("nothing was removed");
    }
}
