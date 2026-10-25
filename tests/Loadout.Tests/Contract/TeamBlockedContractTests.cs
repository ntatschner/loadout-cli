using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// 'team blocked' from the built command line: runs that ended short of their
/// done-when listed with a suggestion, and each way of dealing with one.
/// </summary>
/// <remarks>
/// The runs are journals written to the state directory in the shapes the
/// runner writes, because what is under test is how the command reads and
/// answers them, not a team's agents. Starting a real follow-on run needs an
/// agent, so carrying on is checked as a dry run here, which goes through every
/// refusal the real one does.
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class TeamBlockedContractTests
{
    private const string Team = "iterating-project";

    [BuiltCliFact]
    public async Task A_limited_and_a_failed_run_are_listed_with_a_suggestion_and_a_stopped_one_is_not()
    {
        using var loadout = new LoadoutProcess();
        var state = await StateAsync(loadout);

        WriteRun(state, "20261025-0900-aaaa", Ended("round limit: 3 rounds"));
        WriteRun(state, "20261025-0901-bbbb", Ended("failed"));
        WriteRun(state, "20261025-0902-cccc", Ended("stopped by request"));

        var listed = await loadout.RunAsync("team", "blocked", "list", "--json");

        listed.ExitCode.Should().Be(0, listed.StandardError);
        var runs = listed.Json().GetProperty("runs").EnumerateArray().ToList();
        runs.Select(one => one.GetProperty("id").GetString()).Should().BeEquivalentTo("20261025-0900-aaaa", "20261025-0901-bbbb");

        var limited = runs.Single(one => one.GetProperty("id").GetString() == "20261025-0900-aaaa");
        limited.GetProperty("kind").GetString().Should().Be("limited");
        limited.GetProperty("suggested").GetProperty("action").GetString().Should().Be("raise");
        limited.GetProperty("suggested").GetProperty("reason").GetString().Should().Contain("3 round(s)");
        limited.GetProperty("raise").GetProperty("figure").GetDecimal().Should().Be(3);

        var said = await loadout.RunAsync("team", "blocked", "show", "20261025-0901-bbbb");
        said.ExitCode.Should().Be(0, said.StandardError);
        said.StandardOutput.Should().Contain("Suggested:").And.Contain("--action");

        var stopped = await loadout.RunAsync("team", "blocked", "show", "20261025-0902-cccc");
        stopped.ExitCode.Should().Be(2);
        stopped.StandardError.Should().Contain("stopped by a person");
    }

    [BuiltCliFact]
    public async Task Setting_one_aside_writes_it_down_in_the_runs_journal_and_takes_it_off_the_list()
    {
        using var loadout = new LoadoutProcess();
        var state = await StateAsync(loadout);
        WriteRun(state, "20261025-0900-aaaa", Ended("failed"));

        var preview = await loadout.RunAsync("team", "blocked", "resolve", "20261025-0900-aaaa", "--action", "set-aside", "--dry-run");
        preview.ExitCode.Should().Be(0, preview.StandardError);
        Journal(state, "20261025-0900-aaaa").Should().NotContain("run.resolved", "a dry run writes nothing");

        var resolved = await loadout.RunAsync(
            "team", "blocked", "resolve", "20261025-0900-aaaa", "--action", "set-aside", "--answer", "Needs a rethink", "--json");

        resolved.ExitCode.Should().Be(0, resolved.StandardError);
        resolved.Json().GetProperty("action").GetString().Should().Be("set-aside");

        var line = Journal(state, "20261025-0900-aaaa").Split('\n').Single(one => one.Contains("run.resolved", StringComparison.Ordinal));
        using var written = JsonDocument.Parse(line);
        written.RootElement.GetProperty("data").GetProperty("answer").GetString().Should().Be("Needs a rethink");

        (await loadout.RunAsync("team", "blocked", "list", "--json")).Json().GetProperty("runs").EnumerateArray().Should().BeEmpty();

        var again = await loadout.RunAsync("team", "blocked", "resolve", "20261025-0900-aaaa", "--action", "set-aside");
        again.ExitCode.Should().Be(2);
        again.StandardError.Should().Contain("already dealt with");
    }

    [BuiltCliFact]
    public async Task A_raise_is_refused_when_it_would_take_the_rule_past_its_cap_and_nothing_is_set_aside()
    {
        using var loadout = new LoadoutProcess();
        var state = await StateAsync(loadout);

        (await loadout.RunAsync("trigger", "add", "after-failure", "run-finished", "--team", Team, "--goal", "Look at it.", "--cap-usd", "5", "--budget-usd", "5"))
            .ExitCode.Should().Be(0);

        // The rule has already spent 4.00 of its 5.00 this window.
        var now = DateTimeOffset.UtcNow;
        var journal = Path.Combine(state, "triggers", "journal", now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
        await File.AppendAllTextAsync(journal,
            JsonSerializer.Serialize(new { at = now, kind = "landed", rule = "after-failure", usd = 4.00m, reservation = "r0" }) + "\n");
        var before = await File.ReadAllTextAsync(journal);

        WriteRun(state, "20261025-0900-aaaa", Ended("budget spent: 4.80 of 5.00 USD", cost: 4.80m),
            origin: "trigger:after-failure:1:r0:", budget: 5.00m);

        var refused = await loadout.RunAsync("team", "blocked", "resolve", "20261025-0900-aaaa", "--action", "raise");

        refused.ExitCode.Should().Be(2, refused.StandardOutput);
        refused.StandardError.Should().Contain("$4.00 over this window's cap").And.Contain("#teams/+triggers");
        (await File.ReadAllTextAsync(journal)).Should().Be(before, "nothing is set aside for a run that was refused");
        Journal(state, "20261025-0900-aaaa").Should().NotContain("run.resolved", "a refused raise leaves the run waiting");

        // A figure inside the headroom is let through: as a dry run, which
        // says the run it would start, charged to the same rule.
        // Read as JSON: printed, the line wraps at 80 columns when redirected.
        var allowed = await loadout.RunAsync("team", "blocked", "resolve", "20261025-0900-aaaa", "--action", "raise", "--usd", "0.75", "--dry-run", "--json");

        allowed.ExitCode.Should().Be(0, allowed.StandardError);
        allowed.Json().GetProperty("message").GetString().Should().Contain("--follows").And.Contain("20261025-0900-aaaa").And.Contain("--usd 0.75")
            .And.Contain("--origin trigger:after-failure:");
        (await File.ReadAllTextAsync(journal)).Should().Be(before, "a dry run sets nothing aside");
    }

    [BuiltCliFact]
    public async Task A_raise_is_offered_only_for_a_limited_run()
    {
        using var loadout = new LoadoutProcess();
        var state = await StateAsync(loadout);
        WriteRun(state, "20261025-0900-aaaa", Ended("failed"));

        var refused = await loadout.RunAsync("team", "blocked", "resolve", "20261025-0900-aaaa", "--action", "raise", "--dry-run");

        refused.ExitCode.Should().Be(2);
        refused.StandardError.Should().Contain("not open to run");
    }

    [BuiltCliFact]
    public async Task A_requeued_task_goes_to_the_front_of_its_teams_queue_carrying_on_from_the_run()
    {
        using var loadout = new LoadoutProcess();
        var state = await StateAsync(loadout);
        var project = await MemoryWriteContractTests.Registered(loadout);

        foreach (var task in new[] { "earlier", "fix-login" })
        {
            (await loadout.RunAsync("task", "declare", task, "open", "--title", $"Do {task}", "--done-when", "It works", "--project", project))
                .ExitCode.Should().Be(0);
        }

        (await loadout.RunAsync("team", "queue", "add", Team, "earlier", "--project", project)).ExitCode.Should().Be(0);

        WriteRun(state, "20261025-0900-aaaa", Ended("no progress: two rounds without a request or a finish"), project: project);

        var requeued = await loadout.RunAsync("team", "blocked", "resolve", "20261025-0900-aaaa", "--action", "requeue");

        requeued.ExitCode.Should().Be(0, requeued.StandardError);
        requeued.StandardOutput.Should().Contain("first in");

        var queue = await loadout.RunAsync("team", "queue", "list", Team, "--json");
        queue.ExitCode.Should().Be(0, queue.StandardError);
        queue.StandardOutput.IndexOf("fix-login", StringComparison.Ordinal).Should()
            .BeLessThan(queue.StandardOutput.IndexOf("earlier", StringComparison.Ordinal), "it went to the front, ahead of what was waiting");

        Journal(state, "20261025-0900-aaaa").Should().Contain("run.resolved").And.Contain("requeue");
    }

    [BuiltCliFact]
    public async Task The_task_shows_its_runs_as_a_chain()
    {
        using var loadout = new LoadoutProcess();
        var state = await StateAsync(loadout);
        var project = await MemoryWriteContractTests.Registered(loadout);

        (await loadout.RunAsync("task", "declare", "fix-login", "doing", "--title", "Fix the login", "--project", project))
            .ExitCode.Should().Be(0);

        WriteRun(state, "20261025-0900-aaaa", Ended("blocked"), project: project);
        WriteRun(state, "20261025-1000-bbbb", Ended("done"), project: project, follows: "20261025-0900-aaaa");
        File.AppendAllText(Path.Combine(state, "teams", "runs", "20261025-0900-aaaa", "journal.jsonl"),
            JsonSerializer.Serialize(new
            {
                at = DateTimeOffset.Parse("2026-10-25T09:59:00+00:00", CultureInfo.InvariantCulture),
                run = "r",
                node = (string?)null,
                kind = "run.resolved",
                data = new { action = "carry-on", by = "nrtat", next = "20261025-1000-bbbb" },
            }) + "\n");

        var shown = await loadout.RunAsync("task", "show", "fix-login", "--project", project, "--json");

        shown.ExitCode.Should().Be(0, shown.StandardError);
        var chain = shown.Json().GetProperty("runs").EnumerateArray().Single().EnumerateArray().ToList();
        chain.Select(one => one.GetProperty("id").GetString()).Should().Equal("20261025-0900-aaaa", "20261025-1000-bbbb");
        chain[0].GetProperty("resolved").GetString().Should().Be("carry-on");
        chain[0].GetProperty("next").GetString().Should().Be("20261025-1000-bbbb");
        chain[1].GetProperty("follows").GetString().Should().Be("20261025-0900-aaaa");
    }

    private static async Task<string> StateAsync(LoadoutProcess loadout)
    {
        var doctor = (await loadout.RunAsync("doctor", "--json")).Json();

        return doctor.GetProperty("checks").EnumerateArray()
            .First(check => check.GetProperty("name").GetString() == "State").GetProperty("detail").GetString()!;
    }

    private static string Ended(string ended, decimal cost = 1.20m) =>
        JsonSerializer.Serialize(new
        {
            at = DateTimeOffset.Parse("2026-10-25T10:00:00+00:00", CultureInfo.InvariantCulture),
            run = "r",
            node = (string?)null,
            kind = "run.finished",
            data = new { ended, cost, rounds = 3, merged = Array.Empty<string>() },
        });

    private static void WriteRun(
        string state, string runId, string finished, string? origin = null, decimal? budget = null, string project = "loadout-cli", string? follows = null)
    {
        var started = JsonSerializer.Serialize(new
        {
            at = DateTimeOffset.Parse("2026-10-25T09:00:00+00:00", CultureInfo.InvariantCulture),
            run = "r",
            node = (string?)null,
            kind = "run.started",
            data = new { team = Team, goal = "Fix the login", autonomy = "autonomous", rounds = 3, project, task = "fix-login", origin, budget, follows },
        });

        var directory = Path.Combine(state, "teams", "runs", runId);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "journal.jsonl"), started + "\n" + finished + "\n");
    }

    private static string Journal(string state, string runId) =>
        File.ReadAllText(Path.Combine(state, "teams", "runs", runId, "journal.jsonl"));
}
