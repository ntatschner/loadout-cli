using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Picking up a run that stopped on its lead's questions, from the built
/// command line, with nobody at a terminal.
/// </summary>
/// <remarks>
/// <para>
/// Run 20261023-1740-2677 was picked up twice with its two questions
/// unanswered. Its lead found the same questions and nothing new each time,
/// and the second pick-up cost $6.92 to end failed.
/// </para>
/// <para>
/// The run is fabricated: a journal in which the lead launched, reported and
/// the run ended blocked, and the lead's final report beside it. That is all
/// the refusal reads, and it refuses before anything else is looked up.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class ResumeAnswersContractTests
{
    private const string Run = "20261023-1740-2677";

    private const string Combine = "How do you want the verified branches combined?";
    private const string RealRunner = "Does a separate process holding the lock count as a real runner?";

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

    private static string Event(DateTimeOffset at, string? node, string kind, object data) =>
        JsonSerializer.Serialize(new { at, run = Run, node, kind, data });

    private static async Task StoppedOnQuestionsAsync(LoadoutProcess loadout)
    {
        var directory = Path.Combine(await StateAsync(loadout), "teams", "runs", Run);

        Directory.CreateDirectory(directory);

        var at = DateTimeOffset.UtcNow.AddMinutes(-30);

        await File.WriteAllLinesAsync(Path.Combine(directory, "journal.jsonl"),
        [
            Event(at, null, "run.started", new { team = "iterating-project", goal = "Detect and clear dead runs.", autonomy = "supervised" }),
            Event(at.AddMinutes(1), "lead", "node.launched", new { launch = "L-1", role = "role.project-lead" }),
            Event(at.AddMinutes(5), "lead", "report.checked", new { status = "blocked", outcome = "accepted" }),
            Event(at.AddMinutes(6), null, "run.finished", new { ended = "blocked", outcome = "blocked", rounds = 1, cost = 2.5 }),
        ]);

        await File.WriteAllTextAsync(Path.Combine(directory, "final-report.json"), JsonSerializer.Serialize(new
        {
            contract = "report/1",
            node = "lead",
            status = "blocked",
            summary = "Two calls are yours.",
            deliverables = Array.Empty<object>(),
            evidence = Array.Empty<object>(),
            outward_taken = Array.Empty<string>(),
            blocker = new { what = "two decisions", unblocked_by = "your answers" },
            questions = new[]
            {
                new { question = Combine, options = new[] { "Merge through the gate", "By hand into one PR" }, recommendation = "Merge through the gate" },
                new { question = RealRunner, options = new[] { "Yes", "No" }, recommendation = "Yes" },
            },
        }));
    }

    [BuiltCliFact]
    public async Task Unattended_it_refuses_and_lists_every_open_question()
    {
        using var loadout = new LoadoutProcess();

        await StoppedOnQuestionsAsync(loadout);

        var run = await loadout.RunAsync("team", "resume", Run, "--non-interactive");
        var said = run.StandardOutput + run.StandardError;

        run.ExitCode.Should().Be(11, "nobody can be asked here, which is TerminalRequired: " + said);
        said.Should().Contain(Combine).And.Contain(RealRunner).And.Contain("--answer").And.Contain("--without-answers");
    }

    [BuiltCliFact]
    public async Task An_answer_short_is_refused_with_the_questions_to_answer()
    {
        using var loadout = new LoadoutProcess();

        await StoppedOnQuestionsAsync(loadout);

        var run = await loadout.RunAsync("team", "resume", Run, "--non-interactive", "--answer", "By hand into one PR");
        var said = run.StandardOutput + run.StandardError;

        run.ExitCode.Should().Be(2, said);
        said.Should().Contain("2 question(s) open and 1 answer(s)").And.Contain(RealRunner);
    }

    [BuiltCliTheory]
    [InlineData("--without-answers")]
    [InlineData("--answer=By hand into one PR", "--answer=Yes")]
    public async Task With_answers_or_without_on_purpose_it_gets_past_the_questions(params string[] given)
    {
        using var loadout = new LoadoutProcess();

        await StoppedOnQuestionsAsync(loadout);

        var run = await loadout.RunAsync(["team", "resume", Run, "--non-interactive", .. given]);
        var said = run.StandardOutput + run.StandardError;

        // It stops next on the project, which a fabricated run does not name:
        // the point is that the questions did not stop it.
        said.Should().NotContain(Combine).And.Contain("does not say which project");
    }
}
