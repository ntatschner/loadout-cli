using FluentAssertions;
using Loadout.Agents.Teams;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// A run that stopped on its lead's questions, picked up with the answers.
/// </summary>
public sealed partial class TeamRunnerTests
{
    private static readonly ReportQuestion HowCombined =
        new("How do you want the verified branches combined?", ["Merge through the gate", "By hand into one PR"], "Merge through the gate");

    private static readonly ReportQuestion RealRunnerCounts =
        new("Does a separate process holding the lock count as a real runner?", ["Yes", "No"], "Yes");

    private static Report LeadAsksTwo() => new(
        "lead", ReportStatus.Blocked, "Two calls are yours.", [], [], [],
        Blocker: new ReportBlocker("two decisions only you can make", "your answers"),
        Questions: [HowCombined, RealRunnerCounts]);

    [Fact]
    public async Task A_run_picked_up_with_answers_tells_its_lead_each_question_beside_its_answer()
    {
        var team = await IteratingProjectAsync();

        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadAsksTwo(), 0.50m));

        var first = (await RunAsync(team)).Value!;

        first.Ended.Should().Be("blocked");

        var journal = new RunJournal(_paths);
        var (resuming, _) = RunResumption.Read(journal, first.RunId);

        resuming!.Questions.Select(one => one.Question).Should().Equal(HowCombined.Question, RealRunnerCounts.Question);

        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadDone(), 0.70m), Result(LeadDone(), 0.80m));

        var picked = await new TeamRunner(_launcher, _paths, TimeProvider.System, null, null, null, null).RunAsync(
            new TeamRunRequest("demo", team, await SpecialistsAsync(),
                "Add --since to loadout usage.", "supervised", MaxRounds: 10, Offline: true,
                Resuming: first.RunId,
                ResumeAnswers:
                [
                    new ResumeAnswer(HowCombined.Question, "By hand into one PR"),
                    new ResumeAnswer(RealRunnerCounts.Question, "Yes, as built"),
                ]),
            _console);

        picked.Succeeded.Should().BeTrue(picked.Error);

        var brief = _launcher.Written("role.project-lead")[1];

        brief.Should().Contain("## Your questions, answered");
        brief.Should().Contain($"- Question: {HowCombined.Question}\n  Answer: By hand into one PR".ReplaceLineEndings());
        brief.Should().Contain($"- Question: {RealRunnerCounts.Question}\n  Answer: Yes, as built".ReplaceLineEndings());

        // Written down as decided, so the run no longer reads as waiting on them.
        (await JournalAsync(first)).Should().Contain(line =>
            line.Contains("\"kind\":\"decision\"") && line.Contains("By hand into one PR") && line.Contains("\"by\":\"person\""));
    }

    [Fact]
    public async Task A_question_answered_after_the_lead_asked_it_is_not_open()
    {
        var team = await IteratingProjectAsync();

        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadAsksTwo(), 0.50m));

        var first = (await RunAsync(team)).Value!;

        await File.AppendAllTextAsync(
            Path.Combine(first.Directory!, "journal.jsonl"),
            RunJournalLine.Serialize(DateTimeOffset.UtcNow, first.RunId, null, "decision",
                new { question = HowCombined.Question, answer = "By hand into one PR", by = "person" }) + "\n");

        var (resuming, _) = RunResumption.Read(new RunJournal(_paths), first.RunId);

        resuming!.Questions.Should().ContainSingle().Which.Question.Should().Be(RealRunnerCounts.Question);
    }

    [Fact]
    public async Task A_run_carried_on_without_answers_tells_its_lead_so()
    {
        var team = await IteratingProjectAsync();

        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadAsksTwo(), 0.50m));

        var first = (await RunAsync(team)).Value!;

        _launcher.Script("role.project-lead", Init("lead-1"), Result(LeadDone(), 0.70m), Result(LeadDone(), 0.80m));

        await new TeamRunner(_launcher, _paths, TimeProvider.System, null, null, null, null).RunAsync(
            new TeamRunRequest("demo", team, await SpecialistsAsync(),
                "Add --since to loadout usage.", "supervised", MaxRounds: 10, Offline: true,
                Resuming: first.RunId, WithoutAnswers: true),
            _console);

        _launcher.Written("role.project-lead")[1].Should()
            .Contain("chose to carry on without answering").And.Contain(HowCombined.Question).And.Contain(RealRunnerCounts.Question);
    }
}
