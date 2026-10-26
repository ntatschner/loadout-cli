using Loadout.Models;
using Loadout.Models.Teams;

namespace Loadout.Core.Teams;

/// <summary>A question the lead left open, and what somebody said to it.</summary>
/// <param name="Question">The lead's question, in its words.</param>
/// <param name="Answer">The answer, in the person's.</param>
public sealed record ResumeAnswer(string Question, string Answer);

/// <summary>
/// The questions an ended run's lead asked and nobody answered, and pairing
/// them with what somebody says when they pick it up.
/// </summary>
/// <remarks>
/// <para>
/// A run that stops on questions has stopped because only a person could
/// answer them. Picked up without those answers, its lead finds the same
/// questions and nothing new, and either asks again or gives up. Run
/// 20261023-1740-2677 was picked up that way twice.
/// </para>
/// <para>
/// Open means asked in the lead's last report and not decided since. The
/// report is the final one beside the journal, and the decisions are the
/// journal's, so the two places that show a run agree on which are open.
/// </para>
/// </remarks>
public static class RunQuestions
{
    /// <summary>The lead's questions still open on an ended run, oldest first.</summary>
    public static IReadOnlyList<ReportQuestion> Open(RunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.Running)
        {
            return [];
        }

        var path = Path.Combine(run.Directory, "final-report.json");

        try
        {
            if (!File.Exists(path) || ReportReader.Read(File.ReadAllText(path)) is not { Succeeded: true, Value: { } report })
            {
                return [];
            }

            return
            [
                .. (report.Questions ?? [])
                    .Where(one => one.Question?.Trim() is { Length: > 0 } asked && !run.Answered.Contains(asked)),
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The answers to take into a pick-up, or why it cannot go ahead as asked.
    /// </summary>
    /// <param name="open">The questions still open.</param>
    /// <param name="given">The answers given, one per question in order.</param>
    /// <param name="withoutAnswers">Whether somebody chose to carry on without answering.</param>
    /// <remarks>
    /// Pure, so each refusal is a row in a table. Asking a person at a
    /// terminal is the command's: this says only whether what was given is
    /// enough, and <see cref="Pairing.Ask"/> is the case where nothing was and
    /// somebody could be asked.
    /// </remarks>
    public static Pairing Pair(IReadOnlyList<ReportQuestion> open, IReadOnlyList<string> given, bool withoutAnswers)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(given);

        if (withoutAnswers && given.Count > 0)
        {
            return Pairing.Refused("Give answers with --answer or carry on with --without-answers, not both.", ExitCode.InvalidArguments);
        }

        if (open.Count == 0)
        {
            return given.Count > 0
                ? Pairing.Refused("Its lead left no questions open, so there is nothing to answer. Say something to it with --message instead.", ExitCode.InvalidArguments)
                : Pairing.With([]);
        }

        if (withoutAnswers)
        {
            return Pairing.With([]);
        }

        if (given.Count == 0)
        {
            return Pairing.Asking();
        }

        if (given.Count != open.Count)
        {
            return Pairing.Refused(
                $"Its lead left {open.Count} question(s) open and {given.Count} answer(s) were given. "
                + "Give one --answer per question, in order:" + Environment.NewLine + Listed(open),
                ExitCode.InvalidArguments);
        }

        if (given.FirstOrDefault(one => one.Trim().Length == 0) is not null)
        {
            return Pairing.Refused("An answer was empty. Say something for each, or carry on with --without-answers.", ExitCode.InvalidArguments);
        }

        return Pairing.With([.. open.Select((one, i) => new ResumeAnswer(one.Question.Trim(), given[i].Trim()))]);
    }

    /// <summary>Why an unattended pick-up of a run with open questions is refused.</summary>
    public static string Unanswered(string runId, IReadOnlyList<ReportQuestion> open)
    {
        ArgumentNullException.ThrowIfNull(open);

        return $"Run {runId} stopped with {open.Count} question(s) only you can answer, and nobody can be asked here. "
            + "Answer each with --answer, in order, or pick it up with --without-answers to carry on without:"
            + Environment.NewLine + Listed(open);
    }

    /// <summary>The questions numbered, each with its options and what the lead recommends.</summary>
    public static string Listed(IReadOnlyList<ReportQuestion> open)
    {
        ArgumentNullException.ThrowIfNull(open);

        return string.Join(
            Environment.NewLine,
            open.Select((one, i) =>
                $"  {i + 1}. {one.Question.Trim()}"
                + (one.Options is { Count: > 0 } options ? $" ({string.Join(" / ", options)})" : string.Empty)
                + (one.Recommendation is { Length: > 0 } recommended ? $" Recommended: {recommended}" : string.Empty)));
    }
}

/// <summary>What <see cref="RunQuestions.Pair"/> came to.</summary>
/// <param name="Answers">The answers to take in, or null when refused or asking.</param>
/// <param name="Refusal">Why it cannot go ahead, or null.</param>
/// <param name="Code">The exit code a refusal carries.</param>
/// <param name="Ask">Whether nothing was given and the questions should be put to somebody.</param>
public sealed record Pairing(IReadOnlyList<ResumeAnswer>? Answers, string? Refusal, ExitCode Code, bool Ask)
{
    /// <summary>Go ahead with these.</summary>
    public static Pairing With(IReadOnlyList<ResumeAnswer> answers) => new(answers, null, ExitCode.Success, false);

    /// <summary>Not as asked.</summary>
    public static Pairing Refused(string why, ExitCode code) => new(null, why, code, false);

    /// <summary>Nothing given: ask, or refuse where nobody can be asked.</summary>
    public static Pairing Asking() => new(null, null, ExitCode.Success, true);
}
