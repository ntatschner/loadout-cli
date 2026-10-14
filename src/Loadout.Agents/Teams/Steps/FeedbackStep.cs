using Loadout.Core.Teams;
using Loadout.Models.Teams;

namespace Loadout.Agents.Teams.Steps;

/// <summary>
/// Takes the round's workers' reports back to the lead: their cost, their
/// decisions and their gates, whether the round moved anything, and what the
/// lead is told next.
/// </summary>
internal sealed class FeedbackStep
{
    /// <summary>Why the run ends here, or null when the lead takes another turn.</summary>
    public async Task<string?> RunAsync(
        RunContext run,
        JournalWriter journal,
        Report report,
        Briefing briefing,
        IReadOnlyList<WorkerOutcome> ran,
        CancellationToken ct)
    {
        var (console, autonomy, directory) = (run.Console, run.Autonomy, run.Directory);
        var (feedback, requests, heldForReadings, refused, accepted) =
            (briefing.Feedback, briefing.Requests, briefing.HeldForReadings, briefing.Refused, briefing.Accepted);
        var reports = new List<Report>();

        for (var index = 0; index < accepted.Count; index++)
        {
            var ask = accepted[index].Ask;
            var workerReport = ran[index];

            run.Cost += workerReport.Cost;
            run.Warnings.AddRange(workerReport.Warnings);

            if (workerReport.Report is { } decided)
            {
                // A gate node's answer is the decision it hands back,
                // in the one word its role tells it to use.
                foreach (var deliverable in decided.Deliverables.Where(d => d.Kind == DeliverableKind.Decision))
                {
                    run.Decisions[TeamRunner.BaseNode(ask.Node)] = deliverable.Ref.Trim();
                }
            }

            if (workerReport.Halted)
            {
                run.Ended = $"halted: {ask.Node} took an outward action its brief did not allow";
                run.Final = report;
                return run.Ended;
            }

            var handed = workerReport.Report ?? new Report(
                ask.Node, ReportStatus.Failed, workerReport.Failure ?? "The node ended without a report.", [], [], []);

            reports.Add(handed);
            await TeamRunner.WriteDocumentAsync(directory, $"report-{TeamRunner.Safe(ask.Node)}-{run.Rounds}.json", ReportReader.Write(handed), ct).ConfigureAwait(false);

            if (!await TeamRunner.GateAsync(autonomy, console, $"Hand {ask.Node}'s report ({TeamRunner.Spell(handed.Status)}) to the lead", ct).ConfigureAwait(false))
            {
                run.Ended = "stopped by the person";
                return run.Ended;
            }
        }

        if (report.OutwardRequested is { Count: > 0 } wanted)
        {
            run.Warnings.Add($"The lead asked for outward actions it was not allowed: {string.Join("; ", wanted)}.");
        }

        // A round that asked for nothing moved nothing. A question is
        // not progress either: a lead with several to ask asks them in
        // one report, and one that asks round after round is stuck.
        // A round whose work was held back over its readings is not a
        // quiet one: the lead asked, and was told to think again.
        run.QuietRounds = requests.Count == 0 && !heldForReadings ? run.QuietRounds + 1 : 0;

        // Written down, because a run heading for this is something
        // somebody would want to know before it gets there - and the
        // journal had no way to say "that round asked for nothing".
        await journal.WriteAsync(
            "round.ended",
            null,
            new { round = run.Rounds, requests = requests.Count, quiet = run.QuietRounds },
            ct).ConfigureAwait(false);

        if (run.QuietRounds >= 2)
        {
            run.Ended = "no progress: two rounds without a request or a finish";
            return run.Ended;
        }

        // What it is now held to, said once, in the round after it was
        // agreed. It proposed these and a person may have rewritten
        // them, so the list it reads here is the list ReportCheck will
        // hold its done to - and a lead held to criteria it has not
        // seen cannot report coverage for them.
        if (run.AgreedThisRound is { Count: > 0 } settled)
        {
            feedback.AppendLine("## What this run is judged on").AppendLine();
            feedback.AppendLine(
                "Agreed from what you proposed. " + char.ToUpperInvariant(TeamRunner.CoverageOwed[0]) + TeamRunner.CoverageOwed[1..] + ".")
                .AppendLine();

            for (var i = 0; i < settled.Count; i++)
            {
                feedback.AppendLine($"{i + 1}. {settled[i]}");
            }

            feedback.AppendLine();

            run.AgreedThisRound = null;
        }

        if (reports.Count > 0)
        {
            feedback.AppendLine("## Reports from your requests").AppendLine();

            foreach (var handed in reports)
            {
                feedback.AppendLine("```json").AppendLine(ReportReader.Write(handed)).AppendLine("```").AppendLine();
            }
        }

        if (refused.Count > 0)
        {
            // Said to the lead as well as to the person: a lead that
            // is refused and not told asks again, and did, twice.
            feedback.AppendLine("## Requests refused").AppendLine();

            foreach (var reason in refused)
            {
                feedback.AppendLine($"- {reason}");
            }

            feedback.AppendLine();
        }

        feedback.AppendLine(
            "Decide what happens next: more requests, or finish. Reply with one report/1 "
            + "document. Keep summary short: a few sentences, not a report of its own. "
            + "Status done needs evidence cited from these reports.");

        run.Prompt = feedback.ToString();

        return null;
    }
}
