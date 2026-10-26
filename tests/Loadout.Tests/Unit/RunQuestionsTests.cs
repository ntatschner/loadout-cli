using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Pairing the questions a run stopped on with the answers somebody gave when
/// they picked it up, and refusing a pick-up that would leave them unanswered.
/// </summary>
public sealed class RunQuestionsTests
{
    private static readonly ReportQuestion Combine =
        new("How do you want the branches combined?", ["Merge through the gate", "By hand"], "Merge through the gate");

    private static readonly ReportQuestion RealRunner =
        new("Does a separate process holding the lock count as a real runner?", ["Yes", "No"], "Yes");

    private static readonly ReportQuestion[] Both = [Combine, RealRunner];

    [Fact]
    public void One_answer_per_question_pairs_each_with_its_own_in_order()
    {
        var paired = RunQuestions.Pair(Both, ["By hand", " Yes "], withoutAnswers: false);

        paired.Refusal.Should().BeNull();
        paired.Ask.Should().BeFalse();
        paired.Answers.Should().Equal(new ResumeAnswer(Combine.Question, "By hand"), new ResumeAnswer(RealRunner.Question, "Yes"));
    }

    [Fact]
    public void Nothing_given_asks_rather_than_carrying_on()
    {
        var paired = RunQuestions.Pair(Both, [], withoutAnswers: false);

        paired.Ask.Should().BeTrue("nobody said to carry on without them");
        paired.Answers.Should().BeNull();
    }

    [Fact]
    public void Carrying_on_without_answers_is_a_choice_that_goes_ahead()
    {
        var paired = RunQuestions.Pair(Both, [], withoutAnswers: true);

        paired.Ask.Should().BeFalse();
        paired.Answers.Should().BeEmpty();
    }

    [Theory]
    [InlineData(new[] { "By hand" }, false, "1 answer(s) were given")]
    [InlineData(new[] { "By hand", "Yes", "No" }, false, "3 answer(s) were given")]
    [InlineData(new[] { "By hand", "  " }, false, "An answer was empty")]
    [InlineData(new[] { "By hand", "Yes" }, true, "not both")]
    public void Answers_that_do_not_fit_the_questions_are_refused(string[] given, bool without, string why)
    {
        var paired = RunQuestions.Pair(Both, given, without);

        paired.Refusal.Should().Contain(why);
        paired.Code.Should().Be(ExitCode.InvalidArguments);
        paired.Answers.Should().BeNull();
    }

    [Fact]
    public void A_count_that_does_not_match_lists_the_questions_to_answer()
    {
        RunQuestions.Pair(Both, ["By hand"], false).Refusal.Should()
            .Contain($"1. {Combine.Question}").And.Contain($"2. {RealRunner.Question}");
    }

    [Fact]
    public void A_run_with_no_questions_open_needs_no_answers_and_takes_none()
    {
        RunQuestions.Pair([], [], false).Answers.Should().BeEmpty();
        RunQuestions.Pair([], [], true).Answers.Should().BeEmpty();
        RunQuestions.Pair([], ["yes"], false).Refusal.Should().Contain("no questions open");
    }

    [Fact]
    public void The_unattended_refusal_names_every_question_with_its_options_and_recommendation()
    {
        var said = RunQuestions.Unanswered("20261023-1740-2677", Both);

        said.Should().Contain("2 question(s) only you can answer").And.Contain("--answer").And.Contain("--without-answers");
        said.Should().Contain($"1. {Combine.Question} (Merge through the gate / By hand) Recommended: Merge through the gate");
        said.Should().Contain($"2. {RealRunner.Question} (Yes / No) Recommended: Yes");
    }
}
