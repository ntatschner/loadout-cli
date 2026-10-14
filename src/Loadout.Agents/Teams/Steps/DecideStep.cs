using Loadout.Core.Teams;
using Loadout.Models.Teams;

namespace Loadout.Agents.Teams.Steps;

/// <summary>
/// Puts a lead's questions to whoever can answer them, or takes its own
/// recommendation where nobody can or nobody does in time, and writes down
/// who settled each.
/// </summary>
internal sealed class DecideStep
{
    private readonly TimeProvider _time;

    public DecideStep(TimeProvider time)
    {
        _time = time;
    }

    /// <summary>
    /// Every question's answer, saying who gave it; or null where one went
    /// unanswered and the run stops there.
    /// </summary>
    public async Task<IReadOnlyList<(string Question, string Answer, string By)>?> RunAsync(
        RunContext run,
        JournalWriter journal,
        IReadOnlyList<ReportQuestion> questions,
        CancellationToken ct)
    {
        var (autonomy, console, directory, takeRecommendationAfter) =
            (run.Autonomy, run.Console, run.Directory, run.Request.TakeRecommendationAfter);

        var decided = new List<(string, string, string)>();

        foreach (var question in questions)
        {
            string? answer;
            var timed = false;

            if (autonomy == "autonomous")
            {
                // Nobody is watching. The lead's own recommendation is the
                // only answer available, and the journal says it was taken
                // that way.
                answer = question.Recommendation;
                console.Note($"Decided on the lead's recommendation: {question.Question} → {answer}");
            }
            else if (takeRecommendationAfter is { } wait
                && console.AnswersInPlace
                && question.Recommendation is { Length: > 0 })
            {
                // Decided now, before the wait, so the answer the timer would
                // write is settled whatever happens while it runs. Known by
                // its key rather than its words for the readings, whose words
                // change every time the lead restates them.
                var key = KeyOf(question);
                var sendBack = TeamTimeout.SendsBack(run.OnTimeout, run.SentBackByTimer.Contains(key));

                (answer, timed) = await DecideOrTakeAsync(
                    console, question, directory, wait, sendBack ? TeamRunner.ThinkAgain : question.Recommendation, ct).ConfigureAwait(false);

                if (timed && sendBack)
                {
                    run.SentBackByTimer.Add(key);

                    console.Note(
                        $"Nobody answered in {TeamDuration.Spell(wait)}, so the question went back to the lead to "
                        + $"think again: {question.Question}");
                }
                else if (timed)
                {
                    console.Note(
                        $"Nobody answered in {TeamDuration.Spell(wait)}, so the lead's recommendation was taken: "
                        + $"{question.Question} → {answer}");
                }
            }
            else
            {
                answer = await console.DecideAsync(question, ct).ConfigureAwait(false);
            }

            var rethink = string.Equals(answer, TeamRunner.ThinkAgain, StringComparison.Ordinal);

            var by = autonomy == "autonomous" ? "recommendation"
                : timed && rethink ? "timed think again"
                : timed ? "timed default"
                : answer is null ? "nobody"
                : "person";

            // "by" says who settled it, and a null answer means nobody did:
            // the wait ran out. It said "person" either way, so a run that
            // stopped because somebody was still reading the question was
            // written down as a run somebody had decided to stop.
            await journal.WriteAsync(
                "decision",
                null,
                new
                {
                    question = question.Question,
                    answer = rethink ? "think again" : answer,
                    by,

                    // How long it waited, so "taken automatically" can say
                    // after what.
                    after = timed ? TeamDuration.Spell(takeRecommendationAfter!.Value) : null,
                },
                ct).ConfigureAwait(false);

            if (answer is null)
            {
                return null;
            }

            decided.Add((question.Question, rethink ? TeamRunner.ThinkAgainTold : answer, by));
        }

        return decided;
    }

    /// <summary>
    /// What a question is known by for <see cref="RunContext.SentBackByTimer"/>: its words,
    /// except the readings question, whose words are the lead's readings and
    /// change every time it restates them, but which is still the one question.
    /// </summary>
    private static string KeyOf(ReportQuestion question) =>
        question.Question.StartsWith(TeamRunner.ReadingsQuestionStart, StringComparison.Ordinal)
            ? TeamRunner.ReadingsQuestionStart
            : question.Question;

    /// <summary>
    /// Asks, and answers with <paramref name="chosen"/> if nobody has answered
    /// in time: the lead's recommendation, or <see cref="TeamRunner.ThinkAgain"/> where the
    /// run's on-timeout setting sends it back. Says whether it was answered that
    /// way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Answered the way a person would answer it: the recommendation is written
    /// as the question's answer, so the console waiting on it reads it like
    /// any other and the page watching it stops showing it. Answering the
    /// console's wait from here instead would leave the question on the page,
    /// answerable, for a run that had already moved on.
    /// </para>
    /// <para>
    /// It was taken automatically only if this wrote the answer. Somebody who
    /// answers in the same instant has answered, and is recorded as having
    /// done so.
    /// </para>
    /// </remarks>
    private async Task<(string? Answer, bool Timed)> DecideOrTakeAsync(
        ITeamConsole console,
        ReportQuestion question,
        string directory,
        TimeSpan wait,
        string chosen,
        CancellationToken ct)
    {
        var sentBack = string.Equals(chosen, TeamRunner.ThinkAgain, StringComparison.Ordinal);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Said to whoever is answering, so a page counts down to this rather
        // than to the run giving up, which is later and not what will happen.
        var asking = console.DecideAsync(
            question,
            new QuestionTimer(
                _time.GetUtcNow() + wait,
                sentBack ? QuestionTimer.SendsBack : QuestionTimer.TakesRecommendation),
            ct);

        var clock = Task.Delay(wait, _time, stop.Token);

        if (await Task.WhenAny(asking, clock).ConfigureAwait(false) == asking)
        {
            await stop.CancelAsync().ConfigureAwait(false);

            return (await asking.ConfigureAwait(false), false);
        }

        var wrote = false;

        // Until the question is on disk: a wait shorter than the console takes
        // to write it would otherwise find nothing to answer and leave the run
        // waiting on a person after all.
        while (!asking.IsCompleted)
        {
            var pending = NodePermissions.Pending(directory).FirstOrDefault(one =>
                one.Kind == "question" && string.Equals(one.Asked, question.Question, StringComparison.Ordinal));

            if (pending is not null)
            {
                if (NodePermissions.Answered(directory, pending.Id) is null)
                {
                    await NodePermissions.AnswerAsync(
                        directory,
                        pending.Id,
                        new AskAnswer(
                            true,
                            sentBack
                                ? $"Nobody answered in {TeamDuration.Spell(wait)}, so it went back to the lead to think again."
                                : $"Nobody answered in {TeamDuration.Spell(wait)}, so the lead's recommendation was taken.",
                            Chosen: chosen,
                            By: sentBack ? "timed think again" : "timed default"),
                        ct).ConfigureAwait(false);

                    wrote = true;
                }

                break;
            }

            await Task.WhenAny(asking, Task.Delay(NodePermissions.Glance, _time, ct)).ConfigureAwait(false);
        }

        return (await asking.ConfigureAwait(false), wrote);
    }
}
