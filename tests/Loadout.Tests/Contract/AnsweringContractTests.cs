using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Answering something a run stopped to ask, from the built command line.
/// </summary>
/// <remarks>
/// <para>
/// Both of these are about a thing that was accepted, reported as having
/// worked, and did nothing. The dashboard runs these commands rather than
/// writing files itself, so what the command says is what the page shows -
/// which means a command that says "+ Told run X" over a no-op is a page that
/// lies.
/// </para>
/// <para>
/// The runs here are fabricated: a directory, a journal of two events and an
/// unanswered question. That is the whole of what these commands read, and a
/// real run would mean starting agents and spending money to assert something
/// about a file.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class AnsweringContractTests
{
    private const string Run = "20260922-1440-96fb";

    /// <summary>Where this run's state lives, according to the run itself.</summary>
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

    /// <summary>
    /// Writes a run that is waiting on a lead's question, and optionally one
    /// that gave up waiting and finished.
    /// </summary>
    private static async Task<string> RunAsync(LoadoutProcess loadout, bool finished)
    {
        var directory = Path.Combine(await StateAsync(loadout), "teams", "runs", Run);

        Directory.CreateDirectory(directory);

        var asked = DateTimeOffset.UtcNow.AddMinutes(-10);

        await File.WriteAllTextAsync(
            Path.Combine(directory, "ask-gate-b4302e02.json"),
            JsonSerializer.Serialize(new
            {
                Id = "gate-b4302e02",
                Node = "lead",
                Role = "the lead",
                Tool = "",
                Target = (string?)null,
                At = asked,
                Kind = "question",
                Asked = "When a member's retention gift expires, what happens to their events?",
                Options = new[] { "Ratchet", "Permanent", "Full revert", "Stop the run" },
                Until = asked.AddHours(1),
            }));

        var journal = new List<string>
        {
            Event(asked.AddMinutes(-3), "run.started", new { team = "iterating-project", goal = "Do a thing." }),
        };

        if (finished)
        {
            journal.Add(Event(
                asked.AddMinutes(5),
                "run.finished",
                new { ended = "stopped at a decision", outcome = "stopped", rounds = 1 }));
        }

        await File.WriteAllLinesAsync(Path.Combine(directory, "journal.jsonl"), journal);

        return directory;
    }

    private static string Event(DateTimeOffset at, string kind, object data) =>
        JsonSerializer.Serialize(new { at, run = Run, node = (string?)null, kind, data });

    /// <remarks>
    /// The case that cost a real run: the lead asked at 14:43:55, gave up at
    /// 14:48:55 and the run finished. The answer arrived at 14:51:33, was
    /// written into the directory of a run that no longer existed, and the
    /// dashboard reported that it had gone through. The question file outlives
    /// the run that asked it, so "unanswered question in the directory" was
    /// never evidence that anything was waiting for one.
    /// </remarks>
    [BuiltCliFact]
    public async Task An_answer_to_a_run_that_has_already_finished_is_refused()
    {
        using var loadout = new LoadoutProcess();

        var directory = await RunAsync(loadout, finished: true);

        var run = await loadout.RunAsync(
            "team", "gate", Run, "--gate", "gate-b4302e02", "--answer", "Full revert");

        run.ExitCode.Should().NotBe(0, "nothing is waiting for that answer");

        (run.StandardOutput + run.StandardError).Should().Contain(
            "finished",
            "a refusal has to say why, or somebody sends it again");

        File.Exists(Path.Combine(directory, "answer-gate-b4302e02.json")).Should().BeFalse(
            "writing it would leave an answer nothing will ever read");
    }

    [BuiltCliFact]
    public async Task An_answer_to_a_run_that_is_still_waiting_goes_through()
    {
        using var loadout = new LoadoutProcess();

        var directory = await RunAsync(loadout, finished: false);

        var run = await loadout.RunAsync(
            "team", "gate", Run, "--gate", "gate-b4302e02", "--answer", "Full revert");

        run.ExitCode.Should().Be(0);

        var written = await File.ReadAllTextAsync(
            Path.Combine(directory, "answer-gate-b4302e02.json"));

        var answer = JsonDocument.Parse(written).RootElement;

        answer.GetProperty("Chosen").GetString().Should().Be("Full revert");
    }

    /// <remarks>
    /// What the page's own box sends. The lead offers a fixed set of options
    /// and somebody can agree with none of them - which is what happened the
    /// first time this page was used in anger, and there was nowhere to put it
    /// but the message box, where it could never be read.
    /// </remarks>
    [BuiltCliFact]
    public async Task An_answer_in_your_own_words_is_an_answer_rather_than_a_refusal()
    {
        using var loadout = new LoadoutProcess();

        var directory = await RunAsync(loadout, finished: false);

        var said = "Ratchet, but only for supporters.";

        var run = await loadout.RunAsync(
            "team", "gate", Run, "--gate", "gate-b4302e02", "--answer", said);

        run.ExitCode.Should().Be(0);

        var answer = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(directory, "answer-gate-b4302e02.json"))).RootElement;

        answer.GetProperty("Chosen").GetString().Should().Be(said);

        answer.GetProperty("Allowed").GetBoolean().Should().BeTrue(
            "a question is answered, not permitted: words matching none of the "
            + "options are still what the person said, and writing them down as a "
            + "refusal records the opposite of what happened");
    }

    /// <remarks>
    /// The page now offers a box for this, so the words never become a message.
    /// The command is still reachable by hand, and this is what it used to do
    /// with one: write it, say it would be read at the start of the next round,
    /// and leave out that there is no next round until the question is
    /// answered.
    /// </remarks>
    [BuiltCliFact]
    public async Task A_message_to_a_run_that_is_waiting_on_a_question_says_so()
    {
        using var loadout = new LoadoutProcess();

        await RunAsync(loadout, finished: false);

        var run = await loadout.RunAsync(
            "team", "message", Run, "--message", "Ratchet, but only for supporters.");

        run.ExitCode.Should().Be(0, "the message is still written; it is the silence that was wrong");

        run.StandardOutput.Should().Contain(
            "waiting on a question",
            "a lead stopped on a question has no next round to read this at");

        run.StandardOutput.Should().Contain("gate-b4302e02", "so it can be answered from here");
    }

    /// <remarks>
    /// The page types a command line and the parser decides whether any of it
    /// means anything, so a field added to the form is worth nothing until the
    /// option it maps to is one the command declares. "forget" shipped passing
    /// <c>--yes</c> to a command that does not take it, and the page reported
    /// exit code 1 over a typo three files away.
    /// </remarks>
    [BuiltCliFact]
    public async Task The_model_the_start_form_sends_is_an_option_team_run_declares()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync(
            "team", "run", "iterating-project", "Do a thing.",
            "--project", "demo", "--model", "opus", "--non-interactive", "--dry-run");

        // No project called "demo" in a throwaway home, so this does not get as
        // far as running anything. What is asserted is that the line parsed:
        // Spectre refuses an option a command does not declare before the
        // command is reached at all, with its own message.
        (run.StandardOutput + run.StandardError).Should().NotContain(
            "Unknown option",
            "the page now sends --model, and a line the parser refuses never runs");
    }

    [BuiltCliFact]
    public async Task The_agent_the_start_form_sends_is_an_option_team_run_declares()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync(
            "team", "run", "iterating-project", "Do a thing.",
            "--project", "demo", "--agent", "claude", "--non-interactive", "--dry-run");

        (run.StandardOutput + run.StandardError).Should().NotContain(
            "Unknown option",
            "the page now sends --agent, and a line the parser refuses never runs");
    }

    /// <summary>Adds a node's permission question, offering the agent's three answers.</summary>
    private static async Task PermissionAsync(string directory)
    {
        var asked = DateTimeOffset.UtcNow.AddMinutes(-1);

        await File.WriteAllTextAsync(
            Path.Combine(directory, "ask-implementer-1-toolu_9.json"),
            JsonSerializer.Serialize(new
            {
                Id = "implementer-1-toolu_9",
                Node = "implementer/1",
                Role = "role.implementer",
                Tool = "Bash",
                Target = "git status --short",
                At = asked,
                Kind = "permission",
                Options = new[] { "yes", "yes, and don't ask again for Bash(git status:*)", "no" },
                Until = asked.AddMinutes(5),
            }));
    }

    /// <remarks>
    /// The node's side remembers the rule only when the answer names the
    /// option it offered, word for word. "always" is what somebody types; the
    /// option is what has to be written.
    /// </remarks>
    [BuiltCliFact]
    public async Task Always_is_recorded_as_the_dont_ask_again_option_it_stands_for()
    {
        using var loadout = new LoadoutProcess();

        var directory = await RunAsync(loadout, finished: false);

        File.Delete(Path.Combine(directory, "ask-gate-b4302e02.json"));
        await PermissionAsync(directory);

        var run = await loadout.RunAsync("team", "gate", Run, "--answer", "always");

        run.ExitCode.Should().Be(0, run.StandardOutput + run.StandardError);

        var answer = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(directory, "answer-implementer-1-toolu_9.json"))).RootElement;

        answer.GetProperty("Allowed").GetBoolean().Should().BeTrue();
        answer.GetProperty("Chosen").GetString().Should().Be("yes, and don't ask again for Bash(git status:*)");
    }

    [BuiltCliFact]
    public async Task A_no_with_words_tells_the_node_it_was_refused_and_what_to_do_instead()
    {
        using var loadout = new LoadoutProcess();

        var directory = await RunAsync(loadout, finished: false);

        File.Delete(Path.Combine(directory, "ask-gate-b4302e02.json"));
        await PermissionAsync(directory);

        var run = await loadout.RunAsync(
            "team", "gate", Run, "--answer", "no", "--reason", "read git log instead");

        run.ExitCode.Should().Be(0, run.StandardOutput + run.StandardError);

        var answer = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(directory, "answer-implementer-1-toolu_9.json"))).RootElement;

        answer.GetProperty("Allowed").GetBoolean().Should().BeFalse();

        // The words alone read to the node as advice with no verdict.
        answer.GetProperty("Reason").GetString().Should().Be(
            "The person running this team refused it, and said: read git log instead");
    }

    [BuiltCliFact]
    public async Task A_budget_set_on_a_running_run_is_left_where_the_run_reads_it()
    {
        using var loadout = new LoadoutProcess();

        var directory = await RunAsync(loadout, finished: false);

        var run = await loadout.RunAsync("team", "budget", Run, "--usd", "40");

        run.ExitCode.Should().Be(0, run.StandardOutput + run.StandardError);

        (await File.ReadAllTextAsync(Path.Combine(directory, "control-budget"))).Should().Be("40");
    }

    [BuiltCliFact]
    public async Task A_budget_for_a_run_that_has_finished_is_refused_rather_than_written()
    {
        using var loadout = new LoadoutProcess();

        var directory = await RunAsync(loadout, finished: true);

        var run = await loadout.RunAsync("team", "budget", Run, "--usd", "40");

        run.ExitCode.Should().NotBe(0);
        (run.StandardOutput + run.StandardError).Should().Contain("finished");

        File.Exists(Path.Combine(directory, "control-budget")).Should().BeFalse(
            "nothing is left to read it, and writing it would report a raise that never happens");
    }

    [BuiltCliFact]
    public async Task A_message_to_a_run_that_is_getting_on_with_it_says_nothing_extra()
    {
        using var loadout = new LoadoutProcess();

        var directory = await RunAsync(loadout, finished: false);

        File.Delete(Path.Combine(directory, "ask-gate-b4302e02.json"));

        var run = await loadout.RunAsync(
            "team", "message", Run, "--message", "Ratchet, but only for supporters.");

        run.ExitCode.Should().Be(0);
        run.StandardOutput.Should().NotContain("waiting on a question");
    }
}
